using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

/// <summary>
/// Granica Core (AD-3) sprawdzana na metadanych skompilowanego assembly: uzupełnia BannedApiAnalyzers,
/// który nie widzi typów użytych tylko w sygnaturach i polach.
/// </summary>
public class CoreBoundaryTests
{
    private static readonly string CorePath = typeof(ContractJson).Assembly.Location;

    private static readonly string[] ForbiddenAssemblyNames =
        ["PresentationCore", "PresentationFramework", "WindowsBase", "System.Xaml", "System.IO.Compression.ZipFile"];

    private static readonly string[] ForbiddenAssemblyPrefixes =
        ["MediaStation.", "Microsoft.Web.WebView2", "Microsoft.Win32", "Microsoft.Windows"];

    private static readonly string[] ForbiddenNamespaces =
    [
        "Microsoft.Win32",
        "System.Windows",
        "Microsoft.Web.WebView2",
        "System.IO.Enumeration",
        "System.IO.MemoryMappedFiles",
        "System.IO.IsolatedStorage",
    ];

    private static readonly HashSet<string> ForbiddenFileSystemTypes =
    [
        "System.IO.File",
        "System.IO.Directory",
        "System.IO.FileInfo",
        "System.IO.DirectoryInfo",
        "System.IO.FileSystemInfo",
        "System.IO.FileStream",
        "System.IO.DriveInfo",
        "System.IO.FileSystemWatcher",
        "System.IO.Path",
        "System.IO.RandomAccess",
        "System.IO.Compression.ZipFile",
        "System.IO.Compression.ZipFileExtensions",
    ];

    [Fact]
    public void Core_is_not_platform_specific()
    {
        var attributes = Read(CorePath, reader => reader.GetAssemblyDefinition().GetCustomAttributes()
            .Select(handle => AttributeTypeName(reader, reader.GetCustomAttribute(handle)))
            .ToArray());

        Assert.Contains("System.Runtime.Versioning.TargetFrameworkAttribute", attributes);
        Assert.DoesNotContain("System.Runtime.Versioning.TargetPlatformAttribute", attributes);
    }

    [Fact]
    public void Core_references_no_app_wpf_webview_or_windows_assemblies()
    {
        var references = Read(CorePath, reader => reader.AssemblyReferences
            .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
            .ToArray());

        Assert.Contains("System.Text.Json", references);
        var forbidden = references
            .Where(name => ForbiddenAssemblyNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                || ForbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Assert.Empty(forbidden);
    }

    [Fact]
    public void Core_references_no_file_system_or_windows_types()
    {
        var types = TypeReferences(CorePath);

        Assert.Contains("System.Text.Json.JsonSerializer", types);
        Assert.DoesNotContain(types, IsForbiddenType);
    }

    [Fact]
    public void Type_reference_check_detects_file_system_types()
    {
        // Kontrola pozytywna: ten assembly testów używa File (OpenRead), więc sprawdzenie musi to wykryć.
        var types = TypeReferences(typeof(CoreBoundaryTests).Assembly.Location);

        Assert.Contains("System.IO.File", types.Where(IsForbiddenType));
    }

    [Fact]
    public void Core_has_no_pinvoke_methods()
    {
        var pinvoke = Read(CorePath, reader => reader.MethodDefinitions
            .Select(reader.GetMethodDefinition)
            .Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(method => reader.GetString(method.Name))
            .ToArray());

        Assert.Empty(pinvoke);
    }

    [Fact]
    public void Core_has_no_wake_signal_rule()
    {
        // Reguła deduplikacji wybudzenia mieszka w adapterze (App/Platform/System), nie w Core:
        // Orchestrator widzi wyłącznie gotowy SystemResumed.
        var typeNames = Read(CorePath, reader => reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Select(type => reader.GetString(type.Name))
            .ToArray());

        Assert.Contains("SystemResumed", typeNames);
        Assert.DoesNotContain("WakeSignalGate", typeNames);
        Assert.DoesNotContain(typeNames, name => name.Contains("WakeSignal", StringComparison.Ordinal));
    }

    private static bool IsForbiddenType(string fullName) =>
        ForbiddenFileSystemTypes.Contains(fullName)
        || ForbiddenNamespaces.Any(ns => fullName.StartsWith(ns + ".", StringComparison.Ordinal));

    private static string[] TypeReferences(string path) => Read(path, reader => reader.TypeReferences
        .Select(handle => TypeReferenceName(reader, handle))
        .ToArray());

    private static T Read<T>(string path, Func<MetadataReader, T> read)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return read(pe.GetMetadataReader());
    }

    /// <summary>Pełna nazwa typu; dla typu zagnieżdżonego bierze namespace typu zewnętrznego.</summary>
    private static string TypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return TypeReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        }

        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute) => attribute.Constructor.Kind switch
    {
        HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent switch
        {
            { Kind: HandleKind.TypeReference } parent => TypeReferenceName(reader, (TypeReferenceHandle)parent),
            _ => string.Empty,
        },
        HandleKind.MethodDefinition => TypeDefinitionName(
            reader,
            reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
        _ => string.Empty,
    };

    private static string TypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
