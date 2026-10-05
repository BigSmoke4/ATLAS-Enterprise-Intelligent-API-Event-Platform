#!/usr/bin/env python3
"""Turn a gitleaks SARIF report into GitHub check-run annotations.

The runner log is not readable without repository access, so a failing secret
scan must explain itself through annotations. Each finding becomes one
`::error file=…,line=…::` line naming the rule and file (the secret itself is
already redacted by `--redact`), plus a count summary.

Usage: scripts/gitleaks-annotations.py gitleaks.sarif
"""

from __future__ import annotations

import json
import sys
from pathlib import Path


def escape(value: str) -> str:
    return value.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("usage: gitleaks-annotations.py <sarif-report>", file=sys.stderr)
        return 2

    report = Path(argv[1])
    if not report.exists():
        print(f"::warning::{report} was not produced; the scan failed before writing a report")
        return 0

    try:
        sarif = json.loads(report.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"::warning::could not parse {report}: {error}")
        return 0

    findings = 0
    for run in sarif.get("runs", []):
        rules = {
            rule.get("id", "unknown-rule"): rule.get("name", rule.get("id", "unknown-rule"))
            for rule in run.get("tool", {}).get("driver", {}).get("rules", [])
        }
        for result in run.get("results", []):
            findings += 1
            rule_id = result.get("ruleId", "unknown-rule")
            rule_name = rules.get(rule_id, rule_id)
            message = (result.get("message") or {}).get("text", "secret-shaped value detected")
            locations = (result.get("locations") or [{}])[0].get("physicalLocation", {}) or {}
            artifact = locations.get("artifactLocation", {}).get("uri", "unknown file")
            line = locations.get("region", {}).get("startLine", 1)
            print(f"::error file={escape(artifact)},line={line}::gitleaks [{escape(rule_name)}] {escape(message)}")

    if findings:
        print(f"::error::gitleaks reported {findings} finding(s); see the annotations above")
    else:
        print("gitleaks found no committed secrets in the repository history.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
