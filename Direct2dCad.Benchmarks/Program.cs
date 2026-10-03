using BenchmarkDotNet.Running;

namespace Direct2dCad.Benchmarks;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        Console.Error.WriteLine("Direct2dCad.Benchmarks must run in Release configuration.");
        Console.Error.WriteLine(
            "dotnet run -c Release --project .\\Direct2dCad.Benchmarks\\Direct2dCad.Benchmarks.csproj");
        Environment.ExitCode = 1;
        return;
#else
        if (args.Length == 3 && args[0] == "--dxf-zoom")
        { DxfZoomRunner.Run(args[1], args[2]); return; }
        if(args.Length==2 && args[0]=="--m1-m3-baseline")
        { MilestoneBaselineRunner.Run(args[1]);return; }
        if(args.Length==2 && args[0]=="--m4-m6-evidence")
        { MilestoneDeliveryRunner.Run(args[1]);return; }
        var effectiveArgs = new List<string>(args.Length);
        var smoke = false;
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--smoke", StringComparison.OrdinalIgnoreCase))
            {
                smoke = true;
                continue;
            }

            if (string.Equals(args[index], "--document", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    throw new ArgumentException("--document requires a .d2cad file path.");

                Environment.SetEnvironmentVariable(
                    DocumentIoBenchmarks.DocumentEnvironmentVariable,
                    Path.GetFullPath(args[index]));
                continue;
            }

            effectiveArgs.Add(args[index]);
        }

        if (smoke)
        {
            effectiveArgs.Add("--job");
            effectiveArgs.Add("Dry");
        }

        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run([.. effectiveArgs]);
#endif
    }
}
