#!/usr/bin/env node
/**
 * tools/build-playground-wasm.mjs
 *
 * Publishes src/Otter.Playground.Wasm (real C# Otter.Language + Otter.Runtime
 * targeting .NET 10 browser-wasm) into a clean static bundle directory with a
 * deterministic SHA-256 manifest (playground-manifest.json).
 */

import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import zlib from 'node:zlib';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..');
const projectPath = path.join(repoRoot, 'src/Otter.Playground.Wasm/Otter.Playground.Wasm.csproj');

const args = process.argv.slice(2);
let outDir = path.join(repoRoot, 'artifacts/playground-wasm');
for (let i = 0; i < args.length; i++) {
    if ((args[i] === '--out' || args[i] === '-o') && args[i + 1]) {
        outDir = path.resolve(args[i + 1]);
        i++;
    }
}

const candidateStages = [
    path.join(repoRoot, 'src/Otter.Playground.Wasm/bin/Release/net10.0/publish/wwwroot'),
    path.join(repoRoot, 'src/Otter.Playground.Wasm/bin/Release/net10.0/browser-wasm/publish/wwwroot')
];

console.log('=== Building Otter Playground WebAssembly Runtime ===');
execFileSync(
    'dotnet',
    ['publish', projectPath, '-c', 'Release', '--nologo'],
    { cwd: repoRoot, stdio: 'inherit' }
);

const publishStage = candidateStages.find((d) => fs.existsSync(path.join(d, '_framework', 'dotnet.js')));
if (!publishStage) {
    throw new Error(`Expected published WASM framework under:\n  ${candidateStages.join('\n  ')}`);
}

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(outDir, { recursive: true });

// Copy only runtime files needed by the browser worker (exclude debug symbols & precompressed duplicates)
function copyFiltered(srcDir, dstDir) {
    fs.mkdirSync(dstDir, { recursive: true });
    for (const entry of fs.readdirSync(srcDir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
        const srcPath = path.join(srcDir, entry.name);
        const dstPath = path.join(dstDir, entry.name);
        if (entry.isDirectory()) {
            copyFiltered(srcPath, dstPath);
            continue;
        }
        if (entry.name.endsWith('.pdb') || entry.name.endsWith('.gz') || entry.name.endsWith('.br') || entry.name.endsWith('.map')) {
            continue;
        }
        fs.copyFileSync(srcPath, dstPath);
    }
}

copyFiltered(publishStage, outDir);

let sourceCommit = 'unknown';
try {
    sourceCommit = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repoRoot, encoding: 'utf8' }).trim();
} catch {
    // ignore outside git checkout
}

const files = [];
let totalRawBytes = 0;
let totalGzipBytes = 0;

function collectFiles(dir, prefix = '') {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
        const full = path.join(dir, entry.name);
        const rel = prefix ? `${prefix}/${entry.name}` : entry.name;
        if (entry.isDirectory()) {
            collectFiles(full, rel);
            continue;
        }
        if (rel === 'playground-manifest.json') continue;
        const buf = fs.readFileSync(full);
        const sha256 = crypto.createHash('sha256').update(buf).digest('hex');
        const gzipBytes = zlib.gzipSync(buf, { level: 9 }).length;
        totalRawBytes += buf.length;
        totalGzipBytes += gzipBytes;
        files.push({
            path: rel,
            bytes: buf.length,
            gzipBytes,
            sha256
        });
    }
}

collectFiles(outDir);

const treeHash = crypto
    .createHash('sha256')
    .update(files.map((f) => `${f.path}:${f.sha256}`).join('\n'))
    .digest('hex');

const manifest = {
    schemaVersion: 1,
    languageVersion: '1.0.1',
    engine: 'otter-net-browser-wasm',
    sourceCommit,
    bundleTreeSha256: treeHash,
    fileCount: files.length,
    totalRawBytes,
    totalGzipBytes,
    files
};

fs.writeFileSync(path.join(outDir, 'playground-manifest.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8');

console.log(`Output directory: ${outDir}`);
console.log(`Files: ${files.length}`);
console.log(`Total raw size:  ${(totalRawBytes / (1024 * 1024)).toFixed(2)} MB`);
console.log(`Total gzip size: ${(totalGzipBytes / (1024 * 1024)).toFixed(2)} MB`);
console.log(`Bundle SHA-256:  ${treeHash}`);
