import * as vscode from 'vscode';
import * as cp from 'child_process';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;
let outputChannel: vscode.OutputChannel;

interface SnippetPickItem extends vscode.QuickPickItem {
    snippetId: string;
}

interface PackPickItem extends vscode.QuickPickItem {
    packId: string;
}

interface RecipePickItem extends vscode.QuickPickItem {
    recipeId: string;
}

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel('Otter');
    context.subscriptions.push(outputChannel);

    const otterBin = resolveOtterExecutable();
    outputChannel.appendLine(`Otter extension activated. Using CLI binary: ${otterBin}`);

    // 1. Start Language Server (LSP)
    startLanguageClient(otterBin, context);

    // 2. Register Debug Adapter Factory (DAP)
    const debugFactory = new OtterDebugAdapterDescriptorFactory();
    context.subscriptions.push(
        vscode.debug.registerDebugAdapterDescriptorFactory('otter', debugFactory)
    );

    // 3. Register Commands
    context.subscriptions.push(
        vscode.commands.registerCommand('otter.runFile', () => runCurrentFile(resolveOtterExecutable())),
        vscode.commands.registerCommand('otter.runTests', () => runProjectTests(resolveOtterExecutable())),
        vscode.commands.registerCommand('otter.openLibrary', () => openLibraryExplorer(resolveOtterExecutable())),
        vscode.commands.registerCommand('otter.applyRecipe', () => applyRecipeCommand(resolveOtterExecutable())),
        vscode.commands.registerCommand('otter.doctor', () => runDoctor(resolveOtterExecutable())),
        vscode.commands.registerCommand('otter.restartLsp', () => restartLanguageServer(resolveOtterExecutable(), context))
    );
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}

function executableResponds(bin: string): boolean {
    try {
        const res = cp.spawnSync(bin, ['--version'], {
            encoding: 'utf8',
            timeout: 3000,
            windowsHide: true
        });
        return res.status === 0 && !res.error;
    } catch {
        return false;
    }
}

export function resolveOtterExecutable(): string {
    const config = vscode.workspace.getConfiguration('otter');
    const customPath = config.get<string>('executablePath');
    if (customPath && customPath.trim().length > 0) {
        return customPath.trim();
    }
    // Prefer otter-net when installed alongside otterPS; fall back to otter on PATH.
    if (executableResponds('otter-net')) {
        return 'otter-net';
    }
    return 'otter';
}

function runCliJson(otterBin: string, args: string[], cwd?: string): Promise<any> {
    return new Promise((resolve, reject) => {
        cp.execFile(
            otterBin,
            args,
            { cwd, maxBuffer: 4 * 1024 * 1024, windowsHide: true },
            (err, stdout, stderr) => {
                if (err) {
                    reject(new Error(stderr?.trim() || err.message));
                    return;
                }
                try {
                    resolve(JSON.parse(stdout));
                } catch (parseErr: any) {
                    reject(new Error(`Invalid JSON from '${otterBin} ${args.join(' ')}': ${parseErr.message}`));
                }
            }
        );
    });
}

function quoteForShell(value: string): string {
    return `"${value.replace(/"/g, '\\"')}"`;
}

function startLanguageClient(otterBin: string, _context: vscode.ExtensionContext) {
    const serverOptions: ServerOptions = {
        command: otterBin,
        args: ['lsp'],
        options: {
            shell: false
        }
    };

    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'otter' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.ot')
        },
        outputChannel: outputChannel
    };

    client = new LanguageClient(
        'otterLanguageServer',
        'Otter Language Server',
        serverOptions,
        clientOptions
    );

    client.start().catch((err: any) => {
        outputChannel.appendLine(`Failed to start Otter LSP: ${err}`);
        vscode.window.showErrorMessage(
            `Could not start Otter Language Server using '${otterBin} lsp'. Install otter-net or configure 'otter.executablePath' in Settings.`
        );
    });
}

async function restartLanguageServer(otterBin: string, context: vscode.ExtensionContext) {
    if (client) {
        outputChannel.appendLine('Stopping existing language server...');
        await client.stop();
        client = undefined;
    }
    startLanguageClient(otterBin, context);
    vscode.window.showInformationMessage(`Otter Language Server restarted (${otterBin}).`);
}

