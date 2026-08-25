using System.Runtime.InteropServices;
using System.Text.Json;

namespace WindowsEssentials.AudioHelper;

internal static class Program
{
    private static readonly Guid AudioEndpointVolumeGuid = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "serve")
        {
            return Serve();
        }

        try
        {
            using var endpoint = new EndpointVolume();
            Console.WriteLine(JsonSerializer.Serialize(Execute(endpoint, args)));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Serve()
    {
        try
        {
            using var endpoint = new EndpointVolume();
            string? line;
            while ((line = Console.ReadLine()) is not null)
            {
                try
                {
                    Console.WriteLine(JsonSerializer.Serialize(Execute(endpoint, line.Split(' ', StringSplitOptions.RemoveEmptyEntries))));
                }
                catch (Exception exception)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { error = exception.Message }));
                }
                Console.Out.Flush();
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static VolumeState Execute(EndpointVolume endpoint, string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "get":
                break;
            case "adjust" when int.TryParse(args.ElementAtOrDefault(1), out var ticks):
                endpoint.Adjust(ticks * 0.02f);
                break;
            case "media" when TryGetMediaKey(args.ElementAtOrDefault(1), out var key):
                var before = endpoint.Read();
                SendMediaKey(key, Math.Max(1, int.TryParse(args.ElementAtOrDefault(2), out var count) ? count : 1));
                return WaitForWindowsAudioUpdate(endpoint, before);
            case "key" when TryGetMediaKey(args.ElementAtOrDefault(1), out var mediaKey):
                SendMediaKey(mediaKey, 1);
                break;
            case "toggle-mute":
                endpoint.ToggleMute();
                break;
            default:
                throw new ArgumentException("Use: get | media <up|down|mute> [count] | adjust <signed ticks> | toggle-mute");
        }
        return endpoint.Read();
    }

    private static VolumeState WaitForWindowsAudioUpdate(EndpointVolume endpoint, VolumeState before)
    {
        // keybd_event queues the multimedia key, while Core Audio is updated a few
        // milliseconds later. Polling the endpoint avoids displaying the prior tick.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            Thread.Sleep(2);
            var current = endpoint.Read();
            if (current != before)
            {
                return current;
            }
        }
        return endpoint.Read(); // At 0%/100%, a volume key may correctly make no change.
    }

    private static bool TryGetMediaKey(string? name, out byte key)
    {
        key = name switch {
            "up" => 0xAF,
            "down" => 0xAE,
            "mute" => 0xAD,
            "previous" => 0xB1,
            "next" => 0xB0,
            "play-pause" => 0xB3,
            _ => (byte)0
        };
        return key != 0;
    }

    private static void SendMediaKey(byte key, int count)
    {
        for (var index = 0; index < count; index++)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0x0002, UIntPtr.Zero);
        }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    private sealed class EndpointVolume : IDisposable
    {
        private readonly IMMDevice device;
        private readonly IAudioEndpointVolume volume;

        public EndpointVolume()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out this.device));
            var audioEndpointVolumeGuid = AudioEndpointVolumeGuid;
            Marshal.ThrowExceptionForHR(this.device.Activate(ref audioEndpointVolumeGuid, 23, IntPtr.Zero, out this.volume));
            Marshal.ReleaseComObject(enumerator);
        }

        public VolumeState Read()
        {
            Marshal.ThrowExceptionForHR(this.volume.GetMasterVolumeLevelScalar(out var scalar));
            Marshal.ThrowExceptionForHR(this.volume.GetMute(out var muted));
            return new VolumeState((int)Math.Round(scalar * 100), muted);
        }

        public void Adjust(float delta)
        {
            Marshal.ThrowExceptionForHR(this.volume.GetMasterVolumeLevelScalar(out var scalar));
            var next = Math.Clamp(scalar + delta, 0f, 1f);
            Marshal.ThrowExceptionForHR(this.volume.SetMasterVolumeLevelScalar(next, Guid.Empty));
        }

        public void ToggleMute()
        {
            Marshal.ThrowExceptionForHR(this.volume.GetMute(out var muted));
            Marshal.ThrowExceptionForHR(this.volume.SetMute(!muted, Guid.Empty));
        }

        public void Dispose()
        {
            Marshal.ReleaseComObject(this.volume);
            Marshal.ReleaseComObject(this.device);
        }
    }

    private sealed record VolumeState(int level, bool muted);

    private enum EDataFlow { eRender, eCapture, eAll }
    private enum ERole { eConsole, eMultimedia, eCommunications }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject;

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid interfaceId, int classContext, IntPtr activationParameters, out IAudioEndpointVolume endpointVolume);
        int OpenPropertyStore(int storageAccessMode, out IntPtr properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr callback);
        int UnregisterControlChangeNotify(IntPtr callback);
        int GetChannelCount(out uint channelCount);
        int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, Guid eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        int GetVolumeStepInfo(out uint step, out uint stepCount);
        int VolumeStepUp(Guid eventContext);
        int VolumeStepDown(Guid eventContext);
        int QueryHardwareSupport(out uint hardwareSupportMask);
        int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }
}
