namespace Music2DBridge.Core;

public sealed record MusicalState(
    double PitchHz,
    double Energy,
    int NoteClass,
    string Note,
    bool InKey,
    string Chord,
    int ChordRoot,
    int ChordType,
    string Key,
    int KeyRoot,
    bool KeyIsMinor
);

public static class MusicAnalyzer
{
    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly int[] MajorTemplate = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] MinorTemplate = [0, 2, 3, 5, 7, 8, 10];
    private static readonly ChordPattern[] ChordPatterns =
    [
        new("maj", [0, 4, 7], 1),
        new("min", [0, 3, 7], 2),
        new("dim", [0, 3, 6], 3),
        new("aug", [0, 4, 8], 4),
        new("sus2", [0, 2, 7], 5),
        new("sus4", [0, 5, 7], 5),
        new("maj7", [0, 4, 7, 11], 1),
        new("7", [0, 4, 7, 10], 1),
        new("min7", [0, 3, 7, 10], 2),
        new("m7b5", [0, 3, 6, 10], 3),
        new("dim7", [0, 3, 6, 9], 3)
    ];

    private const double MinimumRmsForTracking = 0.008;
    private static readonly object Gate = new();
    private static readonly Queue<(DateTime Time, int NoteClass)> NoteHistory = new();

    private static double _smoothedPitchHz;
    private static double _lastRms;
    private static bool _fixedKeyEnabled;
    private static int _fixedKeyRoot;
    private static bool _fixedKeyMinor;

    public static void ConfigureFixedKeyFilter(bool enabled, int rootNoteClass = 0, bool isMinor = false)
    {
        lock (Gate)
        {
            _fixedKeyEnabled = enabled;
            _fixedKeyRoot = NormalizeClass(rootNoteClass);
            _fixedKeyMinor = isMinor;
        }
    }

    public static void ProcessPcm16Mono(byte[] buffer, int bytesRecorded, int sampleRate, double minPitchHz, double maxPitchHz)
    {
        var sampleCount = bytesRecorded / 2;
        if (sampleCount <= 0)
        {
            return;
        }

        var samples = new float[sampleCount];
        double sumSq = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            short s16 = BitConverter.ToInt16(buffer, i * 2);
            float s = s16 / 32768f;
            samples[i] = s;
            sumSq += s * s;
        }

        var rms = Math.Sqrt(sumSq / sampleCount);
        var pitch = EstimatePitchAutocorrelation(samples, sampleRate, minPitchHz, maxPitchHz);

        lock (Gate)
        {
            _lastRms = rms;

            if (pitch > 0)
            {
                _smoothedPitchHz = _smoothedPitchHz <= 0 ? pitch : (0.75 * _smoothedPitchHz) + (0.25 * pitch);
                var noteClass = FrequencyToMidiClass(_smoothedPitchHz);
                var inKey = !_fixedKeyEnabled || IsNoteInKey(noteClass, _fixedKeyRoot, _fixedKeyMinor);
                if (noteClass >= 0 && inKey && rms >= MinimumRmsForTracking)
                {
                    NoteHistory.Enqueue((DateTime.UtcNow, noteClass));
                }
            }

            while (NoteHistory.Count > 0 && (DateTime.UtcNow - NoteHistory.Peek().Time).TotalSeconds > 6)
            {
                NoteHistory.Dequeue();
            }
        }
    }

    public static MusicalState Snapshot()
    {
        lock (Gate)
        {
            var pitch = _smoothedPitchHz;
            var rms = _lastRms;
            var noteClass = FrequencyToMidiClass(pitch);
            var inKey = noteClass >= 0 && (!_fixedKeyEnabled || IsNoteInKey(noteClass, _fixedKeyRoot, _fixedKeyMinor));
            if (!inKey)
            {
                noteClass = -1;
            }

            var chord = DetectChord(NoteHistory, 1.8);
            var key = _fixedKeyEnabled
                ? new KeyDetection(_fixedKeyRoot, _fixedKeyMinor, FormatKey(_fixedKeyRoot, _fixedKeyMinor))
                : DetectKey(NoteHistory, 6.0);

            return new MusicalState(
                pitch,
                rms,
                noteClass,
                FormatNote(pitch),
                inKey,
                chord.Name,
                chord.Root,
                chord.Type,
                key.Name,
                key.Root,
                key.IsMinor
            );
        }
    }

    public static double MapClamp(double x, double inMin, double inMax, double outMin, double outMax)
    {
        if (x <= inMin) return outMin;
        if (x >= inMax) return outMax;
        var t = (x - inMin) / (inMax - inMin);
        return outMin + t * (outMax - outMin);
    }

    private static double EstimatePitchAutocorrelation(float[] samples, int sampleRate, double minHz, double maxHz)
    {
        int minLag = (int)(sampleRate / maxHz);
        int maxLag = (int)(sampleRate / minHz);

        if (maxLag >= samples.Length)
        {
            maxLag = samples.Length - 1;
        }

        double bestCorr = 0;
        int bestLag = -1;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double corr = 0;
            double normA = 0;
            double normB = 0;

            int limit = samples.Length - lag;
            for (int i = 0; i < limit; i++)
            {
                double a = samples[i];
                double b = samples[i + lag];
                corr += a * b;
                normA += a * a;
                normB += b * b;
            }

            if (normA <= 1e-12 || normB <= 1e-12)
            {
                continue;
            }

            double normalized = corr / Math.Sqrt(normA * normB);
            if (normalized > bestCorr)
            {
                bestCorr = normalized;
                bestLag = lag;
            }
        }

        if (bestLag < 0 || bestCorr < 0.25)
        {
            return 0;
        }

        return sampleRate / (double)bestLag;
    }

    private static int FrequencyToMidiClass(double freq)
    {
        if (freq <= 0)
        {
            return -1;
        }

        int midi = (int)Math.Round(69 + 12 * Math.Log2(freq / 440.0));
        return ((midi % 12) + 12) % 12;
    }

    private static string FormatNote(double freq)
    {
        if (freq <= 0)
        {
            return "--";
        }

        int midi = (int)Math.Round(69 + 12 * Math.Log2(freq / 440.0));
        int note = ((midi % 12) + 12) % 12;
        int octave = (midi / 12) - 1;
        return NoteNames[note] + octave;
    }

    private static ChordDetection DetectChord(IEnumerable<(DateTime Time, int NoteClass)> history, double secWindow)
    {
        var now = DateTime.UtcNow;
        var window = history.Where(x => (now - x.Time).TotalSeconds <= secWindow).ToList();
        if (window.Count < 8)
        {
            return ChordDetection.None;
        }

        var counts = new int[12];
        foreach (var (_, n) in window)
        {
            counts[n]++;
        }

        double bestScore = 0;
        int bestRoot = -1;
        var bestPattern = default(ChordPattern);
        var hasBestPattern = false;

        for (int root = 0; root < 12; root++)
        {
            foreach (var pattern in ChordPatterns)
            {
                var score = ScorePattern(counts, root, pattern);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRoot = root;
                    bestPattern = pattern;
                    hasBestPattern = true;
                }
            }
        }

        if (!hasBestPattern || bestRoot < 0 || bestScore < 0.58)
        {
            return ChordDetection.None;
        }

        return new ChordDetection(bestRoot, bestPattern.FamilyType, NoteNames[bestRoot] + bestPattern.Suffix);
    }

    private static double ScorePattern(int[] counts, int root, ChordPattern pattern)
    {
        double inPattern = 0;
        double total = counts.Sum();
        if (total <= 0)
        {
            return 0;
        }

        for (int i = 0; i < pattern.Intervals.Length; i++)
        {
            var note = (root + pattern.Intervals[i]) % 12;
            var weight = i == 0 ? 1.4 : (i == 1 ? 1.1 : 1.0);
            inPattern += counts[note] * weight;
        }

        double outOfPattern = 0;
        for (int pc = 0; pc < 12; pc++)
        {
            bool matched = false;
            for (int i = 0; i < pattern.Intervals.Length; i++)
            {
                if (((root + pattern.Intervals[i]) % 12) == pc)
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                outOfPattern += counts[pc];
            }
        }

        var normalizedIn = inPattern / (total * 1.4);
        var penalty = (outOfPattern / total) * 0.55;
        return normalizedIn - penalty;
    }

    private static KeyDetection DetectKey(IEnumerable<(DateTime Time, int NoteClass)> history, double secWindow)
    {
        var now = DateTime.UtcNow;
        var window = history.Where(x => (now - x.Time).TotalSeconds <= secWindow).ToList();
        if (window.Count < 12)
        {
            return KeyDetection.Unknown;
        }

        var counts = new double[12];
        foreach (var (_, n) in window)
        {
            counts[n]++;
        }

        double bestScore = double.MinValue;
        int bestRoot = 0;
        bool bestMinor = false;

        for (int root = 0; root < 12; root++)
        {
            double maj = 0;
            double min = 0;

            foreach (var p in MajorTemplate) maj += counts[(root + p) % 12];
            foreach (var p in MinorTemplate) min += counts[(root + p) % 12];

            if (maj > bestScore)
            {
                bestScore = maj;
                bestRoot = root;
                bestMinor = false;
            }

            if (min > bestScore)
            {
                bestScore = min;
                bestRoot = root;
                bestMinor = true;
            }
        }

        return new KeyDetection(bestRoot, bestMinor, FormatKey(bestRoot, bestMinor));
    }

    private static bool IsNoteInKey(int noteClass, int keyRoot, bool keyIsMinor)
    {
        if (noteClass < 0)
        {
            return false;
        }

        var templates = keyIsMinor ? MinorTemplate : MajorTemplate;
        foreach (var interval in templates)
        {
            if (((keyRoot + interval) % 12) == noteClass)
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatKey(int root, bool isMinor)
    {
        return NoteNames[NormalizeClass(root)] + (isMinor ? " minor" : " major");
    }

    private static int NormalizeClass(int noteClass)
    {
        return ((noteClass % 12) + 12) % 12;
    }

    private readonly record struct ChordDetection(int Root, int Type, string Name)
    {
        public static ChordDetection None => new(-1, 0, "--");
    }

    private readonly record struct ChordPattern(string Suffix, int[] Intervals, int FamilyType);

    private readonly record struct KeyDetection(int Root, bool IsMinor, string Name)
    {
        public static KeyDetection Unknown => new(-1, false, "--");
    }
}