class OtterDebugAdapterDescriptorFactory implements vscode.DebugAdapterDescriptorFactory {
    createDebugAdapterDescriptor(
        _session: vscode.DebugSession,
        _executable: vscode.DebugAdapterExecutable | undefined
    ): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
        const otterBin = resolveOtterExecutable();
        return new vscode.DebugAdapterExecutable(otterBin, ['dap']);
    }
}

function runCurrentFile(otterBin: string) {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        vscode.window.showWarningMessage('No active Otter file open.');
        return;
    }

    const doc = editor.document;
    if (doc.languageId !== 'otter' && !doc.fileName.endsWith('.ot')) {
        vscode.window.showWarningMessage('The active file is not an Otter (.ot) source file.');
        return;
    }

    doc.save().then(() => {
        const terminal = getOrCreateTerminal('Otter');
        terminal.show();
        terminal.sendText(`${quoteForShell(otterBin)} run ${quoteForShell(doc.fileName)}`);
    });
}

function runProjectTests(otterBin: string) {
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath || '.';
    const terminal = getOrCreateTerminal('Otter');
    terminal.show();
    terminal.sendText(`${quoteForShell(otterBin)} test ${quoteForShell(workspaceFolder)}`);
}

function runDoctor(otterBin: string) {
    outputChannel.show(true);
    outputChannel.appendLine(`=== Running Otter Doctor (${otterBin}) ===\n`);
    cp.execFile(otterBin, ['doctor'], { windowsHide: true }, (err, stdout, stderr) => {
        if (stdout) outputChannel.appendLine(stdout);
        if (stderr) outputChannel.appendLine(stderr);
        if (err) {
            outputChannel.appendLine(`Doctor exited with error: ${err.message}`);
        }
    });
}

async function openLibraryExplorer(otterBin: string) {
    const items: vscode.QuickPickItem[] = [
        {
            label: '$(sparkle) Application Recipes',
            description: 'Pre-assembled verified full application features'
        },
        {
            label: '$(package) Official Source Packs',
            description: 'Schema v2 reusable packs for math, text, files, lists, and games'
        },
        {
            label: '$(file-code) Reusable Snippets',
            description: 'Search 38+ tested snippets across collections, loops, math, and I/O'
        }
    ];

    const selection = await vscode.window.showQuickPick(items, {
        placeHolder: 'Select Otter Library resource to explore'
    });

    if (!selection) return;

    if (selection.label.includes('Application Recipes')) {
        await applyRecipeCommand(otterBin);
    } else if (selection.label.includes('Reusable Snippets')) {
        await exploreSnippets(otterBin);
    } else if (selection.label.includes('Official Source Packs')) {
        await explorePacks(otterBin);
    }
}

async function exploreSnippets(otterBin: string) {
    const cwd = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    try {
        const data = await runCliJson(otterBin, ['library', 'search', '', '--kind', 'snippet', '--json'], cwd);
        const snippets = Array.isArray(data) ? data : (data.snippets || []);

        const pickItems: SnippetPickItem[] = snippets.map((s: any) => ({
            label: `$(code) ${s.title || s.id}`,
            description: `[${s.category || 'General'}] ${s.id}`,
            detail: s.description,
            snippetId: s.id
        }));

        const chosen = await vscode.window.showQuickPick(pickItems, {
            placeHolder: 'Search snippets by name, keyword, or category'
        });

        if (!chosen) return;

        const snipData = await runCliJson(otterBin, ['snippet', 'show', chosen.snippetId, '--json'], cwd);
        const snippetCode = snipData.template || snipData.body || '';
        const editor = vscode.window.activeTextEditor;
        if (editor && snippetCode) {
            const choice = await vscode.window.showInformationMessage(
                `Insert snippet '${chosen.snippetId}' at cursor?`,
                'Insert',
                'Cancel'
            );
            if (choice === 'Insert') {
                await editor.edit(editBuilder => {
                    editBuilder.insert(editor.selection.active, snippetCode);
                });
            }
        }
    } catch (err: any) {
        outputChannel.appendLine(`Snippet explorer error: ${err.message}`);
        vscode.window.showErrorMessage(`Failed to query Otter snippets catalog: ${err.message}`);
    }
}

