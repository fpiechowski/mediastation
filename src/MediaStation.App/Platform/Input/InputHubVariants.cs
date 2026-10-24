namespace MediaStation.App.Platform.Input;

/// <summary>
/// Warianty eksperymentalne spike'u 3.11, domyślnie wyłączone (brak zmiennej = dotychczasowe zachowanie).
/// <c>MS_HUB_NO_RAWINPUT=1</c>: bez <c>RegisterRawInputDevices</c>; <c>MS_HUB_RAW_THREAD=1</c>: Raw Input na osobnym wątku;
/// <c>MS_HUB_RAW_HID_ONLY=1</c>: rejestracja tylko HID Consumer, bez klawiatury (dodane po wyniku eksperymentu 4).
/// </summary>
internal sealed record InputHubVariants(bool NoRawInput, bool RawThread, bool RawHidOnly = false)
{
    public static InputHubVariants None { get; } = new(false, false, false);

    public bool Any => NoRawInput || RawThread || RawHidOnly;

    public static InputHubVariants FromEnvironment() => new(
        Environment.GetEnvironmentVariable("MS_HUB_NO_RAWINPUT") == "1",
        Environment.GetEnvironmentVariable("MS_HUB_RAW_THREAD") == "1",
        Environment.GetEnvironmentVariable("MS_HUB_RAW_HID_ONLY") == "1");
}
