using Music2DBridge.Core;
using Music2DBridge.VTubeStudio;
using NAudio.Wave;

internal sealed class Program
{
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

    private static readonly string AppConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TamKungZ_",
        "Music2DBridge"
    );

    private static readonly string TokenPath = Path.Combine(AppConfigDirectory, "vts-token.txt");

    private static async Task Main()
    {
        Console.WriteLine("Starting Music2DBridge...");

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

                await vts.InjectParametersAsync(
                    [
                        (ParamEnergy, energy01, 1.0),
                        (ParamPitch, pitch01, 1.0),
                        (ParamNoteClass, noteClass01, 1.0)
                    ],
                    cts.Token
                );

                Console.WriteLine(
                    $"Pitch: {state.PitchHz,7:F1} Hz | Note: {state.Note,3} | Chord: {state.Chord,-6} | Key: {state.Key,-9} | Energy: {state.Energy:F3}"
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
}
