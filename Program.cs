using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

internal sealed class Program
{
    private const int SampleRate = 44100;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    private const double MinPitchHz = 80.0;
    private const double MaxPitchHz = 1000.0;

    private const string PluginName = "Music2D Instrument Tracker";
    private const string PluginDeveloper = "Local";
    private const string ParamPitch = "ParamInstPitch";
    private const string ParamEnergy = "ParamInstEnergy";
    private const string ParamNoteClass = "ParamInstNoteClass";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private static readonly object StateLock = new();
    private static double _latestPitchHz;
    private static double _smoothedPitchHz;
    private static double _lastRms;
    private static readonly Queue<(DateTime Time, int NoteClass)> NoteHistory = new();

    private static async Task Main()
    {
        Console.WriteLine("Starting VTS Audio Bridge...");

        using var ws = new ClientWebSocket();
        var uri = new Uri("ws://127.0.0.1:8001");
        await ws.ConnectAsync(uri, CancellationToken.None);
        Console.WriteLine("Connected to VTube Studio WebSocket.");

        var token = await RequestAuthTokenAsync(ws);
        await AuthenticateAsync(ws, token);
        Console.WriteLine("Authenticated.");

        using var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            BufferMilliseconds = 30,
            NumberOfBuffers = 3,
            DeviceNumber = 0
        };

        waveIn.DataAvailable += (_, e) => ProcessAudio(e.Buffer, e.BytesRecorded);
        waveIn.StartRecording();
        Console.WriteLine("Mic capture started. Press Ctrl+C to stop.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var (pitch, rms, note, chord, key) = GetMusicalState();

                var mappedPitch = MapClamp(pitch, 100.0, 500.0, 0.0, 1.0);
                var mappedEnergy = MapClamp(rms, 0.01, 0.20, 0.0, 1.0);
                var mappedNoteClass = note >= 0 ? note / 11.0 : 0.0;

                await InjectParametersAsync(ws, mappedPitch, mappedEnergy, mappedNoteClass);

                Console.WriteLine($"Pitch: {pitch,7:F1} Hz | Note: {FormatNote(pitch),3} | Chord: {chord,-6} | Key: {key,-9} | Energy: {rms:F3}");

                await Task.Delay(50, cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
        }
        finally
        {
            waveIn.StopRecording();
            ws.Abort();
        }

        Console.WriteLine("Stopped.");
    }

    private static void ProcessAudio(byte[] buffer, int bytesRecorded)
    {
        var sampleCount = bytesRecorded / 2;
        if (sampleCount <= 0)
        {
            return;
        }

        var samples = new float[sampleCount];
        double sumSq = 0.0;

        for (var i = 0; i < sampleCount; i++)
        {
            short sample16 = BitConverter.ToInt16(buffer, i * 2);
            float s = sample16 / 32768f;
            samples[i] = s;
            sumSq += s * s;
        }

        var rms = Math.Sqrt(sumSq / sampleCount);
        var pitch = EstimatePitchAutocorrelation(samples, SampleRate, MinPitchHz, MaxPitchHz);

        lock (StateLock)
        {
            _lastRms = rms;

            if (pitch > 0)
            {
                _latestPitchHz = pitch;
                _smoothedPitchHz = _smoothedPitchHz <= 0 ? pitch : (0.75 * _smoothedPitchHz) + (0.25 * pitch);

                var noteClass = FrequencyToMidiClass(_smoothedPitchHz);
                if (noteClass >= 0)
                {
                    NoteHistory.Enqueue((DateTime.UtcNow, noteClass));
                }
            }

            while (NoteHistory.Count > 0 && (DateTime.UtcNow - NoteHistory.Peek().Time).TotalSeconds > 6.0)
            {
                NoteHistory.Dequeue();
            }
        }
    }

    private static (double pitch, double rms, int noteClass, string chord, string key) GetMusicalState()
    {
        lock (StateLock)
        {
            var pitch = _smoothedPitchHz;
            var rms = _lastRms;
            var noteClass = FrequencyToMidiClass(pitch);
            var chord = DetectChord(NoteHistory, 1.8);
            var key = DetectKey(NoteHistory, 6.0);
            return (pitch, rms, noteClass, chord, key);
        }
    }

