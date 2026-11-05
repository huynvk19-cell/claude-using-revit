# claude-using-revit — notes for Claude

This repo holds drafting **workflows** (`workflows/`, English: skills in a set order), **skills** (`skills/`, English), **subagents** (`agents/`), **standards** (`domain/`, Vietnamese), MCP **commands** (`revit-commands/`, C#) and the Revit add-in **claude using revit** (`addin/`: C# host in `addin/src/`, ribbon tools + shared tasks in Python in `addin/extension/`). claude using revit is at once the ribbon tab and the MCP server (`http://127.0.0.1:8970/mcp`, server name `claude-using-revit`); it needs neither pyRevit nor another Revit MCP. Installed copies live in `~/.claude/skills/`, `~/.claude/agents/`, `~/.claude/drafting-domain/` and `%APPDATA%\claude-using-revit\`.

## Read only what the task needs

- Never read the whole repo, all of `domain/`, all of `skills/`, all of `workflows/` or all the step cards of a skill.
- Find the topic in the routing table of `skills/drafting-session/SKILL.md` ("Load only what the task needs"). Open that row's skill and standard, nothing else.
- `domain/drafting-tools.md`, `domain/drafting-api-pitfalls.md`: search them (`grep`) for one command or symptom.
- `revit-commands/*.cs`: open only the commands the task touches; the header comment (`/* mcp-tool … */`) holds the parameters, and the `// ---- Details` comment right below it holds the full explanation.
- The `related` list in a standard's header is not a reading list.

## Editing the repo

- A new topic = one skill (`skills/<name>/SKILL.md`) + one standard (`domain/<name>.md`) + a row in the `drafting-session` routing table + rows in `README.md`.
- A new workflow = one file `workflows/<name>.md` + a row in the `drafting-workflow` table + a row in `README.md`. Its row 0 is `drafting-start` gathering every view / sheet of the job once (`drafting-workflow` step 0). A workflow only **calls skills**: rows = skill · cards to run (default all) · applies to, + why this order. Never copy card names, steps, done-when or rules into it: they live in the skill and its cards.
- **Step cards** (divide and conquer): a skill with several steps keeps each step in its own file `skills/<skill>/steps/NN-<item>.md` (title · skill, standard rule, Tools · Applies to · Do · Done when · Skip only if · Never · Score 0–10). `SKILL.md` keeps only the scope, the profile inputs, the ordered list of cards and the skill-wide Never list. Notes shared by several cards: `steps/_<topic>-common.md`. A workflow calls the skill; the card list (order) lives in `SKILL.md`, done-when / skip in the card only. Read only the card of the step at hand.
- Plan and section of the same element are separate topics (e.g. `drafting-stair-plan` vs `drafting-stair-section-parallel`).
- No client names, model names, sheet numbers or machine paths in the repo (README, "Keeping it alive").
- Commands take project values as arguments (from `drafting-profile.md`), never hard-coded.
- Command headers stay short (they are sent to the model on every request): `description` in 1–2 lines, parameter descriptions ≤ 80 characters. Long explanations go in the `// ---- Details` comment below the header.
- Command results stay short by default: counts + only the problems / changed ids (aim ≤ 1.5k characters). Full lists go behind a `verbose` (or `detail`) flag or into the `logPath` file.
- Each skill has a `**Tools**` line listing the commands it uses (loaded in one ToolSearch call). When a skill starts or stops using a command, update that line.
- **Shared tasks** (one job, written once, used by the user and by Claude): a job the user also wants as a ribbon button is a task in `addin/extension/lib/tasks.py` (`@task(help)`, `def name(doc, uidoc, args) -> dict`, no windows, short result, `dryRun` when it writes). The button script calls `tasks.run(...)` or the same lib functions; Claude calls it through the one built-in MCP tool `cur_task`. Never write the same logic again as a C# command; skills name the task instead of describing its steps. Add the task to the table in `domain/drafting-tools.md`.
- Skill `description`s are sent on every request: what it does + trigger words, ≤ ~500 characters. Rules go in the body.

## The claude using revit add-in (`addin/`)

- A new ribbon button = a folder `addin/extension/claude using revit.tab/<Panel>.panel/<Name>.pushbutton/` with `script.py`, `bundle.yaml` (title, tooltip) and `icon.png` (96 px, `addin/icons/make_icons.ps1`), + its name in the panel's `bundle.yaml` layout. Revit shows a new button after a restart; an edited script applies on the next click.
- Scripts are IronPython 2.7 run by the add-in. Host API: `from cur import HOST_APP, EXEC_PARAMS, forms, revit, script` (`addin/extension/lib/cur/`). Never import `pyrevit`.
- A new MCP command = one file `revit-commands/<name>.cs` (header `/* mcp-tool */` + `public static object Run(UIApplication app, JObject args)`). Built-in tools (C#, `addin/src/ClaudeUsingRevit/Mcp/ToolRegistry.cs`) only for what a command file cannot do (the task runner, the server state).
- Before pushing a change to `revit-commands/` or `addin/src/`: build the add-in and run the command check for the Revit versions in use (`addin/build.ps1 -Check`, or `dotnet build addin/src/ClaudeUsingRevit -c Release -p:RevitVersion=2025` then `dotnet run --project addin/tests/CommandCheck -c Release -- 2025`). A change to the MCP server: `bash addin/tests/smoke.sh`.
