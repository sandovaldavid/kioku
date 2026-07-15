---
tags: [devops, docker, deployment]
type: reference
status: evergreen
date: 2025-11-20
---

# Docker deployment

Checklist for deploying the app with Docker in production: build the image from the
multi-stage Dockerfile, tag it with the git SHA, push to the private registry, and roll
it out with zero downtime using the blue-green strategy behind the reverse proxy.

## Key practices

- Never use the latest tag in production; pin image digests.
- Health checks on every container so the orchestrator can restart unhealthy ones.
- Keep secrets out of the image; inject them at runtime via environment variables.
- Log to stdout and let the platform aggregate.
