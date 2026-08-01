# Kioku CI smoke client

`Kioku.Ci` is an executable integration harness used by GitHub Actions to verify real Kioku distributions through the official MCP client SDK.

It supports two modes:

- `stdio`: launches an installed `kioku` .NET tool and validates initialization, ping, tool discovery, and a create/read/delete note flow.
- `http`: launches a published native binary, waits for `/health/ready`, authenticates with an API key, and repeats the same flow over Streamable HTTP.

The harness uses a temporary vault and bounded retries for filesystem-watcher index propagation. It is not intended to replace unit or in-process integration tests.
