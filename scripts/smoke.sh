#!/usr/bin/env bash
set -euo pipefail
BASE_URL="${ATLAS_BASE_URL:-http://localhost:8080}"

curl --fail --silent --show-error "$BASE_URL/health/live" >/dev/null
curl --fail --silent --show-error "$BASE_URL/health/ready" >/dev/null
curl --fail --silent --show-error "$BASE_URL/metrics" | grep -q "http_server_request_duration"
printf 'ATLAS smoke checks passed for %s\n' "$BASE_URL"
