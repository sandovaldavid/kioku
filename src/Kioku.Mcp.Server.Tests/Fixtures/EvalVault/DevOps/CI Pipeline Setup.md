---
tags: [devops, ci]
type: reference
status: evergreen
date: 2025-09-15
---

# CI pipeline setup

The continuous integration pipeline runs on every push: lint, unit tests, build the
Docker image, and integration tests against a disposable database. Merges to main also
publish the image and trigger the deployment workflow.

Cache dependencies aggressively between runs; the pipeline went from twelve minutes to
four just by caching the package restore step.
