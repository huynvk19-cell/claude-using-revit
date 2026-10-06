# claude-using-revit — notes for Claude

This repo holds drafting **skills** (`skills/`, English), **standards** (`domain/`, Vietnamese) and Revit MCP **commands** (`revit-commands/`, C#). Installed copies live in `~/.claude/skills/` and `~/.claude/drafting-domain/`.

## Read only what the task needs

- Never read the whole repo, all of `domain/` or all of `skills/`.
- Find the topic in the routing table of `skills/drafting-session/SKILL.md` ("Load only what the task needs"). Open that row's skill and standard, nothing else.
- `domain/drafting-tools.md`, `domain/drafting-api-pitfalls.md`: search them (`grep`) for one command or symptom.
- `revit-commands/*.cs`: open only the commands the task touches; the header comment (`/* mcp-tool … */`) holds the parameters.
- The `related` list in a standard's header is not a reading list.

## Editing the repo

- A new topic = one skill (`skills/<name>/SKILL.md`) + one standard (`domain/<name>.md`) + a row in the `drafting-session` routing table + rows in `README.md`.
- Plan and section of the same element are separate topics (e.g. `drafting-stair-plan` vs `drafting-stair-section`).
- No client names, model names, sheet numbers or machine paths in the repo (README, "Keeping it alive").
- Commands take project values as arguments (from `drafting-profile.md`), never hard-coded.
