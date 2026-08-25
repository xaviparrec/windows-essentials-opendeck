using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Media.Control;

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
            using var endpoints = new AudioEndpoints();
            Console.WriteLine(JsonSerializer.Serialize(Execute(endpoints, args)));
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
            using var endpoints = new AudioEndpoints();
            string? line;
            while ((line = Console.ReadLine()) is not null)
            {
                try
                {
                    Console.WriteLine(JsonSerializer.Serialize(Execute(endpoints, line.Split(' ', StringSplitOptions.RemoveEmptyEntries))));
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

    private static object Execute(AudioEndpoints endpoints, string[] args)
    {
        var endpoint = endpoints.Master;
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
            case "media-state":
                return ReadMediaPlaybackState();
            case "media-toggle":
                var mediaBefore = ReadMediaPlaybackState();
                SendMediaKey(0xB3, 1);
                return WaitForMediaUpdate(mediaBefore);
            case "toggle-mute":
                endpoint.ToggleMute();
                break;
            case "mic-get":
                return endpoints.Microphone.Read();
            case "mic-adjust" when int.TryParse(args.ElementAtOrDefault(1), out var microphoneTicks):
                endpoints.Microphone.Adjust(microphoneTicks * 0.02f);
                return endpoints.Microphone.Read();
            case "mic-toggle-mute":
                endpoints.Microphone.ToggleMute();
                return endpoints.Microphone.Read();
            case "list-outputs":
                return ListOutputs();
            case "get-default-output":
                return GetDefaultOutput();
            case "set-output" when !string.IsNullOrWhiteSpace(args.ElementAtOrDefault(1)):
                SetDefaultOutput(args[1]);
                return GetDefaultOutput();
            case "cycle-output" when int.TryParse(args.ElementAtOrDefault(1), out var outputTicks):
                return CycleOutput(outputTicks);
            default:
                throw new ArgumentException("Use: get | media <up|down|mute> [count] | adjust <signed ticks> | toggle-mute | mic-get | mic-adjust <signed ticks> | mic-toggle-mute | list-outputs | get-default-output | set-output <device id> | cycle-output <signed ticks>");
        }
        return endpoint.Read();
    }

    private static MediaPlaybackState ReadMediaPlaybackState()
    {
        var manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
        var status = manager.GetCurrentSession()?.GetPlaybackInfo()?.PlaybackStatus;
        return new MediaPlaybackState(status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, status?.ToString() ?? "NoSession");
    }

    private static MediaPlaybackState WaitForMediaUpdate(MediaPlaybackState before)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            Thread.Sleep(10);
            var current = ReadMediaPlaybackState();
            if (current.isPlaying != before.isPlaying)
            {
                return current;
            }
        }
        return ReadMediaPlaybackState();
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

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);

    private static List<AudioOutput> ListOutputs()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1, out var devices));
            try
            {
                Marshal.ThrowExceptionForHR(devices.GetCount(out var count));
                var outputs = new List<AudioOutput>();
                for (uint index = 0; index < count; index++)
                {
                    Marshal.ThrowExceptionForHR(devices.Item(index, out var device));
                    try
                    {
                        Marshal.ThrowExceptionForHR(device.GetId(out var id));
                        outputs.Add(new AudioOutput(id, ReadFriendlyName(device)));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
                return outputs;
            }
            finally
            {
                Marshal.ReleaseComObject(devices);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static AudioOutput GetDefaultOutput()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device));
            try
            {
                Marshal.ThrowExceptionForHR(device.GetId(out var id));
                return new AudioOutput(id, ReadFriendlyName(device));
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static AudioOutput CycleOutput(int ticks)
    {
        var outputs = ListOutputs();
        if (outputs.Count == 0)
        {
            throw new InvalidOperationException("No active audio output is available.");
        }
        var currentId = GetDefaultOutput().id;
        var currentIndex = outputs.FindIndex(output => output.id == currentId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }
        var nextIndex = (currentIndex + (ticks % outputs.Count) + outputs.Count) % outputs.Count;
        var next = outputs[nextIndex];
        SetDefaultOutput(next.id);
        return next;
    }

    private static string ReadFriendlyName(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out var store));
        try
        {
            var key = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try
            {
                return value.Value ?? "Unknown output";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static void SetDefaultOutput(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            foreach (var role in Enum.GetValues<ERole>())
            {
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
            }
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    private sealed class AudioEndpoints : IDisposable
    {
        private EndpointVolume? microphone;

        public AudioEndpoints()
        {
            this.Master = new EndpointVolume(EDataFlow.eRender, ERole.eMultimedia);
        }

        public EndpointVolume Master { get; }
        public EndpointVolume Microphone => this.microphone ??= new EndpointVolume(EDataFlow.eCapture, ERole.eCommunications, ERole.eMultimedia);

        public void Dispose()
        {
            this.microphone?.Dispose();
            this.Master.Dispose();
        }
    }

    private sealed class EndpointVolume : IDisposable
    {
        private readonly IMMDevice device;
        private readonly IAudioEndpointVolume volume;

        public EndpointVolume(EDataFlow dataFlow, params ERole[] roles)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                IMMDevice? selectedDevice = null;
                var result = -1;
                foreach (var role in roles)
                {
                    result = enumerator.GetDefaultAudioEndpoint(dataFlow, role, out selectedDevice);
                    if (result >= 0)
                    {
                        break;
                    }
                }
                if (result < 0 && dataFlow == EDataFlow.eCapture)
                {
                    result = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, 1, out var devices);
                    if (result >= 0)
                    {
                        try
                        {
                            Marshal.ThrowExceptionForHR(devices.GetCount(out var count));
                            if (count > 0)
                            {
                                result = devices.Item(0, out selectedDevice);
                            }
                            else
                            {
                                result = unchecked((int)0x80070490);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(devices);
                        }
                    }
                }
                Marshal.ThrowExceptionForHR(result);
                this.device = selectedDevice!;
                var audioEndpointVolumeGuid = AudioEndpointVolumeGuid;
                Marshal.ThrowExceptionForHR(this.device.Activate(ref audioEndpointVolumeGuid, 23, IntPtr.Zero, out this.volume));
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
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
    private sealed record MediaPlaybackState(bool isPlaying, string status);
    private sealed record AudioOutput(string id, string name);

    private enum EDataFlow { eRender, eCapture, eAll }
    private enum ERole { eConsole, eMultimedia, eCommunications }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public PropertyKey(Guid formatId, uint propertyId) { this.FormatId = formatId; this.PropertyId = propertyId; }
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort valueType;
        [FieldOffset(8)] private IntPtr pointerValue;
        public string? Value => this.valueType == 31 && this.pointerValue != IntPtr.Zero ? Marshal.PtrToStringUni(this.pointerValue) : null;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject;

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        int GetCount(out uint count);
        int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid interfaceId, int classContext, IntPtr activationParameters, out IAudioEndpointVolume endpointVolume);
        int OpenPropertyStore(int storageAccessMode, out IPropertyStore properties);
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

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint count);
        int GetAt(uint index, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient;

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat();
        int GetDeviceFormat();
        int ResetDeviceFormat();
        int SetDeviceFormat();
        int GetProcessingPeriod();
        int SetProcessingPeriod();
        int GetShareMode();
        int SetShareMode();
        int GetPropertyValue();
        int SetPropertyValue();
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        int SetEndpointVisibility();
    }
}
