# Regenerate the DAP protocol types + UTF-8 JSON serializer from the DAP JSON Schema.
#
#   make schema    # fetch upstream DAP schema + merge our overlay  -> Schema/dap-schema.json
#   make codegen   # run NoJsonSchema over dap-schema.json          -> Protocol/*.g.cs
#   make regen     # codegen only (schema is committed; no network)
#   make test      # run the DAP test suite
#   make all       # schema + codegen + test
#
# `codegen` uses the *released* NoJsonSchema.Cli dotnet tool from NuGet (pinned below),
# NOT the local ~/dev/NoJsonSchema source. Bump NOJSONSCHEMA_VERSION to adopt a newer release.

NOJSONSCHEMA_VERSION := 1.2.0

NAMESPACE   := ChibiRuby.Debugger.Dap.Protocol
SCHEMA      := Schema/dap-schema.json
OUT         := Protocol
TOOL_DIR    := .tools
NOJSONSCHEMA := $(TOOL_DIR)/nojsonschema
TEST_PROJECT := ../../tests/ChibiRuby.Debugger.Dap.Tests/ChibiRuby.Debugger.Dap.Tests.csproj

# $defs roots to generate. Synthesized *Body / inline-enum types come along transitively.
INCLUDE_TYPES := AttachRequest,AttachRequestArguments,AttachResponse,Breakpoint,BreakpointMode,Capabilities,Checksum,ChecksumAlgorithm,ColumnDescriptor,ConfigurationDoneArguments,ConfigurationDoneRequest,ConfigurationDoneResponse,ContinueArguments,ContinueRequest,ContinueResponse,ContinuedEvent,DisconnectArguments,DisconnectRequest,DisconnectResponse,ErrorResponse,EvaluateArguments,EvaluateRequest,EvaluateResponse,Event,ExceptionBreakpointsFilter,InitializeRequest,InitializeRequestArguments,InitializeResponse,InitializedEvent,LaunchRequest,LaunchRequestArguments,LaunchResponse,Message,NextArguments,NextRequest,NextResponse,OutputEvent,PauseArguments,PauseRequest,PauseResponse,ProtocolMessage,Request,Response,Scope,ScopesArguments,ScopesRequest,ScopesResponse,SetBreakpointsArguments,SetBreakpointsRequest,SetBreakpointsResponse,Source,SourceBreakpoint,StackFrame,StackFrameFormat,StackTraceArguments,StackTraceRequest,StackTraceResponse,StepInArguments,StepInRequest,StepInResponse,StepOutArguments,StepOutRequest,StepOutResponse,SteppingGranularity,StoppedEvent,TerminateArguments,TerminateRequest,TerminateResponse,TerminatedEvent,Thread,ThreadsRequest,ThreadsResponse,ValueFormat,Variable,VariablePresentationHint,VariablesArguments,VariablesRequest,VariablesResponse

# $defs (and synthesized bodies) emitted as `readonly partial record struct` value objects.
VALUE_OBJECTS := Checksum,ConfigurationDoneArguments,ContinueArguments,ContinueResponseBody,ContinuedEventBody,DisconnectArguments,ErrorResponseBody,NextArguments,PauseArguments,ScopesArguments,ScopesResponseBody,SetBreakpointsResponseBody,StackTraceResponseBody,StepOutArguments,TerminateArguments,TerminatedEventBody,Thread,ThreadsResponseBody,VariablesResponseBody

.PHONY: all schema codegen regen test tool clean-tool

all: schema codegen test

schema:
	./Schema/build-schema.sh

# Install the pinned released tool into a local (gitignored) tool-path, idempotently.
tool:
	@if [ ! -x "$(NOJSONSCHEMA)" ] || [ "$$($(NOJSONSCHEMA) --version 2>/dev/null)" != "$(NOJSONSCHEMA_VERSION)" ]; then \
		echo "installing NoJsonSchema.Cli $(NOJSONSCHEMA_VERSION) -> $(TOOL_DIR)"; \
		rm -rf "$(TOOL_DIR)"; \
		dotnet tool install NoJsonSchema.Cli --version $(NOJSONSCHEMA_VERSION) --tool-path $(TOOL_DIR); \
	fi

codegen regen: tool
	$(NOJSONSCHEMA) generate \
		-i $(SCHEMA) \
		-o $(OUT) \
		-n $(NAMESPACE) \
		--value-object "$(VALUE_OBJECTS)" \
		--include-type "$(INCLUDE_TYPES)"

test:
	dotnet test $(TEST_PROJECT)

clean-tool:
	rm -rf $(TOOL_DIR)
