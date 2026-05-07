using NAudio.Wave;

namespace Music2DBridge.App;

internal sealed class NAudioMicCapture : IMicCapture
{
    private readonly WaveInEvent _waveIn;

    public NAudioMicCapture(int sampleRate, int channels, int bitsPerSample, int chunkMilliseconds)
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(sampleRate, bitsPerSample, channels),
            BufferMilliseconds = chunkMilliseconds,
            NumberOfBuffers = 3,
            DeviceNumber = 0
        };

        _waveIn.DataAvailable += (_, e) => DataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
    }

    public event Action<byte[], int>? DataAvailable;

    public void Start() => _waveIn.StartRecording();

    public void Stop() => _waveIn.StopRecording();

    public void Dispose() => _waveIn.Dispose();
}
