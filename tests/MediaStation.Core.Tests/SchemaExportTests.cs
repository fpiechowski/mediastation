using System.Text;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

// Eksport JSON Schema kontraktów Core do web/contracts/schema (AD-16).
// Uruchamia go target BuildWeb (dotnet test --filter Category=SchemaExport) oraz `npm run contracts:export`.
// Eksport w przebiegu testów zamiast osobnego exe: Application Control blokuje świeże niepodpisane pliki wykonywalne.
public class SchemaExportTests
{
    [Fact]
    [Trait("Category", "SchemaExport")]
    public void ExportSchemas_zapisuje_schematy_do_web_contracts_schema()
    {
        var outDir = Path.Combine(FindRepoRoot(), "web", "contracts", "schema");
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
        }

        // Schemat rekordu usuniętego z Core nie może dalej generować typu TS.
        foreach (var stale in Directory.EnumerateFiles(outDir, "*.schema.json"))
        {
            if (!schemas.ContainsKey(Path.GetFileName(stale)))
            {
                File.Delete(stale);
            }
        }

        Assert.NotEmpty(schemas);
        foreach (var fileName in schemas.Keys)
        {
            Assert.True(File.Exists(Path.Combine(outDir, fileName)), $"Brak schematu {fileName} w {outDir}.");
        }
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MediaStation.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Nie znaleziono korzenia repo (MediaStation.slnx) powyżej " + AppContext.BaseDirectory);
    }
}
