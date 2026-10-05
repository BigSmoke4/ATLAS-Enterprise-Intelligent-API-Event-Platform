#!/usr/bin/env bash
# Deny-list secret scan for committed material.
#
# This is deliberately a *curated* scanner, not a general one: it knows the
# credential formats ATLAS could plausibly leak (cloud keys, GitHub tokens,
# private keys, provider API keys), plus the two house rules that matter more
# than any regex — no real values in .env.example, and no literal
# high-entropy secret assigned to a Password/Secret/ApiKey key.
#
# It runs in CI and locally (`scripts/secret-scan.sh`). Exit code 1 means a
# finding that must be fixed before merge; a finding is never "accepted" by
# editing this script without also documenting why in docs/security.md.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

findings=0
report() {
  # $1 = file, $2 = line number, $3 = rule, $4 = matched text (already redacted)
  echo "::error file=$1,line=$2::$3 ($4)"
  findings=$((findings + 1))
}

# Paths that are generated, vendored or intentionally full of placeholders.
EXCLUDES=(
  ':!*.Designer.cs'
  ':!*/Migrations/*'
  ':!*.lock'
  ':!.env.example'
  ':!scripts/secret-scan.sh'
  ':!docs/*.md'
  ':!*.log'
)

scan() { # scan <rule> <regex>
  local rule="$1" regex="$2" file line text
  while IFS=: read -r file line text; do
    [ -z "${file}" ] && continue
    report "${file}" "${line}" "${rule}" "${text:0:12}…"
  done < <(git grep -nIE "${regex}" -- "${EXCLUDES[@]}" 2>/dev/null)
}

scan "private key material"        '-----BEGIN [A-Z ]*PRIVATE KEY-----'
scan "AWS access key id"           '\bAKIA[0-9A-Z]{16}\b'
scan "GitHub token"                '\b(gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b'
scan "Anthropic API key"           '\bsk-ant-[A-Za-z0-9_\-]{20,}\b'
scan "OpenAI API key"              '\bsk-[A-Za-z0-9]{32,}\b'
scan "Slack token"                 '\bxox[baprs]-[A-Za-z0-9-]{10,}\b'
scan "Google API key"              '\bAIza[0-9A-Za-z_\-]{35}\b'
# A literal 40+ character secret assigned to a secret-ish key. Placeholders
# (`change_me…`, `${VAR}`, `use-a-local-only-password`, empty) never match.
scan "literal secret in configuration" \
  '(Password|Passwd|Secret|ApiKey|ApiSecret|AccessToken|ClientSecret)[[:space:]]*[=:][[:space:]]*["'\'']?[A-Za-z0-9/+_\-]{20,}["'\'']?'

# .env.example must stay placeholder-only. Only secret-shaped keys are checked:
# POSTGRES_DB=atlas is a name, POSTGRES_PASSWORD=<anything literal> is a leak.
while IFS= read -r line; do
  case "${line}" in
    ''|'#'*) continue ;;
  esac
  key="${line%%=*}"
  value="${line#*=}"
  case "${key}" in
    *PASSWORD*|*PASSWD*|*SECRET*|*TOKEN*|*APIKEY*|*API_KEY*) ;;
    *) continue ;;
  esac
  case "${value}" in
    ''|change_me*|*'${'*|*'PLACEHOLDER'*|*'CHANGE_ME'*|*'use-a-local-only'*) ;;
    *) echo "::error file=.env.example::$key holds a literal value, not a placeholder"; findings=$((findings + 1)) ;;
  esac
done < .env.example

if [ "${findings}" -ne 0 ]; then
  echo "secret scan: ${findings} finding(s) — remove the value and rotate the credential."
  exit 1
fi
echo "secret scan: clean (curated deny-list + .env.example placeholder rule)."
