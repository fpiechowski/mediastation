using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediaStation.Core.Contracts;
using MediaStation.Core.Input;

namespace MediaStation.Core.Config;

/// <summary>Trzy pliki configu po walidacji (AD-11); <c>settings</c> z uzupełnionymi wartościami domyślnymi.</summary>
public sealed record LoadedConfig(AppsConfig Apps, InputConfig Input, Settings Settings);

/// <summary>Wynik walidacji: <see cref="Config"/> jest różny od <c>null</c> wtedy i tylko wtedy, gdy <see cref="Errors"/> jest puste.</summary>
public sealed record ConfigResult(LoadedConfig? Config, IReadOnlyList<ConfigError> Errors);

/// <summary>
/// Pełna walidacja configu przy starcie (AD-11). Czysta funkcja: App podaje stany plików, Core niczego nie czyta.
/// Per plik: preflight (<see cref="ConfigPreflight"/>) → ścisła deserializacja (<see cref="ConfigJson"/>, pierwszy błąd
/// struktury, bo STJ przerywa) → reguły semantyczne (wszystkie błędy). Kolejność błędów: apps → input → settings.
/// </summary>
public static class ConfigValidation
{
    public const int CdpPortMin = 9220;
    public const int CdpPortMax = 9239;

    /// <summary>
    /// Polityczna górna granica <c>startingTimeout</c> i <c>stoppingTimeout</c> w sekundach (F18): trzyma oba terminy
    /// daleko poniżej limitu terminu akceptowanego przez wstrzykiwany zegar, więc scheduler zawsze może je utworzyć.
    /// </summary>
    public const int MaxTimeoutSeconds = 600;

    public const string UnknownFieldReason = "nieznane pole";
    public const string DuplicateKeyReason = "zduplikowany klucz";
    public const string NullReason = "pole nie może być null";
    public const string WrongValueReason = "zła wartość albo typ";
    public const string SchemaMismatchReason = "niezgodne ze schematem";
    public const string NullObjectReason = "oczekiwano obiektu, a nie null";

    public static ConfigResult Validate(IReadOnlyDictionary<string, ConfigFileState> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var errors = new List<ConfigError>();
        var apps = Load<AppsConfig>(files, ConfigPreflight.AppsFile, errors, CheckApps);
        var input = Load<InputConfig>(files, ConfigPreflight.InputFile, errors, CheckInput);
        var settings = Load<Settings>(files, ConfigPreflight.SettingsFile, errors, CheckSettings);

        var config = errors.Count == 0 && apps is not null && input is not null && settings is not null
            ? new LoadedConfig(apps, input, settings)
            : null;
        return new ConfigResult(config, errors);
    }

    private delegate void Rules<in T>(T value, FileErrors errors);

    private sealed class FileErrors(string file, List<ConfigError> sink)
    {
        public int Count { get; private set; }

        public void Add(string path, string reason)
        {
            sink.Add(new ConfigError(file, path, reason));
            Count++;
        }
    }

    private static T? Load<T>(
        IReadOnlyDictionary<string, ConfigFileState> files,
        string file,
        List<ConfigError> errors,
        Rules<T> rules)
        where T : class
    {
        var state = files.TryGetValue(file, out var s) ? s : new ConfigFileState.Missing();
        if (ConfigPreflight.CheckFile(file, state) is { } preflight)
        {
            errors.Add(preflight);
            return null;
        }

        if (state is not ConfigFileState.Present present)
        {
            return null;
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(present.Text, ConfigJson.Options);
        }
        catch (JsonException ex)
        {
            errors.Add(StructureError(file, ex.Path, ex.Message));
            return null;
        }
        catch (NotSupportedException ex)
        {
            errors.Add(NotSupportedError(file, ex.Message));
            return null;
        }

        if (value is null)
        {
            errors.Add(new ConfigError(file, "$", NullObjectReason));
            return null;
        }

        var fileErrors = new FileErrors(file, errors);
        rules(value, fileErrors);
        return fileErrors.Count == 0 ? value : null;
    }

    // ── Struktura: polski powód zamiast komunikatu STJ ────────────────────────

