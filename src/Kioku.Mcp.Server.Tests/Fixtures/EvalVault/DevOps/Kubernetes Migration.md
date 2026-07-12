---
tags: [devops, kubernetes]
type: project
status: en-progreso
date: 2026-03-01
---

# Kubernetes migration

Plan to migrate services from single-host Docker to Kubernetes. Phase one: containerize
the remaining cron jobs. Phase two: write Helm charts for the three core services and
move staging traffic. Phase three: production cutover with a rollback window.

Main risks: persistent volumes for the database, and the learning curve of debugging
networking inside the cluster. Decision: managed cluster, not self-hosted control plane.
