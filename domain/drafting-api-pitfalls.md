---
name: drafting-api-pitfalls
description: "Các bẫy Revit API (2023) đã gặp khi tự động hoá triển khai bản vẽ, kèm cách tránh. Revit API pitfalls for drafting automation."
metadata:
  updated: "2026-10-06"
  related: ["drafting-tools", "drafting-dimensions", "drafting-views-sheets"]
---

# Bẫy Revit API (Pitfalls)

| Hiện tượng | Nguyên nhân | Cách làm |
|---|---|---|
| Cửa vẫn thiếu dim sau khi chạy tool theo hàng | Tool tính "đã có dim" theo vị trí (dim nào gần cửa cũng tính), và bỏ qua cả hàng khi hết chỗ. | Kiểm tra theo **tham chiếu** từng cửa (`opening_dims_each audit`), bổ sung từng cửa một. |
| Cao độ level lệch so với cửa khi so sánh | `Level.Elevation` là tuyệt đối, còn toạ độ cửa tính theo gốc view. | Đổi level sang toạ độ view: `VY(new XYZ(O.X, O.Y, level.ProjectElevation))`. |
| Revit "Not Responding" khi chạy trên mặt đứng/mặt cắt | Gọi nhiều view một lần. Lệnh động không ngắt được từ bên ngoài. | Mỗi lần gọi một view, đặt `timeoutSeconds`, kiểm tra `Responding` trước khi gọi. |
| Dựng lại dim thì **mất reference grid** (vd. 31 → 25 đoạn) | Reference grid lấy từ dim cũ hoặc từ `ParseFromStableRepresentation` bị Revit bỏ. | Với grid luôn dùng `new Reference(grid)`. |
| Reference Level bị bỏ khi gộp với reference của instance khác | Revit loại reference không cùng mặt phẳng hoặc không hợp lệ. | Dùng 2 dim thẳng hàng (Level→Bottom và chiều cao khung) thay vì một chuỗi. |
| Cửa cuốn dim ra 3400 thay vì 3000 | Lấy mặt khung / hộp cuốn. | Dùng named reference của family (`GetReferenceName` = TOP / LEFT / RIGHT). |
| Dim hoặc tag cho cửa bị che | Bounding box không biết vật che phía trước. | `ReferenceIntersector` nhiều tia, có `FindReferencesInRevitLinks`; tia trúng vật khác trước thì coi là bị che. |
| Không tìm thấy dim đen trong dependent view | Dim thuộc view cha (`OwnerViewId` ≠ dependent). | Thu thập dim bằng `FilteredElementCollector(doc, viewId)`, không lọc theo `OwnerViewId`. |
| Dim trục không hiện ở dependent | Dim nằm ngoài crop của dependent. | Đặt dim vào trong crop của từng dependent. |
| Sửa crop không có tác dụng | View gắn Scope Box. | Chỉnh scope box bằng tay. |
| Offset annotation crop đọc ra nhỏ hơn thực tế hàng trăm lần | `Left/Right/Top/BottomAnnotationCropOffset` tính bằng **feet trên giấy** (0.0164 ft = 5 mm), không phải feet model. | mm giấy = giá trị × 304.8. Không chia cho tỉ lệ view. |
| Báo "bubble bị annotation crop cắt" nhưng ảnh vẫn thấy bubble | Revit **không cắt trục/bubble** theo annotation crop. Annotation crop chỉ cắt annotation, trong đó có dim. | Kiểm tra annotation crop theo **dim** và **đầu trục có bubble**, không theo vòng tròn bubble. |
| Trong dependent view, dim trục của view cha tham chiếu cả những trục không hiện | Chain của view cha chạy qua mọi dependent. | Tham chiếu tới trục song song không hiện trong view → bỏ qua. Chain phủ đủ các trục đang hiện vẫn tính là chain của view. |
| Dời dim trục ở một dependent làm hỏng dependent bên cạnh | Dim thuộc view cha và hiện ở mọi dependent có annotation crop chứa nó. | Trước khi dời, ghi lại view anh em nào đang hiện dim đó (`grid_dims_band` báo "also shown in"). Sau đó chạy lại view đó. Dim thừa đang được view khác dùng → chỉ ẩn ở view này, không xoá. |
| Đọc `d.Id` sau `RollBack` bị lỗi | Phần tử không còn tồn tại. | Lấy id trước khi rollback. |
| `grid_bubble_elbow` báo "leader not valid" | Hình học bubble hoặc leader không hợp lệ ở tỉ lệ đó. | So le đầu trục 2D thay cho elbow. |
| Đổi hiển thị link trong template | Revit 2023 không có `SetLinkOverrides`. | Điều khiển UI: View Templates › V/G › Revit Links › Display Settings (bấm lần 1 để chọn, lần 2 để mở). Sau mỗi lần OK, Revit regenerate rất chậm. |
| Room tag báo chồng lắp khắp nơi | Bounding box của room tag phủ cả khung label của family, rộng hơn chữ nhiều (~29 mm giấy ở 1:200). | Không dò room tag bằng bounding box (`annotation_overlaps` mặc định bỏ qua); soát room tag bằng ảnh. |
| Phân loại vế thang sai (vế dưới nét cắt bị coi là trên) | `StairsRun.BaseElevation` / `TopElevation` và `StairsLanding.BaseElevation` tính **từ chân thang**, không phải cao độ tuyệt đối. | Cao độ vế = `ProjectElevation` của Base Level + Base Offset của thang + `run.BaseElevation`; so với mặt cắt `GetViewRange()` (CutPlane level + offset). |
| Đếm bậc trên mặt bằng ra một nửa | Vế cắt (V3) và vế sau nét cắt (V1) chỉ hiện một phần; nét cắt che bậc. | Lấy `ActualTreadsNumber` của từng `StairsRun`, độ sâu `Stairs.ActualTreadDepth`. Không đếm nét. |
| `NumberSystem.Create(..., option, LinkElementId)` báo "placementLevelId is not one of the stairs base levels" | Tham số `LinkElementId` cuối là **level đặt số của thang nhiều tầng**, không phải type (2026-10-07, chạy thật). | `NumberSystem.Create(doc, viewId, new LinkElementId(runId), run.GetNumberSystemReference(option))`, rồi `ChangeTypeId(type)`. Vế đã đánh số: `NumberSystem.NumberedElementId`. |
| Số bậc mới tạo hiện mọi số, nằm trên nét biên vế | Mặc định Display Rule = All, Reference = Left/Right (mép vế). | Chép thiết lập từ số bậc đang dùng trên bản vẽ của dự án (`stair_numbers_match`): Display Rule, Number Size, Reference (Left/Right Quarter). |
| `StairsPath.ShowUpText` báo "type of this stairs path is not automatic up/down direction" | Path family **Fixed Up Direction** không có chữ UP/DOWN theo instance; property chỉ dùng cho Automatic. | Tạo thẳng bằng type Fixed Up, không set `ShowUpText/ShowDownText`. Chữ "UP" nếu có là của type (đổi type ảnh hưởng cả dự án → hỏi user). |
| Dim tạo xong không hiện (bounding box `null`), đoạn dim mất | Tham chiếu lấy từ nét thuộc subcategory **bị ẩn** trong view (vd. template ẩn `Railings > <Above> Top Rails`): Revit tạo dim nhưng không vẽ. Mặt 3D của vế/chiếu nghỉ (`StairsRun`/`StairsLanding`) cũng hay bị ẩn (2026-10-07). | `dims_at_positions` bỏ nét có GraphicsStyle bị ẩn. Vế / chiếu nghỉ: tham chiếu nét plan của phần tử **Stairs** (không phải Run/Landing) — `{mm, id:<stairsId>, src:"view"}`. Tay vịn: dùng lại tham chiếu của một dim user vẽ tay (`refDims`). Tường: mặt 3D (`src:"3d"`). Luôn kiểm tra `view_elem_boxes` (Box ≠ null) + ảnh. |
| Dim tham chiếu mặt tường theo view tự đổi giá trị (3140 → 2590) sau vài lần sửa | Mặt tường lấy từ hình học `Options{View=v}` không ổn định (tường có cửa). | Tham chiếu tường bằng mặt 3D (`src:"3d"` / `use3D`). |
| Cao độ (spot) tạo xong không hiện | Tham chiếu mặt 3D của chiếu nghỉ. | Đặt trên mặt plan của phần tử **Stairs** (`annot_place` tự lấy mặt view chứa điểm), giống bản vẽ có sẵn. |
| Chữ dim thành `=  4200`, `1200  CLEAR` (2 dấu cách) | Revit tự chèn 1 dấu cách giữa Prefix / Suffix và số. | Prefix `280mm x 16T =` (không cách cuối), Suffix `CLEAR` (không cách đầu). |
| `ui_view_focus open` báo `Switched:false` nhưng user vẫn thấy sheet | Viewport của view đang **Activate View** trên sheet: `ActiveView` trả về view, mọi yêu cầu chuyển view rơi vào sheet. | Bỏ kích hoạt viewport (double-click ra ngoài viewport) rồi mở lại. Kiểm tra ảnh bằng xuất **view** (`viewIds`), không xuất sheet. |
| Vế/chiếu nghỉ không cho tham chiếu từ hình học theo view | Hình học `Options{View=v}` của `StairsRun`/`StairsLanding` là nét 2D không có `Reference`. | Lấy mặt 3D (`Options{ComputeReferences=true}` không có View): mặt cổ bậc vuông góc hướng đi (`dims_at_positions` tự fallback). |
| Ảnh vùng view (`view_region_image`) trắng trơn, view của user bị zoom ra chỗ trống | `x,y` của tool là **toạ độ model**; khung toạ độ của `stair_plan_audit` / `view_crop_info` là khung view (Right/Up từ `View.Origin`), có thể lệch rất xa. | Dùng ảnh sheet (`export_sheet_images` + `crop.ps1`) hoặc đổi toạ độ trước. |
| Sau `export_sheet_images` (sheet), user thấy sheet trống thay vì view đang làm | Xuất ảnh sheet đưa sheet lên trước. | Khi đang làm: xuất **view** (`sheetNumbers:[], viewIds:[id]`) — view vẫn ở trước. Xuất sheet chỉ khi cần cả sheet, rồi `ui_view_focus open` lại. |
| `ui_view_focus panes hide` báo "was hidden" nhưng Properties / Project Browser vẫn hiện | Tool dò trạng thái pane sai (2026-10-07). | Kiểm tra bằng ảnh màn hình; đóng pane bằng nút X nếu cần. |
| Chữ dim tiếng Việt trong script PowerShell bị lỗi | PowerShell 5.1 đọc file không có BOM. | Lưu `.ps1` dạng UTF-8 with BOM. |

## Mẹo (Tips)

- Lệnh có `mode: preview` → chạy trong transaction rồi rollback, trả về giá trị dự kiến. Đọc kỹ trước khi apply.
- Log `apply` cần giữ lại các id đã tạo hoặc xoá, và stable reference của dim bị xoá, để `dim_restore` và `undo` dùng được.
- Kiểm tra bằng ảnh: script `crop.ps1` (System.Drawing) cắt vùng theo tỉ lệ ảnh: `-Cx -Cy -W -H` (0–1).