    private static async Task<string> RequestAuthTokenAsync(ClientWebSocket ws)
    {
        var req = Envelope("AuthenticationTokenRequest", new
        {
            pluginName = PluginName,
            pluginDeveloper = PluginDeveloper,
            pluginIcon = ""
        });

        await SendJsonAsync(ws, req);
        using var doc = await ReceiveJsonAsync(ws);
        var token = doc.RootElement.GetProperty("data").GetProperty("authenticationToken").GetString();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Failed to receive authentication token from VTube Studio.");
        }

        return token;
    }

    private static async Task AuthenticateAsync(ClientWebSocket ws, string token)
    {
        var req = Envelope("AuthenticationRequest", new
        {
            pluginName = PluginName,
            pluginDeveloper = PluginDeveloper,
            authenticationToken = token
        });

        await SendJsonAsync(ws, req);
        using var doc = await ReceiveJsonAsync(ws);
        var ok = doc.RootElement.GetProperty("data").GetProperty("authenticated").GetBoolean();
        if (!ok)
        {
            throw new InvalidOperationException("Authentication rejected in VTube Studio.");
        }
    }

    private static async Task InjectParametersAsync(ClientWebSocket ws, double pitch01, double energy01, double noteClass01)
    {
        var req = Envelope("InjectParameterDataRequest", new
        {
            faceFound = true,
            mode = "set",
            parameterValues = new object[]
            {
                new { id = ParamEnergy, value = energy01, weight = 1.0 },
                new { id = ParamPitch, value = pitch01, weight = 1.0 },
                new { id = ParamNoteClass, value = noteClass01, weight = 1.0 }
            }
        });

        await SendJsonAsync(ws, req);

        // VTS sends a response for each request; receive and discard to keep socket clean.
        using var _ = await ReceiveJsonAsync(ws);
    }

    private static object Envelope(string messageType, object data) => new
    {
        apiName = "VTubeStudioPublicAPI",
        apiVersion = "1.0",
        requestID = Guid.NewGuid().ToString("N"),
        messageType,
        data
    };

    private static async Task SendJsonAsync(ClientWebSocket ws, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket ws)
    {
        var buf = new byte[32 * 1024];
        using var ms = new System.IO.MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await ws.ReceiveAsync(buf, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("WebSocket closed by server.");
            }

            ms.Write(buf, 0, result.Count);
        }
        while (!result.EndOfMessage);

        ms.Position = 0;
        return await JsonDocument.ParseAsync(ms);
    }

    private static double EstimatePitchAutocorrelation(float[] samples, int sampleRate, double minHz, double maxHz)
    {
        int minLag = (int)(sampleRate / maxHz);
        int maxLag = (int)(sampleRate / minHz);

        if (maxLag >= samples.Length)
        {
            maxLag = samples.Length - 1;
        }

        double bestCorr = 0.0;
        int bestLag = -1;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double corr = 0.0;
            double normA = 0.0;
            double normB = 0.0;

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
            return 0.0;
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

        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
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

        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        if (hasMajor3 && has5)
        {
            return names[root] + "maj";
        }

        if (hasMinor3 && has5)
        {
            return names[root] + "min";
        }

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

        int[] majorTemplate = { 0, 2, 4, 5, 7, 9, 11 };
        int[] minorTemplate = { 0, 2, 3, 5, 7, 8, 10 };

        double bestScore = double.MinValue;
        int bestRoot = 0;
        bool bestMinor = false;

        for (int root = 0; root < 12; root++)
        {
            double maj = 0;
            double min = 0;
            foreach (var p in majorTemplate)
            {
                maj += counts[(root + p) % 12];
            }
            foreach (var p in minorTemplate)
            {
                min += counts[(root + p) % 12];
            }

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

        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return names[bestRoot] + (bestMinor ? " minor" : " major");
    }

    private static double MapClamp(double x, double inMin, double inMax, double outMin, double outMax)
    {
        if (x <= inMin)
        {
            return outMin;
        }

        if (x >= inMax)
        {
            return outMax;
        }

        var t = (x - inMin) / (inMax - inMin);
        return outMin + t * (outMax - outMin);
    }
}
