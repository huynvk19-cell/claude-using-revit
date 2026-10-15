# stair_section_annotate and dims: shared notes (cards 01–08, 10)

Read once per job, before the first card that names it.

```
stair_section_annotate {viewId, mode:"preview", dimTypeName:<check type>, la1LineMm,
  runTagTypes:[{fromMm, type}], spotTypeName, numberSourceId:<a finished NumberSystem>,
  la5TargetMm:1200, la5RailingIds:[<P02…>], la5LeftX, la5RightX,
  parts:[<the card's part>], items:[{part, flight|landing, x, z}]}
```

- Pass `parts` with the card's part only (one card = one part). Without `parts` it runs all of them (LA1 LA3 LA4 LA5 LA6 LB1 LB3 LC).
- Read `Done` (texts, values, positions) → same call with `mode:"apply", logPath` → `view_elem_boxes` on the new ids (Box ≠ null) → export the view image. Undo: `mode:"undo", logPath`.
- References are taken on the **Stairs** element (run / landing 3D faces are hidden: dims on them are created but not drawn); the level plane where there is no landing.
- Layout that worked (1:50): LA3 line at 30 % of the flight height, run tag head at 8 % with a horizontal leader to the soffit; LA1 300 mm inside an existing overall dim; spots on the landing away from the F.. box.
- Each dimension once; dims not over tags, numbers or spots.

## Formula text

```
dims_text {viewId, mode:"apply", logPath, items:[
  {dimId, valueMm:2710, prefix:"169.4mm x 16R = ", below:"(EQUAL RISERS)"},
  {dimId, valueMm:4200, prefix:"280mm x 15T = ",  below:"(EQUAL TREADS)"}]}
```

Text from `RiseText` / `GoingText`. One spelling for the project. Never Replace with text (R9).

## Manual references (cases the tool does not cover)

| Dim | References |
|---|---|
| LA1 rise chain | top faces of the landings / floors (`HostObjectUtils.GetTopFaces` for floors; landing geometry with `Options{ComputeReferences=true, View=v}`), or the level when the finish floor sits on it |
| LA3 going chain | wall finish faces, first / last riser faces of the cut flight (vertical planar faces normal to the view's right direction) |
| LA4 clear height | landing top face → soffit face of the landing / flight above |
| LA5 handrail | landing top → top of the rail along the landing edge (`railing.TopRail` symbol reference; P01: `GetHandRails()`) |
| LA6 | `new Reference(grid)`, wall side faces, door `FamilyInstanceReferenceType.Left / Right` |

Placement per the standard's layout table: LA1 outside one wall, LA2 outside the other, LA3 at mid-height of each cut flight, LA4 inside the core next to the walls, LA5 on each cut landing near the flight edge, LA6 below the section.
