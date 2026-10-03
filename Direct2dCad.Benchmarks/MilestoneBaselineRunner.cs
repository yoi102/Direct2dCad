using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using Direct2dCad.Db.Cad;
using Direct2dCad.IO;

namespace Direct2dCad.Benchmarks;

internal static class MilestoneBaselineRunner
{
    public static void Run(string outputDirectory)
    {
        var directory=Path.GetFullPath(outputDirectory);Directory.CreateDirectory(directory);
        var dispatcher=Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var task=MeasureAsync(directory);
        _=task.ContinueWith(_=>dispatcher.BeginInvokeShutdown(DispatcherPriority.Background),TaskScheduler.Default);
        Dispatcher.Run();task.GetAwaiter().GetResult();
    }
    private static async Task MeasureAsync(string directory)
    {
        var storage=new CadDocumentStorage();var rows=new List<object>();
        var timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(16)};
        var gaps=new List<double>();var last=Stopwatch.GetTimestamp();
        timer.Tick+=(_,_)=>{gaps.Add(Math.Max(0,Stopwatch.GetElapsedTime(last).TotalMilliseconds-16));last=Stopwatch.GetTimestamp();};timer.Start();
        try
        {
            foreach(var count in new[]{100,20000,100000})
            {
                // Fixed generated samples are saved next to results for reproducibility.
                var data=BenchmarkDocumentFactory.Create(count,BenchmarkDocumentKind.Mixed);
                var path=Path.Combine(directory,$"mixed-{count}.d2cad");storage.Save(data.Document,path);
                for(var iteration=0;iteration<3;iteration++)
                {
                    gaps.Clear();last=Stopwatch.GetTimestamp();
                    var start=Stopwatch.GetTimestamp();var loaded=await storage.LoadAsync(path);var load=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    using(var render=new BenchmarkRenderSession(BenchmarkDocumentFactory.FromDocument(loaded))) render.RenderPreparedFirstFrame();
                    var firstPreparedFrame=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var savePath=Path.Combine(directory,$"save-{count}.d2cad");start=Stopwatch.GetTimestamp();
                    await storage.SaveAsync(loaded,savePath,new CadSnapshotCaptureOptions(()=>true,async ct=>await Task.Delay(1,ct)){ExpectedDestination=CadFileRevision.Capture(savePath)});
                    var save=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    start=Stopwatch.GetTimestamp();var snapshot=await storage.CreateIndependentSnapshotAsync(loaded,new(()=>true,async ct=>await Task.Delay(1,ct)));
                    var total=await Task.Run(()=>snapshot.Entities.Values.Count(e=>!e.IsErased));var query=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if(total!=count) throw new InvalidOperationException("Snapshot query lost entities.");
                    using var cancellation=new CancellationTokenSource();var cancelledPath=Path.Combine(directory,"cancelled.d2cad");long requested=0;
                    try
                    {
                        await storage.SaveAsync(loaded,cancelledPath,new CadSnapshotCaptureOptions(()=>true,ct=>{requested=Stopwatch.GetTimestamp();cancellation.Cancel();return ValueTask.CompletedTask;}),cancellation.Token);
                        throw new InvalidOperationException("Cancellation did not stop capture.");
                    }
                    catch(OperationCanceledException){}
                    var cancellationStop=Stopwatch.GetElapsedTime(requested).TotalMilliseconds;
                    if(File.Exists(cancelledPath)) throw new InvalidOperationException("Cancelled save produced a file.");
                    await Task.Delay(32);var sorted=gaps.Order().ToArray();
                    var row=new {entity_count=count,iteration=iteration+1,file_bytes=new FileInfo(path).Length,load_ms=load,
                        prepared_first_present_ms=firstPreparedFrame,save_ms=save,independent_snapshot_query_ms=query,
                        owner_dispatch_p95_delay_ms=sorted.Length==0 ? 0 : sorted[(int)Math.Floor((sorted.Length-1)*.95)],
                        owner_dispatch_max_delay_ms=sorted.DefaultIfEmpty().Max(),capture_cancel_stop_ms=cancellationStop,
                        process_peak_working_set_bytes=Process.GetCurrentProcess().PeakWorkingSet64};
                    rows.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));File.Delete(savePath);
                }
            }
        }
        finally{timer.Stop();}
        var report=new {recorded_at=DateTimeOffset.Now,machine=Environment.MachineName,cpu=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            os=Environment.OSVersion.ToString(),runtime=Environment.Version.ToString(),surface="1600x900 offscreen Direct2D, prepared first Present",rows,
            limitations="Dispatcher timing is an owner-thread heartbeat, not measured mouse latency. Prepared first Present waits for all scene resources; no separate progressive first-visible metric. Peak working set is cumulative within this process. Capture cancellation is cooperative; native resource preparation is not preemptible."};
        await File.WriteAllTextAsync(Path.Combine(directory,"baseline.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
