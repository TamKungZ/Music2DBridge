using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Music2DBridge.App;

internal sealed class WasapiMicCapture : IMicCapture
{
    private readonly WasapiCapture _capture;

    public WasapiMicCapture(int sampleRate, int channels, int bitsPerSample, int chunkMilliseconds, string? deviceId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WASAPI capture is only available on Windows.");
        }

        using var enumerator = new MMDeviceEnumerator();
        var captureDevice = ResolveCaptureDevice(enumerator, deviceId);

        _capture = new WasapiCapture(captureDevice, false, chunkMilliseconds)
        {
            WaveFormat = new WaveFormat(sampleRate, bitsPerSample, channels)
        };

        _capture.DataAvailable += (_, e) => DataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
    }

    public event Action<byte[], int>? DataAvailable;

    public void Start() => _capture.StartRecording();

    public void Stop() => _capture.StopRecording();

    public void Dispose() => _capture.Dispose();

    private static MMDevice ResolveCaptureDevice(MMDeviceEnumerator enumerator, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) ||
            deviceId.Equals(AudioInputDiscovery.DefaultDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }

        try
        {
            return enumerator.GetDevice(deviceId);
        }
        catch
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }
    }
}
