#!/usr/bin/env node
/**
 * verify-vsix.mjs
 *
 * Automated packaging, activation smoke test, and CLI contract verification
 * for the canonical Otter VS Code extension (editors/vscode).
 */

import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import { createRequire } from 'node:module';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const extDir = path.resolve(__dirname, '..');
const repoRoot = path.resolve(extDir, '..', '..');
const vsixPath = path.join(extDir, 'otter-lang-1.0.1.vsix');

function run(cmd, args, cwd = extDir) {
    return execFileSync(cmd, args, {
        cwd,
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'pipe']
    });
}

console.log('=== Otter VS Code Extension Verification ===');

// 1. Verify bundled entry point exists and VSIX exists
assert.ok(fs.existsSync(path.join(extDir, 'dist', 'extension.js')), 'dist/extension.js must exist (run npm run compile first)');
assert.ok(fs.existsSync(vsixPath), 'otter-lang-1.0.1.vsix must exist (run npm run package first)');
const vsixStat = fs.statSync(vsixPath);
console.log(`[PASS] VSIX packaged (${(vsixStat.size / 1024).toFixed(1)} KB)`);
assert.ok(vsixStat.size > 40 * 1024, `VSIX unexpectedly small (${vsixStat.size} bytes) — vscode-languageclient may not be bundled`);

// 2. Extract VSIX into an isolated temp directory (no repo node_modules reachable)
const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'otter-vsix-verify-'));
try {
    if (process.platform === 'win32') {
        run('powershell.exe', [
            '-NoProfile',
            '-Command',
            `Expand-Archive -LiteralPath '${vsixPath.replace(/'/g, "''")}' -DestinationPath '${tmpDir.replace(/'/g, "''")}' -Force`
        ]);
    } else {
        run('unzip', ['-q', vsixPath, '-d', tmpDir]);
    }

    const requiredFiles = [
        'extension/package.json',
        'extension/dist/extension.js',
        'extension/syntaxes/otter.tmLanguage.json',
        'extension/language-configuration.json',
        'extension/LICENSE.txt',
        'extension/readme.md'
    ];
    for (const rel of requiredFiles) {
        const full = path.join(tmpDir, rel);
        assert.ok(fs.existsSync(full), `Missing required VSIX file: ${rel}`);
    }
    console.log(`[PASS] All ${requiredFiles.length} required VSIX files present`);

    // Verify manifest points to bundled dist/extension.js and syntax grammar is valid JSON
    const pkg = JSON.parse(fs.readFileSync(path.join(tmpDir, 'extension/package.json'), 'utf8'));
    assert.equal(pkg.main, './dist/extension.js');
    JSON.parse(fs.readFileSync(path.join(tmpDir, 'extension/syntaxes/otter.tmLanguage.json'), 'utf8'));
    JSON.parse(fs.readFileSync(path.join(tmpDir, 'extension/language-configuration.json'), 'utf8'));
    console.log('[PASS] Packaged manifests and TextMate grammar parse cleanly');

    // 3. Load the unpacked dist/extension.js from /tmp with a minimal stubbed 'vscode' module
    // to prove zero unresolved external npm dependencies (such as vscode-languageclient).
    const require = createRequire(import.meta.url);
    const Module = require('node:module');
    const origResolve = Module._resolveFilename;
    const baseVscode = {
        window: {
            createOutputChannel: () => ({ appendLine() {}, show() {}, dispose() {} }),
            showErrorMessage: () => Promise.resolve(undefined),
            showWarningMessage: () => Promise.resolve(undefined),
            showInformationMessage: () => Promise.resolve(undefined),
            showQuickPick: () => Promise.resolve(undefined),
            createTerminal: () => ({ show() {}, sendText() {} }),
            terminals: []
        },
        workspace: {
            getConfiguration: () => ({ get: () => '' }),
            createFileSystemWatcher: () => ({ dispose() {} }),
            workspaceFolders: []
        },
        commands: {
            registerCommand: () => ({ dispose() {} })
        },
        debug: {
            registerDebugAdapterDescriptorFactory: () => ({ dispose() {} })
        },
        languages: {},
        env: {},
        Uri: { file: (p) => ({ fsPath: p }), parse: (u) => ({ toString: () => u }) },
        EventEmitter: class { event = () => ({ dispose() {} }); fire() {} dispose() {} }
    };
    const vscodeStub = new Proxy(baseVscode, {
        get(target, prop) {
            if (prop in target) return target[prop];
            if (typeof prop === 'string' && /^[A-Z]/.test(prop)) {
                return class StubClass {};
            }
            return undefined;
        }
    });

    Module._resolveFilename = function (request, parent, isMain, options) {
        if (request === 'vscode') return 'vscode';
        return origResolve.call(this, request, parent, isMain, options);
    };
    require.cache['vscode'] = {
        id: 'vscode',
        filename: 'vscode',
        loaded: true,
        exports: vscodeStub
    };

    try {
        const bundledPath = path.join(tmpDir, 'extension/dist/extension.js');
        const extMod = require(bundledPath);
        assert.equal(typeof extMod.activate, 'function', 'Bundled extension must export activate()');
        assert.equal(typeof extMod.deactivate, 'function', 'Bundled extension must export deactivate()');
        assert.equal(typeof extMod.resolveOtterExecutable, 'function', 'Bundled extension must export resolveOtterExecutable()');
        console.log('[PASS] Unpacked dist/extension.js loads cleanly in isolation (vscode-languageclient bundled)');
    } finally {
        Module._resolveFilename = origResolve;
        delete require.cache['vscode'];
    }
} finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
}

