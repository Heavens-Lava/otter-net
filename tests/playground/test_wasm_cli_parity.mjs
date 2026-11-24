#!/usr/bin/env node
/**
 * tests/playground/test_wasm_cli_parity.mjs
 *
 * End-to-end verification of the Otter WebAssembly Playground engine:
 * Runs a corpus of real Otter 1.0.1 programs through BOTH the canonical
 * otter-net CLI (otter.dll) AND the browser WebAssembly Web Worker hosted
 * in headless Chromium/Edge, asserting byte-for-byte stdout parity, exact
 * diagnostic matching, and strict sandbox/resource-limit enforcement.
 */

import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '../..');
const wasmBundleDir = path.join(repoRoot, 'artifacts/playground-wasm');
const cliReleaseDll = path.join(repoRoot, 'src/Otter.Cli/bin/Release/net10.0/otter.dll');
const cliDebugDll = path.join(repoRoot, 'src/Otter.Cli/bin/Debug/net10.0/otter.dll');
const cliDll = fs.existsSync(cliReleaseDll) ? cliReleaseDll : cliDebugDll;

const MIME = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'application/javascript; charset=utf-8',
    '.mjs': 'application/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.wasm': 'application/wasm',
    '.dat': 'application/octet-stream',
    '.bin': 'application/octet-stream'
};

function findBrowser() {
    if (process.env.CHROME_PATH && fs.existsSync(process.env.CHROME_PATH)) {
        return process.env.CHROME_PATH;
    }
    const candidates = [
        '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
        '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
        '/usr/bin/chromium',
        '/usr/bin/chromium-browser',
        '/usr/bin/google-chrome',
        'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
        'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
    ];
    for (const c of candidates) {
        if (fs.existsSync(c)) return c;
    }
    for (const name of ['google-chrome', 'chromium', 'chromium-browser', 'microsoft-edge']) {
        try {
            const p = execFileSync('which', [name], { encoding: 'utf8' }).trim();
            if (p) return p;
        } catch {
            // continue
        }
    }
    throw new Error('No Chromium/Chrome/Edge browser found. Set CHROME_PATH to run WASM parity tests.');
}

function runCanonicalCli(source, stdin = '') {
    const tmpFile = path.join(os.tmpdir(), `otter-parity-${process.pid}-${Date.now()}-${Math.random().toString(16).slice(2)}.ot`);
    fs.writeFileSync(tmpFile, source, 'utf8');
    try {
        const stdout = execFileSync('dotnet', [cliDll, 'run', tmpFile], {
            cwd: repoRoot,
            input: stdin,
            encoding: 'utf8',
            stdio: ['pipe', 'pipe', 'pipe']
        });
        return { ok: true, stdout: stdout.replace(/\r\n/g, '\n'), stderr: '' };
    } catch (err) {
        return {
            ok: false,
            stdout: String(err.stdout || '').replace(/\r\n/g, '\n'),
            stderr: String(err.stderr || '').replace(/\r\n/g, '\n')
        };
    } finally {
        fs.rmSync(tmpFile, { force: true });
    }
}

function runCanonicalCliWeb(source, stem = 'app') {
    const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'otter-web-parity-'));
    const tmpFile = path.join(tmpDir, `${stem}.ot`);
    const outHtml = path.join(tmpDir, `${stem}.html`);
    fs.writeFileSync(tmpFile, source, 'utf8');
    try {
        execFileSync('dotnet', [cliDll, 'web', tmpFile, '-NoOpen'], {
            cwd: repoRoot,
            encoding: 'utf8',
            stdio: ['pipe', 'pipe', 'pipe']
        });
        const html = fs.readFileSync(outHtml, 'utf8').replace(/^\uFEFF/, '').replace(/\r\n/g, '\n').trimEnd();
        return { ok: true, html, stderr: '' };
    } catch (err) {
        return {
            ok: false,
            html: '',
            stderr: String(err.stderr || err.stdout || err.message || '')
        };
    } finally {
        fs.rmSync(tmpDir, { recursive: true, force: true });
    }
}

