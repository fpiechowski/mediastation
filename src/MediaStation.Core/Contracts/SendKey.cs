namespace MediaStation.Core.Contracts;

/// <summary>
/// Komenda Orchestratora do wstrzyknięcia jednego klawisza (AD-3, AD-8): wykonawca wysyła key-down i key-up
/// z <c>InjectionMarker.MediaStation</c>, więc polityka rozpozna własny klawisz, gdy wróci z hooka.
/// Port nie odpowiada <see cref="CommandCompleted"/> — wstrzyknięcie jest fire-and-forget, a porażkę widać w logu.
/// </summary>
/// <param name="Vk">Kod wirtualnego klawisza (1–255); adapter odrzuca wartości spoza zakresu.</param>
public sealed record SendKey(long CmdId, int Vk) : Command(CmdId);
