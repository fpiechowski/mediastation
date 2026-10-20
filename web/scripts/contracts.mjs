// Generuje typy TS z JSON Schema eksportowanej przez Core (AD-16): <nazwa>.schema.json → <nazwa>.ts.
// Schema powstaje w `dotnet build` (tools/MediaStation.SchemaExport); wynik w contracts/generated nie jest w gicie.
import { existsSync } from 'node:fs';
import { mkdir, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compileFromFile } from 'json-schema-to-typescript';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const schemaDir = join(root, 'contracts', 'schema');
const outDir = join(root, 'contracts', 'generated');
const schemaSuffix = '.schema.json';

const schemas = existsSync(schemaDir)
  ? (await readdir(schemaDir)).filter((name) => name.endsWith(schemaSuffix)).sort()
  : [];
if (schemas.length === 0) {
  console.error(`contracts: brak *${schemaSuffix} w ${schemaDir} — uruchom najpierw \`dotnet build\` (eksport schematów z Core).`);
  process.exit(1);
}

const banner = `/* eslint-disable */
/**
 * WYGENEROWANE z JSON Schema MediaStation.Core przez web/scripts/contracts.mjs — nie edytuj ręcznie.
 * Zmień rekord w src/MediaStation.Core/Contracts i uruchom \`dotnet build\`.
 */`;

await mkdir(outDir, { recursive: true });

const outputs = new Set();
for (const schema of schemas) {
  const out = `${schema.slice(0, -schemaSuffix.length)}.ts`;
  outputs.add(out);

  const ts = await compileFromFile(join(schemaDir, schema), {
    bannerComment: banner,
    additionalProperties: false,
    strictIndexSignatures: true,
    cwd: schemaDir,
  });

  const outPath = join(outDir, out);
  const current = existsSync(outPath) ? await readFile(outPath, 'utf8') : null;
  if (current !== ts) {
    await writeFile(outPath, ts, 'utf8');
  }
  console.log(`contracts: ${outPath}`);
}

// Typ rekordu usuniętego z Core nie może zostać w generated.
for (const name of await readdir(outDir)) {
  if (name.endsWith('.ts') && !outputs.has(name)) {
    await rm(join(outDir, name));
    console.log(`contracts: usunięto nieaktualny ${join(outDir, name)}`);
  }
}
