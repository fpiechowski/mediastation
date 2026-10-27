using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;

namespace MediaStation.App.Platform.Audio;

/// <summary>
/// Wrapper COM Core Audio (CsWin32, AD-3): enumeruje sesje wszystkich aktywnych urządzeń renderujących i wystawia
/// je jako <see cref="IAudioSession"/>. Notyfikacje nowej sesji i zmiany urządzeń tylko budzą wątek AudioRoutera
/// przez <c>onChange</c>; cała praca idzie na jego wątku. Tworzony i zwalniany na wątku MTA AudioRoutera
/// (notyfikacje sesji wymagają MTA). Wyjątki z enumeracji lecą do silnika, który zamienia je na <c>AudioUnavailable</c>.
/// </summary>
internal sealed unsafe class CoreAudioSessionBackend : IAudioSessionBackend
{
    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    private readonly global::System.Action _onChange;
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly DeviceWatcher _deviceWatcher;
    private readonly SessionWatcher _sessionWatcher;
    private readonly Dictionary<string, IAudioSessionManager2> _managers = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<IAudioSessionControl> _notified = new();
    private readonly Dictionary<string, VolumeRegistration> _volumeEvents = new(StringComparer.Ordinal);
    private bool _disposed;

    public CoreAudioSessionBackend(global::System.Action onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        _onChange = onChange;
        var clsid = MMDeviceEnumeratorClsid;
        PInvoke.CoCreateInstance<IMMDeviceEnumerator>(in clsid, null, CLSCTX.CLSCTX_ALL, out var enumerator).ThrowOnFailure();
        _enumerator = enumerator;
        _deviceWatcher = new DeviceWatcher(_onChange);
        _sessionWatcher = new SessionWatcher(_notified, _onChange);
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(_deviceWatcher);
        }
        catch
        {
            Marshal.ReleaseComObject(_enumerator);
            throw;
        }
    }

    public IReadOnlyList<IAudioSession> Enumerate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sessions = new List<IAudioSession>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            _enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE.DEVICE_STATE_ACTIVE, out var devices);
            try
            {
                devices.GetCount(out var count);
                var active = new HashSet<string>(StringComparer.Ordinal);
                var failures = 0;
                Exception? lastFailure = null;
                for (uint index = 0; index < count; index++)
                {
                    devices.Item(index, out var device);
                    try
                    {
                        var id = ReadDeviceId(device);
                        active.Add(id);
                        var manager = ManagerFor(id, device);
                        AddSessions(manager, sessions, seen);
                    }
                    catch (Exception ex) when (ex is COMException or InvalidCastException)
                    {
                        // Jedno uszkodzone urządzenie nie może odebrać sesji pozostałym; usługa niedostępna dopiero, gdy padły wszystkie.
                        failures++;
                        lastFailure = ex;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }

                if (count > 0 && failures == count)
                {
                    throw lastFailure!;
                }

                DropManagersExcept(active);
            }
            finally
            {
                Marshal.ReleaseComObject(devices);
            }

            // Sesje zgłoszone notyfikacją: enumerator bywa o krok za nimi.
            while (_notified.TryDequeue(out var control))
            {
                Add(control, sessions, seen);
            }

            DropVolumeEventsExcept(seen);
        }
        catch
        {
            foreach (var session in sessions)
            {
                session.Dispose();
            }

            throw;
        }

        return sessions;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_deviceWatcher);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _ = ex;
        }

        DropVolumeEventsExcept([]);
        DropManagersExcept([]);
        while (_notified.TryDequeue(out var control))
        {
            Marshal.ReleaseComObject(control);
        }

        Marshal.ReleaseComObject(_enumerator);
    }

    private static string ReadDeviceId(IMMDevice device)
    {
        PWSTR raw = default;
        device.GetId(&raw);
        try
        {
            return raw.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem((IntPtr)raw.Value);
        }
    }

    private IAudioSessionManager2 ManagerFor(string id, IMMDevice device)
    {
        if (_managers.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var iid = typeof(IAudioSessionManager2).GUID;
        device.Activate(&iid, CLSCTX.CLSCTX_ALL, null, out var activated);
        var manager = (IAudioSessionManager2)activated;
        manager.RegisterSessionNotification(_sessionWatcher);
        _managers[id] = manager;
        return manager;
    }

    private void DropManagersExcept(HashSet<string> active)
    {
        foreach (var id in new List<string>(_managers.Keys))
        {
            if (active.Contains(id))
            {
                continue;
            }

            var manager = _managers[id];
            _managers.Remove(id);
            try
            {
                manager.UnregisterSessionNotification(_sessionWatcher);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                _ = ex;
            }

            Marshal.ReleaseComObject(manager);
        }
    }

    private void AddSessions(IAudioSessionManager2 manager, List<IAudioSession> sessions, HashSet<string> seen)
    {
        var sessionEnumerator = manager.GetSessionEnumerator();
        try
        {
            // GetCount uruchamia dostarczanie notyfikacji nowych sesji (dokumentacja IAudioSessionManager2).
            sessionEnumerator.GetCount(out var count);
            for (var index = 0; index < count; index++)
            {
                sessionEnumerator.GetSession(index, out var control);
                Add(control, sessions, seen);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(sessionEnumerator);
        }
    }

    private void Add(IAudioSessionControl control, List<IAudioSession> sessions, HashSet<string> seen)
    {
        var session = ComAudioSession.TryCreate(control);
        if (session is null)
        {
            Marshal.ReleaseComObject(control);
            return;
        }

        if (seen.Add(session.Key))
        {
            sessions.Add(session);
            WatchVolume(session);
        }
        else
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Pierwsze spotkanie sesji: rejestruje powiadomienia zmian głośności i oddaje rejestracji jej obiekt (sesja go
    /// nie zwalnia). Błąd rejestracji nie psuje reguły: gwarancją jest okresowy skan, powiadomienia tylko go przyspieszają.
    /// </summary>
    private void WatchVolume(ComAudioSession session)
    {
        if (_volumeEvents.ContainsKey(session.Key))
        {
            return;
        }

        var events = new VolumeWatcher(_onChange);
        try
        {
            session.Control.RegisterAudioSessionNotification(events);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _ = ex;
            return;
        }

        _volumeEvents[session.Key] = new VolumeRegistration(session.Control, events);
        session.KeepControl = true;
    }

    private void DropVolumeEventsExcept(HashSet<string> present)
    {
        foreach (var key in new List<string>(_volumeEvents.Keys))
        {
            if (present.Contains(key))
            {
                continue;
            }

            var registration = _volumeEvents[key];
            _volumeEvents.Remove(key);
            try
            {
                registration.Control.UnregisterAudioSessionNotification(registration.Events);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // Sesja już zniknęła razem z rejestracją.
                _ = ex;
            }

            Marshal.ReleaseComObject(registration.Control);
        }
    }

    /// <summary>Jedna sesja z obiektu <c>IAudioSessionControl</c>; zwalnia go w <see cref="Dispose"/>.</summary>
    private sealed unsafe class ComAudioSession : IAudioSession
    {
        private readonly IAudioSessionControl _control;

        private ComAudioSession(IAudioSessionControl control, string key, int pid)
        {
            _control = control;
            Key = key;
            Pid = pid;
        }

        public string Key { get; }

        public int Pid { get; }

        public IAudioSessionControl Control => _control;

        /// <summary>Obiekt przejęła rejestracja powiadomień: <see cref="Dispose"/> go nie zwalnia.</summary>
        public bool KeepControl { get; set; }

        public static ComAudioSession? TryCreate(IAudioSessionControl control)
        {
            try
            {
                var control2 = (IAudioSessionControl2)control;
                PWSTR raw = default;
                control2.GetSessionInstanceIdentifier(&raw);
                string key;
                try
                {
                    key = raw.ToString();
                }
                finally
                {
                    Marshal.FreeCoTaskMem((IntPtr)raw.Value);
                }

                control2.GetProcessId(out var pid);
                return new ComAudioSession(control, key, (int)pid);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // Sesja zniknęła w trakcie odczytu albo nie jest sesją v2: pomijamy ją, nie całe skanowanie.
                return null;
            }
        }

        public bool? TryGetMute()
        {
            ISimpleAudioVolume volume;
            try
            {
                volume = (ISimpleAudioVolume)_control;
            }
            catch (InvalidCastException)
            {
                return null;
            }

            volume.GetMute(out var muted);
            return muted;
        }

        public bool TrySetMute(bool mute)
        {
            ISimpleAudioVolume volume;
            try
            {
                volume = (ISimpleAudioVolume)_control;
            }
            catch (InvalidCastException)
            {
                return false;
            }

            var context = AudioChangeContext.Own;
            volume.SetMute(mute, &context);
            return true;
        }

        public float[]? TryGetChannelVolumes()
        {
            IChannelAudioVolume channels;
            try
            {
                channels = (IChannelAudioVolume)_control;
            }
            catch (InvalidCastException)
            {
                return null;
            }

            channels.GetChannelCount(out var count);
            if (count == 0)
            {
                return null;
            }

            var levels = new float[count];
            channels.GetAllVolumes(count, levels);
            return levels;
        }

        public bool TrySetChannelVolumes(float[] levels)
        {
            IChannelAudioVolume channels;
            try
            {
                channels = (IChannelAudioVolume)_control;
            }
            catch (InvalidCastException)
            {
                return false;
            }

            var context = AudioChangeContext.Own;
            channels.SetAllVolumes((uint)levels.Length, levels, &context);
            return true;
        }

        public void Dispose()
        {
            if (!KeepControl)
            {
                Marshal.ReleaseComObject(_control);
            }
        }
    }

    /// <summary>Notyfikacje urządzeń: każda zmiana (stan, dodanie, domyślne) tylko budzi wątek AudioRoutera.</summary>
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class DeviceWatcher(global::System.Action onChange) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(PCWSTR pwstrDeviceId, DEVICE_STATE dwNewState) => onChange();

        public void OnDeviceAdded(PCWSTR pwstrDeviceId) => onChange();

        public void OnDeviceRemoved(PCWSTR pwstrDeviceId) => onChange();

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, PCWSTR pwstrDefaultDeviceId) => onChange();

        public void OnPropertyValueChanged(PCWSTR pwstrDeviceId, PROPERTYKEY key)
        {
        }
    }

    /// <summary>Notyfikacja nowej sesji: kolejkuje jej obiekt i budzi wątek AudioRoutera.</summary>
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class SessionWatcher(ConcurrentQueue<IAudioSessionControl> notified, global::System.Action onChange) : IAudioSessionNotification
    {
        public void OnSessionCreated(IAudioSessionControl NewSession)
        {
            notified.Enqueue(NewSession);
            onChange();
        }
    }

    /// <summary>Rejestracja powiadomień sesji: jej obiekt i odbiorca, trzymane między skanami.</summary>
    private sealed record VolumeRegistration(IAudioSessionControl Control, VolumeWatcher Events);

    /// <summary>
    /// Powiadomienia zmian głośności sesji: obca zmiana (mikser, aplikacja) tylko budzi wątek AudioRoutera; własna, z
    /// kontekstem <see cref="AudioChangeContext.Own"/>, nie zleca skanu.
    /// </summary>
    [ClassInterface(ClassInterfaceType.None)]
    private sealed unsafe class VolumeWatcher(global::System.Action onChange) : IAudioSessionEvents
    {
        public void OnDisplayNameChanged(PCWSTR NewDisplayName, Guid* EventContext)
        {
        }

        public void OnIconPathChanged(PCWSTR NewIconPath, Guid* EventContext)
        {
        }

        public void OnSimpleVolumeChanged(float NewVolume, BOOL NewMute, Guid* EventContext) => Notify(EventContext);

        public void OnChannelVolumeChanged(uint ChannelCount, float[] NewChannelVolumeArray, uint ChangedChannel, Guid* EventContext) => Notify(EventContext);

        public void OnGroupingParamChanged(Guid* NewGroupingParam, Guid* EventContext)
        {
        }

        public void OnStateChanged(AudioSessionState NewState)
        {
        }

        public void OnSessionDisconnected(AudioSessionDisconnectReason DisconnectReason)
        {
        }

        private void Notify(Guid* context)
        {
            if (AudioChangeContext.ShouldSweep(context is null ? null : *context))
            {
                onChange();
            }
        }
    }
}
