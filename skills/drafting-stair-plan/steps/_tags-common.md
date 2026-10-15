# Tags, spots, marks: shared notes (cards 08–12)

Read once per job, before the first tag card.

```
annot_place {viewId, mode, items:[{kind:"tag", elementId, typeName, right, up, leader, endRight, endUp}, {kind:"spot", elementId:<STAIRS id>, typeName, right, up}]}
```

- Points in the view frame; read the free space first with `view_elem_boxes {allAnnotations:true}` (ViewBox).
- Type: `ProjectTypes` from the audit (the most used in the project). Never create a type; none used → ask.
- **Leaders are orthogonal** (user rule): one horizontal or vertical segment when the head lines up with the target, otherwise exactly one elbow (V+H or H+V); never diagonal. `annot_place` adds the elbow (`elbowFirst` V default / H); check with `tag_leaders_info` (shape `D` = diagonal → fix).
- Every tag: close to its element, not over a dim or another tag, not on the tread lines. Exactly one tag per element: a duplicate → delete the worse placed one.
