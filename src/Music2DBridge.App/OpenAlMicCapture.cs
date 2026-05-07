using OpenTK.Audio.OpenAL;

namespace Music2DBridge.App;

internal sealed class OpenAlMicCapture : IMicCapture
{
    private readonly int _chunkSamples;
    private readonly short[] _sampleBuffer;
    private readonly byte[] _byteBuffer;
    private ALCaptureDevice _device;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public OpenAlMicCapture(int sampleRate, int chunkMilliseconds)
    {
        _chunkSamples = Math.Max(1, sampleRate * chunkMilliseconds / 1000);
        _sampleBuffer = new short[_chunkSamples];
        _byteBuffer = new byte[_chunkSamples * sizeof(short)];

        _device = ALC.CaptureOpenDevice(null, sampleRate, ALFormat.Mono16, sampleRate);
        if (_device == default)
        {
            throw new InvalidOperationException("Failed to open microphone capture device (OpenAL).");
        }
    }

    public event Action<byte[], int>? DataAvailable;

    public void Start()
    {
        ALC.CaptureStart(_device);
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => CaptureLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loopTask?.Wait(1000);
        }
        catch
        {
        }

        ALC.CaptureStop(_device);
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    private async Task CaptureLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var capturedSamples = ALC.GetInteger(_device, AlcGetInteger.CaptureSamples);

            if (capturedSamples >= _chunkSamples)
            {
                ALC.CaptureSamples(_device, ref _sampleBuffer[0], _chunkSamples);
                Buffer.BlockCopy(_sampleBuffer, 0, _byteBuffer, 0, _byteBuffer.Length);
                DataAvailable?.Invoke(_byteBuffer, _byteBuffer.Length);
            }

            await Task.Delay(10, ct);
        }
    }

    public void Dispose()
    {
        Stop();

        if (_device != default)
        {
            ALC.CaptureCloseDevice(_device);
            _device = default;
        }
    }
}
