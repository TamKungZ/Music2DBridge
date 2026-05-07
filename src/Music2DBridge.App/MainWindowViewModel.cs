using System.Text;
using ReactiveUI;
using System.Reactive;
using System.Collections.Generic;

namespace Music2DBridge.App;

public sealed class MainWindowViewModel : ReactiveObject
{
    private static readonly string[] UiNoteModes = ["class", "per-note"];
    private static readonly string[] UiFixedKeys =
    [
        "Cmaj", "Gmaj", "Dmaj", "Amaj", "Emaj", "Bmaj", "F#maj", "C#maj",
        "Fmaj", "Bbmaj", "Ebmaj", "Abmaj", "Dbmaj", "Gbmaj", "Cbmaj",
        "Amin", "Emin", "Bmin", "F#min", "C#min", "G#min", "D#min", "A#min",
        "Dmin", "Gmin", "Cmin", "Fmin", "Bbmin", "Ebmin", "Abmin"
    ];

    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _runCts;
    private bool _isRunning;
    private string _statusText = "Idle";
    private string _selectedNoteMode = "per-note";
    private bool _isFixedKeyEnabled;
    private string _fixedKeySpec = "Cmaj";

    public MainWindowViewModel()
    {
        StartCommand = ReactiveCommand.CreateFromTask(StartAsync, this.WhenAnyValue(x => x.IsStopped));
        StopCommand = ReactiveCommand.Create(Stop, this.WhenAnyValue(x => x.IsRunning));
        EnableFixedKeyCommand = ReactiveCommand.Create(EnableFixedKey, this.WhenAnyValue(x => x.IsStopped));
        DisableFixedKeyCommand = ReactiveCommand.Create(DisableFixedKey, this.WhenAnyValue(x => x.IsStopped));
        AppendLog("UI mode ready. Select note mode, then click Start.");
        AppendLog("For terminal mode use: Music2DBridge --cli ...");
    }

    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> EnableFixedKeyCommand { get; }
    public ReactiveCommand<Unit, Unit> DisableFixedKeyCommand { get; }
    public IReadOnlyList<string> AvailableNoteModes => UiNoteModes;
    public IReadOnlyList<string> AvailableFixedKeys => UiFixedKeys;

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

    public string SelectedNoteMode
    {
        get => _selectedNoteMode;
        set => this.RaiseAndSetIfChanged(ref _selectedNoteMode, string.IsNullOrWhiteSpace(value) ? "class" : value);
    }

    public bool IsFixedKeyEnabled
    {
        get => _isFixedKeyEnabled;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isFixedKeyEnabled, value);
            this.RaisePropertyChanged(nameof(ShowFixedKeyAddButton));
            this.RaisePropertyChanged(nameof(ShowFixedKeyEditor));
            this.RaisePropertyChanged(nameof(ShowFixedKeyLabel));
        }
    }

    public bool ShowFixedKeyAddButton => !IsFixedKeyEnabled;
    public bool ShowFixedKeyEditor => IsFixedKeyEnabled;
    public bool ShowFixedKeyLabel => IsFixedKeyEnabled;

    public string FixedKeySpec
    {
        get => _fixedKeySpec;
        set => this.RaiseAndSetIfChanged(ref _fixedKeySpec, string.IsNullOrWhiteSpace(value) ? "Cmaj" : value.Trim());
    }

    private async Task StartAsync()
    {
        _runCts = new CancellationTokenSource();
        IsRunning = true;
        StatusText = "Running";
        AppendLog("Starting bridge...");

        try
        {
            var runner = new BridgeRunner();
            var args = new List<string>
            {
                $"--note-mode={SelectedNoteMode}"
            };

            if (IsFixedKeyEnabled)
            {
                args.Add($"--fixed-key={FixedKeySpec}");
            }

            await runner.RunAsync(args.ToArray(), AppendLog, _runCts.Token);
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

    private void EnableFixedKey()
    {
        IsFixedKeyEnabled = true;
        AppendLog($"Fixed key filter configured: {FixedKeySpec}");
    }

    private void DisableFixedKey()
    {
        IsFixedKeyEnabled = false;
        AppendLog("Fixed key filter removed (optional mode off).");
    }

    private void AppendLog(string line)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _log.Append('[').Append(timestamp).Append("] ").AppendLine(line);
        this.RaisePropertyChanged(nameof(LogText));
    }
}
