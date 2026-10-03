using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.IO;
using Direct2dCad.IO.Dxf;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

internal static class MilestoneDeliveryRunner
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
        timer.Tick+=(_,_)=>{gaps.Add(Math.Max(0,Stopwatch.GetElapsedTime(last).TotalMilliseconds-16));last=Stopwatch.GetTimestamp();};
        timer.Start();
        try
        {
            foreach(var count in new[]{100,20000,100000})
            {
                var sample=Path.Combine(directory,$"mixed-{count}.d2cad");
                storage.Save(BenchmarkDocumentFactory.Create(count,BenchmarkDocumentKind.Mixed).Document,sample);
                foreach(var progressive in new[]{false,true})for(var iteration=1;iteration<=3;iteration++)
                {
                    gaps.Clear();last=Stopwatch.GetTimestamp();
                    var start=Stopwatch.GetTimestamp();var document=await storage.LoadAsync(sample);
                    var load=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    double first=0,complete;
                    using(var render=new BenchmarkRenderSession(BenchmarkDocumentFactory.FromDocument(document),progressivePreparation:progressive))
                    {
                        // Identical 100 x 56.25 mm view in both modes. Most large-scene entities are off screen.
                        render.Viewport.SetView(16,new(0,900));
                        var pending=true;
                        while(pending || render.ImageSource.PresentCount==0)
                        {
                            pending=render.RenderHost.PrepareRenderCacheStep();
                            if(render.ImageSource.PresentCount==0 && render.RenderHost.IsInitialViewReady)
                            {
                                render.RenderHost.Render(CadRenderInvalidation.Full,baseSceneChanged:true);
                                if(render.ImageSource.PresentCount>0)first=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                            }
                            if(Stopwatch.GetElapsedTime(start)>TimeSpan.FromMinutes(2))throw new TimeoutException("Scene preparation timed out.");
                            await Dispatcher.Yield(DispatcherPriority.Background);
                        }
                        complete=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        render.RenderHost.Render(CadRenderInvalidation.Full,baseSceneChanged:true);
                        if(!render.RenderHost.HasPresentedScene)throw new InvalidOperationException("No scene was presented.");
                    }
                    await Task.Delay(32);
                    var sorted=gaps.Order().ToArray();
                    var row=new{entity_count=count,mode=progressive?"bounded_visible_priority":"synchronous_full_preparation",iteration,
                        file_bytes=new FileInfo(sample).Length,load_ms=load,first_visible_present_ms=first,all_resources_ready_ms=complete,
                        owner_dispatch_p95_delay_ms=sorted.Length==0?0:sorted[(int)Math.Floor((sorted.Length-1)*.95)],
                        owner_dispatch_max_delay_ms=sorted.DefaultIfEmpty().Max(),heartbeat_samples=sorted.Length,
                        process_working_set_bytes=Process.GetCurrentProcess().WorkingSet64,process_peak_working_set_bytes=Process.GetCurrentProcess().PeakWorkingSet64};
                    rows.Add(row);Console.WriteLine(JsonSerializer.Serialize(row));
                }
            }
            var large=await storage.LoadAsync(Path.Combine(directory,"mixed-100000.d2cad"));
            using var cancellation=new CancellationTokenSource();long requested=0;
            var cancelledPath=Path.Combine(directory,"cancelled-copy.d2cad");
            try
            {
                await storage.SaveAsync(large,cancelledPath,new CadSnapshotCaptureOptions(()=>true,ct=>
                {requested=Stopwatch.GetTimestamp();cancellation.Cancel();return ValueTask.CompletedTask;}),cancellation.Token);
                throw new InvalidOperationException("Cancelled capture completed.");
            }
            catch(OperationCanceledException){}
            var stop=Stopwatch.GetElapsedTime(requested).TotalMilliseconds;
            if(File.Exists(cancelledPath))throw new InvalidOperationException("Cancelled save left an output.");
            var report=new{recorded_at=DateTimeOffset.Now,machine=Environment.MachineName,cpu=Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                os=Environment.OSVersion.ToString(),runtime=Environment.Version.ToString(),surface="1600x900 offscreen Direct2D; 100 x 56.25 mm visible region",rows,
                capture_cancel_stop_ms=stop,limitations="Dispatcher heartbeat measures scheduling delay, not physical mouse latency. Working-set peak is cumulative within this process. Timings include decode, spatial-index build and host creation. Capture is cooperative; individual native geometry calls cannot be preempted. Mixed generated sample excludes large raster/OLE payloads."};
            await File.WriteAllTextAsync(Path.Combine(directory,"capacity.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            CreateExchangeEvidence(directory);
        }
        finally{timer.Stop();}
    }
    private static void CreateExchangeEvidence(string directory)
    {
        var doc=CadDocument.Create("100 mm calibration");
        var layer=doc.CreateLayer("轮廓",CadColor.Red,new(.25));
        doc.AddLine(default,new(100,0),layer);doc.AddCircle(new(25,25),10,layer);
        doc.AddArc(new(60,25),10,0,Math.PI,layer);doc.AddText("100 mm 中文",new(0,5),2.5,layerId:layer);
        doc.AddPolyline([new(0,0),new(10,0),new(10,10)],true,layer);
        var hidden=doc.AddLine(new(500,500),new(501,501));hidden.SetVisible(false);
        var block=doc.CreateBlockDefinition("part",default);var child=doc.AddLine(default,new(10,0));doc.MoveEntityToBlock(child.Id,block);
        doc.AddBlockReference(block,new(80,30),rotationRadians:.3,scaleX:2,scaleY:1.5);
        new CadDxfStorage().Export(doc,Path.Combine(directory,"calibration.dxf"),true);
        var repo=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(repo,"Direct2dCad.slnx")))repo=Directory.GetParent(repo)?.FullName??throw new IOException("Repository not found.");
        var io=new CadDxfStorage();var external=io.Import(Path.Combine(repo,"docs/samples/dxf/ezdxf-closed-loop-arcs.dxf"),Direct2dCad.Db.Cad.Settings.CadUnit.Millimeter);
        io.Export(external.Document,Path.Combine(directory,"external-roundtrip.dxf"),true);
    }
}
