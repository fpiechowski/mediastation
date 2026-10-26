using System;

namespace MediaStation.App.Platform.Audio;

/// <summary>
/// Kiedy wykonać następny skan sesji (AD-15). Notyfikacje sesji i urządzeń wołają skan od razu, a ta polityka
/// dokłada dwie rzeczy: krótki ponowny skan po sesji, której PID jest jeszcze nieznany w migawce procesów
/// (potomek bywa widoczny w audio przed migawką), oraz okresowy skan jako siatkę pod notyfikacje, dopóki
/// jakiekolwiek drzewo jest wyciszone (mute w ok. 1 s).
/// </summary>
internal sealed class AudioSweepSchedule
{
    /// <summary>Opóźnienie ponownego skanu po nieprzypisanej sesji.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Okres skanu zapasowego przy wyciszonym drzewie.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Ile razy z rzędu ponawiamy skan po nieprzypisanej sesji, zanim zostanie sam skan okresowy.</summary>
    public const int MaxRetries = 5;

    private int _retries;

    /// <summary>Opóźnienie do następnego skanu albo <c>null</c>, gdy skan na żądanie wystarczy.</summary>
    public TimeSpan? NextDelay(bool unresolvedSession, bool anyMutedRoot)
    {
        if (unresolvedSession && _retries < MaxRetries)
        {
            _retries++;
            return RetryDelay;
        }

        if (!unresolvedSession)
        {
            _retries = 0;
        }

        return anyMutedRoot ? PollInterval : null;
    }
}
