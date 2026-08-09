# {{project}}

> [!summary] Project workspace
> This note is the project home. Edit it freely — agents read it first via `get_project_context`.

## About

_(what this project is, its goals, and its current state)_

## Key links

- Repository:
- Environments:
- Documentation:

## Decisions

```dataview
TABLE without id file.link AS "ADR", status, date
FROM "{{decisions_folder}}"
SORT file.name DESC
```

## Specs

```dataview
TABLE without id file.link AS "Spec", status, date
FROM "{{project_folder}}"
WHERE type = "spec"
SORT choice(status = "approved", 0, choice(status = "draft", 1, 2)) ASC, date DESC
```

## Active plans

```dataview
TABLE without id file.link AS "Plan", status, date
FROM "{{plans_folder}}"
WHERE status != "done"
SORT date DESC
```

## Open bugs

```dataview
TABLE without id file.link AS "Bug", status, date
FROM "{{bugs_folder}}"
WHERE status = "open"
SORT date DESC
```

## Backlog

```dataview
LIST FROM "{{backlog_folder}}"
WHERE status = "proposed"
SORT date DESC
```

> [!tip] The tables above need the Dataview community plugin. Without it, replace them with manual lists or rely on `get_project_context`.