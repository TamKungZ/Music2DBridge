using NAudio.CoreAudioApi;

namespace Music2DBridge.App;

internal static class AudioInputDiscovery
{
    public const string DefaultDeviceId = "default";

    public static IReadOnlyList<AudioInputDevice> ListCaptureDevices()
    {
        var devices = new List<AudioInputDevice>
        {
            new(DefaultDeviceId, "System Default")
        };

        if (!OperatingSystem.IsWindows())
        {
            return devices;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                devices.Add(new(device.ID, device.FriendlyName));
            }
        }
        catch
        {
        }

        return devices;
    }
}
