using System;
using System.Collections.Generic;

namespace MediaStation.App.Platform.Audio;

/// <summary>
/// Wrapper COM za interfejsem (AD-3, AD-15): zna Core Audio, ale nie zna reguł. <see cref="Enumerate"/> zwraca sesje
/// wszystkich aktywnych urządzeń renderujących; porażka usługi audio to wyjątek, który silnik zamienia na
/// <c>AudioUnavailable</c>.
/// </summary>
internal interface IAudioSessionBackend : IDisposable
{
    /// <summary>Bieżące sesje; wołający zwalnia każdą z nich (<see cref="IDisposable.Dispose"/>).</summary>
    IReadOnlyList<IAudioSession> Enumerate();
}

/// <summary>Jedna sesja audio. Metody <c>Try*</c> zwracają „brak”, gdy interfejs nie jest dostępny (E_NOINTERFACE).</summary>
internal interface IAudioSession : IDisposable
{
    /// <summary>Trwały w obrębie życia sesji identyfikator instancji (obejmuje urządzenie).</summary>
    string Key { get; }

    /// <summary>PID procesu sesji; 0 dla dźwięków systemowych.</summary>
    int Pid { get; }

    /// <summary>Ustawia wyciszenie przez <c>ISimpleAudioVolume</c>; <c>false</c>, gdy interfejsu brak.</summary>
    bool TrySetMute(bool mute);

    /// <summary>Poziomy kanałów <c>IChannelAudioVolume</c> albo <c>null</c>, gdy interfejsu brak lub nie ma kanałów.</summary>
    float[]? TryGetChannelVolumes();

    /// <summary>Ustawia poziomy kanałów; <c>false</c>, gdy interfejsu brak.</summary>
    bool TrySetChannelVolumes(float[] levels);
}
