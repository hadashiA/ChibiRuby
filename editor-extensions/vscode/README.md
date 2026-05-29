# ChibiRuby Debugger (VSCode)

DAP-based debug adapter for [ChibiRuby](https://github.com/hadashiA/ChibiRuby). Pause at
`binding.irb`, inspect locals, and evaluate arbitrary Ruby in the Debug Console.

## Phase 1 capabilities

- Suspend at `binding.irb`
- Evaluate Ruby expressions in the suspended binding (Debug Console / Watch / Hover)
- View `self` and locals in the Variables pane
- Continue / terminate

Not yet supported (Phase 2): line breakpoints, step over/into/out, source-map line
display in the call stack. The stack frame currently shows line 1 as a placeholder.

---

## Starting a debug session without launch.json

For ad-hoc attach against a running host, two zero-config paths are available:

- **Command Palette**: `Cmd+Shift+P` → **"ChibiRuby: Attach to running host"** → type a port
  (default 4711) → Enter. The session starts immediately.
- **Run-and-Debug picker**: with no `.vscode/launch.json` present, opening the Run-and-Debug
  panel offers **"ChibiRuby: Attach to embedded host (4711)"** via the dynamic-configuration
  list. One click starts the session.

Both routes assume `MRubyDapTcpServer.Listen(...)` is already running on the chosen port
in your host process. If you need a non-default host/port or want to switch frequently,
write a `launch.json` (see below).

## Two modes

### Attach mode (typical for embedded hosts)

Your C# / Unity host has its own startup and embeds ChibiRuby for scripting. It exposes a
DAP listener via `MRubyDapTcpServer.Listen(...)`. You launch the host first; VSCode
attaches over TCP.

`.vscode/launch.json`:

```json
{
  "type": "chibiruby",
  "request": "attach",
  "name": "ChibiRuby: Attach",
  "host": "127.0.0.1",
  "port": 4711
}
```

See `sandbox/SampleDebuggerEmbedded/` for a complete working host.

### Launch mode (single .rb file)

VSCode spawns `mruby-debug` as a child process; the adapter loads & runs the script
itself. Useful for one-off scripts without writing host code.

```json
{
  "type": "chibiruby",
  "request": "launch",
  "name": "ChibiRuby: Debug current file",
  "program": "${file}"
}
```

---

## Setup (local development)

### 1. Build the DAP adapter (`mruby-debug`)

From the repo root:

```sh
dotnet build -c Release src/ChibiRuby.Debugger.Cli/ChibiRuby.Debugger.Cli.csproj
```

You have two ways to make the adapter discoverable to VSCode:

#### Option A — `dotnet tool` install (clean, but requires a re-install on each rebuild)

```sh
cd src/ChibiRuby.Debugger.Cli
dotnet pack -c Release
dotnet tool install -g --add-source ./nupkg ChibiRuby.Debugger.Cli
# `mruby-debug` is now on your PATH (~/.dotnet/tools/mruby-debug)
```

The default `adapterCommand: "mruby-debug"` in the extension will resolve via PATH.

#### Option B — point the extension directly at the build output (fast iteration)

In your `.vscode/launch.json`:

```json
{
  "type": "chibiruby",
  "request": "launch",
  "name": "Debug current file (local build)",
  "program": "${file}",
  "adapterCommand": "dotnet",
  "adapterArgs": [
    "/abs/path/to/ChibiRuby-Debugger/src/ChibiRuby.Debugger.Cli/bin/Debug/net9.0/ChibiRuby.Debugger.Cli.dll"
  ]
}
```

No re-install needed; just `dotnet build` after each change.

### 2. Sideload the VSCode extension

From this directory (`editor-extensions/vscode/`):

```sh
# One-time: install @vscode/vsce
npm install -g @vscode/vsce

# Package the extension into a .vsix
vsce package
# -> chibiruby-debugger-0.1.0.vsix

# Install into your VSCode
code --install-extension chibiruby-debugger-0.1.0.vsix
```

Alternatively, **Extension Development Host** for live iteration on the extension itself:

```sh
code editor-extensions/vscode
# In the opened window, press F5 — a second VSCode window opens with the
# extension loaded. Open a .rb file there and start debugging.
```

### 3. Try it

Open `editor-extensions/vscode/examples/sample.rb` in VSCode, press **F5**, choose
the **ChibiRuby: Debug current file** configuration when prompted.

Execution should halt at the `binding.irb` line. In the **Debug Console**, type:

```
> greeting.upcase
"HELLO"
> counter * 10
30
> self.class.to_s
"Object"
```

Hit **Continue** (F5) to finish the script.

---

## launch.json reference

```json
{
  "type": "chibiruby",
  "request": "launch",
  "name": "ChibiRuby",
  "program": "${file}",            // Required. .rb file to execute.
  "adapterCommand": "mruby-debug", // Optional. Executable that speaks DAP on stdio.
  "adapterArgs": []                 // Optional. Args prepended to the adapter command.
}
```

### Variables surface

The Debug Console / Watch pane can use any of:

| Expression                                    | Notes                                  |
|-----------------------------------------------|----------------------------------------|
| `1 + 2`, `[1,2,3].sum`, ...                   | Pure expressions evaluate as expected. |
| `greeting`, `counter`                          | Identifier access via the binding snapshot is not yet wired into the compiler. **Use** the Variables pane, or `binding.local_variable_get(:greeting)`. |
| `self.foo`, `self.class`                       | Self-bound calls work because eval'd code runs via `obj.instance_eval(&proc)`. |
| `raise "x"`                                    | The error is reported in the response; the host program is unaffected. |

A Phase 2 release will hook the compiler's `Upper` context so bare identifiers resolve
to outer locals natively.
