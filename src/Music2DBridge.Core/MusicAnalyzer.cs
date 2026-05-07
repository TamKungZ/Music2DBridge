namespace Music2DBridge.Core;

public sealed record MusicalState(
    double PitchHz,
    double Energy,
    int NoteClass,
    string Note,
    string Chord,
    string Key
);

public static class MusicAnalyzer
{
    private static readonly object Gate = new();
    private static readonly Queue<(DateTime Time, int NoteClass)> NoteHistory = new();

    private static double _smoothedPitchHz;
    private static double _lastRms;

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
                if (noteClass >= 0)
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

            return new MusicalState(
                pitch,
                rms,
                noteClass,
                FormatNote(pitch),
                DetectChord(NoteHistory, 1.8),
                DetectKey(NoteHistory, 6.0)
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

        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        int midi = (int)Math.Round(69 + 12 * Math.Log2(freq / 440.0));
        int note = ((midi % 12) + 12) % 12;
        int octave = (midi / 12) - 1;
        return names[note] + octave;
    }

    private static string DetectChord(IEnumerable<(DateTime Time, int NoteClass)> history, double secWindow)
    {
        var now = DateTime.UtcNow;
        var window = history.Where(x => (now - x.Time).TotalSeconds <= secWindow).ToList();
        if (window.Count < 6)
        {
            return "--";
        }

        var counts = new int[12];
        foreach (var (_, n) in window)
        {
            counts[n]++;
        }

        int root = Array.IndexOf(counts, counts.Max());
        bool hasMinor3 = counts[(root + 3) % 12] > 0;
        bool hasMajor3 = counts[(root + 4) % 12] > 0;
        bool has5 = counts[(root + 7) % 12] > 0;

        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

        if (hasMajor3 && has5) return names[root] + "maj";
        if (hasMinor3 && has5) return names[root] + "min";
        return "--";
    }

    private static string DetectKey(IEnumerable<(DateTime Time, int NoteClass)> history, double secWindow)
    {
        var now = DateTime.UtcNow;
        var window = history.Where(x => (now - x.Time).TotalSeconds <= secWindow).ToList();
        if (window.Count < 12)
        {
            return "--";
        }

        var counts = new double[12];
        foreach (var (_, n) in window)
        {
            counts[n]++;
        }

        int[] majorTemplate = [0, 2, 4, 5, 7, 9, 11];
        int[] minorTemplate = [0, 2, 3, 5, 7, 8, 10];

        double bestScore = double.MinValue;
        int bestRoot = 0;
        bool bestMinor = false;

        for (int root = 0; root < 12; root++)
        {
            double maj = 0;
            double min = 0;

            foreach (var p in majorTemplate) maj += counts[(root + p) % 12];
            foreach (var p in minorTemplate) min += counts[(root + p) % 12];

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

        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return names[bestRoot] + (bestMinor ? " minor" : " major");
    }
}
