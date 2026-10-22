using MediaStation.Core.Cli;

namespace MediaStation.Core.Tests;

public class ExitCodeTests
{
    [Fact]
    public void Exit_codes_have_exactly_the_pinned_names_and_values()
    {
        (string Name, int Value)[] expected =
        [
            ("Ok", 0),
            ("InvalidArguments", 2),
            ("AlreadyRunning", 3),
            ("Unsupported", 4),
            ("ShellStartFailed", 5),
            ("ConfigInvalid", 6),
            ("InputHubFailed", 7),
        ];

        var actual = Enum.GetValues<ExitCode>()
            .Select(code => (code.ToString(), (int)code))
            .ToArray();

        Assert.Equal(expected, actual);
    }
}
