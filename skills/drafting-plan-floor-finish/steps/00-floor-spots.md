# 00 · Floor spot elevations (tag cao độ sàn)

**Skill** `drafting-plan-floor-finish` · **Standard** `drafting-annotation.md` §2b FL1–FL3
**Applies to** every floor finish plan view of the job, except OVERALL / key plans.

This step is the same as `drafting-plan-dims` card 03. Open `~/.claude/skills/drafting-plan-dims/steps/03-floor-spots.md` and follow it on the floor finish plans: Do, Done when, Skip only if and Never all apply unchanged. Load its **Tools** line.

A view runs this step from one skill only: when the floor plan workflow already ran `drafting-plan-dims` card 03 on a view, skip it here for that view.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
