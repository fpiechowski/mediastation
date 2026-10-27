using System;

namespace MediaStation.App.Platform.Audio;

/// <summary>
/// Kontekst własnych zmian głośności AudioRoutera (bez COM). Zmiany z tym GUID-em robi sam silnik, więc ich
/// powiadomienia nie zlecają skanu; zmiana z dowolnym innym kontekstem (mikser Windows, aplikacja) go zleca.
/// </summary>
internal static class AudioChangeContext
{
    /// <summary>Stały GUID kontekstu własnych zmian.</summary>
    public static readonly Guid Own = new("6b0f7b3e-4c1d-4f0a-9d52-5a3f1e8c2d71");

    /// <summary>Czy powiadomienie z danym kontekstem ma zlecić skan (brak kontekstu i obce tak, własne nie).</summary>
    public static bool ShouldSweep(Guid? context) => context is null || context != Own;
}
