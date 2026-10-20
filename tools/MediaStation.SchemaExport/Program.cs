// Eksportuje JSON Schema kontraktów Core do web/contracts/schema (AD-16).
// Użycie: MediaStation.SchemaExport [katalog-wyjściowy]; domyślnie web/contracts/schema względem repo.
using System.Text;
using MediaStation.Core.Contracts;

var outDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web", "contracts", "schema"));

Directory.CreateDirectory(outDir);
var schemas = ContractJson.ExportSchemas();

foreach (var (fileName, json) in schemas)
{
    var path = Path.Combine(outDir, fileName);
    var schema = json + "\n";

    // Zapis tylko przy zmianie, żeby nie psuć inkrementalności buildu web.
    if (!File.Exists(path) || File.ReadAllText(path) != schema)
    {
        File.WriteAllText(path, schema, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    Console.WriteLine($"SchemaExport: {path}");
}

// Schemat rekordu usuniętego z Core nie może dalej generować typu TS.
foreach (var stale in Directory.EnumerateFiles(outDir, "*.schema.json"))
{
    if (!schemas.ContainsKey(Path.GetFileName(stale)))
    {
        File.Delete(stale);
        Console.WriteLine($"SchemaExport: usunięto nieaktualny {stale}");
    }
}
