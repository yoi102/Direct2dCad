using Avalonia;
using Avalonia.Headless;

namespace Direct2dCad.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Services.AppCrashLog.Install();
        try
        {
            if (args is ["--gpu-test-offscreen", var report]) return OffscreenGpuProbe.Run(Path.GetFullPath(report));
            return BuildAvaloniaApp(args.Contains("--smoke-test-headless")).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Services.AppCrashLog.Write(ex,"Main");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp(bool headless = false)
    {
        var builder = AppBuilder.Configure<App>().UseSkia().UseHarfBuzz()
            .With(new global::Avalonia.Media.FontManagerOptions
            {
                DefaultFamilyName = "Microsoft YaHei UI",
                FontFallbacks = [new global::Avalonia.Media.FontFallback { FontFamily = new("Microsoft YaHei UI") }, new global::Avalonia.Media.FontFallback { FontFamily = new("Yu Gothic UI") }]
            });
        return headless ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }) : builder.UseWin32();
    }
}

