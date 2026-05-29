# ChibiRuby Debugger (VSCode)

DAP-based debug adapter for [ChibiRuby](https://github.com/hadashiA/ChibiRuby).
Set breakpoints in `.rb` files, inspect locals, and evaluate arbitrary Ruby in
the Debug Console.

## Install

[![Visual Studio Marketplace](https://img.shields.io/visual-studio-marketplace/v/hadashiA.chibiruby-debugger?label=Marketplace)](https://marketplace.visualstudio.com/items?itemName=hadashiA.chibiruby-debugger)

```
code --install-extension hadashiA.chibiruby-debugger
```

Or search **"ChibiRuby Debugger"** in the Extensions panel.

## Capabilities

- **Line breakpoints** in `.rb` files — click the gutter or press **F9**
- Evaluate Ruby expressions in the suspended binding (Debug Console / Watch / Hover)
- View `self` and locals in the Variables pane
- Continue / terminate
- `binding.irb` as an inline pause (also supported, useful when you can't easily
  set a gutter breakpoint — e.g. generated code)

Not yet supported: launch mode (run a `.rb` file from VSCode), step
over/into/out.

---

## Quick start (attach to an embedded host)

The extension supports **attach mode** only. Your C# / Unity host embeds
ChibiRuby and opens a TCP listener via `new MRubyDapServer(...).StartAsync()`;
VSCode connects over TCP, sends your breakpoints, and execution halts when a
breakpoint hits (or at any `binding.irb` call).

See [`sandbox/SampleDebuggerEmbedded/`](../../sandbox/SampleDebuggerEmbedded/)
for a complete working host you can run with `dotnet run`.

### Zero-config attach

Two paths skip the `launch.json` boilerplate:

- **Command Palette**: `Cmd+Shift+P` → **"ChibiRuby: Attach to running host"** →
  type a port (default 4711) → Enter. The session starts immediately.
- **Run-and-Debug picker**: with no `.vscode/launch.json` present, opening the
  Run-and-Debug panel offers **"ChibiRuby: Attach to embedded host (4711)"** via
  the dynamic-configuration list. One click starts the session.

Both routes assume `MRubyDapServer` is already listening on the chosen port. If
you need a non-default host/port or want to switch frequently, write a
`launch.json`.

### With launch.json

`.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "chibiruby",
      "request": "attach",
      "name": "ChibiRuby: Attach",
      "host": "127.0.0.1",
      "port": 4711
    }
  ]
}
```

Start the host, then press **F5** in VSCode.

### Try it with the sample host

1. Open the workspace [`sandbox/SampleDebuggerEmbedded/`](../../sandbox/SampleDebuggerEmbedded/)
   in VSCode (it ships with a ready `launch.json`).
2. Open the script (e.g. `scenarios/quest.rb`) and click the gutter on any line
   to set a breakpoint.
3. Start the sample host in a terminal:

   ```sh
   dotnet run --project sandbox/SampleDebuggerEmbedded
   ```

4. Press **F5** in VSCode to attach. Execution halts at your breakpoint.

In the **Debug Console**, try evaluating expressions against the current binding:

```
> greeting.upcase
"HELLO"
> counter * 10
30
> self.class.to_s
"Object"
```

Hit **Continue** (F5) to resume.

---

## launch.json reference

| Field   | Required | Default       | Notes                                                          |
|---------|----------|---------------|----------------------------------------------------------------|
| `type`  | yes      | —             | Must be `"chibiruby"`.                                         |
| `request` | yes    | —             | Must be `"attach"` (launch mode not yet supported).            |
| `host`  | no       | `127.0.0.1`   | Hostname / IP `MRubyDapServer` is bound to.                    |
| `port`  | no       | `4711`        | TCP port `MRubyDapServer` is listening on.                     |

### Variables surface

The Debug Console / Watch pane can use any of:

| Expression                                    | Notes                                  |
|-----------------------------------------------|----------------------------------------|
| `1 + 2`, `[1,2,3].sum`, ...                   | Pure expressions evaluate as expected. |
| `greeting`, `counter`                          | Identifier access via the binding snapshot is not yet wired into the compiler. **Use** the Variables pane, or `binding.local_variable_get(:greeting)`. |
| `self.foo`, `self.class`                       | Self-bound calls work because eval'd code runs via `obj.instance_eval(&proc)`. |
| `raise "x"`                                    | The error is reported in the response; the host program is unaffected. |

A future release will hook the compiler's `Upper` context so bare identifiers
resolve to outer locals natively.

---

## Contributing — dev install

If you're hacking on the extension itself:

### Extension Development Host (live iteration)

```sh
code editor-extensions/vscode
# Press F5 in the opened window — a second VSCode window starts with the
# extension loaded. Open a .rb file there and start debugging.
```

### Package & sideload a .vsix

```sh
# One-time:
npm install -g @vscode/vsce

cd editor-extensions/vscode
vsce package
# -> chibiruby-debugger-<version>.vsix

code --install-extension chibiruby-debugger-*.vsix
```

### Publish to Marketplace

Releases are cut by the [`release` workflow](../../.github/workflows/release.yaml),
which packages and publishes to Marketplace when run with `dry-run: false`.
Requires the `VSCE_PAT` repository secret (Azure DevOps PAT, Marketplace →
Manage scope).
