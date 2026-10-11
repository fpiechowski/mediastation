// S9: per-process audio sessions via Core Audio
// (IAudioSessionManager2 -> IAudioSessionControl2::GetProcessId -> ISimpleAudioVolume::SetMute).
// C# 5 only: compiled by Add-Type in Windows PowerShell 5.1.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MsAudio
{
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore(int access, out IntPtr store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        void GetGroupingParam(out Guid param);
        void SetGroupingParam(ref Guid param, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr client);
        void UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6F187E86F7F2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid context);
        void GetMasterVolume(out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IChannelAudioVolume
    {
        void GetChannelCount(out uint count);
        void SetChannelVolume(uint index, float level, ref Guid context);
        void GetChannelVolume(uint index, out float level);
        void SetAllVolumes(uint count, [MarshalAs(UnmanagedType.LPArray)] float[] levels, ref Guid context);
        void GetAllVolumes(uint count, [MarshalAs(UnmanagedType.LPArray), Out] float[] levels);
    }

    // Plain data only: no COM references leave the MTA worker thread.
    public class Session
    {
        public uint Pid;
        public int State; // 0 inactive, 1 active, 2 expired
        public bool Muted;
        public float Volume;
        public string DisplayName;
        public string Device;
        public string Method; // "mute" (ISimpleAudioVolume) or "channel" (IChannelAudioVolume fallback)
        public string Error;
    }

    public static class Mixer
    {
        const int eRender = 0, DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 23;

        // Core Audio work runs on an MTA thread to avoid cross-apartment proxies from PowerShell's STA thread.
        static T OnMta<T>(Func<T> work)
        {
            T result = default(T);
            Exception error = null;
            Thread thread = new Thread(delegate ()
            {
                try { result = work(); }
                catch (Exception e) { error = e; }
            });
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            thread.Join();
            if (error != null) throw new InvalidOperationException(error.Message, error);
            return result;
        }

        public static List<Session> Sessions()
        {
            return OnMta(delegate () { return Enumerate(null, false); });
        }

        // Mutes/unmutes every session owned by one of pids; returns the sessions that changed.
        public static List<Session> SetMute(int[] pids, bool mute)
        {
            HashSet<uint> targets = new HashSet<uint>();
            foreach (int pid in pids) targets.Add((uint)pid);
            return OnMta(delegate () { return Enumerate(targets, mute); });
        }

        static List<Session> Enumerate(HashSet<uint> muteTargets, bool mute)
        {
            List<Session> all = new List<Session>();
            List<Session> changed = new List<Session>();
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
            IMMDeviceCollection devices;
            enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out devices);
            uint deviceCount;
            devices.GetCount(out deviceCount);
            for (uint i = 0; i < deviceCount; i++)
            {
                IMMDevice device;
                devices.Item(i, out device);
                string deviceId;
                device.GetId(out deviceId);
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object manager;
                device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out manager);
                IAudioSessionEnumerator sessions;
                ((IAudioSessionManager2)manager).GetSessionEnumerator(out sessions);
                int sessionCount;
                sessions.GetCount(out sessionCount);
                for (int j = 0; j < sessionCount; j++)
                {
                    IAudioSessionControl2 control;
                    sessions.GetSession(j, out control);
                    Session s = new Session();
                    s.Device = deviceId;
                    try
                    {
                        control.GetProcessId(out s.Pid);
                        control.GetState(out s.State);
                        control.GetDisplayName(out s.DisplayName);
                        bool target = muteTargets != null && muteTargets.Contains(s.Pid);
                        Guid context = Guid.Empty;
                        ISimpleAudioVolume volume = control as ISimpleAudioVolume;
                        if (volume != null)
                        {
                            s.Method = "mute";
                            volume.GetMute(out s.Muted);
                            volume.GetMasterVolume(out s.Volume);
                            if (target && s.Muted != mute)
                            {
                                volume.SetMute(mute, ref context);
                                s.Muted = mute;
                                changed.Add(s);
                            }
                        }
                        else
                        {
                            // Windows 11 build 26300 (smoke test 2026-10-11): session objects no longer answer
                            // QueryInterface(ISimpleAudioVolume). Fall back to per-channel volume 0 / 1.0.
                            IChannelAudioVolume channels = (IChannelAudioVolume)control;
                            s.Method = "channel";
                            uint count;
                            channels.GetChannelCount(out count);
                            float[] levels = new float[count];
                            channels.GetAllVolumes(count, levels);
                            float max = 0;
                            foreach (float level in levels) max = Math.Max(max, level);
                            s.Volume = max;
                            s.Muted = max == 0;
                            if (target && s.Muted != mute)
                            {
                                float[] next = new float[count];
                                for (int k = 0; k < count; k++) next[k] = mute ? 0f : 1f;
                                channels.SetAllVolumes(count, next, ref context);
                                s.Muted = mute;
                                s.Volume = mute ? 0f : 1f;
                                changed.Add(s);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        s.Error = e.Message;
                    }
                    all.Add(s);
                }
            }
            return muteTargets == null ? all : changed;
        }
    }
}
