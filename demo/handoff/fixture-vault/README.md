# Kioku multi-agent handoff demo — fixture vault

This is a minimal, disposable Obsidian vault used only by
`scripts/Kioku.HandoffDemo` to reproduce the multi-agent handoff scenario from
issue #257: one agent persists a plan, an ADR, and a bug report, then ends its
session; a second agent, in a completely separate process and MCP connection,
starts fresh, reads that context back with `get_project_context`, and
continues the work without ever touching the first agent's session.

Nothing in this vault is real project data — the demo project ("acme-checkout")
and its plan/ADR/bug content are fictional fixtures invented for this
walkthrough. See `docs/multi-agent-handoff-demo.md` for the full script and
captured output.

Running `dotnet run --project scripts/Kioku.HandoffDemo` does not modify this
checked-in copy: by default the driver copies this seed into a fresh temporary
directory before writing anything, so repeated runs stay reproducible and
never dirty `git status`. Pass `--vault <path>` to point the driver at a real
vault instead.
