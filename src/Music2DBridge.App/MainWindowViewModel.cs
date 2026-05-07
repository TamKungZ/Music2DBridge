using System.Text;
using ReactiveUI;
using System.Reactive;

namespace Music2DBridge.App;

public sealed class MainWindowViewModel : ReactiveObject
{
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _runCts;
    private bool _isRunning;
    private string _statusText = "Idle";

    public MainWindowViewModel()
    {
        StartCommand = ReactiveCommand.CreateFromTask(StartAsync, this.WhenAnyValue(x => x.IsStopped));
        StopCommand = ReactiveCommand.Create(Stop, this.WhenAnyValue(x => x.IsRunning));
        AppendLog("UI mode ready. Click Start to run bridge with default settings.");
        AppendLog("For terminal mode use: Music2DBridge --cli ...");
    }

    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isRunning, value);
            this.RaisePropertyChanged(nameof(IsStopped));
        }
    }

    public bool IsStopped => !IsRunning;

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string LogText => _log.ToString();

    private async Task StartAsync()
    {
        _runCts = new CancellationTokenSource();
        IsRunning = true;
        StatusText = "Running";
        AppendLog("Starting bridge...");

        try
        {
            var runner = new BridgeRunner();
            await runner.RunAsync(Array.Empty<string>(), AppendLog, _runCts.Token);
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            StatusText = "Idle";
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void Stop()
    {
        _runCts?.Cancel();
        AppendLog("Stop requested.");
    }

    private void AppendLog(string line)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _log.Append('[').Append(timestamp).Append("] ").AppendLine(line);
        this.RaisePropertyChanged(nameof(LogText));
    }
}