async function explorePacks(otterBin: string) {
    const cwd = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    try {
        const data = await runCliJson(otterBin, ['pack', 'list', '--json'], cwd);
        const packs = Array.isArray(data) ? data : (data.packs || []);

        const pickItems: PackPickItem[] = packs.map((p: any) => {
            const packId = p.id || (p.publisher && p.name ? `${p.publisher}/${p.name}` : p.name);
            const opNames = Array.isArray(p.provides?.operations)
                ? p.provides.operations.map((op: any) => op.name).filter(Boolean)
                : (Array.isArray(p.exports) ? p.exports : []);
            const prefix = p.variablePrefix ? ` · prefix ${p.variablePrefix}` : '';
            return {
                label: `$(package) ${packId} v${p.version}`,
                description: p.description || p.summary || '',
                detail: opNames.length > 0 ? `Operations: ${opNames.join(', ')}${prefix}` : `Schema v${p.schemaVersion || 1}${prefix}`,
                packId
            };
        });

        const chosen = await vscode.window.showQuickPick(pickItems, {
            placeHolder: 'Official Otter Source Packs (select to install into workspace)'
        });

        if (!chosen) return;

        const confirm = await vscode.window.showInformationMessage(
            `Install Pack '${chosen.packId}' into the current workspace?`,
            'Install Pack',
            'Cancel'
        );
        if (confirm === 'Install Pack') {
            const workspaceFolder = cwd || '.';
            const terminal = getOrCreateTerminal('Otter');
            terminal.show();
            terminal.sendText(`${quoteForShell(otterBin)} pack install ${quoteForShell(chosen.packId)} --project ${quoteForShell(workspaceFolder)}`);
        }
    } catch (err: any) {
        outputChannel.appendLine(`Pack explorer error: ${err.message}`);
        vscode.window.showErrorMessage(`Failed to query Otter packs catalog: ${err.message}`);
    }
}

async function applyRecipeCommand(otterBin: string) {
    const cwd = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    try {
        const data = await runCliJson(otterBin, ['recipe', 'search', '', '--json'], cwd);
        const recipes = Array.isArray(data) ? data : (data.recipes || []);

        const pickItems: RecipePickItem[] = recipes.map((r: any) => ({
            label: `$(sparkle) ${r.title || r.id}`,
            description: `v${r.version} [${r.target || r.category || 'app'}]`,
            detail: r.description || r.summary || '',
            recipeId: r.id
        }));

        const chosen = await vscode.window.showQuickPick(pickItems, {
            placeHolder: 'Select an Application Recipe to preview and apply'
        });

        if (!chosen) return;

        const confirm = await vscode.window.showInformationMessage(
            `Apply Recipe '${chosen.recipeId}' to the current project?\n\nThis will configure dependencies and generate tested application files.`,
            { modal: true },
            'Apply Recipe',
            'Cancel'
        );

        if (confirm === 'Apply Recipe') {
            const workspaceFolder = cwd || '.';
            const terminal = getOrCreateTerminal('Otter');
            terminal.show();
            terminal.sendText(`${quoteForShell(otterBin)} recipe apply ${quoteForShell(chosen.recipeId)} --project ${quoteForShell(workspaceFolder)}`);
            vscode.window.showInformationMessage(`Applying recipe '${chosen.recipeId}' in the Otter terminal.`);
        }
    } catch (err: any) {
        outputChannel.appendLine(`Recipe explorer error: ${err.message}`);
        vscode.window.showErrorMessage(`Failed to query Otter recipe registry: ${err.message}`);
    }
}

function getOrCreateTerminal(name: string): vscode.Terminal {
    const existing = vscode.window.terminals.find(t => t.name === name);
    if (existing) return existing;
    return vscode.window.createTerminal(name);
}
