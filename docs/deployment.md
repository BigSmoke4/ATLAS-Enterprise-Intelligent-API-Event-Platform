# Deployment

`docker-compose.yml` brings up Postgres, Redis, Kafka (+ Zookeeper),
Kafka UI, and `atlas-web` built from the included `Dockerfile`
(multi-stage: SDK build → ASP.NET runtime image). Configuration is via
environment variables / `appsettings.*.json`; no credentials are committed —
`.env.example` documents the required variables and `.env` is git-ignored.
