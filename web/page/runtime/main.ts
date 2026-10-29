// Wejście bundla IIFE (esbuild, AD-6): bez eksportów i bez globalnej nazwy. Host owija plik w funkcję
// z parametrem `__msPageConfig` (BrowserLaunch.BuildInitScript), więc jedyną globalną stroną zostaje binding hosta.
import { start, type PageConfig } from './runtime';

declare const __msPageConfig: PageConfig;

start(__msPageConfig, window);
