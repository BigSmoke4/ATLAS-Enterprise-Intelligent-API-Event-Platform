#!/usr/bin/env python3
"""Turn a k6 ``--summary-export`` file into GitHub workflow annotations.

A workflow run's raw log is not readable without repository write access, while
workflow commands survive as run annotations. When the smoke gate exits
non-zero, this script publishes the few lines that say *why*: the threshold
values and every check that failed.
"""

import json
import sys
from pathlib import Path


def emit(level: str, message: str) -> None:
    print(f"::{level}::{message}")


def _values(metrics: dict, name: str) -> dict:
    metric = metrics.get(name)
    return metric.get("values", {}) if isinstance(metric, dict) else {}


def main() -> int:
    if len(sys.argv) != 2:
        emit("error", "usage: k6-summary-annotations.py <summary.json>")
        return 2

    path = Path(sys.argv[1])
    if not path.is_file():
        emit("error", f"k6 summary not found at {path}")
        return 1

    summary = json.loads(path.read_text(encoding="utf-8"))
    metrics = summary.get("metrics", {})

    failed = _values(metrics, "http_req_failed")
    checks_rate = _values(metrics, "checks")
    emit("error", f"k6 http_req_failed rate={failed.get('rate', 'n/a')} (threshold < 0.01)")
    emit("error", f"k6 checks rate={checks_rate.get('rate', 'n/a')} (threshold == 1)")

    liveness = _values(metrics, "http_req_duration{endpoint:liveness}")
    if liveness:
        emit("error", f"k6 liveness p95={liveness.get('p(95)', 'n/a')} ms (threshold < 250)")

    checks = summary.get("root_group", {}).get("checks", {})
    for name, result in checks.items():
        failures = result.get("fails", 0) if isinstance(result, dict) else 0
        if failures:
            emit(
                "error",
                f"k6 check failed: {name} "
                f"(passes={result.get('passes')}, fails={failures})",
            )
    if not checks:
        emit("error", "k6 summary contains no check results; the scenario did not run")

    return 0


if __name__ == "__main__":
    sys.exit(main())
