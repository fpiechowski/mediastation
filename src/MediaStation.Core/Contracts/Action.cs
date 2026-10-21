namespace MediaStation.Core.Contracts;

/// <summary>Logiczny klawisz Action (AD-8).</summary>
public enum ActionKind
{
    Up,
    Down,
    Left,
    Right,
    Ok,
    Back,
    Home,
    AppMenu,
    Keyboard,
}

/// <summary>Faza Action; timingiem (repeat, long-press) zarządza InputHub (AD-8).</summary>
public enum ActionPhase
{
    Press,
    Repeat,
    Long,
}

/// <summary>Klasa urządzenia, z którego przyszła Action. Sygnatura urządzenia zostaje w <c>input.json</c>.</summary>
public enum ActionSource
{
    Remote,
    Keyboard,
    Gamepad,
}

/// <summary>
/// Jedno naciśnięcie z pilota, klawiatury albo pada zamienione przez InputHub na logiczną akcję (AD-8).
/// </summary>
/// <param name="TsMono">Milisekundy zegara monotonicznego (jak <c>time</c> z hooka LL).</param>
public sealed record Action(ActionKind Kind, ActionPhase Phase, ActionSource Source, long TsMono) : IOrchestratorMessage;