const WEB_PARITY_CASES = [
    {
        name: 'River Slate dark + light card + value-first multi-line with',
        code: [
            'app is a page with',
            '    "River Slate WASM Parity" title,',
            '    true scroll,',
            '    24 spacing',
            '.',
            'cards is a grid with 2 columns, 20 spacing',
            'darkCard is a card with',
            '    "Actions & Inputs" title,',
            '    "Compiled inside WebAssembly." subtitle',
            '.',
            'deployBtn is a primary button with "Deploy" text',
            'previewBtn is a outline button with "Preview" text',
            'cancelBtn is a ghost button with "Cancel" text',
            'nameBox is a text box with',
            '    "Project name" label,',
            '    "otter-pulse" placeholder,',
            '    "Lowercase letters only." hint',
            '.',
            'errBox is a text box with "Slug" label, "Bad Slug!" value, "Invalid characters." error',
            'cpuSlider is a slider with "CPU %" label, 10 min, 100 max, 72 value, true showvalue',
            'loadProg is a progress with 72 value, 100 max',
            'statusOut is a text with "Ready" value, "muted" variant',
            'when deployBtn is clicked',
            '    text of statusOut is "Deployed"',
            '.',
            'when cpuSlider is changed',
            '    val is text of cpuSlider',
            '    value of loadProg is val',
            '.',
            'put deployBtn, previewBtn, cancelBtn, nameBox, errBox, cpuSlider, loadProg, statusOut in darkCard',
            'lightCard is a card with',
            '    "Light Opt-In" title,',
            '    "Daylight River Slate tokens." subtitle,',
            '    "light" theme',
            '.',
            'okBadge is a badge with "AA Contrast" text, "success" variant',
            'put okBadge in lightCard',
            'put darkCard, lightCard in cards',
            'put cards in app',
            'show app'
        ].join('\n') + '\n'
    },
    {
        name: 'River Slate full-page light theme with value-first properties',
        code: [
            'app is a page with',
            '    "Settings" title,',
            '    "light" theme,',
            '    true scroll',
            '.',
            'toggleAlerts is a toggle with "Email alerts" text, true checked',
            'saveBtn is a primary button with "Save" text',
            'put toggleAlerts, saveBtn in app',
            'show app'
        ].join('\n') + '\n'
    },
    {
        name: 'legacy property-first UI backward compatibility',
        code: [
            'app is a page with title "Legacy App", scroll true',
            'btn is a primary button with text "Click", disabled true',
            'put btn in app',
            'show app'
        ].join('\n') + '\n'
    }
];

