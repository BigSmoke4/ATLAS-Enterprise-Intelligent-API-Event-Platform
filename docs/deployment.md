# Deployment

`docker-compose.yml` brings up Postgres, Redis, Kafka (+ Zookeeper), Kafka UI,
Prometheus, Grafana, and `atlas-web` built from the included multi-stage
`Dockerfile`. Configuration is via environment variables /
`appsettings.*.json`; no credentials are committed and `.env.example`
documents required variables.

## Release order

1. Provision PostgreSQL, Redis, and Kafka.
2. Run the reviewed EF migrations with `scripts/migrate.sh` using a temporary
   schema-migration identity.
3. Deploy the application with a restricted runtime database identity.
4. Verify `/health/live`, then `/health/ready`.
5. Enable development seeding only in development environments.
6. Monitor `/metrics`, logs, traces, and deployment regression analysis.

## CI integration environment

GitHub Actions provisions PostgreSQL, Redis, Zookeeper, and Kafka. The
WebApplicationFactory integration suite verifies API authorization and
readiness against those services before the Docker build. Readiness failures
are surfaced as dependency failures rather than masked by a hard-coded 200.

For a running Compose deployment, execute `scripts/smoke.sh` (or set
`ATLAS_BASE_URL`) to verify liveness, dependency readiness, and the
Prometheus-compatible metrics endpoint.
