#!/usr/bin/env python3
"""Validate the Kioku ChatGPT Agent Skills suite with the Python standard library."""

from __future__ import annotations

import json
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


def load_json(path: Path, errors: list[str]) -> object | None:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"{path.relative_to(ROOT)}: invalid JSON: {exc}")
        return None


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
        if len(text.splitlines()) > 500:
            warnings.append(f"{directory_name}: SKILL.md exceeds 500 lines")

    expected = {
        "kioku-chat-orchestrator", "kioku-project-context", "kioku-memory-publisher",
        "kioku-session-handoff", "github-issue-resolution", "github-documentation-maintenance",
        "github-pull-request-review", "github-issue-status-sync", "github-repo-docs-to-vault",
    }
    missing = expected - names
    if missing:
        errors.append(f"missing expected skills: {', '.join(sorted(missing))}")

    manifest = load_json(ROOT / "manifest.json", errors)
    if isinstance(manifest, dict):
        declared = {item.get("name") for item in manifest.get("skills", []) if isinstance(item, dict)}
        if declared != names:
            errors.append("manifest skill names do not match discovered skills")
        for item in manifest.get("skills", []):
            if not isinstance(item, dict):
                continue
            unknown = set(item.get("depends_on", [])) - names
            if unknown:
                errors.append(f"{item.get('name')}: unknown dependencies: {', '.join(sorted(unknown))}")
        profile = manifest.get("observed_vault_profile", {})
        if profile.get("projects_root") != "20-execution":
            errors.append("manifest observed vault profile must use projects_root=20-execution")

    evals = load_json(ROOT / "evals" / "activation-cases.json", errors)
    if isinstance(evals, dict) and not evals.get("cases"):
        errors.append("activation cases must not be empty")

    contracts = [
        ROOT / "kioku-memory-publisher" / "references" / "vault-contract.md",
        ROOT / "github-repo-docs-to-vault" / "references" / "vault-contract.md",
    ]
    if all(path.exists() for path in contracts):
        if contracts[0].read_text(encoding="utf-8") != contracts[1].read_text(encoding="utf-8"):
            errors.append("duplicated vault contracts have diverged")

    checked_paths = [
        ROOT / "README.md",
        ROOT / "fallback" / "PROJECT_INSTRUCTIONS.md",
        ROOT / "kioku-chat-orchestrator" / "SKILL.md",
        ROOT / "kioku-project-context" / "SKILL.md",
        ROOT / "kioku-memory-publisher" / "SKILL.md",
        ROOT / "kioku-session-handoff" / "SKILL.md",
        *contracts,
    ]
    corpus = "\n".join(path.read_text(encoding="utf-8") for path in checked_paths)
    fallback_text = (ROOT / "fallback" / "PROJECT_INSTRUCTIONS.md").read_text(encoding="utf-8")
    if "Load the relevant project workspace under `Projects/<owner>/<repository>/`" in fallback_text:
        errors.append("fallback still instructs the obsolete owner/repository workspace layout")
    for contract in contracts:
        contract_text = contract.read_text(encoding="utf-8")
        if "\ntype: adr\n" in contract_text:
            errors.append(f"{contract.relative_to(ROOT)}: obsolete ADR type remains")
    if "status: completed" in (ROOT / "kioku-session-handoff" / "SKILL.md").read_text(encoding="utf-8"):
        errors.append("session handoff still uses completed instead of done")
    for required in ["20-execution", "type: guide", ".kioku/embeddings.bin", "YYYY-MM-DD-HHmm-chatgpt.md"]:
        if required not in corpus:
            errors.append(f"required Cortex-L7 convention missing: {required}")

    for warning in warnings:
        print(f"WARNING: {warning}")
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    if errors:
        return 1
    print(f"Validated {len(skill_files)} skills, manifest, evals, and Cortex-L7 conventions.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
