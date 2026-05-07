using System.Text;
using ReactiveUI;
using System.Reactive;
using System.Collections.Generic;
using Avalonia.Threading;
using Music2DBridge.Core;

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
    private double _inputGain = 1.0;
    private double _noiseGate = 0.01;
    private string _currentNote = "--";
    private string _currentChord = "--";
    private string _currentKey = "--";
    private string _currentPitch = "0.0 Hz";
    private string _currentEnergy = "0.000";

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

    public double InputGain
    {
        get => _inputGain;
        set
        {
            this.RaiseAndSetIfChanged(ref _inputGain, Math.Clamp(value, 0.1, 8.0));
            this.RaisePropertyChanged(nameof(InputGainText));
        }
    }

    public string InputGainText => $"{InputGain:F2}x";

    public double NoiseGate
    {
        get => _noiseGate;
        set
        {
            this.RaiseAndSetIfChanged(ref _noiseGate, Math.Clamp(value, 0.0, 0.08));
            this.RaisePropertyChanged(nameof(NoiseGateText));
        }
    }

    public string NoiseGateText => NoiseGate.ToString("F3");

    public string CurrentNote
    {
        get => _currentNote;
        private set => this.RaiseAndSetIfChanged(ref _currentNote, value);
    }

    public string CurrentChord
    {
        get => _currentChord;
        private set => this.RaiseAndSetIfChanged(ref _currentChord, value);
    }

    public string CurrentKey
    {
        get => _currentKey;
        private set => this.RaiseAndSetIfChanged(ref _currentKey, value);
    }

    public string CurrentPitch
    {
        get => _currentPitch;
        private set => this.RaiseAndSetIfChanged(ref _currentPitch, value);
    }

    public string CurrentEnergy
    {
        get => _currentEnergy;
        private set => this.RaiseAndSetIfChanged(ref _currentEnergy, value);
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
                $"--note-mode={SelectedNoteMode}",
                $"--gain={InputGain:F2}",
                $"--gate={NoiseGate:F3}"
            };

            if (IsFixedKeyEnabled)
            {
                args.Add($"--fixed-key={FixedKeySpec}");
            }

            await runner.RunAsync(args.ToArray(), AppendLog, UpdateLiveState, _runCts.Token);
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

    private void UpdateLiveState(MusicalState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CurrentNote = state.Note;
            CurrentChord = state.Chord;
            CurrentKey = state.Key;
            CurrentPitch = $"{state.PitchHz:F1} Hz";
            CurrentEnergy = state.Energy.ToString("F3");
        });
    }
}
