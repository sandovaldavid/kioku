# MCP tool contract policy

Kioku publishes MCP `ToolAnnotations` for every tool returned by `tools/list`. These values are safety and UX hints, not authorization controls. Filesystem policy, transport authentication, bridge capability negotiation, and runtime validation remain authoritative.

## Annotation decisions

| Hint | Kioku convention |
|---|---|
| `readOnlyHint` | `true` only when a call cannot mutate the vault, Obsidian, local services, or external state. |
| `destructiveHint` | `true` when a successful call may overwrite, delete, move, restore, rewrite, or broadly reformat existing user data. |
| `idempotentHint` | `true` only when retrying the same request with the same arguments has no additional effect. |
| `openWorldHint` | `true` when behavior or returned data depends on a process or plugin outside Kioku's indexed vault boundary, including Ollama and the Obsidian bridge. |

Tools with conditional dry-run parameters are annotated for their mutating execution path. Clients must not assume that a default dry run makes the complete tool read-only.

## Compatibility rules

The annotation policy is centralized in `KiokuToolAnnotations`. Both stdio and Streamable HTTP apply it through the shared `tools/list` request filter.

Any pull request that adds or renames a tool must:

1. review all four hints;
2. add or update contract tests;
3. document migrations for removed or renamed tools;
4. regenerate the public command reference when its generator includes annotation output.

Unknown tools receive conservative defaults: not read-only, not destructive, not idempotent, and closed-world. This prevents a newly introduced tool from being accidentally advertised as safe before review.

## Generated reference

See the generated [MCP commands reference](commands-reference.md) for the
current tool inventory, schemas, and annotation values.