    private static readonly Regex UnmappedProperty = new(@"The JSON property '(?<name>.*)' could not be mapped to any \.NET member", RegexOptions.CultureInvariant);
    private static readonly Regex DuplicateProperty = new(@"Duplicate property '(?<name>.*)' encountered", RegexOptions.CultureInvariant);
    private static readonly Regex DuplicateMetadata = new(@"duplicate '(?<name>.*)' metadata property", RegexOptions.CultureInvariant);
    private static readonly Regex DuplicateDictionaryKey = new(@"^Duplicate properties not allowed", RegexOptions.CultureInvariant);
    private static readonly Regex MissingRequired = new(@"was missing required properties including: (?<list>.*?)\.(\s|$)", RegexOptions.CultureInvariant);
    private static readonly Regex QuotedName = new(@"'(?<name>[^']*)'", RegexOptions.CultureInvariant);
    private static readonly Regex UnknownDiscriminator = new(@"unrecognized type discriminator id '(?<id>.*)'", RegexOptions.CultureInvariant);
    private static readonly Regex NullNotAllowed = new(@"doesn't allow (setting )?null values", RegexOptions.CultureInvariant);
    private static readonly Regex NotConvertible = new(@"could not be converted to", RegexOptions.CultureInvariant);
    private static readonly Regex MissingDiscriminator = new(@"must specify a type discriminator", RegexOptions.CultureInvariant);
    private static readonly Regex MessagePath = new(@"Path: (?<path>.*?) \| LineNumber", RegexOptions.CultureInvariant);

    private static ConfigError StructureError(string file, string? stjPath, string message)
    {
        var segments = JsonErrorPath.Parse(stjPath);
        var path = segments is null ? stjPath ?? "$" : JsonErrorPath.Format(segments);

        string reason;
        Match m;
        if ((m = UnmappedProperty.Match(message)).Success)
        {
            reason = $"{UnknownFieldReason} {Quote(m.Groups["name"].Value)}";
        }
        else if ((m = DuplicateProperty.Match(message)).Success || (m = DuplicateMetadata.Match(message)).Success)
        {
            reason = $"{DuplicateKeyReason} {Quote(m.Groups["name"].Value)}";
        }
        else if (DuplicateDictionaryKey.IsMatch(message))
        {
            reason = segments is [.., JsonErrorPath.Segment { Name: { } key }]
                ? $"{DuplicateKeyReason} {Quote(key)}"
                : DuplicateKeyReason;
        }
        else if ((m = MissingRequired.Match(message)).Success)
        {
            var names = QuotedName.Matches(m.Groups["list"].Value).Select(n => Quote(n.Groups["name"].Value)).ToArray();
            reason = names.Length == 1
                ? $"brak wymaganego pola {names[0]}"
                : $"brak wymaganych pól {string.Join(", ", names)}";
        }
        else if ((m = UnknownDiscriminator.Match(message)).Success)
        {
            reason = $"nieznany typ {Quote(m.Groups["id"].Value)} (dozwolone: {AllowedAppTypes()})";
        }
        else if (NullNotAllowed.IsMatch(message))
        {
            reason = NullReason;
        }
        else if (NotConvertible.IsMatch(message))
        {
            reason = WrongValueReason;
        }
        else
        {
            reason = SchemaMismatchReason;
        }

        return new ConfigError(file, path, reason);
    }

    private static ConfigError NotSupportedError(string file, string message)
    {
        var stjPath = MessagePath.Match(message) is { Success: true } m ? m.Groups["path"].Value : null;
        var path = JsonErrorPath.Parse(stjPath) is { } segments ? JsonErrorPath.Format(segments) : stjPath ?? "$";
        var reason = MissingDiscriminator.IsMatch(message)
            ? $"brak pola „type” (dozwolone: {AllowedAppTypes()})"
            : SchemaMismatchReason;
        return new ConfigError(file, path, reason);
    }

    private static string AllowedAppTypes() =>
        string.Join(", ", ConfigJson.Options.GetTypeInfo(typeof(AppEntry)).PolymorphismOptions!.DerivedTypes
            .Select(d => d.TypeDiscriminator?.ToString()));

