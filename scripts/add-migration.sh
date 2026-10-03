#!/usr/bin/env bash
set -euo pipefail
NAME="${1:?Usage: scripts/add-migration.sh MigrationName}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STARTUP="$ROOT/src/Atlas.Web/Atlas.Web.csproj"
modules=(
  "Identity:src/Modules/Identity/Atlas.Modules.Identity.csproj:IdentityDbContext"
  "Organizations:src/Modules/Organizations/Atlas.Modules.Organizations.csproj:OrganizationsDbContext"
  "APIManagement:src/Modules/APIManagement/Atlas.Modules.APIManagement.csproj:ApiManagementDbContext"
  "TrafficManagement:src/Modules/TrafficManagement/Atlas.Modules.TrafficManagement.csproj:TrafficManagementDbContext"
  "ServiceRegistry:src/Modules/ServiceRegistry/Atlas.Modules.ServiceRegistry.csproj:ServiceRegistryDbContext"
  "EventPlatform:src/Modules/EventPlatform/Atlas.Modules.EventPlatform.csproj:EventPlatformDbContext"
  "Observability:src/Modules/Observability/Atlas.Modules.Observability.csproj:ObservabilityDbContext"
  "IncidentManagement:src/Modules/IncidentManagement/Atlas.Modules.IncidentManagement.csproj:IncidentManagementDbContext"
  "DeploymentIntelligence:src/Modules/DeploymentIntelligence/Atlas.Modules.DeploymentIntelligence.csproj:DeploymentIntelligenceDbContext"
  "PolicyEngine:src/Modules/PolicyEngine/Atlas.Modules.PolicyEngine.csproj:PolicyEngineDbContext"
  "Audit:src/Modules/Audit/Atlas.Modules.Audit.csproj:AuditDbContext"
)
for entry in "${modules[@]}"; do
  IFS=: read -r name project context <<< "$entry"
  echo "Creating $name migration $NAME"
  dotnet ef migrations add "$NAME" --project "$ROOT/$project" --startup-project "$STARTUP" --context "$context" --output-dir Infrastructure/Migrations --verbose
 done
