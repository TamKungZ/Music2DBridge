using Music2DBridge.Core;
using Music2DBridge.VTubeStudio;
using NAudio.Wave;

namespace Music2DBridge.App;

internal sealed class BridgeRunner
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] NoteParamSuffixes = ["C", "Cs", "D", "Ds", "E", "F", "Fs", "G", "Gs", "A", "As", "B"];

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
    private const string DefaultPerNotePrefix = "ParamInstNote";

    private static readonly string AppConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TamKungZ_",
        "Music2DBridge"
    );

    private static readonly string TokenPath = Path.Combine(AppConfigDirectory, "vts-token.txt");

    public async Task RunAsync(string[] args, Action<string>? log, CancellationToken cancellationToken)
    {
        var fixedKeyConfig = ParseFixedKeyConfig(args);
        var noteOutputConfig = ParseNoteOutputConfig(args);

        MusicAnalyzer.ConfigureFixedKeyFilter(fixedKeyConfig.Enabled, fixedKeyConfig.Root, fixedKeyConfig.IsMinor);

        log?.Invoke(
            fixedKeyConfig.Enabled
                ? $"Fixed key filter: ON ({FormatKey(fixedKeyConfig.Root, fixedKeyConfig.IsMinor)})"
                : "Fixed key filter: OFF"
        );

        log?.Invoke(noteOutputConfig.Mode == NoteOutputMode.PerNote
            ? $"Note mode: per-note (prefix: {noteOutputConfig.Prefix})"
            : "Note mode: class (ParamInstNoteClass)");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = linkedCts.Token;

        await using var vts = new VtsClient
        {
            TokenStorePath = TokenPath
        };

        await vts.ConnectAsync(new Uri("ws://127.0.0.1:8001"), ct);
        await vts.AuthenticateAsync(PluginName, PluginDeveloper, ct);
        log?.Invoke($"Auth token path: {TokenPath}");

        foreach (var definition in BuildParameterDefinitions(noteOutputConfig))
        {
            await vts.EnsureParameterAsync(definition.Id, definition.Min, definition.Max, definition.DefaultValue, definition.Explanation, ct);
        }

        log?.Invoke("Custom parameters ensured.");

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
        log?.Invoke("Connected and recording.");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var state = MusicAnalyzer.Snapshot();

                var pitch01 = MusicAnalyzer.MapClamp(state.PitchHz, 100.0, 500.0, 0.0, 1.0);
                var energy01 = MusicAnalyzer.MapClamp(state.Energy, 0.01, 0.2, 0.0, 1.0);
                var noteClassValue = state.NoteClass >= 0 ? state.NoteClass : 0.0;
                var inKey01 = state.InKey ? 1.0 : 0.0;
                var chordRoot01 = state.ChordRoot >= 0 ? state.ChordRoot / 11.0 : 0.0;
                var chordType01 = state.ChordType > 0
                    ? MusicAnalyzer.MapClamp(state.ChordType, 1.0, 5.0, 0.2, 1.0)
                    : 0.0;
                var keyRoot01 = state.KeyRoot >= 0 ? state.KeyRoot / 11.0 : 0.0;
                var keyMode01 = state.KeyIsMinor ? 1.0 : 0.0;

                var parameters = new List<(string Id, double Value, double Weight)>
                {
                    (ParamEnergy, energy01, 1.0),
                    (ParamPitch, pitch01, 1.0),
                    (ParamInKey, inKey01, 1.0),
                    (ParamChordRoot, chordRoot01, 1.0),
                    (ParamChordType, chordType01, 1.0),
                    (ParamKeyRoot, keyRoot01, 1.0),
                    (ParamKeyMode, keyMode01, 1.0)
                };

                if (noteOutputConfig.Mode == NoteOutputMode.PerNote)
                {
                    var notePresence = BuildNotePresence(state);
                    for (var i = 0; i < 12; i++)
                    {
                        parameters.Add(($"{noteOutputConfig.Prefix}{NoteParamSuffixes[i]}", notePresence[i], 1.0));
                    }
                }
                else
                {
                    parameters.Add((ParamNoteClass, noteClassValue, 1.0));
                }

                await vts.InjectParametersAsync(parameters, ct);

                log?.Invoke(
                    $"Pitch: {state.PitchHz,7:F1} Hz | Note: {state.Note,3} | InKey: {(state.InKey ? "Y" : "N")} | Chord: {state.Chord,-6} | Key: {state.Key,-9} | Energy: {state.Energy:F3}"
                );

                await Task.Delay(50, ct);
            }
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            waveIn.StopRecording();
            log?.Invoke("Stopped.");
        }
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

    private static NoteOutputConfig ParseNoteOutputConfig(string[] args)
    {
        string? mode = null;
        string? prefix = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--note-mode=", StringComparison.OrdinalIgnoreCase))
            {
                mode = arg["--note-mode=".Length..];
                continue;
            }

            if (arg.StartsWith("--note-params-prefix=", StringComparison.OrdinalIgnoreCase))
            {
                prefix = arg["--note-params-prefix=".Length..];
            }
        }

        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = Environment.GetEnvironmentVariable("M2D_NOTE_MODE");
        }

        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = "class";
        }

        var normalizedMode = mode.Trim().ToLowerInvariant();
        var resolvedMode = normalizedMode switch
        {
            "class" => NoteOutputMode.Class,
            "noteclass" => NoteOutputMode.Class,
            "per-note" => NoteOutputMode.PerNote,
            "pernote" => NoteOutputMode.PerNote,
            "split" => NoteOutputMode.PerNote,
            _ => NoteOutputMode.Class
        };

        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = Environment.GetEnvironmentVariable("M2D_NOTE_PARAMS_PREFIX");
        }

        var resolvedPrefix = string.IsNullOrWhiteSpace(prefix) ? DefaultPerNotePrefix : prefix.Trim();

        return new NoteOutputConfig(resolvedMode, resolvedPrefix);
    }

    private static double[] BuildNotePresence(MusicalState state)
    {
        var presence = new double[12];

        if (state.NoteClass >= 0)
        {
            presence[state.NoteClass] = 1.0;
        }

        if (state.ChordRoot < 0 || state.ChordType <= 0)
        {
            return presence;
        }

        foreach (var interval in GetChordIntervals(state.ChordType))
        {
            var noteClass = (state.ChordRoot + interval) % 12;
            presence[noteClass] = 1.0;
        }

        return presence;
    }

    private static int[] GetChordIntervals(int chordType)
    {
        return chordType switch
        {
            1 => [0, 4, 7],
            2 => [0, 3, 7],
            3 => [0, 3, 6],
            4 => [0, 4, 8],
            5 => [0, 5, 7],
            _ => []
        };
    }

    private static IEnumerable<ParameterDefinition> BuildParameterDefinitions(NoteOutputConfig noteOutputConfig)
    {
        yield return new ParameterDefinition(ParamEnergy, 0.0, 1.0, 0.0, "Instrument energy");
        yield return new ParameterDefinition(ParamPitch, 0.0, 1.0, 0.0, "Instrument pitch");
        yield return new ParameterDefinition(ParamInKey, 0.0, 1.0, 0.0, "Note in key");
        yield return new ParameterDefinition(ParamChordRoot, 0.0, 1.0, 0.0, "Chord root");
        yield return new ParameterDefinition(ParamChordType, 0.0, 1.0, 0.0, "Chord type");
        yield return new ParameterDefinition(ParamKeyRoot, 0.0, 1.0, 0.0, "Key root");
        yield return new ParameterDefinition(ParamKeyMode, 0.0, 1.0, 0.0, "Key mode");

        if (noteOutputConfig.Mode == NoteOutputMode.PerNote)
        {
            for (var i = 0; i < 12; i++)
            {
                yield return new ParameterDefinition($"{noteOutputConfig.Prefix}{NoteParamSuffixes[i]}", 0.0, 1.0, 0.0, "Per-note presence");
            }
            yield break;
        }

        yield return new ParameterDefinition(ParamNoteClass, 0.0, 11.0, 0.0, "Detected note class");
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
    private readonly record struct NoteOutputConfig(NoteOutputMode Mode, string Prefix);
    private readonly record struct ParameterDefinition(string Id, double Min, double Max, double DefaultValue, string Explanation);

    private enum NoteOutputMode
    {
        Class,
        PerNote
    }
}
