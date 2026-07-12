---
tags: [devops, docker]
type: reference
status: evergreen
date: 2025-10-02
---

# Docker Compose basics

Compose lets me define the whole local stack in one YAML file: app, database, cache.
One command brings everything up with the right networks and volumes. Useful patterns:
override files for local-only tweaks, profiles to start optional services, and named
volumes so database data survives container recreation.

Compose is for development and small single-host deployments; for anything bigger,
move to an orchestrator.
