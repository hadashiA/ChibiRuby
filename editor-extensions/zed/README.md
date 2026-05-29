# ChibiRuby Debug Adapter for Zed

Bridges Zed's built-in DAP UI to [`MRubyDapServer`](../../src/ChibiRuby.Debugger.Dap/),
the debug server that ships inside the ChibiRuby host process (typically Unity).

The extension itself ships no binary — it only tells Zed:

- the adapter name `chibiruby` exists,
- when activated, **do not** spawn a process,
- instead **connect TCP** to the host/port the user specified in `.zed/debug.json`
  (default `127.0.0.1:4711`).

That's the entirety of it. The actual debug server lives in your Unity (or other
.NET) host process via `new MRubyDapServer(...).StartAsync()`.

## Dev install (current distribution)

The extension is not yet on the Zed extension registry. To use it, install it
locally as a dev extension:

1. **Prerequisites** (one-time):
   - [Rust](https://rustup.rs) toolchain
   - `wasm32-wasip2` target: `rustup target add wasm32-wasip2`

2. **Install in Zed**:
   - In Zed, open the command palette (`cmd-shift-p`).
   - Run **`zed: install dev extension`**.
   - Pick this folder (`editor-extensions/zed/`).
   - Zed builds the WASM blob and registers it.

3. **Use it**: create `.zed/debug.json` in your workspace:

   ```json
   [
     {
       "label": "Attach to ChibiRuby",
       "adapter": "chibiruby",
       "request": "attach",
       "tcp_connection": { "host": "127.0.0.1", "port": 4711 }
     }
   ]
   ```

4. Start your Unity (or other host) so `MRubyDapServer` is listening on 4711.
5. In Zed, open the Debug panel (`cmd-shift-d`) → pick **Attach to ChibiRuby** → run.
   Breakpoints in `.rb` files become active once the session is live.

## debug.json fields

- `adapter` (**required**): must be `"chibiruby"` for this extension to handle it.
- `request` (optional, default `"attach"`): pass `"attach"` to connect to a running
  `MRubyDapServer`. `"launch"` is forwarded but not supported by `MRubyDapServer`'s
  attach-only flow today; treat it as future-proofing.
- `tcp_connection.host` (optional, default `"127.0.0.1"`): host where the server
  listens. Use `IPAddress.Any` on the server side if attaching from another machine
  on your LAN (iPhone development, etc.).
- `tcp_connection.port` (optional, default `4711`): TCP port the server is bound to.
- `tcp_connection.timeout` (optional): connect-timeout in milliseconds. Defaults to
  Zed's built-in timeout if omitted.

## When something goes wrong

- **"Failed to connect to debug adapter"** at session start usually means
  `MRubyDapServer` isn't listening yet. Start the host first, then attach.
- **Gutter breakpoints don't fire**: confirm the `Source.path` on the wire matches
  what `MRubyCompiler.Compile(filename: ...)` recorded in the bytecode's DBG
  section. The simplest rule of thumb is to pass an **absolute path** at compile
  time (e.g. `Application.dataPath + "/ruby/sample.rb"` in Unity Editor).
- **Session drops when host stops** (e.g. Unity Play → Stop): the server sends a
  `terminated` event with `restart: true`, but whether the editor auto-reconnects
  depends on the editor. Re-running the debug config is always safe.

## Roadmap

- Submit to [zed-industries/extensions](https://github.com/zed-industries/extensions)
  once the API surface stabilizes — then no Rust toolchain / dev install dance.
- Add scenario auto-creation (`dap_config_to_scenario`) so users without a
  `debug.json` can still start a session via the Zed UI.
