namespace Music2DBridge.App;

internal interface IMicCapture : IDisposable
{
    event Action<byte[], int>? DataAvailable;
    void Start();
    void Stop();
}
