using System.IO;
using System.Security;
using MediaStation.Core.Config;

namespace MediaStation.Core.Tests;

public class ConfigFileStateTests
{
    [Fact]
    public void File_not_found_maps_to_missing()
    {
        Assert.IsType<ConfigFileState.Missing>(
            ConfigFileState.FromReadError(new FileNotFoundException("brak pliku")));
    }

    [Fact]
    public void Directory_not_found_maps_to_missing()
    {
        Assert.IsType<ConfigFileState.Missing>(
            ConfigFileState.FromReadError(new DirectoryNotFoundException("brak katalogu")));
    }

    [Fact]
    public void Io_exception_maps_to_unreadable_with_message()
    {
        var state = Assert.IsType<ConfigFileState.Unreadable>(
            ConfigFileState.FromReadError(new IOException("plik zablokowany")));

        Assert.Equal("plik zablokowany", state.Detail);
    }

    [Fact]
    public void Unauthorized_access_maps_to_unreadable_with_message()
    {
        var state = Assert.IsType<ConfigFileState.Unreadable>(
            ConfigFileState.FromReadError(new UnauthorizedAccessException("brak uprawnień")));

        Assert.Equal("brak uprawnień", state.Detail);
    }

    [Fact]
    public void Security_exception_maps_to_unreadable_with_message()
    {
        var state = Assert.IsType<ConfigFileState.Unreadable>(
            ConfigFileState.FromReadError(new SecurityException("polityka zabrania")));

        Assert.Equal("polityka zabrania", state.Detail);
    }

    [Fact]
    public void Other_exception_maps_to_null()
    {
        Assert.Null(ConfigFileState.FromReadError(new InvalidOperationException("nie I/O")));
    }

    [Fact]
    public void Null_error_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigFileState.FromReadError(null!));
    }
}
