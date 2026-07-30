# Order module context

## Phạm vi

Module Order quản lý danh sách và chi tiết đơn hàng phía Admin, vòng đời trạng thái,
thanh toán, vận chuyển, chứng từ đơn hàng, hoàn trả và các dữ liệu đối soát liên quan.

## Điểm tích hợp giao diện trên trang Admin Order Details

Trang `Areas/Admin/Views/Order/Details.cshtml` sử dụng layout
`Areas/Admin/Views/Shared/_LayoutAdmin.cshtml` và có bố cục hai cột:

- cột trái: thông tin khách hàng, thanh toán, vận chuyển và sản phẩm;
- cột phải: trạng thái vận hành, đối soát tài chính, công cụ đơn hàng,
  hành trình đơn hàng và thao tác điều phối.

Các tiện ích dùng `body` TagHelper để tránh nhân đôi nghiệp vụ trong view:

- `OrderFinancialSummaryBodyTagHelper`;
- `OrderReceiptActionBodyTagHelper`;
- `ShippingTimelineBodyTagHelper`.

## Quy tắc gắn UI bắt buộc

- Trên route `Admin/Order/Details`, không được dùng selector chung như `main`,
  `.main-content` hoặc `.content-wrapper main` làm fallback.
- Bảng chi tiết thanh toán phải được gắn vào sidebar của chi tiết đơn hàng.
- Nút mở chứng từ và nút chi tiết vận chuyển phải nằm trong card
  `Công cụ đơn hàng`, không được nổi đè lên nội dung Admin.
- Nếu không xác định được sidebar Admin, tiện ích phải ẩn an toàn và ghi cảnh báo
  console thay vì chèn vào layout cấp cao.
- Trên giao diện khách hàng, hành vi hiện có của các tiện ích không thay đổi.

## Sự cố giao diện đã xử lý ngày 30/07/2026

### Hiện tượng

Bảng `Chi tiết thanh toán` được JavaScript chèn vào thẻ `main` của layout Admin,
làm nó xuất hiện phía trên header. Nút chứng từ và nút vận chuyển rơi về chế độ
floating, gây che nội dung trang.

### Nguyên nhân

Các script tích hợp dùng selector fallback quá rộng. Thẻ
`<main class="admin-main-content">` bị nhận nhầm là vùng nội dung của đơn hàng.

### Cách xử lý

- Phát hiện riêng bố cục chi tiết đơn hàng bên trong `.content-wrapper`.
- Tạo host tài chính và card công cụ trong đúng sidebar.
- Loại bỏ fallback `main` cho chế độ Admin.
- Chuyển nút vận chuyển sang dạng inline trong card công cụ.
- Thêm breakpoint để bố cục hai cột chuyển thành một cột ở màn hình hẹp.
- Ẩn bảng tài chính trước khi script hoàn tất mount để tránh nháy sai vị trí.

## File liên quan trực tiếp

- `wwwroot/js/shared/order-financial-summary.js`
- `wwwroot/js/shared/order-receipt-action.js`
- `wwwroot/js/shared/shipping-timeline.js`
- `wwwroot/css/shared/order-financial-summary.css`
- `wwwroot/css/admin/order-details-integrations.css`

## Database

Thay đổi này chỉ liên quan DOM, CSS và JavaScript. Không thay đổi model, EF mapping
hoặc schema; không cần migration.
