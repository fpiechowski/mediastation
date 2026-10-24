using MediaStation.Core.Input;

namespace MediaStation.App.Platform.Input;

/// <summary>Spike 3.11, <c>MS_NO_GATE=1</c>: sonda bramki zawsze mówi „managed", więc bramka foreground nie blokuje niczego.</summary>
internal sealed class AlwaysManagedProbe : IForegroundProbe
{
    public bool IsManaged() => true;
}
