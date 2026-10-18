// Eksportuje JSON Schema kontraktów Core do web/contracts/schema (AD-16).
// Użycie: MediaStation.SchemaExport [katalog-wyjściowy]; domyślnie web/contracts/schema względem repo.
using System.Text;
using MediaStation.Core.Contracts;

var outDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web", "contracts", "schema"));

Directory.CreateDirectory(outDir);
var path = Path.Combine(outDir, "shell-snapshot.schema.json");
var schema = ContractJson.ExportSchema() + "\n";

// Zapis tylko przy zmianie, żeby nie psuć inkrementalności buildu web.
if (!File.Exists(path) || File.ReadAllText(path) != schema)
{
    File.WriteAllText(path, schema, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

Console.WriteLine($"SchemaExport: {path}");
