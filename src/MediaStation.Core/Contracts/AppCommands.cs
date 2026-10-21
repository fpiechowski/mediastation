namespace MediaStation.Core.Contracts;

/// <summary>
/// Komenda startu aplikacji do hosta jej <see cref="AppType"/> (AD-4, AD-11). Niesie wyłącznie <see cref="AppId"/>,
/// bo to jedyny identyfikator aplikacji w systemie, a host zna swoje aplikacje.
/// </summary>
public sealed record StartApp(long CmdId, string AppId) : Command(CmdId);

/// <summary>
/// Baza komend WindowManagera, jedynego kodu zmieniającego widoczność, Z-order i foreground okien (AD-15).
/// </summary>
public abstract record WindowCommand(long CmdId) : Command(CmdId);

/// <summary>WindowManager przenosi okno aplikacji na pierwszy plan.</summary>
public sealed record ActivateWindow(long CmdId, long Hwnd) : WindowCommand(CmdId);

/// <summary>WindowManager minimalizuje okno aplikacji; dla przeglądarki nigdy <c>SW_HIDE</c> (AD-5).</summary>
public sealed record MinimizeWindow(long CmdId, long Hwnd) : WindowCommand(CmdId);

/// <summary>WindowManager pokazuje ekran główny nad aplikacjami.</summary>
public sealed record ShowShell(long CmdId) : WindowCommand(CmdId);
