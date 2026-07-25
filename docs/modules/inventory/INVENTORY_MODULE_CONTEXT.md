# Ngữ cảnh triển khai module Inventory

> **Nguồn thông tin bắt buộc phải đọc trước mọi thay đổi liên quan đến tồn kho**
>
> Repository: `QuocLac/TMDT_LT`  
> Nhánh làm việc: `stabilize-lac12`  
> Commit đã rà trước lượt đơn giản hóa: `4cf52a51891487a4703931cf9763f97e582f4d87`  
> Cập nhật gần nhất: `2026-07-26`

## 1. Mục đích của tài liệu

Tài liệu này lưu trạng thái triển khai, quy tắc nghiệp vụ, ranh giới kiến trúc,
quy tắc đặt tên, tình trạng migration và yêu cầu bàn giao của module Inventory.
Mọi chat mới hoặc agent mới phải đọc file này trước khi phân tích hay sửa code.

Không dùng lịch sử hội thoại làm nguồn duy nhất để tiếp tục công việc.

## 2. Phạm vi nghiệp vụ

Module Inventory vẫn chịu trách nhiệm bảo toàn tồn kho, lô, serial và giá vốn,
nhưng giao diện quản trị được thu gọn còn ba điểm vào ổn định:

- `Tổng quan`: lượng hàng, giá trị tồn, tiền nhập, tiền thực thu và thống kê sản phẩm;
- `Nhập kho`: nhận hàng nhà cung cấp và thêm nhanh sản phẩm/biến thể mới;
- `Hoạt động kho`: xuất kho, chuyển kho và kiểm kê.

FIFO, lô, serial, lịch sử và kiểm tra chênh lệch vẫn là quy tắc nền. Không đưa
các thuật ngữ triển khai này lên giao diện nếu người dùng không cần quyết định dựa
trên chúng.

## 3. Quy tắc nghiệp vụ không được phá vỡ

1. Tồn vật lý được suy ra từ các lô đang hoạt động và chưa bị xóa.
2. `ProductVariants.Stock` chỉ là snapshot tổng hợp, không phải sổ tồn gốc.
3. Mọi thay đổi tồn phải tạo `InventoryTransactions`.
4. Xuất tồn theo FIFO: `ReceivedDate`, sau đó `LotId`.
5. Số serial trạng thái `InStock` phải khớp `RemainingQuantity` của lô.
6. Điều chuyển kho phải giữ nguyên tuổi hàng và giá vốn gốc.
7. Kiểm kê theo quy trình: snapshot → kiểm đếm → gửi duyệt → ghi sổ.
8. Phiên kiểm kê mở chặn điều chuyển/phân phối cùng SKU tại cùng kho.
9. Tồn đã reservation không được xem là tồn khả dụng để xuất hoặc chuyển.
10. VAT đầu vào, landed cost, COGS và VAT đầu ra phải tách biệt.
11. Không sửa trực tiếp số lượng lô, kho, nhà cung cấp, PO tham chiếu hoặc giá vốn lịch sử.
12. Controller không được khởi tạo hoặc gọi controller khác.
13. Business logic dùng chung phải đi qua service được đăng ký bằng DI.

## 4. Cấu trúc production sau khi làm sạch

```text
AGENTS.md

docs/modules/inventory/
└── INVENTORY_MODULE_CONTEXT.md

Areas/Admin/Controllers/
├── InventoryController.cs
├── InventoryReceivingController.cs
├── InventoryCountingController.cs
├── InventoryOperationsController.cs
├── InventoryTransferController.cs
├── InventoryDistributionController.cs
└── InventoryReconciliationController.cs

Areas/Admin/Views/Inventory/
├── Dashboard.cshtml
├── Receiving.cshtml
├── Operations.cshtml
└── Distribution.cshtml

Services/Inventory/
├── InventoryServiceCollectionExtensions.cs
├── InventoryDashboardService.cs
├── InventoryDistributionService.cs
└── Contracts/
    ├── InventoryDashboardResponse.cs
    ├── InventoryReceivingRequests.cs
    ├── InventoryCountRequests.cs
    ├── InventoryOperationRequests.cs
    └── InventoryDistributionRequests.cs

Models/Inventory/
├── InventoryCountRules.cs
├── InventoryCountSession.cs
├── InventoryCountLine.cs
├── InventoryEntityExtensions.cs
└── InventoryTransactionAuditFields.cs

Data/
└── ApplicationDbContext.Inventory.cs

wwwroot/js/admin/
├── inventory-dashboard.js
├── inventory-receiving.js
├── inventory-operations.js
└── inventory-distribution.js

wwwroot/css/admin/
├── inventory-dashboard.css
├── inventory-receiving.css
├── inventory-operations.css
└── inventory-distribution.css
```

Tên production phải mô tả năng lực nghiệp vụ ổn định. Không tạo class, route,
asset hoặc tài liệu đang hoạt động với tên `PhaseB`, `PhaseC`, `PhaseD`, `D1`,
`D2`, `D3`, `Fix`, `Final`, `New` hoặc `Legacy`.

