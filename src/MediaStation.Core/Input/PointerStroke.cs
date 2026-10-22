namespace MediaStation.Core.Input;

/// <summary>Rodzaj zdarzenia wskaźnika z hooka <c>WH_MOUSE_LL</c>.</summary>
public enum PointerMessage
{
    Move,
    ButtonDown,
    ButtonUp,
    Wheel,
}

/// <summary>Przycisk wskaźnika, którego dotyczy zdarzenie; <see cref="None"/> dla ruchu i kółka.</summary>
public enum PointerButton
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2,
    None,
}

/// <summary>
/// Jedno zdarzenie wskaźnika z hooka LL przeniesione do Core bez Win32 (AD-3, AD-8). <paramref name="X"/> i
/// <paramref name="Y"/> to pozycja ekranowa w pikselach fizycznych (tak jak <c>MSLLHOOKSTRUCT.pt</c>),
/// <paramref name="WheelDelta"/> — surowa delta kółka (pionowego albo poziomego), <paramref name="Injected"/>
/// odpowiada <c>LLMHF_INJECTED</c>, <paramref name="TimeMs"/> — polu <c>time</c> zdarzenia hooka,
/// a <paramref name="ExtraInfo"/> — polu <c>dwExtraInfo</c> (znacznik własnego wstrzyknięcia, patrz
/// <see cref="InjectionMarker"/>).
/// </summary>
public sealed record PointerStroke(
    PointerMessage Message,
    PointerButton Button,
    int X,
    int Y,
    int WheelDelta,
    bool Injected,
    long TimeMs,
    long ExtraInfo = 0);

/// <summary>
/// Prostokąt ekranu w pikselach fizycznych, w którym <c>ClipCursor</c> więzi kursor w trybie <c>osk</c>.
/// Prawa i dolna krawędź są wyłączone, zgodnie z <c>RECT</c> z <c>GetWindowRect</c>: punkt
/// <c>(Right, Top)</c> jest już poza prostokątem, więc klikanie w krawędź nie jest „wewnątrz” klawiatury.
/// </summary>
public sealed record ScreenRectangle(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Czy punkt ekranowy leży wewnątrz prostokąta (krawędzie prawa i dolna wyłączna)?</summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}