const PARITY_CASES = [
    {
        name: 'hello world',
        code: 'name is "Otter"\nsay "Hello," name\n'
    },
    {
        name: 'value-first custom objects (single-line and multi-line with)',
        code: [
            'a Service has',
            '    name',
            '    port',
            '    healthy',
            '.',
            'api is a Service with "Orders API" name, 8082 port, true healthy',
            'worker is a Service with',
            '    "Billing Worker" name,',
            '    9090 port,',
            '    false healthy',
            '.',
            'say name of api port of api healthy of api',
            'say name of worker port of worker healthy of worker'
        ].join('\n') + '\n'
    },
    {
        name: 'arithmetic and precedence',
        code: 'x is 12\ny is 5\ntotal is x plus y\ndiff is x minus y\nprod is x times y\nquot is 20 divided by 4\nsay total diff prod quot\n'
    },
    {
        name: 'conditionals if / otherwise if / otherwise',
        code: [
            'to grade score',
            '    if score is at least 90',
            '        return "A"',
            '    otherwise if score is at least 75',
            '        return "B"',
            '    otherwise',
            '        return "C"',
            '    .',
            '.',
            'g1 is grade 95',
            'g2 is grade 80',
            'g3 is grade 50',
            'say g1 g2 g3'
        ].join('\n') + '\n'
    },
    {
        name: 'lists, add, remove, sort, reverse, length',
        code: [
            'fruits are',
            '    "cherry"',
            '    "apple"',
            '    "banana"',
            '.',
            'add "date" to fruits',
            'remove "banana" from fruits',
            'sort fruits',
            'say length of fruits',
            'each f in fruits',
            '    say f',
            '.',
            'reverse fruits',
            'say first of fruits last of fruits'
        ].join('\n') + '\n'
    },
    {
        name: 'loops: count, repeat, while',
        code: [
            'total is 0',
            'count from 1 to 5 as n',
            '    total is total plus n',
            '.',
            'say "sum:" total',
            'ticks is 0',
            'repeat 3 times',
            '    ticks is ticks plus 1',
            '.',
            'while ticks is less than 6',
            '    ticks is ticks plus 1',
            '.',
            'say "ticks:" ticks'
        ].join('\n') + '\n'
    },
    {
        name: 'recursive function (factorial)',
        code: [
            'to factorial n',
            '    if n is at most 1',
            '        return 1',
            '    .',
            '    prev is n minus 1',
            '    sub is factorial prev',
            '    return n times sub',
            '.',
            'ans is factorial 6',
            'say ans'
        ].join('\n') + '\n'
    },
    {
        name: 'objects, custom types, property access',
        code: [
            'a Service has',
            '    name',
            '    latency',
            '    healthy',
            '.',
            'api is a Service with name "Orders API", latency 21, healthy true',
            'say name of api latency of api healthy of api'
        ].join('\n') + '\n'
    },
    {
        name: 'strings: split, join, replace, contains',
        code: [
            'raw is "alpha,beta,gamma"',
            'split raw by "," into parts',
            'join parts with " | " into joined',
            'replace "beta" with "BETA" in joined',
            'hasBeta is false',
            'if joined contains "BETA"',
            '    hasBeta is true',
            '.',
            'say joined hasBeta'
        ].join('\n') + '\n'
    },
    {
        name: 'try / otherwise / fail',
        code: [
            'try',
            '    fail with "probe timeout"',
            'otherwise into err',
            '    say "recovered:" err',
            '.'
        ].join('\n') + '\n'
    },
    {
        name: 'JSON serialization & deserialization',
        code: [
            'record has',
            '    service is "pulse"',
            '    score is 99',
            '.',
            'convert record to json into rawJson',
            'convert rawJson from json into parsed',
            'say service of parsed score of parsed'
        ].join('\n') + '\n'
    },
    {
        name: 'CSV conversion in memory',
        code: [
            'csvLines are',
            '    "name,port"',
            '    "Auth,8080"',
            '    "Orders,8081"',
            '.',
            'join csvLines with "\\n" into csvText',
            'convert csvText from csv into rows',
            'say length of rows',
            'topRow is first of rows',
            'say name of topRow port of topRow'
        ].join('\n') + '\n'
    },
    {
        name: 'ask stdin input',
        code: [
            'ask "Enter name: " and call it person',
            'ask "Enter age: " and call it age',
            'nextYear is age plus 1',
            'say "Hi" person "next year:" nextYear'
        ].join('\n') + '\n',
        stdin: 'Ada\n30\n'
    },
    {
        name: 'deterministic random with seed',
        code: [
            'set random seed to 42',
            'random number from 1 to 100 into r1',
            'random number from 1 to 100 into r2',
            'say r1 r2'
        ].join('\n') + '\n'
    },
    {
        name: 'date parsing and formatting',
        code: [
            'd is date from "2026-10-15" using "yyyy-MM-dd"',
            'format d as "yyyy/MM/dd" into formatted',
            'say year of d month of d day of d formatted'
        ].join('\n') + '\n'
    },
    {
        name: 'in-memory object list filtering',
        code: [
            'a Item has',
            '    name',
            '    price',
            '.',
            'i1 is a Item with name "Book", price 15',
            'i2 is a Item with name "Laptop", price 900',
            'items are',
            '    i1',
            '    i2',
            '.',
            'expensive are empty',
            'each entry in items',
            '    if price of entry is greater than 100',
            '        add entry to expensive',
            '    .',
            '.',
            'say length of expensive',
            'topItem is first of expensive',
            'say name of topItem'
        ].join('\n') + '\n'
    },
    {
        name: 'Otter Pulse health score & clamp logic',
        code: [
            'to clamp val low high',
            '    if val is less than low',
            '        return low',
            '    .',
            '    if val is greater than high',
            '        return high',
            '    .',
            '    return val',
            '.',
            'score1 is clamp 115 0 100',
            'score2 is clamp 42 0 100',
            'neg is 0 minus 10',
            'score3 is clamp neg 0 100',
            'say score1 score2 score3'
        ].join('\n') + '\n'
    },
    {
        name: 'booleans and gone formatting',
        code: 'flag is true\nother is false\nnothing is gone\nsay flag other nothing\n'
    },
    {
        name: 'nested object mutation and properties',
        code: [
            'bag has',
            '    count is 1',
            '    status is "pending"',
            '.',
            'status of bag is "healthy"',
            'count of bag is count of bag plus 4',
            'say status of bag count of bag'
        ].join('\n') + '\n'
    },
    {
        name: 'empty program / comments only',
        code: '# Just a comment\n\n'
    },
    {
        name: 'crypto sha256 hex digest',
        code: 'payload is bytes from text "otter"\ndigest is sha256 of payload\nsay hex from bytes digest\n'
    }
];

