using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace MediaStation.App.Platform.Window;

/// <summary>Migawka procesów (<c>CreateToolhelp32Snapshot</c>) jako pary <c>(pid, ppid)</c> dla drzewa procesów harnessu.</summary>
internal static class Win32ProcessSnapshot
{
    public static unsafe IReadOnlyList<KeyValuePair<int, int>> ParentMap()
    {
        var result = new List<KeyValuePair<int, int>>();
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            return result;
        }

        var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
        if (!PInvoke.Process32FirstW(snapshot, ref entry))
        {
            return result;
        }

        do
        {
            result.Add(new KeyValuePair<int, int>((int)entry.th32ProcessID, (int)entry.th32ParentProcessID));
        }
        while (PInvoke.Process32NextW(snapshot, ref entry));

        return result;
    }
}
