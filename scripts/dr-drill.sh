#!/usr/bin/env bash
# Disaster-recovery drill: prove the documented RPO/RTO with a real restore.
#
# What it does, against whatever PostgreSQL the connection string points at:
#   1. records the pre-drill state (row counts of a few business tables),
#   2. takes a `pg_dump` backup (this is the artefact a real DR plan restores),
#   3. destroys the schema — the simulated total loss,
#   4. restores from the backup with `psql`,
#   5. re-checks the state and prints the measured RPO and RTO.
#
# Run it in a maintenance window, never against production data you cannot
# lose. Its value is that the numbers in docs/disaster-recovery.md stop being
# estimates: the drill either finishes and prints the real timings, or it fails
# where the plan is wrong.
#
#   PGHOST=localhost PGPORT=5432 PGDATABASE=atlas PGUSER=atlas PGPASSWORD=... \
#       scripts/dr-drill.sh
#
# Deliberately NOT executed in this repository's CI: a destructive restore has
# no place in a pipeline that shares one database with other suites. CI proves
# the schema rebuilds from migrations instead (see the "Generate and apply
# disposable CI schema" step).
set -euo pipefail

: "${PGDATABASE:?PGDATABASE is required}"
: "${PGUSER:?PGUSER is required}"
BACKUP="${BACKUP_PATH:-/tmp/atlas-dr-drill-$(date -u +%Y%m%dT%H%M%SZ).dump}"

echo "== ATLAS disaster-recovery drill =="
echo "target: ${PGUSER}@${PGHOST:-localhost}:${PGPORT:-5432}/${PGDATABASE}"
echo "backup: ${BACKUP}"

command -v pg_dump >/dev/null || { echo "pg_dump not found — install the PostgreSQL client"; exit 1; }
command -v psql >/dev/null || { echo "psql not found — install the PostgreSQL client"; exit 1; }

# Business tables whose contents prove the data came back, one per schema.
TABLES=(
  '"organizations"."Organizations"'
  '"identity"."Users"'
  '"apimanagement"."ApiDefinitions"'
  '"incidentmanagement"."Incidents"'
  '"audit"."AuditEntries"'
)

count_all() {
  local total=0 table count
  for table in "${TABLES[@]}"; do
    count=$(psql -tAq -c "SELECT count(*) FROM ${table}" 2>/dev/null || echo 0)
    echo "    ${table}: ${count}" >&2
    total=$((total + count))
  done
  echo "${total}"
}

echo "-- 1/5 pre-drill state"
before=$(count_all)
echo "   rows: ${before}"

echo "-- 2/5 pg_dump (the recovery point)"
dump_start=$(date +%s)
pg_dump --format=custom --file="${BACKUP}" --no-owner --no-privileges
dump_seconds=$(( $(date +%s) - dump_start ))
echo "   backup written in ${dump_seconds}s ($(du -h "${BACKUP}" | cut -f1))"

echo "-- 3/5 destroying the schema (simulated loss)"
psql -q -v ON_ERROR_STOP=1 -c 'DROP SCHEMA IF EXISTS public CASCADE' \
  -c 'DROP SCHEMA IF EXISTS identity CASCADE' \
  -c 'DROP SCHEMA IF EXISTS organizations CASCADE' \
  -c 'DROP SCHEMA IF EXISTS apimanagement CASCADE' \
  -c 'DROP SCHEMA IF EXISTS trafficmanagement CASCADE' \
  -c 'DROP SCHEMA IF EXISTS serviceregistry CASCADE' \
  -c 'DROP SCHEMA IF EXISTS eventplatform CASCADE' \
  -c 'DROP SCHEMA IF EXISTS observability CASCADE' \
  -c 'DROP SCHEMA IF EXISTS incidentmanagement CASCADE' \
  -c 'DROP SCHEMA IF EXISTS deploymentintelligence CASCADE' \
  -c 'DROP SCHEMA IF EXISTS policyengine CASCADE' \
  -c 'DROP SCHEMA IF EXISTS audit CASCADE'

echo "-- 4/5 restoring"
restore_start=$(date +%s)
pg_restore --dbname="${PGDATABASE}" --no-owner --no-privileges --clean --if-exists "${BACKUP}" 2>&1 | grep -v 'does not exist, skipping' || true
# A second pass without --clean applies anything a partially-created object blocked.
pg_restore --dbname="${PGDATABASE}" --no-owner --no-privileges "${BACKUP}" >/dev/null 2>&1 || true
restore_seconds=$(( $(date +%s) - restore_start ))

echo "-- 5/5 post-drill state"
after=$(count_all)
echo "   rows: ${after}"

# RPO: the backup is the recovery point, so data loss is bounded by how recent
# it is. This drill takes it immediately before the loss, i.e. RPO ≈ 0.
echo
echo "== result =="
echo "RTO (measured): ${restore_seconds}s restore on this host"
echo "RPO (this drill): ~0s — the backup was taken immediately before the loss; in"
echo "                  production it equals the backup interval (see"
echo "                  docs/disaster-recovery.md for the documented target)."
if [ "${before}" -ne "${after}" ]; then
  echo "MISMATCH: ${before} rows before, ${after} after. The restore is incomplete." >&2
  exit 1
fi
echo "row counts match (${after} rows): restore verified."
