---
name: drafting-standards-db
description: "Standards database (SQLite): look up the sheet catalogue across building types by stage, conditions (elevator, basement, pool), drawing kind, level; log and search what the user corrected. Use when the user corrects a result or decides a rule (sửa lại, góp ý, từ nay, ghi nhớ), asks what was corrected before, or asks which drawings a set needs. Tra danh mục bản vẽ, nhật ký sửa, database chuẩn."
---

# Standards database

Standard: `~/.claude/drafting-domain/drafting-standards-db.md` (Vietnamese, DB1–DB5).

One script, plain Python 3 (`python`, or `py -3` on Windows), no Revit needed:
- repo checkout: `tools/standards-db/standards_db.py`
- installed copy: `~/.claude/drafting-domain/tools/standards-db/standards_db.py` (read only)

It rebuilds the database from the `.md` standards and the CSV files on every call, so there is nothing to refresh. No Python on the machine → read the sheet-set file as before, and put the user's correction in the report.

## Look up the catalogue (DB3, DB4)

- `standards_db.py types`: building type keys, paper, row counts.
- `standards_db.py sheets --type <key> --stage <YT|CS|KT|TC|HC> --has <keys>`: the drawings the set needs, in sheet order, with kind and level; then the rows left out and why. Condition keys: `conditions`. `--detail` adds the mandatory content.
- A question no command answers: `sql "SELECT …"` on the tables of `schema.sql` (read only).
- The catalogue rows come from the `.md` tables. To change a row, edit the `.md` file, never the database.

## Log a correction (DB2)

When the user corrects a result or decides how drawings are made:

1. Write the rule into its standard first (`domain/…`), then fix the model.
2. Add one row, in the repo checkout:
   `standards_db.py correction-add --date <day the user said it> --slug <short-name> --scope <all | type keys> --topic <word> --rule "<one sentence>" [--kind <kind keys>] [--why "<the user's reason>"] --ref "<file> <§ or rule code>"`
3. The rule cannot go into a standard this turn → leave out `--ref`: the row is `open`. Later: `correction-set <id> --ref "…"`.
4. The user changed their mind → add the new row, then `correction-set <old id> --superseded-by <new id>`.
5. `standards_db.py check`, then commit `data/corrections.csv` with the standard.

Before working on a drawing kind or a building type: `corrections --scope <type> --kind <kind> --status open`. An open row wins over the standard it contradicts.

## Never

- Edit `standards.db` or the catalogue tables by hand: change the text source.
- Guess a reason: `--why` holds only what the user said.
- Put client names, model names, sheet numbers or machine paths in a row; a project-only lesson goes in the profile.
- Delete or reword an old row when the user changes their mind: supersede it.
- Log a correction with the installed copy: the next install overwrites it.