// 4. Verify CLI JSON contracts invoked by extension.ts against built otter.dll
const releaseDll = path.join(repoRoot, 'src/Otter.Cli/bin/Release/net10.0/otter.dll');
const debugDll = path.join(repoRoot, 'src/Otter.Cli/bin/Debug/net10.0/otter.dll');
const cliDll = fs.existsSync(releaseDll) ? releaseDll : debugDll;
assert.ok(fs.existsSync(cliDll), 'Build Otter.Cli before running verify-vsix.mjs');

const runOtterJson = (args) => JSON.parse(run('dotnet', [cliDll, ...args], repoRoot));

const snippets = runOtterJson(['library', 'search', '', '--kind', 'snippet', '--json']);
assert.ok(Array.isArray(snippets) && snippets.length >= 30, `Expected >=30 snippets, got ${snippets?.length}`);
assert.ok(snippets[0].id && snippets[0].title, 'Snippet entry missing id/title');

const shownSnippet = runOtterJson(['snippet', 'show', snippets[0].id, '--json']);
assert.ok(typeof shownSnippet.template === 'string' && shownSnippet.template.length > 0, 'snippet show --json must return non-empty template');

const packs = runOtterJson(['pack', 'list', '--json']);
assert.ok(Array.isArray(packs) && packs.length >= 5, `Expected >=5 packs, got ${packs?.length}`);
assert.ok(packs.every((p) => p.publisher && p.name && p.version && Array.isArray(p.provides?.operations)), 'Every pack must expose publisher, name, version, and provides.operations');

const recipes = runOtterJson(['recipe', 'search', '', '--json']);
assert.ok(Array.isArray(recipes) && recipes.length >= 4, `Expected >=4 recipes, got ${recipes?.length}`);
assert.ok(recipes[0].id && recipes[0].title, 'Recipe entry missing id/title');

const doctorOut = run('dotnet', [cliDll, 'doctor'], repoRoot);
assert.ok(doctorOut.includes('Otter Doctor Diagnostics'), 'doctor output missing expected banner');

console.log(`[PASS] CLI JSON contracts verified (${snippets.length} snippets, ${packs.length} packs, ${recipes.length} recipes, doctor OK)`);
console.log('=== All VS Code Extension Checks PASSED ===');
