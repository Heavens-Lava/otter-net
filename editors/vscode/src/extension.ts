import * as vscode from 'vscode';
import * as path from 'path';
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

interface RecipePickItem extends vscode.QuickPickItem {
    recipeId: string;
}

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel('Otter');
    context.subscriptions.push(outputChannel);

    const otterBin = getOtterExecutable();
    outputChannel.appendLine(`Otter extension activated. Using CLI binary: ${otterBin}`);

    // 1. Start Language Server (LSP)
    startLanguageClient(otterBin, context);

    // 2. Register Debug Adapter Factory (DAP)
    const debugFactory = new OtterDebugAdapterDescriptorFactory(otterBin);
    context.subscriptions.push(
        vscode.debug.registerDebugAdapterDescriptorFactory('otter', debugFactory)
    );

    // 3. Register Commands
    context.subscriptions.push(
        vscode.commands.registerCommand('otter.runFile', () => runCurrentFile(otterBin)),
        vscode.commands.registerCommand('otter.runTests', () => runProjectTests(otterBin)),
        vscode.commands.registerCommand('otter.openLibrary', () => openLibraryExplorer(otterBin)),
        vscode.commands.registerCommand('otter.applyRecipe', () => applyRecipeCommand(otterBin)),
        vscode.commands.registerCommand('otter.doctor', () => runDoctor(otterBin)),
        vscode.commands.registerCommand('otter.restartLsp', () => restartLanguageServer(otterBin, context))
    );
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}

function getOtterExecutable(): string {
    const config = vscode.workspace.getConfiguration('otter');
    const customPath = config.get<string>('executablePath');
    if (customPath && customPath.trim().length > 0) {
        return customPath.trim();
    }
    return 'otter';
}

function startLanguageClient(otterBin: string, context: vscode.ExtensionContext) {
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
        vscode.window.showErrorMessage(`Could not start Otter Language Server. Please ensure '${otterBin}' is installed and on PATH.`);
    });
}

async function restartLanguageServer(otterBin: string, context: vscode.ExtensionContext) {
    if (client) {
        outputChannel.appendLine('Stopping existing language server...');
        await client.stop();
        client = undefined;
    }
    startLanguageClient(otterBin, context);
    vscode.window.showInformationMessage('Otter Language Server restarted.');
}

class OtterDebugAdapterDescriptorFactory implements vscode.DebugAdapterDescriptorFactory {
    constructor(private otterBin: string) {}

    createDebugAdapterDescriptor(
        _session: vscode.DebugSession,
        _executable: vscode.DebugAdapterExecutable | undefined
    ): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
        return new vscode.DebugAdapterExecutable(this.otterBin, ['dap']);
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
        terminal.sendText(`${otterBin} run "${doc.fileName}"`);
    });
}

function runProjectTests(otterBin: string) {
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath || '.';
    const terminal = getOrCreateTerminal('Otter');
    terminal.show();
    terminal.sendText(`${otterBin} test "${workspaceFolder}"`);
}

function runDoctor(otterBin: string) {
    outputChannel.show(true);
    outputChannel.appendLine('=== Running Otter Doctor ===\n');
    cp.exec(`${otterBin} doctor`, (err, stdout, stderr) => {
        if (stdout) outputChannel.appendLine(stdout);
        if (stderr) outputChannel.appendLine(stderr);
        if (err) {
            outputChannel.appendLine(`Doctor exited with code ${err.code}`);
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
            description: 'Reusable packages for math, text, files, and gaming'
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
    cp.exec(`${otterBin} snippet list --json`, async (err, stdout) => {
        if (err || !stdout) {
            vscode.window.showErrorMessage('Failed to query Otter snippets catalog.');
            return;
        }

        try {
            const data = JSON.parse(stdout);
            const snippets = data.snippets || [];

            const pickItems: SnippetPickItem[] = snippets.map((s: any) => ({
                label: `$(code) ${s.title || s.id}`,
                description: `[${s.category}] ${s.id}`,
                detail: s.description,
                snippetId: s.id
            }));

            const chosen = await vscode.window.showQuickPick(pickItems, {
                placeHolder: 'Search snippets by name, keyword, or category'
            });

            if (!chosen) return;

            cp.exec(`${otterBin} snippet show ${chosen.snippetId} --json`, (showErr, showStdout) => {
                if (showErr || !showStdout) return;
                try {
                    const snipData = JSON.parse(showStdout);
                    const editor = vscode.window.activeTextEditor;
                    if (editor && snipData.body) {
                        vscode.window.showInformationMessage(
                            `Insert snippet '${chosen.snippetId}' at cursor?`,
                            'Insert',
                            'Cancel'
                        ).then(choice => {
                            if (choice === 'Insert') {
                                editor.edit(editBuilder => {
                                    editBuilder.insert(editor.selection.active, snipData.body);
                                });
                            }
                        });
                    }
                } catch { }
            });
        } catch { }
    });
}

async function explorePacks(otterBin: string) {
    cp.exec(`${otterBin} pack list --json`, async (err, stdout) => {
        if (err || !stdout) {
            vscode.window.showErrorMessage('Failed to query Otter packs registry.');
            return;
        }

        try {
            const data = JSON.parse(stdout);
            const packs = data.packs || [];

            const pickItems = packs.map((p: any) => ({
                label: `$(package) ${p.id} v${p.version}`,
                description: p.summary,
                detail: `Exports: ${(p.exports || []).join(', ')}`
            }));

            await vscode.window.showQuickPick(pickItems, {
                placeHolder: 'Official Otter Source Packs'
            });
        } catch { }
    });
}

async function applyRecipeCommand(otterBin: string) {
    cp.exec(`${otterBin} recipe list --json`, async (err, stdout) => {
        if (err || !stdout) {
            vscode.window.showErrorMessage('Failed to query Otter recipe registry.');
            return;
        }

        try {
            const data = JSON.parse(stdout);
            const recipes = data.recipes || [];

            const pickItems: RecipePickItem[] = recipes.map((r: any) => ({
                label: `$(sparkle) ${r.title || r.id}`,
                description: `v${r.version} [${r.category}]`,
                detail: r.summary,
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
                const workspaceFolder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath || '.';
                const terminal = getOrCreateTerminal('Otter');
                terminal.show();
                terminal.sendText(`${otterBin} recipe apply ${chosen.recipeId} "${workspaceFolder}"`);
                vscode.window.showInformationMessage(`Recipe '${chosen.recipeId}' applied! Check the terminal for generated files.`);
            }
        } catch { }
    });
}

function getOrCreateTerminal(name: string): vscode.Terminal {
    const existing = vscode.window.terminals.find(t => t.name === name);
    if (existing) return existing;
    return vscode.window.createTerminal(name);
}