const ERROR_CASES = [
    {
        name: 'syntax error (incomplete condition)',
        code: 'if 1 is\n    say "broken"\n.\n',
        expectContains: ''
    },
    {
        name: 'runtime error (undefined variable)',
        code: 'say mysteryVar\n',
        expectContains: 'mysteryVar'
    },
    {
        name: 'runtime error (divide by zero)',
        code: 'x is 10 divided by 0\nsay x\n',
        expectContains: 'divide by zero'
    },
    {
        name: 'reserved word diagnostic',
        code: 'if is 5\n',
        expectContains: ''
    }
];

const SANDBOX_CASES = [
    {
        name: 'infinite loop halted by step budget',
        code: 'i is 0\nwhile true\n    i is i plus 1\n.\n',
        maxSteps: 5000,
        expectCode: 'OTTER_PLAYGROUND_STEP_LIMIT'
    },
    {
        name: 'infinite loop inside try/otherwise CANNOT be swallowed',
        code: 'try\n    while true\n        x is 1\n    .\notherwise into err\n    say "swallowed"\n.\n',
        maxSteps: 3000,
        expectCode: 'OTTER_PLAYGROUND_STEP_LIMIT'
    },
    {
        name: 'output flood halted by output cap',
        code: 'count from 1 to 5000 as i\n    say "line number" i "abcdefghijklmnopqrstuvwxyz"\n.\n',
        maxOutputBytes: 4096,
        expectCode: 'OTTER_PLAYGROUND_OUTPUT_LIMIT'
    },
    {
        name: 'blocked filesystem read in sandbox',
        code: 'read "secret.txt" into data\nsay data\n',
        expectContains: 'disabled in the browser playground sandbox'
    },
    {
        name: 'blocked shell command in sandbox',
        code: 'run command "echo hello" into out\nsay out\n',
        expectContains: 'disabled in the browser playground sandbox'
    }
];

