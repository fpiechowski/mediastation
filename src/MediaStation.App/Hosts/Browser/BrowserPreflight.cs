using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;

namespace MediaStation.App.Hosts.Browser;

/// <summary>
/// Produkcyjne sondy przed startem przeglądarki (AD-13): profil w użyciu, zajęty port CDP i <c>FileVersion</c>
/// binarki. Bez typów WPF i Win32, więc działają w testach na prawdziwych zasobach. Żadna nie rzuca.
/// </summary>
internal static class BrowserPreflight
{
    /// <summary>Plik, który Chromium trzyma otwarty (zapis, współdzielenie odczytu, usunięcie przy zamknięciu) przez całe życie profilu.</summary>
    public const string LockFileName = "lockfile";

    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>
    /// <c>true</c>, gdy inny proces trzyma <c>&lt;profileDir&gt;\lockfile</c>: otwarcie do zapisu kończy się naruszeniem
    /// współdzielenia albo blokady, albo odmową dostępu (usuwanie w toku, gdy przeglądarka się kończy). Brak pliku lub
    /// katalogu i każdy inny błąd to <c>false</c>.
    /// </summary>
    public static bool IsProfileInUse(string profileDir)
    {
        try
        {
            using var _ = new FileStream(
                Path.Combine(profileDir, LockFileName),
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException ex) when (ex.HResult is SharingViolation or LockViolation)
        {
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Brak pliku albo katalogu (FileNotFound/DirectoryNotFound) i każdy inny błąd: profil uznajemy za wolny.
            return false;
        }
    }

    /// <summary><c>true</c>, gdy jakikolwiek nasłuch TCP (dowolny adres) stoi na <paramref name="port"/>; błąd to <c>false</c>.</summary>
    public static bool IsPortInUse(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary><c>FileVersion</c> pliku jako <c>Major.Minor.Build.Private</c>; <c>null</c> dla samych zer albo błędu.</summary>
    public static string? ReadFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0 && info.FilePrivatePart == 0)
            {
                return null;
            }

            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
