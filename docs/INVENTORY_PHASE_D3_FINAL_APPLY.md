# Inventory Phase D3 — Complete Warehouse Operations

## Baseline đã rà soát

Bộ bàn giao này được xây trên nhánh `stabilize-lac12`, commit:

- `2591b81b99fb8647d1d8facd3ad89a287223b500`
- `fix(inventory): define count journal keys and adjustment relation`

Commit baseline đã có Phase D1 nhận hàng, Phase D2 kiểm kê và migration `InventoryCountJournalPhaseD2`.

## Phạm vi hoàn thiện

### 1. Nhận hàng nhà cung cấp — Phase D1

- Danh sách SKU phân trang tại server.
- Lọc theo kho, danh mục, thương hiệu và trạng thái tồn.
- Preview tiền hàng, VAT đầu vào, chi phí nhập phân bổ và landed cost.
- Chặn trùng chứng từ theo nhà cung cấp.
- Transaction Serializable.
- Ghi PO, chi tiết PO, lô, serial, sổ kho và tồn tổng.

### 2. Kiểm soát tồn và kiểm kê — Phase D2

- Dashboard theo kho: On hand, Reserved, Available, giá trị tồn và tuổi tồn.
- Phiên kiểm kê có snapshot, dòng đếm, reason code, gửi duyệt và post.
- Chặn post nếu tồn đã thay đổi sau snapshot.
- Chênh lệch tăng tạo lô CountAdjustment.
- Chênh lệch giảm theo FIFO đúng kho.
- Ghi số dư trước/sau và giá trị chênh lệch vào sổ kho.

### 3. Vận hành kho — Phase D3

- Trung tâm `/Admin/Inventory/Operations`.
- Đề xuất bổ sung hàng theo nhu cầu lịch sử, lead time và safety days.
- Điều chuyển nhiều SKU trong một phiếu.
- Kiểm tra tồn khả dụng sau reservation trước khi điều chuyển.
- Chặn điều chuyển SKU đang nằm trong phiên kiểm kê mở.
- Trừ lô FIFO ở kho nguồn.
- Tạo lô ở kho đích nhưng giữ nguyên ngày nhập gốc để không làm mới tuổi FIFO.
- Di chuyển serial sang lô đích.
- Ghi hai đầu OUT/IN dùng chung reference ID.
- Không thay đổi tổng tồn toàn hệ thống.

### 4. Xuất kho phân phối

- Giao diện mới `/Admin/Inventory/CreateSO`.
- Danh sách sản phẩm phân trang theo kho.
- Hiển thị On hand, Reserved và Available.
- Preview FIFO COGS, VAT, doanh thu, vận chuyển, lợi nhuận và biên lợi nhuận.
- Chặn xuất nếu có phiên kiểm kê mở hoặc không đủ Available.
- POST dùng anti-forgery token.
- Tận dụng engine FIFO/tài chính Phase B đã được đối soát.

### 5. Health check và đối soát

- Lệch `ProductVariants.Stock` với tổng lô hoạt động.
- Lệch serial InStock với RemainingQuantity từng lô.
- Lô âm, vượt số lượng nhập, thiếu kho hoặc giá vốn âm.
- Lô tồn lâu hơn 90 ngày.
- Phiên kiểm kê đang mở và reservation đang hoạt động.
- Cho phép sửa duy nhất snapshot tồn tổng từ nguồn sự thật là sổ lô.
- Không tự tạo/xóa serial khi health check phát hiện lệch.

### 6. Khóa mutation legacy

Các route ghi tồn cũ sau đây trả HTTP 410 và hướng người dùng sang quy trình mới:

- `AdjustStock`
- `UpdateLotDetail`
- `DeleteLotPhysical`
- `RestoreLot`
- `TransferLot`
- `SubmitPO`
- `QuickAddProduct`
- `EstimateSO`
- `SubmitSO`

Các API đọc/report cũ vẫn được giữ để giảm rủi ro làm hỏng phần báo cáo liên quan.

## Database và migration

ZIP **không chứa file migration**, đúng quy tắc dự án.

Commit baseline đã có migration:

```text
20260718085504_InventoryCountJournalPhaseD2
```

Trong quá trình rà migration baseline, phát hiện EF đã sinh quan hệ shadow dư:

```text
InventoryTransactions.InventoryLotsLotId
```

