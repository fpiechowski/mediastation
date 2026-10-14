# Reads master volume and mute of the default playback endpoint (Core Audio IAudioEndpointVolume).
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume { int f1(); int f2(); int f3(); int f4(); int f5(); int f6();
  int GetMasterVolumeLevelScalar(out float l); int f8(); int f9(); int f10(); int f11(); int f12();
  int GetMute([MarshalAs(UnmanagedType.Bool)] out bool m); }
[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice { int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); }
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator { int f1(); int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice d); }
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator {}
public static class Ep { public static string Read() {
  var en = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator(); IMMDevice d; en.GetDefaultAudioEndpoint(0, 1, out d);
  var iid = typeof(IAudioEndpointVolume).GUID; object o; d.Activate(ref iid, 23, IntPtr.Zero, out o);
  var v = (IAudioEndpointVolume)o; float l; bool m; v.GetMasterVolumeLevelScalar(out l); v.GetMute(out m);
  return string.Format("volume={0:P0} mute={1}", l, m); } }
'@
[Ep]::Read()
