#!/usr/bin/env python3
"""Fail CI when Cobertura line coverage for the Kioku server falls below the baseline."""

from __future__ import annotations

import argparse
import glob
import os
import sys
import xml.etree.ElementTree as ET


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pattern", default="coverage/**/coverage.cobertura.xml")
    parser.add_argument("--assembly", default="Kioku.Mcp.Server")
    parser.add_argument("--minimum", type=float, default=40.0)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    files = sorted(glob.glob(args.pattern, recursive=True))
    if not files:
        print(f"[error] No coverage files matched {args.pattern!r}.", file=sys.stderr)
        return 2

    covered = 0
    valid = 0
    matched_packages: set[str] = set()

    for path in files:
        root = ET.parse(path).getroot()
        for package in root.findall(".//package"):
            name = package.attrib.get("name", "")
            if name != args.assembly and not name.startswith(f"{args.assembly}."):
                continue

            matched_packages.add(name)
            for line in package.findall(".//line"):
                valid += 1
                if int(line.attrib.get("hits", "0")) > 0:
                    covered += 1

    if valid == 0:
        print(
            f"[error] No executable lines found for assembly prefix {args.assembly!r}.",
            file=sys.stderr,
        )
        return 2

    percentage = covered * 100.0 / valid
    summary = (
        f"Kioku server line coverage: {percentage:.2f}% "
        f"({covered}/{valid}); required: {args.minimum:.2f}%"
    )
    print(summary)

    github_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if github_summary:
        with open(github_summary, "a", encoding="utf-8") as output:
            output.write("## Coverage gate\n\n")
            output.write(f"- {summary}\n")
            output.write(f"- Packages: {', '.join(sorted(matched_packages))}\n")

    if percentage + 1e-9 < args.minimum:
        print(f"[error] {summary}", file=sys.stderr)
        return 1

    print("[ok] Coverage threshold satisfied.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