Nguyên nhân là `InventoryLots` từng khai báo collection `InventoryTransactions` trong khi Phase B đã cấu hình quan hệ thật bằng `InventoryTransactions.LotId`. Bản D3 đã bỏ navigation dư, vì vậy cần tạo migration cleanup thủ công:

```powershell
Add-Migration InventoryCountJournalShadowRelationCleanup
```

Migration cleanup dự kiến chỉ phải:

- Drop FK `FK_InventoryTransactions_InventoryLots_InventoryLotsLotId`.
- Drop index `IX_InventoryTransactions_InventoryLotsLotId`.
- Drop column `InventoryLotsLotId` khỏi `InventoryTransactions`.
- Có thể điều chỉnh snapshot quan hệ nhưng không được xóa `LotId`, `WarehouseId`, các bảng kiểm kê hoặc dữ liệu tồn.

Sau khi kiểm tra migration:

```powershell
Update-Database
```

Quy trình này dùng được trong cả hai trường hợp:

- D2 đã được áp dụng: cleanup sẽ xóa quan hệ shadow dư.
- D2 chưa được áp dụng: `Update-Database` sẽ chạy D2 rồi chạy cleanup ngay sau đó.

## Cách áp dụng

1. Sao lưu project và database đang dùng.
2. Giải nén ZIP vào thư mục gốc project `TMDT_LT`.
3. Chọn Replace/Overwrite all.
4. Xóa `bin` và `obj`.
5. Clean Solution và Rebuild Solution.
6. Chạy `Add-Migration InventoryCountJournalShadowRelationCleanup`.
7. Kiểm tra migration chỉ dọn quan hệ shadow như mô tả ở trên.
8. Chạy `Update-Database`.
9. Đăng nhập bằng tài khoản Admin.

## Smoke test bắt buộc

### Nhận hàng

1. Mở `/Admin/Inventory/CreatePO`.
2. Chọn kho và nhà cung cấp.
3. Tìm SKU, thêm dòng, preview và ghi nhận.
4. Kiểm tra PO, InventoryLots, ProductSerials, InventoryTransactions.

### Kiểm kê

1. Mở `/Admin/Inventory`.
2. Tạo phiên kiểm kê cho một số SKU.
3. Nhập số đếm, reason code và gửi duyệt.
4. Post phiên và kiểm tra sổ kho.

### Điều chuyển

1. Mở `/Admin/Inventory/Operations`.
2. Chọn kho nguồn/đích, thêm nhiều SKU.
3. Preview và ghi sổ.
4. Kiểm tra số lượng hai kho, serial, lô đích và cặp OUT/IN.

### Xuất phân phối

1. Mở `/Admin/Inventory/CreateSO`.
2. Chọn kho và cửa hàng/đại lý.
3. Thêm SKU, preview tài chính và ghi sổ.
4. Kiểm tra SalesOrders, SalesOrderDetails, lô, serial và sổ kho.

### Health

1. Vào tab Đối soát hệ thống.
2. Quét health.
3. Nếu chỉ lệch snapshot tổng, nhập lý do và đồng bộ.
4. Nếu lệch serial, xử lý bằng kiểm kê vật lý; không sửa trực tiếp DB.

## Commit đề xuất theo phase

### Phase D3.0

```text
fix(inventory): remove duplicate lot transaction navigation and shadow foreign key
```

### Phase D3.1

```text
feat(inventory): add demand-based replenishment and multi-sku warehouse transfers
```

### Phase D3.2

```text
feat(inventory): rebuild fifo distribution workspace with reservation protection
```

### Phase D3.3

```text
fix(inventory): disable legacy stock mutations and add reconciliation health checks
```

### Squash commit nếu muốn gom một commit

```text
feat(inventory): complete warehouse operations and secure stock lifecycle
```

## Giới hạn kiến trúc còn được giữ tương thích

`OrderReservations` hiện chưa có `WarehouseId`. Vì vậy reservation hoạt động được bảo vệ tại kho chính, đúng với cơ chế fulfillment hiện tại và tránh trừ lặp ở nhiều kho. Việc phân bổ reservation thật sự theo từng kho cần một phase riêng đồng thời sửa checkout, fulfillment routing và schema; D3 không tự thay đổi phần đó để tránh làm hỏng vòng đời đơn hàng hiện có.
