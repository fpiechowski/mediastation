using System.Collections.Generic;
using System.Text.Json.Serialization;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Config;

/// <summary>
/// <c>config/apps.json</c>: rejestr aplikacji (AD-11). Kolejność wpisów to kolejność kafelków w zakładce <c>category</c>.
/// </summary>
public sealed record AppsConfig(IReadOnlyList<AppEntry> Apps);

/// <summary>Zakładka ekranu głównego, w której stoi kafelek aplikacji (AD-11).</summary>
public enum AppCategory
{
    Video,
    Audio,
    Game,
}

/// <summary>Przeglądarka aplikacji webowej; tabela „serwis → przeglądarka” żyje w <c>apps.json</c> (AD-11).</summary>
public enum BrowserKind
{
    Edge,
    Chrome,
}

/// <summary>
/// Wpis <c>apps.json</c>; <c>type</c> (<c>web|moonlight|native</c>) wybiera rekord pochodny.
/// Rekordy mają wyłącznie pola pliku: każda inna właściwość byłaby polem, które plik po cichu przyjmuje.
/// Domyślne polityki <c>back</c> i <c>pointer</c> (AD-11) siedzą w rekordach, więc konsumenci ich nie powtarzają.
/// </summary>
/// <param name="Id"><c>appId</c>, kebab-case, unikalny w całym systemie (AD-11).</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WebApp), "web")]
[JsonDerivedType(typeof(MoonlightApp), "moonlight")]
[JsonDerivedType(typeof(NativeApp), "native")]
public abstract record AppEntry(string Id, AppCategory Category, string Title, string Icon);

/// <summary>
/// Aplikacja w przeglądarce sterowanej przez CDP (AD-1). Back idzie zawsze do strony (AD-7, AD-8), więc nie ma pola <c>back</c>.
/// </summary>
/// <param name="Service">Nazwa drivera w <c>web/page/drivers/&lt;service&gt;</c> (AD-6).</param>
/// <param name="CdpPort">Port CDP na loopbacku, 9220–9239, unikalny (AD-11).</param>
public sealed record WebApp(
    string Id,
    AppCategory Category,
    string Title,
    string Icon,
    string Service,
    BrowserKind Browser,
    int CdpPort)
    : AppEntry(Id, Category, Title, Icon)
{
    public PointerPolicy Pointer { get; init; } = PointerPolicy.Blocked;
}

/// <summary>Klient Moonlight streamujący <see cref="App"/> z hosta <see cref="Host"/>.</summary>
public sealed record MoonlightApp(
    string Id,
    AppCategory Category,
    string Title,
    string Icon,
    string Host,
    string App)
    : AppEntry(Id, Category, Title, Icon)
{
    /// <summary>Polityka Back (AD-8): <c>home</c> albo <c>key:&lt;nazwa&gt;</c>.</summary>
    public string Back { get; init; } = "key:Escape";

    public PointerPolicy Pointer { get; init; } = PointerPolicy.Allowed;
}

/// <summary>Aplikacja natywna uruchamiana z <see cref="Exe"/> (np. Plex HTPC).</summary>
public sealed record NativeApp(
    string Id,
    AppCategory Category,
    string Title,
    string Icon,
    string Exe)
    : AppEntry(Id, Category, Title, Icon)
{
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>Polityka Back (AD-8): <c>home</c> albo <c>key:&lt;nazwa&gt;</c>.</summary>
    public string Back { get; init; } = "home";

    public PointerPolicy Pointer { get; init; } = PointerPolicy.Allowed;
}
