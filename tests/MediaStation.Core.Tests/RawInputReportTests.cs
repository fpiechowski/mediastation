using MediaStation.App.Platform.Input;

namespace MediaStation.Core.Tests;

/// <summary>
/// Walidacja odczytu <c>GetRawInputData</c>: pełny raport jest poprawny, błąd API (<c>uint.MaxValue</c>) i odczyt
/// krótszy od nagłówka są odrzucane — porównanie z rozmiarem bufora <c>pcbSize</c> nie jest wyznacznikiem.
/// </summary>
public class RawInputReportTests
{
    /// <summary><c>sizeof(RAWINPUTHEADER)</c> na x64.</summary>
    private const uint HeaderSize = 24;

    [Fact]
    public void Full_report_read_into_a_larger_buffer_is_valid()
    {
        // 40 bajtów raportu w 256-bajtowym buforze: pcbSize (256) nie jest liczbą skopiowanych bajtów.
        Assert.True(RawInputReport.IsValid(40, HeaderSize));
    }

    [Fact]
    public void Api_error_is_invalid()
    {
        Assert.False(RawInputReport.IsValid(uint.MaxValue, HeaderSize));
    }

    [Fact]
    public void Read_shorter_than_the_header_is_invalid()
    {
        Assert.False(RawInputReport.IsValid(8, HeaderSize));
    }
}
