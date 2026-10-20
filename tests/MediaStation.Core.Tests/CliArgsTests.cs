using MediaStation.Core.Cli;

namespace MediaStation.Core.Tests;

public class CliArgsTests
{
    [Fact]
    public void No_args_is_run_mode()
    {
        var mode = ParsedMode();

        Assert.IsType<CliMode.Run>(mode);
        Assert.Equal("run", mode.LogName);
    }

    [Fact]
    public void Smoke_with_app_id_is_smoke_mode()
    {
        var mode = ParsedMode("--smoke", "netflix");

        var smoke = Assert.IsType<CliMode.Smoke>(mode);
        Assert.Equal("netflix", smoke.AppId);
        Assert.Equal("smoke", mode.LogName);
    }

    [Fact]
    public void Smoke_keeps_app_id_case_and_inner_dashes()
    {
        var smoke = Assert.IsType<CliMode.Smoke>(ParsedMode("--smoke", "Netflix-PL"));

        Assert.Equal("Netflix-PL", smoke.AppId);
    }

    [Fact]
    public void Calibrate_input_is_calibrate_mode()
    {
        var mode = ParsedMode("--calibrate-input");

        Assert.IsType<CliMode.CalibrateInput>(mode);
        Assert.Equal("calibrateInput", mode.LogName);
    }

    [Fact]
    public void Smoke_without_app_id_is_invalid()
    {
        Assert.Equal("brak appId po --smoke", InvalidReason("--smoke"));
    }

    [Fact]
    public void Smoke_with_option_instead_of_app_id_is_invalid()
    {
        Assert.Equal("appId nie może zaczynać się od '-'", InvalidReason("--smoke", "--x"));
    }

    [Fact]
    public void Smoke_with_leading_dash_app_id_is_invalid()
    {
        Assert.Equal("appId nie może zaczynać się od '-'", InvalidReason("--smoke", "-netflix"));
    }

    [Fact]
    public void Smoke_with_empty_app_id_is_invalid()
    {
        Assert.Equal("appId nie może być pusty", InvalidReason("--smoke", ""));
    }

    [Fact]
    public void Smoke_with_extra_argument_is_invalid()
    {
        Assert.Equal("nadmiarowy argument: extra", InvalidReason("--smoke", "netflix", "extra"));
    }

    [Fact]
    public void Calibrate_input_with_extra_argument_is_invalid()
    {
        Assert.Equal("nadmiarowy argument: extra", InvalidReason("--calibrate-input", "extra"));
    }

    [Fact]
    public void Unknown_argument_is_invalid()
    {
        Assert.Equal("nieznany argument: --foo", InvalidReason("--foo"));
    }

    [Fact]
    public void Bare_argument_is_unknown()
    {
        Assert.Equal("nieznany argument: netflix", InvalidReason("netflix"));
    }

    [Fact]
    public void Mode_matching_is_case_sensitive()
    {
        Assert.Equal("nieznany argument: --SMOKE", InvalidReason("--SMOKE"));
    }

    [Fact]
    public void Repeated_smoke_mode_is_invalid()
    {
        Assert.Equal("tryb podano więcej niż raz", InvalidReason("--smoke", "netflix", "--smoke", "other"));
    }

    [Fact]
    public void Repeated_calibrate_mode_is_invalid()
    {
        Assert.Equal("tryb podano więcej niż raz", InvalidReason("--calibrate-input", "--calibrate-input"));
    }

    [Fact]
    public void Two_different_modes_are_invalid()
    {
        Assert.Equal("tryb podano więcej niż raz", InvalidReason("--calibrate-input", "--smoke", "netflix"));
    }

    [Fact]
    public void Smoke_mode_as_app_id_is_invalid()
    {
        Assert.Equal("tryb podano więcej niż raz", InvalidReason("--smoke", "--calibrate-input"));
    }

    [Fact]
    public void Null_args_throw()
    {
        Assert.Throws<ArgumentNullException>(() => CliArgs.Parse(null!));
    }

    private static CliMode ParsedMode(params string[] args) =>
        Assert.IsType<CliParseResult.Parsed>(CliArgs.Parse(args)).Mode;

    private static string InvalidReason(params string[] args) =>
        Assert.IsType<CliParseResult.Invalid>(CliArgs.Parse(args)).Reason;
}
