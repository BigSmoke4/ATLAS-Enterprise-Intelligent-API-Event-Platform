#!/usr/bin/env bash
set -euo pipefail
BASE_URL="${ATLAS_BASE_URL:-http://localhost:8080}"

curl --fail --silent --show-error "$BASE_URL/health/live" >/dev/null
curl --fail --silent --show-error "$BASE_URL/health/ready" >/dev/null
curl --fail --silent --show-error "$BASE_URL/metrics" | grep -q "http_server_request_duration"

# MVC authorization wiring: an unauthenticated browser hitting the
# command center must be challenged (redirect to sign-in), never served
# the control-room shell.
dashboard_code=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/Dashboard")
if [ "$dashboard_code" != "302" ] && [ "$dashboard_code" != "401" ]; then
  printf 'Expected an authentication challenge for /Dashboard, got HTTP %s\n' "$dashboard_code" >&2
  exit 1
fi

printf 'ATLAS smoke checks passed for %s\n' "$BASE_URL"
