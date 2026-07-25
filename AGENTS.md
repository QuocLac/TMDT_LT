# Quy tắc làm việc với project

Đây là project sàn thương mại điện tử phát triển dài hạn. Mọi thay đổi phải được
tổ chức theo năng lực nghiệp vụ ổn định, không theo lượt chat, bản vá tạm hay giai
đoạn triển khai.

## Quy trình bắt buộc

Trước khi lập kế hoạch hoặc sửa bất kỳ module nào:

1. Xác định commit mới nhất trên nhánh mục tiêu.
2. Tìm và đọc file ngữ cảnh của module trong `docs/modules/<module>/`.
3. Rà toàn bộ controller, service, model, view, script, EF mapping, migration và
   dependency liên quan trước khi code.
4. Tham khảo nghiệp vụ hiện hành và tài liệu kỹ thuật chính thức liên quan.
5. Nêu impact plan ngắn, sau đó triển khai thay đổi nhỏ nhất nhưng hoàn chỉnh.
6. Cập nhật file ngữ cảnh module trước khi bàn giao.

Đối với Inventory, file bắt buộc là:

`docs/modules/inventory/INVENTORY_MODULE_CONTEXT.md`

## Quy tắc production naming

- Đặt tên class, file, route và asset theo năng lực nghiệp vụ ổn định.
- Không dùng `PhaseB`, `PhaseC`, `PhaseD`, `D1`, `D2`, `D3`, `Fix`, `Final`,
  `New` hoặc `Legacy` trong production code đang hoạt động.
- Không đổi tên migration lịch sử hoặc commit đã tồn tại chỉ để làm đẹp tên.
- Controller không được khởi tạo hay gọi controller khác.
- Request contract không đặt bên trong controller.
- Business logic dùng chung phải nằm trong service và được đăng ký bằng DI.

## Database và migration

- Không tự tạo migration thay người dùng.
- Khi cần đổi schema, chỉ cập nhật model/configuration, mô tả migration dự kiến
  và để người dùng tự chạy `Add-Migration` sau khi review.
- Refactor cấu trúc thuần túy không được phát sinh schema migration.
- Không chỉnh migration đã áp dụng chỉ để đổi tên.

## Bàn giao qua chat/ZIP

Mỗi lần bàn giao phải có:

- commit nền đã rà;
- file thêm, sửa và xóa;
- ZIP giải nén tại project root;
- script cleanup xác định nếu phải xóa/đổi file cũ;
- trạng thái migration;
- commit message đề xuất;
- kiểm tra thực sự đã chạy và phần chưa xác minh;
- file ngữ cảnh module đã cập nhật.

Không tuyên bố build hoặc runtime thành công khi chưa kiểm tra thực tế.