Tên migration lịch sử và lịch sử commit không được đổi sau khi đã áp dụng.

## 5. Ranh giới trách nhiệm

- `InventoryController`: endpoint trang tổng quan và danh sách tồn theo sản phẩm.
- `InventoryReceivingController`: HTTP endpoints cho nhận hàng nhà cung cấp.
- `InventoryCountingController`: HTTP endpoints cho count journal.
- `InventoryOperationsController`: workspace, product query và replenishment query.
- `InventoryTransferController`: preview và ghi nhận điều chuyển kho.
- `InventoryDistributionController`: HTTP endpoints cho phân phối.
- `InventoryReconciliationController`: health check và sửa snapshot tổng hợp có kiểm soát.
- `InventoryDashboardService`: tổng hợp chỉ số kho, tài chính và thống kê sản phẩm cho Dashboard.
- `InventoryServiceCollectionExtensions`: điểm đăng ký DI duy nhất của module.
- `InventoryDistributionService`: availability validation, FIFO planning, COGS,
  serial dispatch và ghi sổ phiếu phân phối.
- `Services/Inventory/Contracts`: request contracts dùng chung giữa controller và service.
- `ApplicationDbContext.Inventory.cs`: toàn bộ mapping EF bổ sung của module.

Không đưa request DTO vào trong controller. Không để service phụ thuộc namespace MVC Area.

## 6. Route chính thức

| Năng lực | Route |
|---|---|
| Dashboard | `/Admin/Inventory` |
| Dashboard API | `/Admin/Inventory/Bootstrap`, `/Stock`, `/Transactions` |
| Nhận hàng | `/Admin/Inventory/Receiving` |
| Alias tương thích nhận hàng | `/Admin/Inventory/CreatePO` |
| Count journal | `/Admin/Inventory/Counts` |
| Vận hành và replenishment | `/Admin/Inventory/Operations` |
| Điều chuyển | `/Admin/Inventory/Transfers` |
| Phân phối | `/Admin/Inventory/Distribution` |
| Alias tương thích phân phối | `/Admin/Inventory/CreateSO` |
| Đối soát | `/Admin/Inventory/Reconciliation` |
| Từ chối truy cập | `/Home/AccessDenied` |

Alias chỉ dùng cho điểm vào giao diện cũ. Không duy trì endpoint mutation cũ.

## 7. Trạng thái database và migration

Lịch sử schema hiện có gồm:

- kho vật lý, FIFO và order allocation;
- count journal;
- cleanup quan hệ lô bị sinh shadow foreign key.

Quy tắc:

- Không sửa hoặc đổi tên migration đã áp dụng chỉ để làm đẹp tên.
- Không tự tạo migration khi bàn giao qua chat.
- Khi cần đổi schema, chỉ sửa model/configuration, mô tả migration dự kiến và để
  người dùng tự chạy `Add-Migration` sau khi review.
- Refactor tên file, class, route và service không được tạo schema migration.
- Nếu EF đề xuất drop/recreate bảng kiểm kê sau khi đổi tên class, phải dừng và
  kiểm tra mapping trước khi cập nhật database.

Class C# dùng tên số ít `InventoryCountSession` và `InventoryCountLine`, nhưng
vẫn map vào bảng hiện hữu `InventoryCountSessions` và `InventoryCountLines`.

**Lần làm sạch cấu trúc này không yêu cầu migration mới.**

## 8. Quy trình bắt buộc trước khi code

1. Đọc file này và `AGENTS.md`.
2. Xác định commit mới nhất của nhánh mục tiêu.
3. So sánh với commit nền ghi trong file này.
4. Rà controller, service, model, view, script, EF mapping, migration và module liên quan.
5. Tham khảo nghiệp vụ sàn thương mại điện tử/kho hiện hành và tài liệu kỹ thuật chính thức.
6. Viết impact plan ngắn trước khi sửa.
7. Tái sử dụng service; tuyệt đối không gọi controller-to-controller.
8. Giữ transaction, concurrency, audit, FIFO và serial consistency.
9. Chạy kiểm tra có thể thực hiện và ghi rõ phần chưa xác minh.
10. Cập nhật file này trước khi bàn giao.

## 9. Quy tắc tổ chức và đặt tên

- Class entity/request dùng tên số ít.
- Controller chỉ đại diện một năng lực nghiệp vụ rõ ràng.
- JavaScript/CSS đặt theo màn hình hoặc capability.
- Shared business behavior nằm trong service.
- Mapping database nằm trong `ApplicationDbContext.Inventory.cs`.
- Không dùng route priority để che nhiều implementation cùng một endpoint.
- Không giữ controller/service cũ chỉ để “phòng khi cần”; lịch sử đã có trong Git.
- Migration là lịch sử schema, không phải nơi phản ánh tên kiến trúc production hiện tại.

## 10. Quy tắc bàn giao qua chat mới

Mỗi lần bàn giao phải có:

