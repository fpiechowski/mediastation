using System.Net;
using System.Net.Sockets;
using MediaStation.App.Hosts.Browser;

namespace MediaStation.Core.Tests;

/// <summary>
/// Story 6.4 (AD-13): produkcyjne sondy przed startem na prawdziwych zasobach: <c>lockfile</c> profilu trzymany
/// jak przez Chromium (zapis, współdzielenie tylko odczytu), nasłuch TCP na loopbacku i <c>FileVersion</c> pliku.
/// </summary>
public sealed class BrowserPreflightTests : IDisposable
{
    private readonly string _profileDir = Path.Combine(Path.GetTempPath(), "ms-preflight-" + Guid.NewGuid().ToString("N"));

    public BrowserPreflightTests() => Directory.CreateDirectory(_profileDir);

    private string LockPath => Path.Combine(_profileDir, BrowserPreflight.LockFileName);

    public void Dispose()
    {
        try
        {
            if (File.Exists(LockPath))
            {
                File.SetAttributes(LockPath, FileAttributes.Normal);
            }

            Directory.Delete(_profileDir, recursive: true);
        }
        catch (IOException)
        {
            // Katalog tymczasowy: sprzątanie najlepszym wysiłkiem.
        }
    }

    [Fact]
    public void Lockfile_held_with_read_sharing_means_the_profile_is_in_use()
    {
        using (new FileStream(LockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
        {
            Assert.True(BrowserPreflight.IsProfileInUse(_profileDir));
        }

        // Uchwyt zamknięty: ten sam plik nie trzyma już profilu.
        Assert.False(BrowserPreflight.IsProfileInUse(_profileDir));
    }

    [Fact]
    public void Lockfile_left_without_a_holder_is_free_and_stays_on_disk()
    {
        File.WriteAllText(LockPath, string.Empty);

        Assert.False(BrowserPreflight.IsProfileInUse(_profileDir));
        Assert.True(File.Exists(LockPath));
    }

    [Fact]
    public void Lockfile_that_refuses_write_access_means_the_profile_is_in_use()
    {
        // Odmowa dostępu (jak przy usuwaniu w toku, gdy przeglądarka się kończy) to profil w użyciu.
        File.WriteAllText(LockPath, string.Empty);
        File.SetAttributes(LockPath, FileAttributes.ReadOnly);

        Assert.True(BrowserPreflight.IsProfileInUse(_profileDir));
    }

    [Fact]
    public void Missing_lockfile_or_missing_folder_is_free()
    {
        Assert.False(BrowserPreflight.IsProfileInUse(_profileDir));
        Assert.False(BrowserPreflight.IsProfileInUse(Path.Combine(_profileDir, "missing", "profile")));
    }

    [Fact]
    public void Listener_on_loopback_port_zero_is_seen_on_its_port_until_stopped()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port;
        try
        {
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.True(BrowserPreflight.IsPortInUse(port));
        }
        finally
        {
            listener.Stop();
        }

        Assert.False(BrowserPreflight.IsPortInUse(port));
    }

    [Fact]
    public void File_version_of_a_versioned_binary_has_four_parts()
    {
        var version = BrowserPreflight.ReadFileVersion(typeof(BrowserPreflight).Assembly.Location);

        Assert.NotNull(version);
        Assert.Equal(4, version.Split('.').Length);
        Assert.NotNull(BrowserVersion.Parse(version));
    }

    [Fact]
    public void File_without_a_version_or_missing_file_is_null()
    {
        var plain = Path.Combine(_profileDir, "plain.txt");
        File.WriteAllText(plain, "no version resource");

        Assert.Null(BrowserPreflight.ReadFileVersion(plain));
        Assert.Null(BrowserPreflight.ReadFileVersion(Path.Combine(_profileDir, "missing.exe")));
    }
}
