#!/usr/bin/env python3
"""Validate the local Agent Skills suite using only the Python standard library."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NAME_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")


def parse_frontmatter(path: Path) -> tuple[dict[str, str], str]:
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        raise ValueError("missing opening YAML frontmatter delimiter")
    try:
        end = next(i for i in range(1, len(lines)) if lines[i].strip() == "---")
    except StopIteration as exc:
        raise ValueError("missing closing YAML frontmatter delimiter") from exc

    data: dict[str, str] = {}
    current_key: str | None = None
    for raw in lines[1:end]:
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        if raw.startswith(" ") and current_key:
            continue
        if ":" not in raw:
            raise ValueError(f"invalid frontmatter line: {raw!r}")
        key, value = raw.split(":", 1)
        current_key = key.strip()
        data[current_key] = value.strip().strip('"').strip("'")
    return data, text


def main() -> int:
    errors: list[str] = []
    warnings: list[str] = []
    names: set[str] = set()
    skill_files = sorted(ROOT.glob("*/SKILL.md"))

    if not skill_files:
        errors.append("no immediate child skill directories found")

    for skill_file in skill_files:
        directory_name = skill_file.parent.name
        try:
            metadata, text = parse_frontmatter(skill_file)
        except ValueError as exc:
            errors.append(f"{skill_file.relative_to(ROOT)}: {exc}")
            continue

        name = metadata.get("name", "")
        description = metadata.get("description", "")

        if not name:
            errors.append(f"{directory_name}: missing name")
        elif name != directory_name:
            errors.append(f"{directory_name}: frontmatter name {name!r} must match directory")
        elif not NAME_RE.fullmatch(name):
            errors.append(f"{directory_name}: invalid skill name")
        elif len(name) > 64:
            errors.append(f"{directory_name}: name exceeds 64 characters")

        if name in names:
            errors.append(f"{directory_name}: duplicate skill name {name}")
        names.add(name)

        if not description:
            errors.append(f"{directory_name}: missing description")
        elif len(description) > 1024:
            errors.append(f"{directory_name}: description exceeds 1024 characters")

        line_count = len(text.splitlines())
        if line_count > 500:
            warnings.append(f"{directory_name}: SKILL.md has {line_count} lines; prefer under 500")

    expected = {
        "kioku-chat-orchestrator",
        "kioku-project-context",
        "kioku-memory-publisher",
        "kioku-session-handoff",
        "github-issue-resolution",
        "github-documentation-maintenance",
        "github-pull-request-review",
        "github-issue-status-sync",
        "github-repo-docs-to-vault",
    }
    missing = expected - names
    if missing:
        errors.append(f"missing expected skills: {', '.join(sorted(missing))}")

    for warning in warnings:
        print(f"WARNING: {warning}")
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)

    if errors:
        return 1

    print(f"Validated {len(skill_files)} skills.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