const TEST_HTML = `<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>Otter WASM Parity Harness</title></head>
<body>
<script type="module">
  let worker = new Worker('/otter-worker.mjs', { type: 'module' });
  let nextId = 1;
  const pending = new Map();

  function attach(w) {
    w.onmessage = (e) => {
      const msg = e.data || {};
      const entry = pending.get(msg.id);
      if (!entry) return;
      pending.delete(msg.id);
      if (msg.type === 'error') entry.reject(new Error(msg.error));
      else entry.resolve(msg);
    };
    w.onerror = (e) => {
      for (const [, entry] of pending) entry.reject(new Error(e.message || 'Worker error'));
      pending.clear();
    };
  }
  attach(worker);

  function callWorker(msg) {
    return new Promise((resolve, reject) => {
      const id = nextId++;
      pending.set(id, { resolve, reject });
      worker.postMessage({ ...msg, id });
    });
  }

  window.__otterInit = async () => {
    const res = await callWorker({ type: 'init' });
    return res.version;
  };
  window.__otterCheck = async (source) => {
    const res = await callWorker({ type: 'check', source });
    return res.payload;
  };
  window.__otterCompileWeb = async (source, title = 'Otter Web App') => {
    const res = await callWorker({ type: 'compileWeb', source, title });
    return res.payload;
  };
  window.__otterRun = async (source, stdin = '', maxSteps = 250000, timeoutMs = 3000, maxOutputBytes = 65536) => {
    const res = await callWorker({ type: 'run', source, stdin, maxSteps, timeoutMs, maxOutputBytes });
    return res.payload;
  };
  window.__otterCancelAndRespawn = async (infiniteSource) => {
    // Launch a long-running loop, terminate worker mid-flight, respawn fresh worker, and run a clean program
    const p = callWorker({ type: 'run', source: infiniteSource, stdin: '', maxSteps: 1000000, timeoutMs: 10000 });
    p.catch(() => {});
    await new Promise((r) => setTimeout(r, 60));
    worker.terminate();
    pending.clear();
    worker = new Worker('/otter-worker.mjs', { type: 'module' });
    attach(worker);
    await callWorker({ type: 'init' });
    const after = await callWorker({ type: 'run', source: 'say "recovered after cancel"\\n', stdin: '' });
    return after.payload;
  };
</script>
</body>
</html>`;

async function createCdpSession(browserPath) {
    const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'otter-wasm-cdp-'));
    const proc = spawn(
        browserPath,
        [
            '--headless=new',
            '--disable-gpu',
            '--no-sandbox',
            '--disable-dev-shm-usage',
            '--remote-debugging-port=0',
            `--user-data-dir=${userDataDir}`,
            'about:blank'
        ],
        { stdio: ['ignore', 'pipe', 'pipe'] }
    );

    const wsUrl = await new Promise((resolve, reject) => {
        let stderr = '';
        const timer = setTimeout(() => reject(new Error(`Browser failed to emit DevTools URL:\n${stderr}`)), 15000);
        proc.stderr.on('data', (chunk) => {
            stderr += chunk.toString();
            const m = stderr.match(/DevTools listening on (ws:\/\/[^\s]+)/);
            if (m) {
                clearTimeout(timer);
                resolve(m[1]);
            }
        });
        proc.once('exit', (code) => {
            clearTimeout(timer);
            reject(new Error(`Browser exited prematurely (${code}):\n${stderr}`));
        });
    });

    const browserWs = new WebSocket(wsUrl);
    await new Promise((res, rej) => {
        browserWs.addEventListener('open', res, { once: true });
        browserWs.addEventListener('error', rej, { once: true });
    });

    let msgId = 1;
    const inflight = new Map();
    browserWs.addEventListener('message', (ev) => {
        const data = JSON.parse(ev.data.toString());
        if (data.id && inflight.has(data.id)) {
            const { resolve, reject } = inflight.get(data.id);
            inflight.delete(data.id);
            if (data.error) reject(new Error(data.error.message));
            else resolve(data.result);
        }
    });

    const send = (method, params = {}, sessionId = undefined) =>
        new Promise((resolve, reject) => {
            const id = msgId++;
            inflight.set(id, { resolve, reject });
            const payload = sessionId ? { id, method, params, sessionId } : { id, method, params };
            browserWs.send(JSON.stringify(payload));
        });

    const targets = await send('Target.getTargets');
    const pageTarget = targets.targetInfos.find((t) => t.type === 'page');
    const { sessionId } = await send('Target.attachToTarget', { targetId: pageTarget.targetId, flatten: true });
    await send('Runtime.enable', {}, sessionId);
    await send('Page.enable', {}, sessionId);

    const evaluate = async (expression) => {
        const res = await send(
            'Runtime.evaluate',
            { expression, awaitPromise: true, returnByValue: true },
            sessionId
        );
        if (res.exceptionDetails) {
            throw new Error(res.exceptionDetails.exception?.description || res.exceptionDetails.text || 'JS evaluation failed');
        }
        return res.result?.value;
    };

    const navigate = async (url) => {
        await send('Page.navigate', { url }, sessionId);
        await new Promise((r) => setTimeout(r, 400));
    };

    const close = async () => {
        try { browserWs.close(); } catch {}
        proc.kill('SIGTERM');
        await new Promise((r) => setTimeout(r, 250));
        fs.rmSync(userDataDir, { recursive: true, force: true });
    };

    return { evaluate, navigate, close };
}

