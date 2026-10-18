// Generuje typy TS z JSON Schema eksportowanej przez Core (AD-16).
// Schema powstaje w `dotnet build` (tools/MediaStation.SchemaExport); wynik w contracts/generated nie jest w gicie.
import { existsSync } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compileFromFile } from 'json-schema-to-typescript';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const schemaDir = join(root, 'contracts', 'schema');
const outDir = join(root, 'contracts', 'generated');

const contracts = [{ schema: 'shell-snapshot.schema.json', out: 'shell-snapshot.ts' }];

const banner = `/* eslint-disable */
/**
 * WYGENEROWANE z JSON Schema MediaStation.Core przez web/scripts/contracts.mjs — nie edytuj ręcznie.
 * Zmień rekord w src/MediaStation.Core/Contracts i uruchom \`dotnet build\`.
 */`;

await mkdir(outDir, { recursive: true });

for (const { schema, out } of contracts) {
  const schemaPath = join(schemaDir, schema);
  if (!existsSync(schemaPath)) {
    console.error(`contracts: brak ${schemaPath} — uruchom najpierw \`dotnet build\` (eksport schematu z Core).`);
    process.exit(1);
  }

  const ts = await compileFromFile(schemaPath, {
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