    private static string Quote(string value) => $"„{value}”";

    // ── Reguły semantyczne ────────────────────────────────────────────────────

    // \z, nie $: w .NET `$` pasuje też przed końcowym \n („0x26\n” wywaliłby Convert.ToInt32).
    private static readonly Regex KebabCase = new(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant);
    private static readonly Regex BackPolicy = new(@"^(home|key:[A-Za-z0-9]+)\z", RegexOptions.CultureInvariant);
    private static readonly Regex HidKey = new(@"^0x([0-9A-Fa-f]{1,4}):0x([0-9A-Fa-f]{1,4})\z", RegexOptions.CultureInvariant);

    private const int HidConsumerPage = 0x000C;

    private static readonly int[] HidReservedUsages = [0x0223, 0x0224];

    private static readonly Regex VirtualKey = new(@"^0x[0-9A-Fa-f]{2}\z", RegexOptions.CultureInvariant);
    private static readonly Regex GitHubRepo = new(@"^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?/[A-Za-z0-9._-]+\z", RegexOptions.CultureInvariant);

    private static void CheckApps(AppsConfig config, FileErrors errors)
    {
        var appsPath = JsonErrorPath.Property("$", "apps");
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var ports = new Dictionary<int, string>();

        for (var i = 0; i < config.Apps.Count; i++)
        {
            var entryPath = JsonErrorPath.Index(appsPath, i);
            var entry = config.Apps[i];
            if (entry is null)
            {
                errors.Add(entryPath, NullObjectReason);
                continue;
            }

            var idPath = JsonErrorPath.Property(entryPath, "id");
            if (!KebabCase.IsMatch(entry.Id))
            {
                errors.Add(idPath, $"id {Quote(entry.Id)} nie jest w kebab-case (np. „netflix”, „plex-htpc”)");
            }
            else if (!ids.TryAdd(entry.Id, entryPath))
            {
                errors.Add(idPath, $"id {Quote(entry.Id)} jest już użyte w {ids[entry.Id]}");
            }

            switch (entry)
            {
                case WebApp web:
                    var portPath = JsonErrorPath.Property(entryPath, "cdpPort");
                    if (web.CdpPort is < CdpPortMin or > CdpPortMax)
                    {
                        errors.Add(portPath, $"port {web.CdpPort} poza zakresem {CdpPortMin}–{CdpPortMax}");
                    }
                    else if (!ports.TryAdd(web.CdpPort, entryPath))
                    {
                        errors.Add(portPath, $"port {web.CdpPort} jest już użyty w {ports[web.CdpPort]}");
                    }

                    break;

                case MoonlightApp moonlight:
                    CheckBack(moonlight.Back, entryPath, errors);
                    break;

                case NativeApp native:
                    var argsPath = JsonErrorPath.Property(entryPath, "args");
                    if (native.Args is null)
                    {
                        errors.Add(argsPath, NullReason);
                    }
                    else
                    {
                        for (var a = 0; a < native.Args.Count; a++)
                        {
                            if (native.Args[a] is null)
                            {
                                errors.Add(JsonErrorPath.Index(argsPath, a), "oczekiwano tekstu, a nie null");
                            }
                        }
                    }

                    CheckBack(native.Back, entryPath, errors);
                    break;
            }
        }
    }

    // `null` w init-właściwości rekordu z konstruktorem parametrycznym (back, args) STJ przepuszcza mimo
    // RespectNullableAnnotations (sprawdzone testem), więc reguła semantyczna daje ten sam powód co struktura.
    private static void CheckBack(string? back, string entryPath, FileErrors errors)
    {
        var backPath = JsonErrorPath.Property(entryPath, "back");
        if (back is null)
        {
            errors.Add(backPath, NullReason);
        }
        else if (!BackPolicy.IsMatch(back))
        {
            errors.Add(backPath, $"{Quote(back)} nie jest polityką Back (dozwolone: „home” albo „key:<nazwa>”)");
        }
    }

