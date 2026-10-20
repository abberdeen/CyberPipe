using System.Runtime.InteropServices;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Host.Media;

/// <summary>
/// Реализация контроллера медиа и громкости для Windows через Win32 API и Windows CoreAudio COM (TASK-050).
/// При запуске на не-Windows возвращает HostSkillResult.NotSupported.
/// </summary>
public sealed class WindowsMediaController : IHostMediaController
{
    private static readonly Guid EndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private int _fallbackVolume = 50;
    private bool _fallbackIsMuted;
    private bool _isPlaying;

    /// <inheritdoc />
    public Task<HostSkillResult> PlayAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HostSkillResult.NotSupported("Host media controller is only supported on Windows."));
        }

        try
        {
            _isPlaying = true;
            SendVirtualKey(VK_MEDIA_PLAY_PAUSE);
            return Task.FromResult(HostSkillResult.Ok("{\"action\":\"play\",\"isPlaying\":true}", "Playback started or resumed."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HostSkillResult.Failed($"Failed to trigger media playback: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<HostSkillResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HostSkillResult.NotSupported("Host media controller is only supported on Windows."));
        }

        try
        {
            _isPlaying = false;
            SendVirtualKey(VK_MEDIA_PLAY_PAUSE);
            return Task.FromResult(HostSkillResult.Ok("{\"action\":\"pause\",\"isPlaying\":false}", "Playback paused."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HostSkillResult.Failed($"Failed to pause media playback: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<HostSkillResult> PlayPauseAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HostSkillResult.NotSupported("Host media controller is only supported on Windows."));
        }

        try
        {
            _isPlaying = !_isPlaying;
            SendVirtualKey(VK_MEDIA_PLAY_PAUSE);
            return Task.FromResult(HostSkillResult.Ok($"{{\"action\":\"playpause\",\"isPlaying\":{(_isPlaying ? "true" : "false")}}}", "Toggled playback state."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HostSkillResult.Failed($"Failed to toggle media playback: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<HostSkillResult> NextTrackAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HostSkillResult.NotSupported("Host media controller is only supported on Windows."));
        }

        try
        {
            SendVirtualKey(VK_MEDIA_NEXT_TRACK);
            return Task.FromResult(HostSkillResult.Ok("{\"action\":\"next\"}", "Skipped to next track."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HostSkillResult.Failed($"Failed to skip to next track: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<HostSkillResult> PreviousTrackAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HostSkillResult.NotSupported("Host media controller is only supported on Windows."));
        }

        try
        {
            SendVirtualKey(VK_MEDIA_PREV_TRACK);
            return Task.FromResult(HostSkillResult.Ok("{\"action\":\"previous\"}", "Returned to previous track."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HostSkillResult.Failed($"Failed to return to previous track: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<int> GetVolumeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(_fallbackVolume);
        }

        var endpoint = TryGetAudioEndpointVolume();
        if (endpoint is not null)
        {
            try
            {
                int hr = endpoint.GetMasterVolumeLevelScalar(out float level);
                if (hr == 0)
                {
                    int percent = (int)Math.Round(level * 100f);
                    _fallbackVolume = percent;
                    return Task.FromResult(percent);
                }
            }
            catch
            {
                // Fall back to stored state if COM communication fails
            }
        }

        return Task.FromResult(_fallbackVolume);
    }

    /// <inheritdoc />
    public Task<HostSkillResult> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HostSkillResult.NotSupported("Host media controller is only supported on Windows."));
        }

        if (volumePercent is < 0 or > 100)
        {
            return Task.FromResult(HostSkillResult.ValidationError($"Volume percent must be between 0 and 100. Received: {volumePercent}."));
        }

        try
        {
            _fallbackVolume = volumePercent;
            var endpoint = TryGetAudioEndpointVolume();
            if (endpoint is not null)
            {
                float scalar = volumePercent / 100f;
                Guid context = Guid.Empty;
                endpoint.SetMasterVolumeLevelScalar(scalar, ref context);
            }

            return Task.FromResult(HostSkillResult.Ok($"{{\"volume\":{volumePercent}}}", $"Volume set to {volumePercent}%."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HostSkillResult.Failed($"Failed to set master volume: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<HostSkillResult> VolumeUpAsync(int step = 5, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return HostSkillResult.NotSupported("Host media controller is only supported on Windows.");
        }

        if (step is <= 0 or > 100)
        {
            return HostSkillResult.ValidationError($"Volume step must be between 1 and 100. Received: {step}.");
        }

        int current = await GetVolumeAsync(cancellationToken);
        int target = Math.Min(100, current + step);
        return await SetVolumeAsync(target, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<HostSkillResult> VolumeDownAsync(int step = 5, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return HostSkillResult.NotSupported("Host media controller is only supported on Windows.");
        }

        if (step is <= 0 or > 100)
        {
            return HostSkillResult.ValidationError($"Volume step must be between 1 and 100. Received: {step}.");
        }

        int current = await GetVolumeAsync(cancellationToken);
        int target = Math.Max(0, current - step);
        return await SetVolumeAsync(target, cancellationToken);
    }

    /// <inheritdoc />
    public Task<HostSkillResult> MuteAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HostSkillResult.NotSupported("Host media controller is only supported on Windows."));
        }

        try
        {
            _fallbackIsMuted = true;
            var endpoint = TryGetAudioEndpointVolume();
            if (endpoint is not null)
            {
                Guid context = Guid.Empty;
                endpoint.SetMute(true, ref context);
            }
            else
            {
                SendVirtualKey(VK_VOLUME_MUTE);
            }

            return Task.FromResult(HostSkillResult.Ok("{\"isMuted\":true}", "Host audio muted."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HostSkillResult.Failed($"Failed to mute host audio: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<HostSkillResult> UnmuteAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(HostSkillResult.NotSupported("Host media controller is only supported on Windows."));
        }

        try
        {
            _fallbackIsMuted = false;
            var endpoint = TryGetAudioEndpointVolume();
            if (endpoint is not null)
            {
                Guid context = Guid.Empty;
                endpoint.SetMute(false, ref context);
            }
            else
            {
                SendVirtualKey(VK_VOLUME_MUTE);
            }

            return Task.FromResult(HostSkillResult.Ok("{\"isMuted\":false}", "Host audio unmuted."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HostSkillResult.Failed($"Failed to unmute host audio: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<HostSkillResult> ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return HostSkillResult.NotSupported("Host media controller is only supported on Windows.");
        }

        bool muted = await IsMutedAsync(cancellationToken);
        return muted ? await UnmuteAsync(cancellationToken) : await MuteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsMutedAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(_fallbackIsMuted);
        }

        var endpoint = TryGetAudioEndpointVolume();
        if (endpoint is not null)
        {
            try
            {
                int hr = endpoint.GetMute(out bool isMuted);
                if (hr == 0)
                {
                    _fallbackIsMuted = isMuted;
                    return Task.FromResult(isMuted);
                }
            }
            catch
            {
                // Fall back to stored state if COM communication fails
            }
        }

        return Task.FromResult(_fallbackIsMuted);
    }

    private static IAudioEndpointVolume? TryGetAudioEndpointVolume()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            int hr = enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device);
            if (hr != 0 || device is null)
            {
                return null;
            }

            Guid iid = EndpointVolumeIid;
            hr = device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out IntPtr pIface);
            if (hr != 0 || pIface == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pIface);
            }
            finally
            {
                Marshal.Release(pIface);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void SendVirtualKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
        [PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }
}
