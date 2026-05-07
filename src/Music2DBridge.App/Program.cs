using Avalonia;
using Avalonia.ReactiveUI;
using Music2DBridge.App;
using System.Runtime.InteropServices;

internal sealed class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (ShouldRunCli(args))
        {
            PrepareCliConsole();

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

    private static void PrepareCliConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const int AttachParentProcess = -1;
        _ = AttachConsole(AttachParentProcess) || AllocConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
    }
}
