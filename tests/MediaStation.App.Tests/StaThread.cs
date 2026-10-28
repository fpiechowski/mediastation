using System.Runtime.ExceptionServices;

namespace MediaStation.App.Tests;

/// <summary>
/// Uruchamia ciało testu na świeżym wątku STA: <c>HwndSource</c> (WPF) wymaga STA, a okno i jego pętla
/// komunikatów muszą żyć na jednym wątku. Wyjątek z wątku wraca do testu z zachowanym śladem.
/// </summary>
internal static class StaThread
{
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
