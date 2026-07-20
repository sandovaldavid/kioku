# MCP Contract Reference

> Generated from live MCP discovery. Do not edit manually.
> Regenerate: `node scripts/generate-public-docs.mjs --write`
> Verify: `node scripts/generate-public-docs.mjs --check`

## Profiles

- Default profile: **43 tools**.
- All-capabilities profile: **59 tools**.
- Prompts: **11**; direct resources: **2**; resource templates: **1**.

Enabled by default: `tasks`, `organization`, `sessions`, `workflows`, `graph`, `engineering`.

Disabled by default: `research`, `generation`, `css`, `assets`, `bridge`, `plugin`.

## Tools

`*` marks required fields. Schemas and behavioral annotations come directly from MCP discovery.

| Tool | Profile | Input schema | Output schema | Behavioral annotations |
|---|---|---|---|---|
| `add_backlog_item` | default | description:string*; project:string*; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `apply_template` | optional | target_note:string; template_path:string* | — | readOnly=false; destructive=false; idempotent=false; openWorld=true |
| `audit_citations` | optional | folder:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `audit_vault` | default | stale_days:integer | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_folder_readme` | default | folder:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=true; idempotent=true; openWorld=false |
| `create_implementation_plan` | default | objective:string*; project:string*; status:string; steps:string*; tags:string; ticket:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_literature_note` | default | author:string*; folder:string; source:string; summary:string; tags:string; title:string*; year:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_moc` | default | folder:string*; output_folder:string; output_name:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=true; idempotent=true; openWorld=false |
| `create_note` | default | author:string; content:string; folder:string; kind:string; link_related:boolean; max_links:integer; name:string; output_folder:string; output_name:string; source:string; status:string; summary:string; tags:string; template:string; type:string; year:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_project_doc` | default | alternatives:string; consequences:string; content:string; context:string; decision:string; description:string; doc_type:string*; fix:string; objective:string; project:string; related_files:string; root_cause:string; status:string; steps:string; symptom:string; tags:string; ticket:string; title:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_regular_note` | default | content:string; folder:string; name:string*; status:string; tags:string; template:string; type:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_zettel` | default | content:string*; folder:string; link_related:boolean; max_links:integer; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=true |
| `delete_note` | default | dry_run:boolean; note:string*; permanent:boolean | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `edit_in_obsidian` | optional | mode:string*; text:string* | — | readOnly=false; destructive=true; idempotent=false; openWorld=true |
| `edit_note` | default | add_separator:boolean; content:string*; mode:string; note:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `end_work_session` | default | agent:string; project:string; session_id:string; session_note:string; summary:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `export_citations` | optional | folder:string; format:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `find_duplicate_notes` | default | max_results:integer; threshold:number | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `find_orphan_assets` | optional | dry_run:boolean | — | readOnly=true; destructive=false; idempotent=false; openWorld=false |
| `find_similar_notes` | default | max_results:integer; min_score:number; note:string* | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `generate_flashcards` | optional | count:integer; dry_run:boolean; format:string; note:string*; output_note:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=true |
| `get_concept_map` | default | depth:integer; max_nodes:integer; note:string* | — | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_installed_plugins` | optional | object | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `get_links` | default | direction:string; format:string; note:string* | — | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_obsidian_state` | optional | object | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `get_project_context` | default | include_content:boolean; limit:integer; project:string*; types:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_server_status` | default | object | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `get_vault_snapshot` | default | island_threshold:integer | — | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_work_context` | default | inbox_folder:string; max_per_section:integer; recent_folder:string; recent_limit:integer | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `import_bibtex` | optional | dry_run:boolean; folder:string; source:string*; update_existing:boolean | — | readOnly=false; destructive=false; idempotent=false; openWorld=true |
| `lint` | optional | note:string; scope:string* | — | readOnly=false; destructive=true; idempotent=false; openWorld=true |
| `list_notes` | default | date_from:string\|null; date_to:string\|null; folder:string; format:string; limit:integer; offset:integer; status:string\|null; tag:string\|null; type:string\|null | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_projects` | default | object | — | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_tasks` | default | folder:string; limit:integer; note:string; offset:integer; overdue_only:boolean; status:string; tag:string | — | readOnly=true; destructive=false; idempotent=false; openWorld=false |
| `list_work_sessions` | default | include_activity:boolean; project:string; sessions_folder:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `manage_css_snippets` | optional | action:string; css_content:string\|null; enable:boolean\|null; name:string\|null | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `manage_tags` | default | dry_run:boolean; new_tag:string; old_tag:string; operation:string*; source_tag:string; target_tag:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `manage_templates` | default | action:string; content:string; name:string; reset_to_default:boolean; scope:string; templates_folder:string; type_key:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `manage_trash` | default | action:string; destination:string; dry_run:boolean; limit:integer; note:string; offset:integer; prefix:string | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `move_note` | default | destination_folder:string; dry_run:boolean; new_name:string; note:string*; update_links:boolean | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `open_note_in_obsidian` | optional | note:string*; split:boolean | — | readOnly=false; destructive=false; idempotent=true; openWorld=true |
| `process_inbox` | default | apply:boolean; inbox_folder:string; max_notes:integer | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `query_dataview` | optional | query:string* | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `read_note` | default | format:string; metadata_only:boolean; note:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `rebuild_index` | default | object | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `record_adr` | default | alternatives:string; consequences:string*; context:string*; decision:string*; project:string*; status:string; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `record_bug` | default | fix:string*; project:string*; related_files:string; root_cause:string*; status:string; symptom:string*; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `save_project_knowledge` | default | content:string*; project:string; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `search_notes` | default | format:string; max_results:integer; min_score:number; mode:string; query:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `set_task_state` | default | completed:boolean*; line_number:integer*; note:string* | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `setup_agent_workflow` | default | patch_config:boolean; project:string; write_templates:boolean | — | readOnly=false; destructive=false; idempotent=true; openWorld=false |
| `start_work_session` | default | agent:string; goal:string; parent_session_id:string; project:string; session_id:string; session_name:string; sessions_folder:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `suggest_folder` | default | max_suggestions:integer; note:string* | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `suggest_links` | default | apply:boolean; max_suggestions:integer; min_similarity:number; note:string; section:string; targets:string | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `suggest_tags` | default | max_suggestions:integer; note:string* | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `summarize_note` | optional | max_words:integer; note:string*; style:string | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `tidy_attachments` | optional | dry_run:boolean; normalize_names:boolean; target_folder:string | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `trigger_obsidian_command` | optional | command_id:string* | — | readOnly=false; destructive=true; idempotent=false; openWorld=true |
| `update_frontmatter` | default | add_tags:string; clear_tags:boolean; note:string*; remove_tags:string; status:string; tags:string; type:string | — | readOnly=false; destructive=true; idempotent=true; openWorld=false |

## Prompts

| Prompt | Arguments |
|---|---|
| `literature_review` | topic* |
| `log_bugfix` | project* |
| `plan_feature` | project*; feature* |
| `process_inbox` | inbox |
| `project_task` | project*; task* |
| `record_decision` | project*; topic* |
| `research_digest` | folder |
| `resume_project` | project* |
| `weekly_review` | — |
| `work_on_ticket` | project*; ticket* |
| `write_daily` | project* |

## Resources

| URI | Kind |
|---|---|
| `kioku://note/seed.md` | direct |
| `kioku://vault/stats` | direct |
| `kioku://note/{path}` | template |
