#!/usr/bin/env bash
set -euo pipefail

# Apply reviewed EF Core migrations for every module. This script never calls
# EnsureCreated. Run after PostgreSQL is reachable and connection settings are
# supplied through the normal ASP.NET configuration environment.
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
  echo "Applying $name migrations ($context)"
  dotnet ef database update --project "$ROOT/$project" --startup-project "$STARTUP" --context "$context"
done
