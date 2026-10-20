namespace MediaStation.Core.Logging;

/// <summary>
/// Port zapisu gotowej linii logu (bez znaku nowej linii) do pliku o podanej nazwie. Implementacja w App (I/O poza Core, AD-3);
/// nie przepuszcza wyjątków zapisu.
/// </summary>
public interface ILogLineSink
{
    void Append(string fileName, string line);
}
