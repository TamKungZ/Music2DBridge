using Avalonia;
using Avalonia.ReactiveUI;
using Music2DBridge.App;

internal sealed class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (ShouldRunCli(args))
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var runner = new BridgeRunner();
            Console.WriteLine("Starting Music2DBridge (CLI mode)...");
            await runner.RunAsync(args, Console.WriteLine, cts.Token);
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static bool ShouldRunCli(string[] args)
    {
        return args.Any(a => a.Equals("--cli", StringComparison.OrdinalIgnoreCase));
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
    }
}
