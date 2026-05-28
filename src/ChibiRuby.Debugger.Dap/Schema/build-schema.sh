#!/usr/bin/env bash
# Fetch the upstream DAP JSON Schema and deep-merge our ChibiRuby overlay on top.
# Output: dap-schema.json (the file fed to NoJsonSchema). Commit the result so
# regeneration doesn't require network access.
#
# Usage:  ./Schema/build-schema.sh           # fetch + merge
#         ./Schema/build-schema.sh --offline # skip fetch, only re-merge cached upstream
#
# Requires `curl` and `jq`.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UPSTREAM_URL="https://raw.githubusercontent.com/microsoft/debug-adapter-protocol/gh-pages/debugAdapterProtocol.json"
UPSTREAM_CACHE="$SCRIPT_DIR/dap-schema.upstream.json"
OVERLAY="$SCRIPT_DIR/dap-schema.overlay.json"
OUTPUT="$SCRIPT_DIR/dap-schema.json"

if [[ "${1:-}" != "--offline" ]]; then
    echo "fetch  $UPSTREAM_URL"
    curl -fsSL "$UPSTREAM_URL" -o "$UPSTREAM_CACHE.tmp"
    mv "$UPSTREAM_CACHE.tmp" "$UPSTREAM_CACHE"
fi

if [[ ! -f "$UPSTREAM_CACHE" ]]; then
    echo "error: no cached upstream at $UPSTREAM_CACHE — run without --offline first" >&2
    exit 1
fi

# Strip our _comment_* annotation keys from the overlay before merging — they're for humans
# reading the overlay file only and would otherwise leak into the generator's input. Then
# deep-merge with `*` (recursive object merge; right side wins on scalar conflicts; arrays
# are replaced wholesale, which is what we want for our new `oneOf` on Request).
jq -s '
    def strip_comments:
        if type == "object" then
            with_entries(select(.key | startswith("_comment_") | not)) | map_values(strip_comments)
        elif type == "array" then
            map(strip_comments)
        else .
        end;
    .[0] * (.[1] | strip_comments)
' "$UPSTREAM_CACHE" "$OVERLAY" > "$OUTPUT.tmp"
mv "$OUTPUT.tmp" "$OUTPUT"

echo "wrote  $OUTPUT"
