// VSCode extension entry point for the ChibiRuby DAP debugger.
//
// Intentionally written in plain JS with zero npm dependencies (other than VSCode's
// own runtime API which is provided implicitly). To package: `npx @vscode/vsce package`
// from this directory.

const vscode = require('vscode');

/**
 * @param {vscode.ExtensionContext} context
 */
function activate(context) {
  // Resolve absent fields in the user's launch.json with sensible defaults.
  const resolver = {
    resolveDebugConfiguration(folder, config /*, token */) {
      if (!config.type && !config.request && !config.name) {
        // F5 with no launch.json: try to do something sensible.
        const editor = vscode.window.activeTextEditor;
        if (editor && editor.document.languageId === 'ruby') {
          config = {
            type: 'chibiruby',
            request: 'launch',
            name: 'ChibiRuby (auto)',
            program: editor.document.uri.fsPath
          };
        }
      }
      // Launch mode requires a program; attach mode requires host:port (both have defaults).
      if (config.request === 'launch' && !config.program) {
        return vscode.window.showInformationMessage(
          'Cannot debug: launch configuration is missing `program`.'
        ).then(() => undefined);
      }
      return config;
    }
  };
  context.subscriptions.push(
    vscode.debug.registerDebugConfigurationProvider('chibiruby', resolver)
  );

  // Two adapter topologies:
  //  - launch mode: spawn `mruby-debug` as a child process and speak DAP over its stdio
  //  - attach mode: connect to a TCP port where the host app is already running an
  //    `MRubyDapTcpServer`
  const factory = {
    /**
     * @param {vscode.DebugSession} session
     */
    createDebugAdapterDescriptor(session /*, executable */) {
      const cfg = session.configuration || {};
      if (cfg.request === 'attach') {
        const port = cfg.port || 4711;
        const host = cfg.host || '127.0.0.1';
        return new vscode.DebugAdapterServer(port, host);
      }
      // launch (default)
      const command = cfg.adapterCommand || 'mruby-debug';
      const args = Array.isArray(cfg.adapterArgs) ? cfg.adapterArgs : [];
      return new vscode.DebugAdapterExecutable(command, args);
    }
  };
  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory('chibiruby', factory)
  );

  // -- Command Palette: attach without a launch.json ----------------------------------
  // The user has a host process running with MRubyDapTcpServer.Listen() somewhere.
  // They can invoke "ChibiRuby: Attach to running host" from Cmd+Shift+P, type a port
  // (default 4711), and the debug session starts immediately.
  context.subscriptions.push(
    vscode.commands.registerCommand('chibiruby.attachToRunningHost', async () => {
      const portInput = await vscode.window.showInputBox({
        prompt: 'ChibiRuby DAP server port (host running MRubyDapTcpServer.Listen)',
        value: '4711',
        validateInput: v => /^\d+$/.test(v) ? null : 'port must be an integer'
      });
      if (!portInput) return; // cancelled
      const port = parseInt(portInput, 10);
      const folder = vscode.workspace.workspaceFolders?.[0];
      await vscode.debug.startDebugging(folder, {
        type: 'chibiruby',
        request: 'attach',
        name: `ChibiRuby: Attach (port ${port})`,
        host: '127.0.0.1',
        port
      });
    })
  );

  // -- Dynamic configuration provider --------------------------------------------------
  // Surfaces "ChibiRuby: Attach to embedded host (4711)" in the Run-and-Debug "Show all
  // automatic debug configurations" picker, so users without a launch.json can still
  // start a session in one click.
  context.subscriptions.push(
    vscode.debug.registerDebugConfigurationProvider('chibiruby', {
      provideDebugConfigurations() {
        return [
          {
            type: 'chibiruby',
            request: 'attach',
            name: 'ChibiRuby: Attach to embedded host (4711)',
            host: '127.0.0.1',
            port: 4711
          }
        ];
      }
    }, vscode.DebugConfigurationProviderTriggerKind.Dynamic)
  );
}

function deactivate() {}

module.exports = { activate, deactivate };
