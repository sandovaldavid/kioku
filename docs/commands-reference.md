# MCP Contract Reference

> Generated from live MCP discovery. Do not edit manually.
> Regenerate: `node scripts/generate-public-docs.mjs --write`
> Verify: `node scripts/generate-public-docs.mjs --check`

## Profiles

- Default profile: **43 tools**.
- All-capabilities profile: **76 tools**.
- Prompts: **12**; direct resources: **2**; resource templates: **4**.

Enabled by default: `tasks`, `organization`, `sessions`, `workflows`, `graph`, `engineering`.

Disabled by default: `research`, `generation`, `css`, `assets`, `bridge`, `plugin`, `coordination`.

## Tools

`*` marks required fields. Schemas and behavioral annotations come directly from MCP discovery.

| Tool | Profile | Input schema | Output schema | Behavioral annotations |
|---|---|---|---|---|
| `acquire_coordination_claim` | optional | agent:string; attempt_id:string*; lease_seconds:integer; resource_key:string*; run_id:string*; session_id:string*; transition_id:string*; work_item_id:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=true; openWorld=false |
| `add_backlog_item` | default | claim_id:string; description:string*; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; project:string*; resource_key:string; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `apply_template` | optional | target_note:string; template_path:string* | — | readOnly=false; destructive=false; idempotent=false; openWorld=true |
| `audit_citations` | optional | folder:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `audit_vault` | default | stale_days:integer | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_coordination_work_item` | optional | agent:string; attempt_id:string; project:string*; resource_scope:string; run_id:string; session_id:string; summary:string; transition_id:string; work_item_id:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_folder_readme` | default | claim_id:string; expected_hash:string; expected_revision:string; fence_generation:integer; folder:string*; mutation_id:string; resource_key:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=true; idempotent=true; openWorld=false |
| `create_implementation_plan` | default | claim_id:string; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; objective:string*; project:string*; resource_key:string; status:string; steps:string*; tags:string; ticket:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_literature_note` | default | author:string*; claim_id:string; expected_hash:string; expected_revision:string; fence_generation:integer; folder:string; mutation_id:string; resource_key:string; source:string; summary:string; tags:string; title:string*; year:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_moc` | default | claim_id:string; expected_hash:string; expected_revision:string; fence_generation:integer; folder:string*; mutation_id:string; output_folder:string; output_name:string; resource_key:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=true; idempotent=true; openWorld=false |
| `create_note` | default | author:string; claim_id:string; content:string; expected_hash:string; expected_revision:string; fence_generation:integer; folder:string; kind:string; link_related:boolean; max_links:integer; mutation_id:string; name:string; output_folder:string; output_name:string; resource_key:string; source:string; status:string; summary:string; tags:string; template:string; type:string; year:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_project_doc` | default | alternatives:string; claim_id:string; consequences:string; content:string; context:string; decision:string; description:string; doc_type:string*; expected_hash:string; expected_revision:string; fence_generation:integer; fix:string; mutation_id:string; objective:string; project:string; related_files:string; resource_key:string; root_cause:string; status:string; steps:string; symptom:string; tags:string; ticket:string; title:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_regular_note` | default | claim_id:string; content:string; expected_hash:string; expected_revision:string; fence_generation:integer; folder:string; mutation_id:string; name:string*; resource_key:string; status:string; tags:string; template:string; type:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `create_zettel` | default | claim_id:string; content:string*; expected_hash:string; expected_revision:string; fence_generation:integer; folder:string; link_related:boolean; max_links:integer; mutation_id:string; resource_key:string; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=true |
| `delete_note` | default | claim_id:string; dry_run:boolean; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; note:string*; permanent:boolean; resource_key:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `edit_in_obsidian` | optional | mode:string*; text:string* | — | readOnly=false; destructive=true; idempotent=false; openWorld=true |
| `edit_note` | default | add_separator:boolean; claim_id:string; content:string*; expected_hash:string; expected_revision:string; fence_generation:integer; mode:string; mutation_id:string; note:string*; resource_key:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `end_work_session` | default | agent:string; claim_id:string; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; project:string; resource_key:string; session_id:string; session_note:string; summary:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `expire_coordination_claim` | optional | attempt_id:string*; claim_id:string*; fence_generation:integer*; reason:string; resource_key:string*; run_id:string*; transition_id:string*; work_item_id:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=true; openWorld=false |
| `export_citations` | optional | folder:string; format:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `find_duplicate_notes` | default | max_results:integer; threshold:number | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `find_orphan_assets` | optional | dry_run:boolean | — | readOnly=true; destructive=false; idempotent=false; openWorld=false |
| `find_similar_notes` | default | max_results:integer; min_score:number; note:string* | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `generate_flashcards` | optional | claim_id:string; count:integer; dry_run:boolean; expected_hash:string; expected_revision:string; fence_generation:integer; format:string; mutation_id:string; note:string*; output_note:string; resource_key:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=true |
| `get_concept_map` | default | depth:integer; max_nodes:integer; note:string* | — | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_coordination_handoff` | optional | run_id:string*; work_item_id:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_coordination_work_item` | optional | run_id:string*; work_item_id:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_installed_plugins` | optional | object | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `get_links` | default | direction:string; format:string; note:string* | — | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_obsidian_state` | optional | object | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `get_project_context` | default | include_content:boolean; limit:integer; project:string*; types:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_server_status` | default | object | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `get_vault_snapshot` | default | island_threshold:integer | — | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `get_work_context` | default | inbox_folder:string; max_per_section:integer; recent_folder:string; recent_limit:integer | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `import_bibtex` | optional | dry_run:boolean; folder:string; source:string*; update_existing:boolean | — | readOnly=false; destructive=false; idempotent=false; openWorld=true |
| `lint` | optional | note:string; scope:string* | — | readOnly=false; destructive=true; idempotent=false; openWorld=true |
| `list_coordination_blockers` | optional | limit:integer; offset:integer; run_id:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_coordination_claims` | optional | limit:integer; offset:integer; run_id:string; status:string; work_item_id:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_coordination_conflicts` | optional | limit:integer; offset:integer; run_id:string; status:string; work_item_id:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_coordination_history` | optional | limit:integer; offset:integer; run_id:string*; work_item_id:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_coordination_runs` | optional | limit:integer; offset:integer | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_coordination_work_items` | optional | limit:integer; offset:integer; project:string; run_id:string; state:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_failed_coordination_attempts` | optional | limit:integer; offset:integer; run_id:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_notes` | default | date_from:string\|null; date_to:string\|null; folder:string; format:string; limit:integer; offset:integer; status:string\|null; tag:string\|null; type:string\|null | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_projects` | default | object | — | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_stale_coordination_work` | optional | limit:integer; offset:integer; run_id:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `list_tasks` | default | folder:string; limit:integer; note:string; offset:integer; overdue_only:boolean; status:string; tag:string | — | readOnly=true; destructive=false; idempotent=false; openWorld=false |
| `list_work_sessions` | default | include_activity:boolean; project:string; sessions_folder:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `manage_css_snippets` | optional | action:string; css_content:string\|null; enable:boolean\|null; name:string\|null | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `manage_tags` | default | dry_run:boolean; new_tag:string; old_tag:string; operation:string*; source_tag:string; target_tag:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `manage_templates` | default | action:string; claim_id:string; content:string; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; name:string; reset_to_default:boolean; resource_key:string; scope:string; templates_folder:string; type_key:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `manage_trash` | default | action:string; claim_id:string; destination:string; dry_run:boolean; expected_hash:string; expected_revision:string; fence_generation:integer; limit:integer; mutation_id:string; note:string; offset:integer; prefix:string; resource_key:string | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `move_note` | default | claim_id:string; destination_folder:string; dry_run:boolean; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; new_name:string; note:string*; resource_key:string; update_links:boolean | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `open_note_in_obsidian` | optional | note:string*; split:boolean | — | readOnly=false; destructive=false; idempotent=true; openWorld=true |
| `process_inbox` | default | apply:boolean; inbox_folder:string; max_notes:integer | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `query_dataview` | optional | query:string* | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `read_note` | default | format:string; metadata_only:boolean; note:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=false |
| `rebuild_index` | default | object | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `record_adr` | default | alternatives:string; claim_id:string; consequences:string*; context:string*; decision:string*; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; project:string*; resource_key:string; status:string; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `record_bug` | default | claim_id:string; expected_hash:string; expected_revision:string; fence_generation:integer; fix:string*; mutation_id:string; project:string*; related_files:string; resource_key:string; root_cause:string*; status:string; symptom:string*; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `release_coordination_claim` | optional | agent:string; attempt_id:string*; claim_id:string*; fence_generation:integer*; reason:string; resource_key:string*; run_id:string*; session_id:string*; transition_id:string*; work_item_id:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=true; openWorld=false |
| `renew_coordination_claim` | optional | agent:string; attempt_id:string*; claim_id:string*; fence_generation:integer*; lease_seconds:integer; reason:string; resource_key:string*; run_id:string*; session_id:string*; transition_id:string*; work_item_id:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=true; openWorld=false |
| `resolve_coordination_conflict` | optional | agent:string; conflict_id:string*; resolution:string*; session_id:string; status:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=true; openWorld=false |
| `save_project_knowledge` | default | claim_id:string; content:string*; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; project:string; resource_key:string; tags:string; title:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `search_notes` | default | format:string; max_results:integer; min_score:number; mode:string; query:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `set_task_state` | default | claim_id:string; completed:boolean*; expected_hash:string; expected_revision:string; fence_generation:integer; line_number:integer*; mutation_id:string; note:string*; resource_key:string | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `setup_agent_workflow` | default | patch_config:boolean; project:string; write_templates:boolean | — | readOnly=false; destructive=false; idempotent=true; openWorld=false |
| `start_work_session` | default | agent:string; claim_id:string; expected_hash:string; expected_revision:string; fence_generation:integer; goal:string; mutation_id:string; parent_session_id:string; project:string; resource_key:string; session_id:string; session_name:string; sessions_folder:string | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `suggest_folder` | default | max_suggestions:integer; note:string* | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `suggest_links` | default | apply:boolean; claim_id:string; expected_hash:string; expected_revision:string; fence_generation:integer; max_suggestions:integer; min_similarity:number; mutation_id:string; note:string; resource_key:string; section:string; targets:string | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `suggest_tags` | default | max_suggestions:integer; note:string* | — | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `summarize_note` | optional | max_words:integer; note:string*; style:string | — | readOnly=true; destructive=false; idempotent=true; openWorld=true |
| `tidy_attachments` | optional | dry_run:boolean; normalize_names:boolean; target_folder:string | — | readOnly=false; destructive=true; idempotent=false; openWorld=false |
| `transition_coordination_work_item` | optional | agent:string; attempt_id:string; claim_id:string; error_code:string; event_type:string; expected_state_version:integer; fence_generation:integer; next_state:string*; outcome:string; progress_reference:string; reason:string; result_reference:string; run_id:string*; session_id:string; transition_id:string; work_item_id:string* | data:object*; error:object\|null*; pagination:object\|null*; success:boolean*; warnings:array<string>* | readOnly=false; destructive=false; idempotent=false; openWorld=false |
| `trigger_obsidian_command` | optional | command_id:string* | — | readOnly=false; destructive=true; idempotent=false; openWorld=true |
| `update_frontmatter` | default | add_tags:string; claim_id:string; clear_tags:boolean; expected_hash:string; expected_revision:string; fence_generation:integer; mutation_id:string; note:string*; remove_tags:string; resource_key:string; status:string; tags:string; type:string | — | readOnly=false; destructive=true; idempotent=true; openWorld=false |

## Prompts

| Prompt | Arguments |
|---|---|
| `coordinate_work` | run_id; work_item_id |
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
| `kioku://coordination/handoff/{run_id}/{work_item_id}` | template |
| `kioku://coordination/history/{run_id}/{work_item_id}` | template |
| `kioku://coordination/work/{run_id}/{work_item_id}` | template |
| `kioku://note/{path}` | template |