    private static void CheckInput(InputConfig config, FileErrors errors)
    {
        var devicesPath = JsonErrorPath.Property("$", "devices");
        var signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var kindsByVirtualKey = new Dictionary<int, (ActionKind Kind, string Path)>();

        for (var i = 0; i < config.Devices.Count; i++)
        {
            var devicePath = JsonErrorPath.Index(devicesPath, i);
            var device = config.Devices[i];
            if (device is null)
            {
                errors.Add(devicePath, NullObjectReason);
                continue;
            }

            var signaturePath = JsonErrorPath.Property(devicePath, "signature");
            if (string.IsNullOrWhiteSpace(device.Signature))
            {
                errors.Add(signaturePath, "sygnatura nie może być pusta");
            }
            else if (!signatures.TryAdd(device.Signature, devicePath))
            {
                errors.Add(signaturePath, $"sygnatura {Quote(device.Signature)} jest już użyta w {signatures[device.Signature]}");
            }
            else if (device.Signature == InputDevice.WildcardSignature && device.Source != ActionSource.Keyboard)
            {
                // Wildcard z `remote` logowałby w kalibracji surowe kody zwykłej klawiatury (AD-13).
                errors.Add(
                    JsonErrorPath.Property(devicePath, "source"),
                    $"sygnatura {Quote(InputDevice.WildcardSignature)} wymaga source {Quote("keyboard")}");
            }
            else if (DeviceSignature.HasEmbeddedWildcard(device.Signature))
            {
                // Ścieżka Raw Input nigdy nie zawiera `*`, więc taki wpis nie dopasuje niczego (DW-8).
                errors.Add(
                    signaturePath,
                    $"sygnatura {Quote(device.Signature)} może zawierać „*” tylko jako całą sygnaturę");
            }

            var keysPath = JsonErrorPath.Property(devicePath, "keys");
            var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, kind) in device.Keys)
            {
                var keyPath = JsonErrorPath.Property(keysPath, key);
                if (!VirtualKey.IsMatch(key))
                {
                    errors.Add(keyPath, $"klucz {Quote(key)} nie jest kodem VK w formacie 0xNN");
                    continue;
                }

                var virtualKey = Convert.ToInt32(key[2..], 16);
                if (virtualKey is < 0x01 or > 0xFE)
                {
                    errors.Add(keyPath, $"kod VK {Quote(key)} poza zakresem 0x01–0xFE");
                    continue;
                }

                if (!codes.TryAdd(key, key))
                {
                    errors.Add(keyPath, $"kod VK {Quote(key)} powtarza {Quote(codes[key])}");
                    continue;
                }

                if (kind == ActionKind.AppMenu)
                {
                    errors.Add(keyPath, "„appMenu” niedozwolone w mapowaniu (AppMenu to długie „ok” w Shell)");
                    continue;
                }

                // Ten sam VK w dwóch urządzeniach musi mieć ten sam kind: połknięcie i kind zapadają po samym VK,
                // a walidacja broni politykę przed niejednoznacznym mapowaniem.
                if (kindsByVirtualKey.TryGetValue(virtualKey, out var first))
                {
                    if (first.Kind != kind)
                    {
                        errors.Add(
                            keyPath,
                            $"kod VK {Quote(key)} ma już kind {Quote(KindName(first.Kind))} w {first.Path}");
                    }
                }
                else
                {
                    kindsByVirtualKey.Add(virtualKey, (kind, keyPath));
                }
            }