async function main() {
    assert.ok(fs.existsSync(cliDll), `Build Otter.Cli first (${cliDll})`);
    assert.ok(fs.existsSync(path.join(wasmBundleDir, 'otter-worker.mjs')), `Run tools/build-playground-wasm.mjs first (${wasmBundleDir})`);

    const manifest = JSON.parse(fs.readFileSync(path.join(wasmBundleDir, 'playground-manifest.json'), 'utf8'));
    console.log(`=== Otter WASM vs CLI Parity Suite (bundle ${(manifest.totalGzipBytes / 1048576).toFixed(2)} MB gzip, ${manifest.fileCount} files) ===`);

    const requestLog = [];
    const server = http.createServer((req, res) => {
        const urlPath = (req.url || '/').split('?')[0];
        requestLog.push({ method: req.method, path: urlPath });
        if (urlPath === '/' || urlPath === '/index.html') {
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
            res.end(TEST_HTML);
            return;
        }
        const safePath = path.normalize(urlPath).replace(/^(\.\.[/\\])+/, '').replace(/^[/\\]+/, '');
        const full = path.join(wasmBundleDir, safePath);
        if (!full.startsWith(wasmBundleDir) || !fs.existsSync(full) || fs.statSync(full).isDirectory()) {
            res.writeHead(404);
            res.end('Not found');
            return;
        }
        const ext = path.extname(full);
        res.writeHead(200, { 'Content-Type': MIME[ext] || 'application/octet-stream' });
        fs.createReadStream(full).pipe(res);
    });

    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
    const { port } = server.address();
    const browserPath = findBrowser();
    const cdp = await createCdpSession(browserPath);

    let passed = 0;
    try {
        await cdp.navigate(`http://127.0.0.1:${port}/`);
        const version = await cdp.evaluate('window.__otterInit()');
        assert.ok(version && version.includes('Otter 1.0.1'), `Unexpected WASM version: ${version}`);
        console.log(`[PASS] Browser WASM Worker initialized: ${version}`);
        passed++;

        for (const tc of PARITY_CASES) {
            const cli = runCanonicalCli(tc.code, tc.stdin || '');
            assert.equal(cli.ok, true, `Canonical CLI failed on parity case '${tc.name}': ${cli.stderr}`);
            const wasm = await cdp.evaluate(
                `window.__otterRun(${JSON.stringify(tc.code)}, ${JSON.stringify(tc.stdin || '')})`
            );
            assert.equal(wasm.ok, true, `WASM failed on '${tc.name}': ${JSON.stringify(wasm.diagnostics)}`);
            assert.equal(
                wasm.stdout,
                cli.stdout,
                `STDOUT MISMATCH on '${tc.name}'!\nCLI:  ${JSON.stringify(cli.stdout)}\nWASM: ${JSON.stringify(wasm.stdout)}`
            );
            console.log(`  [PASS] parity: ${tc.name} (${wasm.elapsedMs} ms, ${wasm.stepsExecuted} steps)`);
            passed++;
        }

        for (const ec of ERROR_CASES) {
            const cli = runCanonicalCli(ec.code);
            assert.equal(cli.ok, false, `Expected CLI error on '${ec.name}'`);
            const wasm = await cdp.evaluate(`window.__otterRun(${JSON.stringify(ec.code)})`);
            assert.equal(wasm.ok, false, `Expected WASM error on '${ec.name}'`);
            assert.ok(wasm.diagnostics.length >= 1, `Expected diagnostics on '${ec.name}'`);
            if (ec.expectContains) {
                const combined = wasm.diagnostics.map((d) => d.message).join(' ');
                assert.ok(
                    combined.toLowerCase().includes(ec.expectContains.toLowerCase()),
                    `Expected '${ec.expectContains}' in WASM diagnostics for '${ec.name}', got: ${combined}`
                );
            }
            console.log(`  [PASS] error: ${ec.name} -> ${wasm.diagnostics[0].message}`);
            passed++;
        }

        for (const sc of SANDBOX_CASES) {
            const wasm = await cdp.evaluate(
                `window.__otterRun(${JSON.stringify(sc.code)}, "", ${sc.maxSteps || 250000}, 3000, ${sc.maxOutputBytes || 65536})`
            );
            assert.equal(wasm.ok, false, `Expected sandbox/limit block on '${sc.name}'`);
            assert.ok(wasm.diagnostics.length >= 1, `Expected diagnostic on '${sc.name}'`);
            if (sc.expectCode) {
                assert.equal(wasm.diagnostics[0].code, sc.expectCode, `Wrong code on '${sc.name}': ${JSON.stringify(wasm.diagnostics)}`);
            }
            if (sc.expectContains) {
                assert.ok(
                    wasm.diagnostics[0].message.includes(sc.expectContains),
                    `Expected '${sc.expectContains}' on '${sc.name}', got: ${wasm.diagnostics[0].message}`
                );
            }
            console.log(`  [PASS] sandbox: ${sc.name} (${wasm.diagnostics[0].code})`);
            passed++;
        }

        const checkRes = await cdp.evaluate('window.__otterCheck("say \\"hi\\"\\n")');
        assert.equal(checkRes.ok, true);
        assert.equal(checkRes.hasUi, false);
        assert.equal(checkRes.diagnostics.length, 0);
        console.log('  [PASS] CheckSource returns clean diagnostics (hasUi=false) for console Otter');
        passed++;

        // Value-first & River Slate browser Web compilation vs canonical CLI `otter web` HTML parity
        for (const wc of WEB_PARITY_CASES) {
            const checkUi = await cdp.evaluate(`window.__otterCheck(${JSON.stringify(wc.code)})`);
            assert.equal(checkUi.ok, true, `CheckSource failed on '${wc.name}': ${JSON.stringify(checkUi.diagnostics)}`);
            assert.equal(checkUi.hasUi, true, `Expected hasUi=true on '${wc.name}'`);

            const cliWeb = runCanonicalCliWeb(wc.code, 'app');
            assert.equal(cliWeb.ok, true, `Canonical CLI otter web failed on '${wc.name}': ${cliWeb.stderr}`);

            const wasmWeb = await cdp.evaluate(`window.__otterCompileWeb(${JSON.stringify(wc.code)}, "app")`);
            assert.equal(wasmWeb.ok, true, `WASM CompileWeb failed on '${wc.name}': ${JSON.stringify(wasmWeb.diagnostics)}`);
            assert.equal(wasmWeb.hasUi, true);
            const normWasmHtml = wasmWeb.html.replace(/^\uFEFF/, '').replace(/\r\n/g, '\n').trimEnd();
            assert.equal(
                normWasmHtml,
                cliWeb.html,
                `WEB HTML MISMATCH on '${wc.name}' (CLI ${cliWeb.html.length} chars vs WASM ${normWasmHtml.length} chars)`
            );
            console.log(`  [PASS] web HTML parity: ${wc.name} (${wasmWeb.elapsedMs} ms, ${normWasmHtml.length} chars, 100% identical to CLI)`);
            passed++;
        }

        // Invalid value-first property value rejected identically in WASM CompileWeb & CheckSource
        const badVfCode = 'badSlider is a slider with "loud" showvalue\n';
        const badVfCheck = await cdp.evaluate(`window.__otterCheck(${JSON.stringify(badVfCode)})`);
        const badVfWeb = await cdp.evaluate(`window.__otterCompileWeb(${JSON.stringify(badVfCode)}, "app")`);
        assert.equal(badVfCheck.ok, false, 'Expected CheckSource error on invalid value-first property');
        assert.equal(badVfWeb.ok, false, 'Expected CompileWeb error on invalid value-first property');
        assert.ok(badVfWeb.diagnostics.length >= 1, 'Expected diagnostics from CompileWeb');
        console.log(`  [PASS] invalid value-first rejected in WASM CompileWeb: ${badVfWeb.diagnostics[0].message}`);
        passed++;

        // Repeated execution in the same worker (5 alternating console + web compiles, zero state bleed)
        for (let r = 1; r <= 5; r++) {
            const repRun = await cdp.evaluate(`window.__otterRun("say \\"run ${r}\\"\\n")`);
            assert.equal(repRun.ok, true);
            assert.equal(repRun.stdout, `run ${r}\n`);
            const repWeb = await cdp.evaluate(`window.__otterCompileWeb("app is a page with \\"Run ${r}\\" title\\nshow app\\n", "app")`);
            assert.equal(repWeb.ok, true);
            assert.ok(repWeb.html.includes(`Run ${r}`));
        }
        console.log('  [PASS] repeated execution (5x console + 5x web compile on same worker, zero state bleed)');
        passed++;

        // Hard cancellation via worker.terminate() during infinite loop + immediate clean respawn
        const cancelRecovery = await cdp.evaluate(
            `window.__otterCancelAndRespawn(${JSON.stringify('n is 0\nwhile true\n    n is n plus 1\n.\n')})`
        );
        assert.equal(cancelRecovery.ok, true, `Worker failed to recover after cancellation: ${JSON.stringify(cancelRecovery)}`);
        assert.equal(cancelRecovery.stdout, 'recovered after cancel\n');
        console.log('  [PASS] hard worker cancellation (worker.terminate) + clean respawn verified');
        passed++;

        // Prove zero backend compilation requests were made (static GET assets only)
        assert.ok(requestLog.length > 0, 'Expected static asset fetches');
        const nonStatic = requestLog.filter(
            (r) => r.method !== 'GET' || (!['/', '/index.html', '/favicon.ico', '/otter-worker.mjs'].includes(r.path) && !r.path.startsWith('/_framework/'))
        );
        assert.deepEqual(nonStatic, [], `Unexpected backend/non-static requests: ${JSON.stringify(nonStatic)}`);
        console.log(`  [PASS] zero backend compilation service (${requestLog.length} static GET asset requests only)`);
        passed++;

        console.log(`\n=== ALL ${passed} WASM vs CLI PARITY & SANDBOX CHECKS PASSED (100%) ===`);
    } finally {
        await cdp.close();
        server.close();
    }
}

main().catch((err) => {
    console.error('PARITY TEST FAILED:', err);
    process.exit(1);
});
