// Zed extension for the ChibiRuby DAP server.
//
// All this extension does is tell Zed: "when the user picks the `chibiruby` adapter,
// don't spawn a process — open a TCP connection to the host they specified (or
// 127.0.0.1:4711 by default) and speak DAP." MRubyDapServer itself lives inside the
// user's host process (typically a Unity scene) and is already listening when we
// connect.
//
// Two trait methods are required:
//   * `get_dap_binary`  — returns a `DebugAdapterBinary` describing how to reach the
//     server. `command: None` + `connection: Some(...)` tells Zed to skip launching
//     a child process and just connect to the given TCP endpoint.
//   * `dap_request_kind` — peeks at the user's debug.json `request` field so Zed
//     knows whether this is a launch- or attach-shaped session. We always treat
//     missing/unknown as attach, since launch (start a Ruby host on demand) isn't
//     supported by MRubyDapServer's current API.

use zed_extension_api::{
    self as zed, DebugAdapterBinary, DebugConfig, DebugScenario, DebugTaskDefinition, Extension,
    StartDebuggingRequestArguments, StartDebuggingRequestArgumentsRequest, TcpArguments, Worktree,
};

const ADAPTER_NAME: &str = "chibiruby";
const DEFAULT_HOST: u32 = 0x7F00_0001; // 127.0.0.1, packed network-order u32 per Zed's API.
const DEFAULT_PORT: u16 = 4711;

struct ChibiRubyExtension;

impl Extension for ChibiRubyExtension {
    fn new() -> Self {
        Self
    }

    fn get_dap_binary(
        &mut self,
        adapter_name: String,
        config: DebugTaskDefinition,
        _user_provided_debug_adapter_path: Option<String>,
        _worktree: &Worktree,
    ) -> Result<DebugAdapterBinary, String> {
        ensure_known_adapter(&adapter_name)?;

        let (host, port, timeout) = match config.tcp_connection {
            Some(t) => (
                t.host.unwrap_or(DEFAULT_HOST),
                t.port.unwrap_or(DEFAULT_PORT),
                t.timeout,
            ),
            None => (DEFAULT_HOST, DEFAULT_PORT, None),
        };

        let request = request_kind_from_config_string(&config.config);

        Ok(DebugAdapterBinary {
            command: None,
            arguments: vec![],
            envs: vec![],
            cwd: None,
            connection: Some(TcpArguments { host, port, timeout }),
            request_args: StartDebuggingRequestArguments {
                configuration: config.config,
                request,
            },
        })
    }

    fn dap_request_kind(
        &mut self,
        adapter_name: String,
        config: serde_json::Value,
    ) -> Result<StartDebuggingRequestArgumentsRequest, String> {
        ensure_known_adapter(&adapter_name)?;
        Ok(request_kind_from_value(&config))
    }

    fn dap_config_to_scenario(&mut self, _config: DebugConfig) -> Result<DebugScenario, String> {
        // MRubyDapServer is always embedded — there's no scenario the extension can
        // synthesize from "given a project, figure out how to debug it". Users write
        // their debug.json by hand for now. (We could relax this later if scenario
        // auto-creation becomes useful.)
        Err("chibiruby: please configure the session manually in .zed/debug.json".into())
    }
}

zed::register_extension!(ChibiRubyExtension);

fn ensure_known_adapter(adapter_name: &str) -> Result<(), String> {
    if adapter_name == ADAPTER_NAME {
        Ok(())
    } else {
        Err(format!(
            "chibiruby extension does not handle adapter '{adapter_name}' (only '{ADAPTER_NAME}')"
        ))
    }
}

fn request_kind_from_config_string(config: &str) -> StartDebuggingRequestArgumentsRequest {
    serde_json::from_str::<serde_json::Value>(config)
        .map(|v| request_kind_from_value(&v))
        .unwrap_or(StartDebuggingRequestArgumentsRequest::Attach)
}

fn request_kind_from_value(config: &serde_json::Value) -> StartDebuggingRequestArgumentsRequest {
    match config.get("request").and_then(|v| v.as_str()) {
        Some("launch") => StartDebuggingRequestArgumentsRequest::Launch,
        // "attach" (the canonical value) or anything else → attach.  MRubyDapServer is
        // an embedded server we connect to; we never launch a child process.
        _ => StartDebuggingRequestArgumentsRequest::Attach,
    }
}