            CheckHid(device, devicePath, errors);
        }

        CheckGamepad(config, errors);

        var timingPath = JsonErrorPath.Property("$", "timing");
        CheckPositive(config.Timing.LongPressMs, JsonErrorPath.Property(timingPath, "longPressMs"), errors);
        CheckPositive(config.Timing.RepeatDelayMs, JsonErrorPath.Property(timingPath, "repeatDelayMs"), errors);
        CheckPositive(config.Timing.RepeatIntervalMs, JsonErrorPath.Property(timingPath, "repeatIntervalMs"), errors);
        CheckRepeatFitsHold(config.Timing, timingPath, errors);

        var homePath = JsonErrorPath.Property("$", "gamepadHome");
        var buttons = new Dictionary<GamepadButton, string>();
        for (var i = 0; i < config.GamepadHome.Count; i++)
        {
            var button = config.GamepadHome[i];
            var buttonPath = JsonErrorPath.Index(homePath, i);
            if (!buttons.TryAdd(button, buttonPath))
            {
                errors.Add(buttonPath, $"przycisk {Quote(JsonNamingPolicy.CamelCase.ConvertName(button.ToString()))} powtarza {buttons[button]}");
            }
        }

        if (buttons.Count < 2)
        {
            errors.Add(homePath, "potrzeba co najmniej 2 różnych przycisków");
        }
    }

    /// <summary>
    /// Sekcja <c>hid</c> urządzenia (AD-8): tylko <c>remote</c>, strona Consumer <c>0x000C</c>, wartość <c>keyboard</c>,
    /// bez usage Home/Back (<c>0x0223</c>/<c>0x0224</c>, korelacja <c>source</c>) i bez usage 0 (znacznik zwolnienia);
    /// jeden klucz na Action w urządzeniu.
    /// </summary>
    private static void CheckHid(InputDevice device, string devicePath, FileErrors errors)
    {
        if (device.Hid is null)
        {
            return;
        }

        var hidPath = JsonErrorPath.Property(devicePath, "hid");
        if (device.Source != ActionSource.Remote || device.Signature == InputDevice.WildcardSignature)
        {
            errors.Add(hidPath, $"sekcja „hid” jest dozwolona tylko na urządzeniu z source {Quote("remote")} (nie na {Quote(InputDevice.WildcardSignature)})");
            return;
        }

        var seen = new Dictionary<(int Page, int Usage), string>();
        var kinds = new Dictionary<ActionKind, string>();
        foreach (var (key, kind) in device.Hid)
        {
            var keyPath = JsonErrorPath.Property(hidPath, key);
            var match = HidKey.Match(key);
            if (!match.Success)
            {
                errors.Add(keyPath, $"klucz {Quote(key)} nie jest w formacie 0x<strona>:0x<usage>");
                continue;
            }

            var page = Convert.ToInt32(match.Groups[1].Value, 16);
            var usage = Convert.ToInt32(match.Groups[2].Value, 16);
            if (page != HidConsumerPage)
            {
                errors.Add(keyPath, $"strona {Quote($"0x{page:X4}")} niedozwolona (tylko 0x000C)");
            }
            else if (usage == 0 || Array.IndexOf(HidReservedUsages, usage) >= 0)
            {
                errors.Add(keyPath, $"usage {Quote($"0x{usage:X4}")} niedozwolone (0x0000 to zwolnienie, 0x0223 i 0x0224 to Home i Back)");
            }
            else if (kind != ActionKind.Keyboard)
            {
                errors.Add(keyPath, $"wartość {Quote(KindName(kind))} niedozwolona w „hid” (tylko „keyboard”)");
            }
            else if (!seen.TryAdd((page, usage), keyPath))
            {
                errors.Add(keyPath, $"klucz {Quote(key)} powtarza {seen[(page, usage)]}");
            }
            else if (!kinds.TryAdd(kind, keyPath))
            {
                errors.Add(keyPath, $"Action {Quote(KindName(kind))} ma już klucz w {kinds[kind]}");
            }
        }
    }

    /// <summary>
    /// Mapowanie pada (AD-8): <c>appMenu</c> nie ma odpowiednika na padzie, a przycisk kombinacji Home nie może mieć
    /// własnego mapowania, żeby Home nie dublowało Actions składowych.
    /// </summary>
    private static void CheckGamepad(InputConfig config, FileErrors errors)
    {
        if (config.Gamepad is null)
        {
            errors.Add(JsonErrorPath.Property("$", "gamepad"), NullReason);
            return;
        }

        var gamepadPath = JsonErrorPath.Property("$", "gamepad");
        foreach (var (button, kind) in config.Gamepad.Mappings())
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(button.ToString());
            var buttonPath = JsonErrorPath.Property(gamepadPath, name);
            if (kind == ActionKind.AppMenu)
            {
                errors.Add(buttonPath, "„appMenu” niedozwolone w mapowaniu (AppMenu to długie „ok” w Shell)");
            }
            else if (config.GamepadHome.Contains(button))
            {
                errors.Add(
                    buttonPath,
                    $"przycisk {Quote(name)} należy do gamepadHome i nie może mieć własnego mapowania; ustaw dla niego null");
            }
        }
    }

    private static void CheckSettings(Settings settings, FileErrors errors)
    {
        CheckPositive(settings.RatingPendingDays, JsonErrorPath.Property("$", "ratingPendingDays"), errors);
        CheckPositive(settings.StartingTimeout, JsonErrorPath.Property("$", "startingTimeout"), errors);
        CheckPositive(settings.StoppingTimeout, JsonErrorPath.Property("$", "stoppingTimeout"), errors);
        CheckPositive(settings.CdpConnectTimeout, JsonErrorPath.Property("$", "cdpConnectTimeout"), errors);
        CheckPositive(settings.CdpReconnectTimeout, JsonErrorPath.Property("$", "cdpReconnectTimeout"), errors);
        CheckAtMost(settings.StartingTimeout, MaxTimeoutSeconds, "startingTimeout", errors);
        CheckAtMost(settings.StoppingTimeout, MaxTimeoutSeconds, "stoppingTimeout", errors);

        if (settings.CdpConnectTimeout > 0
            && settings.StartingTimeout > 0
            && settings.CdpConnectTimeout >= settings.StartingTimeout)
        {
            errors.Add(
                JsonErrorPath.Property("$", "cdpConnectTimeout"),
                $"musi być mniejsze niż startingTimeout ({settings.StartingTimeout})");
        }

        if (!GitHubRepo.IsMatch(settings.Update.Repo))
        {
            errors.Add(
                JsonErrorPath.Property(JsonErrorPath.Property("$", "update"), "repo"),
                $"{Quote(settings.Update.Repo)} nie jest w formacie owner/name");
        }
    }

    private static void CheckPositive(int value, string path, FileErrors errors)
    {
        if (value <= 0)
        {
            errors.Add(path, "wartość musi być większa od 0");
        }
    }

    /// <summary>Nazwa <c>kind</c> w komunikatach: camelCase, jak w <c>input.json</c>.</summary>
    private static string KindName(ActionKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());

    /// <summary>
    /// Siatka <c>repeat</c> kończy się po <see cref="InputPolicy.MaxRepeatHoldMs"/>: opóźnienie początkowe i pierwszy
    /// interwał muszą się w nim zmieścić, inaczej config po cichu wyłącza repeat.
    /// </summary>
    private static void CheckRepeatFitsHold(InputTiming timing, string timingPath, FileErrors errors)
    {
        const int max = InputPolicy.MaxRepeatHoldMs;
        if (timing.RepeatDelayMs >= max)
        {
            errors.Add(JsonErrorPath.Property(timingPath, "repeatDelayMs"), $"wartość musi być mniejsza niż {max}");
        }
        else if (timing.RepeatIntervalMs > 0 && (long)timing.RepeatDelayMs + timing.RepeatIntervalMs >= max)
        {
            errors.Add(
                JsonErrorPath.Property(timingPath, "repeatIntervalMs"),
                $"repeatDelayMs + repeatIntervalMs musi być mniejsze niż {max}");
        }
    }

    private static void CheckAtMost(int value, int max, string name, FileErrors errors)
    {
        if (value > max)
        {
            errors.Add(JsonErrorPath.Property("$", name), $"wartość musi być nie większa niż {max}");
        }
    }
}

/// <summary>
/// Ścisła deserializacja plików configu (AD-11): struktura zgodna ze schematem (pola i nazwy enumów), a reguły
/// semantyczne — zakresy, kebab-case, VK, unikalność — tylko w runtime. Baza to <see cref="ContractJson.ReadOptions"/>,
/// do tego reguły plików: nieznane pole i zduplikowany klucz to błąd, a <c>type</c> nie musi być pierwszy.
/// </summary>
public static class ConfigJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(ContractJson.ReadOptions)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            AllowOutOfOrderMetadataProperties = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
