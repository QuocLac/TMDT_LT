# Áp dụng gói đơn giản hóa Inventory

## Commit nền

`29f8aa4cba0826e0ca90d86cb27d3d8d8f740d11`

## Thứ tự áp dụng

1. Giải nén gói tại thư mục chứa `TMDT_LT.csproj`.
2. Chọn ghi đè toàn bộ file trùng tên.
3. Chạy:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\inventory\apply-inventory-ui-cleanup.ps1
```

4. Xóa thư mục `bin` và `obj`.
5. Clean Solution và Rebuild Solution.
6. Kiểm tra các trang:
   - `/Admin/Inventory`
   - `/Admin/Inventory/Receiving`
   - `/Admin/Inventory/Operations?tab=outbound`
   - `/Admin/Inventory/Operations?tab=transfer`
   - `/Admin/Inventory/Operations?tab=counting`

## File giao diện cũ được xóa

- `Areas/Admin/Views/Inventory/Distribution.cshtml`
- `wwwroot/css/admin/inventory-dashboard.css`
- `wwwroot/css/admin/inventory-receiving.css`
- `wwwroot/css/admin/inventory-operations.css`
- `wwwroot/css/admin/inventory-distribution.css`
- `wwwroot/js/admin/inventory-distribution.js`

Controller phân phối không bị xóa vì các endpoint tính và xác nhận xuất kho vẫn được màn hình Hoạt động kho sử dụng. Route giao diện cũ chỉ chuyển người dùng về tab Xuất kho mới.

## Database

Không thay đổi schema và không cần migration.

## Commit đề xuất

`refactor(inventory): simplify receiving and consolidate warehouse operations`
