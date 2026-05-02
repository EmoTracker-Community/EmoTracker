// EmoTracker Lua Debugger — VS Code DAP client.
//
// The C#-side server lives in EmoTracker.Data/Debugging/ and listens
// on a TCP port (default 27126). This extension implements the
// minimal glue: when the user starts an "emotracker-lua" debug
// session, we ask VS Code to use a "server" debug adapter that just
// pipes its stdio to that TCP port. VS Code handles the rest of DAP
// itself.
//
// No bundled DAP adapter binary, no compiled native bits, no
// dependency on @vscode/debugadapter — keeps the surface small.

import * as net from 'net';
import * as vscode from 'vscode';

class EmoTrackerDebugAdapterDescriptorFactory
    implements vscode.DebugAdapterDescriptorFactory
{
    createDebugAdapterDescriptor(
        session: vscode.DebugSession,
        _executable: vscode.DebugAdapterExecutable | undefined
    ): vscode.ProviderResult<vscode.DebugAdapterDescriptor> {
        const cfg = session.configuration as { host?: string; port?: number };
        const host = cfg.host ?? 'localhost';
        const port = cfg.port ?? 27126;
        return new vscode.DebugAdapterServer(port, host);
    }
}

class EmoTrackerConfigurationProvider implements vscode.DebugConfigurationProvider {
    resolveDebugConfiguration(
        _folder: vscode.WorkspaceFolder | undefined,
        config: vscode.DebugConfiguration,
        _token?: vscode.CancellationToken
    ): vscode.ProviderResult<vscode.DebugConfiguration> {
        // If the user invoked F5 with no launch.json, surface a
        // sensible default rather than a blank window.
        if (!config.type && !config.request && !config.name) {
            config.type = 'emotracker-lua';
            config.request = 'attach';
            config.name = 'Attach to EmoTracker (Lua)';
            config.host = 'localhost';
            config.port = 27126;
        }
        return config;
    }
}

export function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(
        vscode.debug.registerDebugAdapterDescriptorFactory(
            'emotracker-lua',
            new EmoTrackerDebugAdapterDescriptorFactory()
        ),
        vscode.debug.registerDebugConfigurationProvider(
            'emotracker-lua',
            new EmoTrackerConfigurationProvider()
        )
    );
}

export function deactivate() {
    // No persistent state to clean up — the DAP server lives in the
    // EmoTracker process, and VS Code closes the TCP socket on
    // session end.
}
