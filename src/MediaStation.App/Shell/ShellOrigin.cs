namespace MediaStation.App.Shell;

/// <summary>
/// Origin strony shella (AD-13, AD-16): jedyne źródło, z którego most przyjmuje wiadomości. Okno nawiguje
/// pod ten sam <see cref="Uri"/>, a <see cref="ShellMessageGate"/> porównuje z nim <c>e.Source</c> (scheme, host, port).
/// </summary>
internal static class ShellOrigin
{
#if SHELL_DEV_SERVER
    // Debug: shell z Vite dev servera (AD-13), z HMR.
    internal static Uri Uri { get; } = new("http://localhost:5173/");
#else
    // Release: shell z plików lokalnych <exe>/shell przez wirtualny host.
    internal const string Host = "shell.mediastation.example";

    internal static Uri Uri { get; } = new($"https://{Host}/index.html");
#endif
}
