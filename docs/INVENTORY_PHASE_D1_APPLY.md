# Inventory Phase D1 — Receiving Workspace

## Phạm vi

- Sửa dứt điểm luồng tải danh sách sản phẩm ở trang thêm phiếu nhập bằng endpoint riêng `/Admin/Inventory/PhaseD/Products`.
- Phân trang, tìm kiếm và lọc server-side theo kho, danh mục, thương hiệu, trạng thái tồn.
- Giao diện nhận hàng mới, tách khỏi form tạo nhanh sản phẩm.
- Preview landed cost trước khi ghi nhận.
- Kiểm tra trùng số hóa đơn/chứng từ theo nhà cung cấp.
- Anti-forgery cho preview và submit.
- Submit trong transaction `Serializable`.
- Đồng bộ PO, PO detail, lot, stock unit, transaction và `ProductVariants.Stock`.

## Cách áp dụng

1. Sao lưu project hoặc tạo branch mới.
2. Giải nén ZIP vào thư mục gốc project `TMDT_LT`.
3. Chọn **Replace/Overwrite all**.
4. Xóa thư mục `bin` và `obj`.
5. Chạy `dotnet build` hoặc Build Solution trong Visual Studio.
6. Mở `/Admin/Inventory/CreatePO` và kiểm thử theo checklist bên dưới.

## Migration

**Không có cập nhật schema DB trong Phase D1, không tạo migration.**

Phase D1 sử dụng các cột/tables đã được thêm từ migration Phase B `InventoryWarehouseOrderFifo`. Nếu máy chưa cập nhật migration cũ, chạy:

```powershell
Update-Database
```

Không tạo migration mới cho gói này.

## Commit đề xuất

```text
feat(inventory): rebuild supplier receiving workspace with paged product loading
```

## Checklist kiểm thử

1. Trang CreatePO tải được warehouse, supplier và sản phẩm.
2. Đổi warehouse làm tải lại tồn theo kho.
3. Search bằng tên và mã VariantId/SKU.
4. Phân trang không làm mất các dòng đã chọn.
5. Không preview khi thiếu chứng từ, nhà cung cấp, kho hoặc dòng hàng.
6. Preview hiển thị tiền hàng, VAT, phí nhập, phải trả NCC và vốn hóa tồn kho.
7. Cùng nhà cung cấp + cùng số chứng từ bị chặn.
8. Submit thành công tạo PO, lot, stock units và inventory transaction.
9. `ProductVariants.Stock` bằng tổng `InventoryLots.RemainingQuantity` của tất cả kho.
10. Tải lại trang không phát sinh submit trùng.

## Giới hạn có chủ đích

Schema hiện tại chỉ hỗ trợ nhận hàng trực tiếp và trạng thái PO dạng chuỗi. Quy trình chuẩn hơn gồm `Draft → Ordered → Partially Received → Received/Closed`, discrepancy/damaged quantity và chứng từ ASN cần một phase thay đổi DB riêng. Không ghép thay đổi đó vào D1 để tránh migration lớn trong bản sửa lỗi tải sản phẩm.
