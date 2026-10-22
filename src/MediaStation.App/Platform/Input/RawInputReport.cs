namespace MediaStation.App.Platform.Input;

/// <summary>
/// Walidacja odczytu <c>GetRawInputData</c> bez Win32: <c>uint.MaxValue</c> to błąd API, a wynik krótszy od
/// nagłówka <c>RAWINPUTHEADER</c> to uszkodzony raport. <c>pcbSize</c> nie jest rozmiarem kopii (API go nie
/// zmniejsza), więc nie wolno porównywać z nim wyniku.
/// </summary>
internal static class RawInputReport
{
    /// <summary>Czy <paramref name="bytesRead"/> opisuje pełny raport z nagłówkiem?</summary>
    internal static bool IsValid(uint bytesRead, uint headerSize) =>
        bytesRead != uint.MaxValue && bytesRead >= headerSize;
}
