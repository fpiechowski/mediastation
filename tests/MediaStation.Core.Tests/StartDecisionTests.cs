using MediaStation.Core.Cli;
using MediaStation.Core.Config;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

public class StartDecisionTests
{
    [Fact]
    public void Invalid_arguments_exit_with_error_before_touching_mutex()
    {
        var acquireCalls = 0;

        var decision = StartDecision.Decide(["--foo"], () =>
        {
            acquireCalls++;
            return true;
        });

        var exit = Assert.IsType<StartDecision.Exit>(decision);
        Assert.Equal(ExitCode.InvalidArguments, exit.Code);
        Assert.Equal(LogLevel.Error, exit.Level);
        Assert.Equal("invalidArguments", exit.Msg);
        Assert.Equal("nieznany argument: --foo", exit.Data["reason"]);
        Assert.Equal(0, acquireCalls);
    }

    [Fact]
    public void Busy_instance_in_run_mode_exits_already_running()
    {
        var decision = StartDecision.Decide([], () => false);

        var exit = Assert.IsType<StartDecision.Exit>(decision);
        Assert.Equal(ExitCode.AlreadyRunning, exit.Code);
        Assert.Equal(LogLevel.Warn, exit.Level);
        Assert.Equal("instanceRejected", exit.Msg);
        Assert.Equal("run", exit.Data["mode"]);
        Assert.Single(exit.Data);
    }

    [Fact]
    public void Busy_instance_in_smoke_mode_exits_already_running()
    {
        var decision = StartDecision.Decide(["--smoke", "netflix"], () => false);

        var exit = Assert.IsType<StartDecision.Exit>(decision);
        Assert.Equal(ExitCode.AlreadyRunning, exit.Code);
        Assert.Equal("instanceRejected", exit.Msg);
        Assert.Equal("smoke", exit.Data["mode"]);
        Assert.Single(exit.Data);
    }

    [Fact]
    public void Free_instance_in_run_mode_runs()
    {
        var acquireCalls = 0;

        var decision = StartDecision.Decide([], () =>
        {
            acquireCalls++;
            return true;
        });

        Assert.IsType<StartDecision.Run>(decision);
        Assert.Equal(1, acquireCalls);
    }

    [Fact]
    public void Free_instance_in_smoke_mode_exits_unsupported_with_app_id()
    {
        var decision = StartDecision.Decide(["--smoke", "netflix"], () => true);

        var exit = Assert.IsType<StartDecision.Exit>(decision);
        Assert.Equal(ExitCode.Unsupported, exit.Code);
        Assert.Equal(LogLevel.Warn, exit.Level);
        Assert.Equal("modeUnsupported", exit.Msg);
        Assert.Equal("smoke", exit.Data["mode"]);
        Assert.Equal("netflix", exit.Data["appId"]);
    }

    [Fact]
    public void Free_instance_in_calibrate_mode_starts_calibration()
    {
        var acquireCalls = 0;

        var decision = StartDecision.Decide(["--calibrate-input"], () =>
        {
            acquireCalls++;
            return true;
        });

        Assert.IsType<StartDecision.CalibrateInput>(decision);
        Assert.Equal(1, acquireCalls);
    }

    [Fact]
    public void Busy_instance_in_calibrate_mode_exits_already_running()
    {
        var decision = StartDecision.Decide(["--calibrate-input"], () => false);

        var exit = Assert.IsType<StartDecision.Exit>(decision);
        Assert.Equal(ExitCode.AlreadyRunning, exit.Code);
        Assert.Equal(LogLevel.Warn, exit.Level);
        Assert.Equal("instanceRejected", exit.Msg);
        Assert.Equal("calibrateInput", exit.Data["mode"]);
        Assert.Single(exit.Data);
    }

    [Fact]
    public void Calibration_exit_code_is_config_invalid_when_config_has_errors()
    {
        Assert.Equal(ExitCode.ConfigInvalid, StartDecision.CalibrationExit(new ConfigResult(null, [])));
    }

    [Fact]
    public void Calibration_exit_code_is_ok_for_a_valid_config()
    {
        var valid = new LoadedConfig(new AppsConfig([]), new InputConfig([], []), new Settings());

        Assert.Equal(ExitCode.Ok, StartDecision.CalibrationExit(new ConfigResult(valid, [])));
    }

    [Fact]
    public void Null_config_result_throws()
    {
        Assert.Throws<ArgumentNullException>(() => StartDecision.CalibrationExit(null!));
    }

    [Fact]
    public void Null_args_throw()
    {
        Assert.Throws<ArgumentNullException>(() => StartDecision.Decide(null!, () => true));
    }

    [Fact]
    public void Null_acquire_callback_throws()
    {
        Assert.Throws<ArgumentNullException>(() => StartDecision.Decide([], null!));
    }
}