1. Commit nền đã rà.
2. Danh sách file thêm, sửa và xóa.
3. ZIP giải nén tại project root.
4. Script cleanup xác định nếu cần xóa/đổi file cũ.
5. Tên commit đề xuất.
6. Trạng thái migration rõ ràng.
7. Kiểm tra thực sự đã chạy và phần chưa thể xác minh.
8. File này đã cập nhật.

Không tuyên bố build hoặc runtime thành công khi chưa chạy thực tế.

## 11. Checklist xác minh

- Solution build thành công.
- `/Admin/Inventory` mở được bằng tài khoản role `Admin`.
- Access denied trả HTTP 403, không phải 404.
- Product search, preview và submit nhận hàng hoạt động.
- Count session create/save/submit/post/cancel hoạt động.
- Transfer preview/submit giữ nguyên tổng tồn toàn hệ thống.
- Distribution preview/submit dùng FIFO và cập nhật serial.
- Reconciliation phát hiện lệch lot/serial/snapshot.
- Source/asset production không còn tên theo phase.
- Không còn controller tồn kho cũ được compile.
- Refactor không phát sinh migration ngoài dự kiến.

## 12. Hạn chế hiện tại

`OrderReservations` chưa có `WarehouseId`. Trong thời gian chuyển tiếp, reservation
đang hoạt động chỉ được quy về kho chính để tránh trừ lặp ở nhiều kho. Việc bổ
sung warehouse-aware reservation phải là một thay đổi schema có chủ đích và cần
cập nhật checkout, fulfillment, expiration và toàn bộ inventory query liên quan.

## 13. Trạng thái bàn giao hiện tại

- Đã đổi production naming sang domain-first.
- Đã loại controller-to-controller trong luồng phân phối.
- Đã tách request contracts khỏi controller và MVC Area.
- Đã chuẩn hóa view, asset và route theo capability.
- Đã có script xóa implementation cũ có kiểm soát.
- Không sửa/xóa migration lịch sử.
- Cần chạy build và smoke test trên máy có .NET SDK sau khi áp dụng ZIP.

## 14. Hướng đơn giản hóa giao diện đang triển khai

### Ngôn ngữ giao diện

- Chỉ dùng từ ngữ vận hành thông thường bằng tiếng Việt.
- Không hiển thị các từ `preview`, `transaction`, `snapshot`, `reservation`,
  `ledger`, `health check`, `reconciliation`, `landed cost`, `capitalized cost`
  hoặc `COGS` cho người dùng cuối.
- FIFO vẫn bắt buộc trong service nhưng giao diện chỉ mô tả: “Hệ thống tự xuất
  từ lô nhập trước”.
- Không dùng tiêu đề tiếng Anh, biểu tượng trang trí hoặc mô tả cách code hoạt động.

### Phong cách giao diện

- Dùng chung font, màu, card, bảng, form và nút của Admin hiện tại.
- Màu chính `#111827`, nền `#F3F4F6`, card trắng, viền `#E5E7EB`.
- Điều hướng Inventory chỉ còn `Tổng quan`, `Nhập kho`, `Hoạt động kho`.
- Không tạo hệ component riêng cho từng màn hình.

### Trạng thái triển khai hai lượt

**Lượt 1/2 — đã bàn giao trong gói hiện tại**

- Thêm partial điều hướng dùng chung `_InventoryNavigation.cshtml`.
- Thêm stylesheet dùng chung `inventory.css`.
- Làm lại Dashboard theo phong cách Admin.
- Bổ sung bộ lọc kho và thời gian.
- Bổ sung tiền nhập, tiền thực thu, giá vốn đã xuất, lợi nhuận tạm tính,
  công nợ chưa thu, hàng sắp hết và hàng tồn lâu.
- Bổ sung biểu đồ nhập/xuất, giá trị tồn theo kho, sản phẩm nổi bật và hoạt động gần đây.
- Thu gọn bảng tồn về các cột phục vụ quyết định hằng ngày.

**Lượt 2/2 — chưa triển khai trong gói này**

- Làm lại Nhập kho và thêm nhanh sản phẩm/biến thể.
- Gộp Xuất kho, Chuyển kho và Kiểm kê vào Hoạt động kho.
- Xóa giao diện Operations/Distribution cũ sau khi chức năng đã chuyển đủ.
- Áp dụng stylesheet và partial điều hướng chung cho hai màn hình còn lại.

### Cách hiểu số liệu tài chính trên Dashboard

- `Tiền nhập trong kỳ`: tổng phải trả của phiếu nhập thuộc kho và khoảng ngày đã chọn.
- `Tiền thực thu`: thanh toán thành công trừ tiền đã hoàn của đơn bán lẻ toàn hệ thống.
- `Giá vốn đã xuất`: giá vốn đơn bán lẻ cộng giá vốn phiếu phân phối tại kho đã chọn.
- `Lợi nhuận tạm tính`: lợi nhuận tiền thu của đơn bán lẻ cộng lợi nhuận phân phối;
  chưa phải lợi nhuận ròng nếu hệ thống chưa có đủ phí cổng thanh toán và hóa đơn
  đối soát vận chuyển.

Lượt 1 không thay đổi database schema và không yêu cầu migration.
