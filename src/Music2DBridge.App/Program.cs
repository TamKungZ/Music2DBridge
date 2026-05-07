using Music2DBridge.Core;
using Music2DBridge.VTubeStudio;
using NAudio.Wave;

internal sealed class Program
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private const int SampleRate = 44100;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    private const double MinPitchHz = 80.0;
    private const double MaxPitchHz = 1000.0;

    private const string PluginName = "Music2D Instrument Tracker";
    private const string PluginDeveloper = "TamKungZ_";

    private const string ParamPitch = "ParamInstPitch";
    private const string ParamEnergy = "ParamInstEnergy";
    private const string ParamNoteClass = "ParamInstNoteClass";
    private const string ParamInKey = "ParamInstInKey";
    private const string ParamChordRoot = "ParamInstChordRoot";
    private const string ParamChordType = "ParamInstChordType";
    private const string ParamKeyRoot = "ParamInstKeyRoot";
    private const string ParamKeyMode = "ParamInstKeyMode";

    private static readonly string AppConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TamKungZ_",
        "Music2DBridge"
    );

    private static readonly string TokenPath = Path.Combine(AppConfigDirectory, "vts-token.txt");

    private static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Music2DBridge...");

        var fixedKeyConfig = ParseFixedKeyConfig(args);
        MusicAnalyzer.ConfigureFixedKeyFilter(fixedKeyConfig.Enabled, fixedKeyConfig.Root, fixedKeyConfig.IsMinor);
        Console.WriteLine(
            fixedKeyConfig.Enabled
                ? $"Fixed key filter: ON ({FormatKey(fixedKeyConfig.Root, fixedKeyConfig.IsMinor)})"
                : "Fixed key filter: OFF"
        );

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await using var vts = new VtsClient();
        vts.TokenStorePath = TokenPath;
        await vts.ConnectAsync(new Uri("ws://127.0.0.1:8001"), cts.Token);
        await vts.AuthenticateAsync(PluginName, PluginDeveloper, cts.Token);
        Console.WriteLine($"Auth token path: {TokenPath}");

        using var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            BufferMilliseconds = 30,
            NumberOfBuffers = 3,
            DeviceNumber = 0
        };

        waveIn.DataAvailable += (_, e) => MusicAnalyzer.ProcessPcm16Mono(
            e.Buffer,
            e.BytesRecorded,
            SampleRate,
            MinPitchHz,
            MaxPitchHz
        );

        waveIn.StartRecording();
        Console.WriteLine("Connected and recording. Press Ctrl+C to stop.");

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var state = MusicAnalyzer.Snapshot();

                var pitch01 = MusicAnalyzer.MapClamp(state.PitchHz, 100.0, 500.0, 0.0, 1.0);
                var energy01 = MusicAnalyzer.MapClamp(state.Energy, 0.01, 0.2, 0.0, 1.0);
                var noteClass01 = state.NoteClass >= 0 ? state.NoteClass / 11.0 : 0.0;
                var inKey01 = state.InKey ? 1.0 : 0.0;
                var chordRoot01 = state.ChordRoot >= 0 ? state.ChordRoot / 11.0 : 0.0;
                var chordType01 = state.ChordType > 0
                    ? MusicAnalyzer.MapClamp(state.ChordType, 1.0, 5.0, 0.2, 1.0)
                    : 0.0;
                var keyRoot01 = state.KeyRoot >= 0 ? state.KeyRoot / 11.0 : 0.0;
                var keyMode01 = state.KeyIsMinor ? 1.0 : 0.0;

                await vts.InjectParametersAsync(
                    [
                        (ParamEnergy, energy01, 1.0),
                        (ParamPitch, pitch01, 1.0),
                        (ParamNoteClass, noteClass01, 1.0),
                        (ParamInKey, inKey01, 1.0),
                        (ParamChordRoot, chordRoot01, 1.0),
                        (ParamChordType, chordType01, 1.0),
                        (ParamKeyRoot, keyRoot01, 1.0),
                        (ParamKeyMode, keyMode01, 1.0)
                    ],
                    cts.Token
                );

                Console.WriteLine(
                    $"Pitch: {state.PitchHz,7:F1} Hz | Note: {state.Note,3} | InKey: {(state.InKey ? "Y" : "N")} | Chord: {state.Chord,-6} | Key: {state.Key,-9} | Energy: {state.Energy:F3}"
                );

                await Task.Delay(50, cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            waveIn.StopRecording();
        }

        Console.WriteLine("Stopped.");
    }

    private static FixedKeyConfig ParseFixedKeyConfig(string[] args)
    {
        string? keySpec = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--fixed-key=", StringComparison.OrdinalIgnoreCase))
            {
                keySpec = arg["--fixed-key=".Length..];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(keySpec))
        {
            keySpec = Environment.GetEnvironmentVariable("M2D_FIXED_KEY");
        }

        if (!TryParseKeySpec(keySpec, out var root, out var isMinor))
        {
            return new FixedKeyConfig(false, 0, false);
        }

        return new FixedKeyConfig(true, root, isMinor);
    }

    private static bool TryParseKeySpec(string? spec, out int root, out bool isMinor)
    {
        root = 0;
        isMinor = false;

        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var normalized = spec.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.EndsWith("MINOR", StringComparison.Ordinal))
        {
            isMinor = true;
            normalized = normalized[..^5];
        }
        else if (normalized.EndsWith("MIN", StringComparison.Ordinal))
        {
            isMinor = true;
            normalized = normalized[..^3];
        }
        else if (normalized.EndsWith("MAJOR", StringComparison.Ordinal))
        {
            normalized = normalized[..^5];
        }
        else if (normalized.EndsWith("MAJ", StringComparison.Ordinal))
        {
            normalized = normalized[..^3];
        }

        normalized = normalized.Replace("H", "B", StringComparison.Ordinal);

        var noteMap = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["C"] = 0,
            ["B#"] = 0,
            ["C#"] = 1,
            ["DB"] = 1,
            ["D"] = 2,
            ["D#"] = 3,
            ["EB"] = 3,
            ["E"] = 4,
            ["FB"] = 4,
            ["F"] = 5,
            ["E#"] = 5,
            ["F#"] = 6,
            ["GB"] = 6,
            ["G"] = 7,
            ["G#"] = 8,
            ["AB"] = 8,
            ["A"] = 9,
            ["A#"] = 10,
            ["BB"] = 10,
            ["B"] = 11,
            ["CB"] = 11
        };

        if (!noteMap.TryGetValue(normalized, out root))
        {
            return false;
        }

        return true;
    }

    private static string FormatKey(int root, bool isMinor)
    {
        return NoteNames[((root % 12) + 12) % 12] + (isMinor ? " minor" : " major");
    }

    private readonly record struct FixedKeyConfig(bool Enabled, int Root, bool IsMinor);
}
