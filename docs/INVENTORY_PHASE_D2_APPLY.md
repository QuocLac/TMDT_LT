# Inventory Phase D2 — Warehouse Control & Inventory Count Journal

## Mục tiêu phase

Phase D2 thay giao diện tồn kho cũ bằng trung tâm kiểm soát theo từng kho và thay nghiệp vụ sửa tồn trực tiếp bằng quy trình kiểm kê có audit:

1. Chọn kho và SKU cần kiểm kê.
2. Tạo phiên, chụp snapshot On hand và giá vốn tại thời điểm tạo.
3. Nhập số thực đếm theo từng SKU.
4. Bắt buộc chọn lý do nếu có chênh lệch.
5. Gửi phiên sang trạng thái chờ duyệt.
6. Duyệt và ghi sổ trong transaction Serializable.
7. Tạo lịch sử trước/sau, giá trị chênh lệch, kho, lô và tham chiếu phiên.

## File ghi đè / file mới

- `Areas/Admin/Controllers/InventoryControlPhaseDController.cs` — mới
- `Areas/Admin/Views/Inventory/Index.cshtml` — ghi đè
- `Models/InventoryCountSessions.cs` — mới
- `Models/InventoryCountLines.cs` — mới
- `Models/InventoryLots.cs` — ghi đè
- `Models/InventoryTransactions.InventoryControlPhaseD2.cs` — mới, partial extension
- `wwwroot/css/admin/inventory-control-phase-d2.css` — mới
- `wwwroot/js/admin/inventory-control-phase-d2.js` — mới

## Thay đổi schema database

Phase D2 **có thay đổi database** nhưng ZIP **không chứa file migration**, đúng quy tắc dự án.

Migration dự kiến phải gồm:

### Bảng mới

- `InventoryCountSessions`
- `InventoryCountLines`

### `InventoryLots`

- Cho phép `POId` nullable để lô kiểm kê không bị gắn giả vào phiếu nhập.
- Cho phép `SupplierId` nullable để lô kiểm kê không bị gắn giả vào nhà cung cấp.
- Thêm `SourceType`.
- Thêm `SourceReference`.
- Thêm `InventoryCountLineId` và khóa ngoại tới dòng kiểm kê.

### `InventoryTransactions`

- Thêm `ReferenceType`.
- Thêm `QuantityBefore`.
- Thêm `QuantityAfter`.
- Thêm `ReasonCode`.
- Thêm `ValueImpact`.

Các trường kho, lô, `UnitCostSnapshot` và `TotalCostSnapshot` đã thuộc Phase B nên D2 không khai báo lại.

## Tạo migration thủ công

Sau khi giải nén và ghi đè file, mở Package Manager Console tại project web:

```powershell
Add-Migration InventoryCountJournalPhaseD2
```

Trước khi cập nhật DB, kiểm tra file migration được sinh ra:

- Không được xóa dữ liệu bảng kho/lô/giao dịch hiện có.
- `InventoryLots.POId` và `InventoryLots.SupplierId` phải chuyển sang nullable.
- Hai bảng kiểm kê phải có khóa ngoại và index.
- Các cột mới của `InventoryTransactions` phải nullable để dữ liệu lịch sử cũ vẫn hợp lệ.

Sau khi kiểm tra:

```powershell
Update-Database
```

## Kiểm tra sau khi áp dụng

1. Clean Solution, xóa `bin` và `obj` nếu Visual Studio còn cache model cũ.
2. Build solution.
3. Mở `/Admin/Inventory`.
4. Chọn từng kho và kiểm tra các chỉ số On hand, Reserved, Available, giá trị tồn.
5. Chọn một số SKU, tạo phiên kiểm kê chu kỳ.
6. Thử dòng không chênh lệch và dòng có chênh lệch.
7. Xác nhận dòng chênh lệch không thể lưu nếu thiếu reason code.
8. Gửi duyệt và post phiên.
9. Kiểm tra `InventoryLots`, `ProductSerials`, `InventoryTransactions` và `ProductVariants.Stock`.
10. Tạo một phiên khác, sau đó phát sinh nhập/xuất làm thay đổi tồn trước khi post; hệ thống phải chặn phiên snapshot cũ.

## Quy tắc dữ liệu được áp dụng

- Không chỉnh trực tiếp `ProductVariants.Stock` từ giao diện kiểm kê.
- Không sửa giá vốn lịch sử của lô nhập để khớp số kiểm đếm.
- Chênh lệch tăng tạo lô `CountAdjustment`, không giả `POId`/`SupplierId`.
- Chênh lệch giảm trừ FIFO trong đúng kho.
- Serial phải khớp số lượng điều chỉnh; nếu lệch, post bị chặn để đối soát.
- `ProductVariants.Stock` chỉ được đồng bộ lại từ tổng lô sau khi post thành công.
- Nếu tồn hiện tại khác snapshot, post bị chặn thay vì ghi đè giao dịch mới.
- Reservation hiện tại chưa có `WarehouseId`; trong giai đoạn chuyển tiếp chỉ quy reservation đang hoạt động về kho chính để tránh trừ lặp ở nhiều kho.

## Commit đề xuất

```text
feat(inventory): add auditable warehouse cycle counts and stock ledger
```

## Phase tiếp theo đề xuất

D3 nên xử lý phân bổ reservation theo kho, điều chuyển kho có trạng thái, ngưỡng bổ sung hàng theo SKU/kho và cảnh báo tồn chậm theo cấu hình thay vì ngưỡng cố định.
