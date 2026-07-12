# KẾ HOẠCH ỔN ĐỊNH, TÁI CẤU TRÚC VÀ HOÀN THIỆN TMDT_LT

> Phạm vi áp dụng: branch `Lac12`
>
> Mục tiêu: đưa project về trạng thái build ổn định, luồng nghiệp vụ rõ ràng, dễ debug, có thể giải thích khi trình bày và không tiếp tục phát triển theo kiểu chắp vá.
>
> Nguyên tắc quan trọng với giao diện: việc gom CSS là **refactor bảo toàn giao diện**, không phải thiết kế lại. Mọi thay đổi nhìn thấy được phải được xem là regression nếu không có yêu cầu sửa UI cụ thể.

---

## 1. Mục tiêu tổng quát

1. Khôi phục baseline build và migration sạch.
2. Dọn các file sinh tự động, file dump và file đặt sai vị trí.
3. Chuẩn hóa cấu trúc Models, ViewModels, DTO, Services, Integrations, CSS và JavaScript.
4. Gom toàn bộ thay đổi trạng thái đơn hàng về một service điều phối duy nhất.
5. Tách riêng vòng đời Order, Payment, Shipping và Return.
6. Gom mọi biến động tồn kho về `InventoryService` để tránh hoàn kho hoặc trừ kho lặp.
7. Hoàn thiện GHN từ tính phí đến tạo vận đơn và đồng bộ trạng thái.
8. Viết lại Apriori theo pipeline có thể kiểm chứng bằng dữ liệu chuẩn và unit test.
9. Thiết kế exception, logging và breakpoint checkpoint để debug nhanh.
10. Bảo toàn giao diện hiện tại gần như pixel-perfect trong quá trình gom CSS.

---

## 2. Nguyên tắc triển khai bắt buộc

### 2.1. Không sửa trực tiếp mọi thứ trong một commit

Mỗi commit chỉ giải quyết một nhóm vấn đề độc lập và có thể rollback:

- dọn repository;
- sửa build;
- sửa migration;
- gom kho;
- gom vòng đời đơn;
- hoàn thiện payment;
- hoàn thiện shipping;
- viết lại Apriori;
- gom CSS;
- tách JavaScript;
- bổ sung test.

### 2.2. Controller phải mỏng

Controller chỉ được làm các việc:

1. nhận input;
2. kiểm tra quyền truy cập;
3. gọi service;
4. chuyển kết quả thành View, Redirect hoặc HTTP response.

Controller không được tiếp tục tự:

- cộng/trừ stock;
- thay đổi nhiều bảng trong một transaction;
- tính giá đơn hàng;
- tính quota Flash Sale;
- hoàn quota Flash Sale;
- đổi trạng thái payment;
- đổi trạng thái shipping;
- phát sinh điểm thưởng;
- gọi trực tiếp nhiều integration trong một action.

### 2.3. Không thay đổi giao diện khi gom CSS

Phần CSS phải tuân thủ:

- chụp baseline từng trang trước khi sửa;
- giữ nguyên selector, độ ưu tiên và giá trị trong giai đoạn di chuyển;
- không đổi màu, font, kích thước, spacing, border, shadow, animation hoặc layout;
- không đổi class trong View và CSS cùng một commit;
- xác định rule đang thắng bằng DevTools `Computed` trước khi xóa rule trùng;
- không xóa file CSS cũ trước khi xác nhận file mới cho kết quả tương đương;
- mỗi commit CSS chỉ xử lý một nhóm như layout, table, modal hoặc form;
- mọi sai lệch nhìn thấy được phải được sửa trước khi merge.

### 2.4. Không dùng `Debugger.Break()` trong nghiệp vụ

Breakpoint phải dựa trên:

- custom exception;
- exception settings của IDE;
- conditional breakpoint;
- structured logging;
- assertion cho trạng thái bất khả thi trong môi trường debug.

---

## 3. Các vấn đề ưu tiên P0

### 3.1. Lỗi compile trong `CartController`

Trong `UpdateQuantity` đang có biến cục bộ bị khai báo trùng trong cùng scope. Phải sửa trước mọi công việc khác.

Điều kiện hoàn thành:

```bash
dotnet clean
dotnet restore
dotnet build
```

Kết quả yêu cầu:

```text
0 errors
```

### 3.2. Migration Flash Sale bị trùng

Hai migration đang có khả năng thêm lặp:

- cột `Orders.IsStockDeducted`;
- cột `Orders.StockDeductedAt`;
- cột `OrderDetails.IsFlashSaleItem`;
- cột `OrderDetails.FlashSaleItemId`;
- index và foreign key liên quan Flash Sale.

Trước khi xóa hoặc sửa migration phải kiểm tra:

```sql
SELECT MigrationId
FROM __EFMigrationsHistory
ORDER BY MigrationId;
```

Không xóa migration đã chạy trên database cần giữ dữ liệu nếu chưa có kế hoạch repair.

Với database sandbox, hướng ưu tiên:

1. backup dữ liệu test cần giữ;
2. hoàn chỉnh entity và mapping;
3. tạo baseline migration sạch;
4. tạo database mới;
5. chạy toàn bộ migration từ đầu;
6. seed dữ liệu kiểm thử.

### 3.3. EF Core package lệch phiên bản

Đồng bộ toàn bộ package EF Core theo cùng major/minor phù hợp với `.NET 9`.

Không để `Microsoft.EntityFrameworkCore.Tools` khác major với runtime/design package.

### 3.4. Secret đang nằm trong cấu hình version control

Việc cần làm:

- rotate token và secret đã commit;
- chuyển secret sang User Secrets hoặc environment variables;
- không log raw token;
- bỏ file credentials khỏi source control;
- kiểm tra lịch sử Git nếu cần xóa secret khỏi commit cũ.

Bổ sung `.gitignore`:

```gitignore
ga4-credentials.json
appsettings.Local.json
appsettings.Secrets.json
secrets/
wwwroot/uploads/
*_All.txt
Images_List.txt
structure.txt
```

### 3.5. File rác và file đặt sai vị trí

Nhóm cần xóa khỏi repository sau khi xác nhận không còn reference:

```text
bin/
obj/
TMDT_LT.csproj.user
Areas_All.txt
Controllers_All.txt
Models_All.txt
Services_All.txt
Views_All.txt
Images_List.txt
structure.txt
Models/Product.cshtml
Models/ProductReviews.cshtml
```

`Product.cshtml` và `ProductReviews.cshtml` phải chỉ tồn tại trong thư mục `Views` phù hợp.

---

## 4. Cấu trúc thư mục mục tiêu

```text
TMDT_LT/
├── Areas/
│   └── Admin/
│       ├── Controllers/
│       └── Views/
├── Controllers/
├── Data/
├── Domain/
│   ├── Orders/
│   ├── Payments/
│   ├── Shipping/
│   ├── Returns/
│   ├── Inventory/
│   └── CrossSell/
├── Models/
│   ├── Entities/
│   ├── Requests/
│   │   ├── Orders/
│   │   ├── Inventory/
│   │   ├── Shipping/
│   │   ├── FlashSales/
│   │   └── Analytics/
│   └── ViewModels/
│       ├── Admin/
│       │   ├── Products/
│       │   ├── Inventory/
│       │   ├── Orders/
│       │   ├── Shipping/
│       │   └── CrossSell/
│       └── Storefront/
│           ├── Catalog/
│           ├── ProductDetail/
│           ├── Cart/
│           └── Checkout/
├── Services/
│   ├── Orders/
│   ├── Payments/
│   ├── Shipping/
│   ├── Inventory/
│   ├── Pricing/
│   ├── Returns/
│   └── CrossSell/
├── Integrations/
│   ├── Ghn/
│   ├── VnPay/
│   └── GoogleAnalytics/
├── Algorithms/
│   └── Apriori/
├── Migrations/
├── Tests/
│   ├── UnitTests/
│   └── IntegrationTests/
├── Views/
└── wwwroot/
    ├── css/
    └── js/
```

Không bắt buộc di chuyển toàn bộ entity ngay trong lần đầu. Có thể thực hiện dần theo feature để giảm rủi ro namespace.

---

## 5. Chuẩn hóa ViewModel và Request DTO

### 5.1. Xóa ViewModel trùng tên

Đang tồn tại nhiều class như `ProductCreateVM` ở namespace khác nhau nhưng chức năng chồng chéo.

Mục tiêu:

```text
Models/ViewModels/Admin/Products/ProductCreateVM.cs
Models/ViewModels/Admin/Products/ProductEditVM.cs
```

Storefront không dùng ViewModel của Admin.

### 5.2. Không đặt request class trong controller

Các request DTO trong `InventoryController`, `FlashSaleController`, `OrderController` hoặc controller khác phải được chuyển ra file riêng.

Ví dụ:

```text
Models/Requests/Inventory/CreatePurchaseOrderRequest.cs
Models/Requests/Inventory/CreateSalesOrderRequest.cs
Models/Requests/Inventory/AdjustStockRequest.cs
Models/Requests/Shipping/CreateShipmentRequest.cs
Models/Requests/Orders/CancelOrderRequest.cs
```

---

## 6. Thiết kế vòng đời nghiệp vụ

### 6.1. Order lifecycle

```text
PendingPayment
    ├── PaymentSucceeded → PendingConfirmation
    └── PaymentFailed    → Cancelled

PendingConfirmation
    ├── Confirm          → Processing
    └── Cancel           → Cancelled

Processing
    ├── ReadyToShip      → ReadyToShip
    └── Cancel           → Cancelled

ReadyToShip
    ├── CarrierAccepted  → AwaitingPickup
    └── Cancel           → Cancelled

AwaitingPickup
    └── PickedUp         → Shipping

Shipping
    ├── Delivered        → Delivered
    ├── DeliveryFailed   → DeliveryException
    └── Returning        → ReturningToSender

Delivered
    ├── CustomerConfirm  → Completed
    ├── AutoComplete     → Completed
    └── RequestReturn    → ReturnInProgress

ReturnInProgress
    ├── ReturnRejected   → Completed
    └── ReturnCompleted  → Returned
```

### 6.2. Payment lifecycle

```text
Pending
Paid
Failed
Cancelled
RefundPending
PartiallyRefunded
Refunded
```

### 6.3. Shipping lifecycle

```text
Draft
Quoted
Created
AwaitingPickup
PickedUp
InTransit
OutForDelivery
Delivered
DeliveryFailed
Returning
Returned
Cancelled
```

### 6.4. Return lifecycle

```text
Requested
Approved
Rejected
AwaitingCustomerShipment
ReceivedAtWarehouse
Inspecting
RefundPending
Completed
```

### 6.5. Quy tắc quan trọng

- `Orders.Status` không chứa trạng thái chi tiết của return hoặc shipping.
- `Shipping.Status` là nguồn thật của vòng đời vận chuyển.
- `OrderReturns.Status` là nguồn thật của vòng đời trả hàng.
- `Payments.PaymentStatus` là nguồn thật của dòng tiền.
- `OrderHistory` dùng để audit, không dùng làm nguồn trạng thái chính.
- Mọi chuyển trạng thái phải kiểm tra transition hợp lệ.
- Mọi thao tác có side effect phải idempotent.

---

## 7. Service cần xây dựng

### 7.1. `IOrderLifecycleService`

```csharp
public interface IOrderLifecycleService
{
    Task<OrderTransitionResult> TransitionAsync(
        int orderId,
        OrderStatus targetStatus,
        TransitionContext context,
        CancellationToken cancellationToken = default);
}
```

Trách nhiệm:

- tải đơn và dữ liệu liên quan;
- kiểm tra transition;
- khóa transaction;
- gọi inventory/payment/shipping/return service;
- ghi history;
- kiểm tra idempotency;
- commit hoặc rollback.

### 7.2. `IInventoryService`

```csharp
public interface IInventoryService
{
    Task ReserveForOrderAsync(...);
    Task ReleaseForOrderAsync(...);
    Task ReceivePurchaseOrderAsync(...);
    Task DispatchSalesOrderAsync(...);
    Task AdjustStockAsync(...);
    Task ReturnToStockAsync(...);
}
```

Mọi thay đổi sau phải đi qua service này:

```text
ProductVariants.Stock
InventoryLots.RemainingQuantity
ProductSerials.Status
InventoryTransactions
Orders.IsStockDeducted
Orders.StockDeductedAt
FlashSaleItems.Sold
```

Controller không được cộng/trừ `variant.Stock` trực tiếp.

### 7.3. `IOrderPricingService`

Gom logic:

- giá thường;
- giá Flash Sale;
- quota khách hàng;
- bundle discount;
- voucher;
- shipping fee;
- final total.

Đầu ra là snapshot bất biến:

```csharp
public sealed record OrderPricingSnapshot(
    IReadOnlyList<PricedOrderLine> Lines,
    decimal Subtotal,
    decimal ProductDiscount,
    decimal VoucherDiscount,
    decimal ShippingFee,
    decimal FinalTotal);
```

Checkout phải tính lại server-side, không tin giá gửi từ client.

### 7.4. `IPaymentService`

Trách nhiệm:

- tạo payment;
- xác nhận payment;
- đánh dấu thất bại;
- nhận callback;
- chống callback lặp;
- yêu cầu refund;
- lưu mã giao dịch gateway;
- lưu số tiền gateway xác nhận;
- lưu response code;
- audit refund.

Các trường nên bổ sung:

```text
GatewayTransactionId
GatewayResponseCode
GatewayAmount
CallbackReceivedAt
RefundTransactionId
RefundedAmount
IdempotencyKey
RawPayloadHash
```

### 7.5. `IReturnService`

Trách nhiệm:

- tạo return request;
- duyệt hoặc từ chối;
- xác nhận khách đã gửi hàng;
- nhận hàng tại kho;
- kiểm định;
- hoàn kho;
- hoàn tiền;
- hoàn thành return.

Loại bỏ mọi logic hard-code refund thành công trong controller.

### 7.6. `IFileStorageService` và `IEmailService`

Tách upload file và gửi email khỏi controller để:

- kiểm soát định dạng file;
- giới hạn dung lượng;
- tránh path traversal;
- dễ mock khi test;
- log lỗi tập trung;
- không fire-and-forget bằng `_ = SendEmailAsync(...)` trong controller.

---

## 8. Hoàn thiện GHN và vòng đời shipping

### 8.1. Kiến trúc integration

```text
Integrations/Ghn/
├── GhnOptions.cs
├── IGhnClient.cs
├── GhnClient.cs
├── GhnApiException.cs
├── Requests/
│   ├── CalculateFeeRequest.cs
│   ├── CreateOrderRequest.cs
│   └── CancelOrderRequest.cs
└── Responses/
    ├── GhnApiResponse.cs
    ├── CalculateFeeResponse.cs
    ├── CreateOrderResponse.cs
    └── MasterDataResponses.cs
```

Không dùng anonymous object cho request/response GHN.

### 8.2. Dữ liệu Shipping cần bổ sung

```text
ShippingId
OrderId
CarrierId
ProviderCode
ProviderOrderCode
ServiceId
ServiceTypeId
ShippingFee
CodAmount
InsuranceValue
Status
ProviderStatus
TrackingNumber
CreatedAt
UpdatedAt
PickedUpAt
DeliveredAt
CancelledAt
LastSyncedAt
FailureReason
RowVersion
```

Tạo bảng `ShippingEvents`:

```text
ShippingEventId
ShippingId
ProviderStatus
InternalStatus
EventTime
PayloadHash
RawPayload
ProcessedAt
```

### 8.3. Luồng GHN hoàn chỉnh

```text
Checkout
→ lấy province/district/ward từ GHN
→ lấy service khả dụng
→ tính phí
→ lưu service + carrier + shipping fee vào order snapshot

Admin xác nhận đóng gói
→ Order = ReadyToShip
→ gọi GHN Create Order
→ lưu ProviderOrderCode
→ Shipping = AwaitingPickup

GHN webhook hoặc polling
→ map provider status
→ ghi ShippingEvents
→ cập nhật Shipping.Status
→ yêu cầu OrderLifecycleService chuyển trạng thái đơn

GHN Delivered
→ Shipping = Delivered
→ Order = Delivered

GHN Return
→ Shipping = Returning hoặc Returned
→ xử lý hàng hoàn qua OrderLifecycleService và InventoryService
```

### 8.4. COD amount

```text
COD order: CodAmount = FinalTotal
VNPAY đã thanh toán: CodAmount = 0
```

### 8.5. Chống lỗi integration

- cấu hình timeout rõ ràng;
- retry chỉ với lỗi mạng hoặc HTTP 5xx;
- không retry create order mù quáng;
- dùng idempotency nội bộ;
- log provider request ID;
- không log token;
- lưu response lỗi đã che dữ liệu nhạy cảm;
- có mock GHN cho integration test;
- webhook phải chống xử lý cùng payload nhiều lần.

---

## 9. Viết lại Apriori theo hướng kiểm chứng được

### 9.1. Vấn đề của implementation hiện tại

- mining lại trên mỗi request recommendation;
- mining lại khi tính bundle discount;
- mining lại khi admin preview;
- training và inference nằm chung service;
- không có model version;
- không có training run;
- không persist rule;
- không có golden dataset test;
- thuật toán bị trộn với chính sách giảm giá;
- input phụ thuộc trạng thái đơn dạng chuỗi không thống nhất;
- không có thống kê candidate, itemset và rule theo mỗi lần chạy.

### 9.2. Cấu trúc Apriori mới

```text
Algorithms/Apriori/
├── TransactionBasket.cs
├── Itemset.cs
├── FrequentItemset.cs
├── AssociationRule.cs
├── AprioriOptions.cs
├── IAprioriMiner.cs
├── AprioriMiner.cs
├── CandidateGenerator.cs
└── AssociationRuleGenerator.cs

Services/CrossSell/
├── BasketBuilder.cs
├── CrossSellTrainingService.cs
├── CrossSellRuleRepository.cs
├── CrossSellRecommendationService.cs
└── BundlePromotionService.cs
```

`AprioriMiner` phải là pure C#:

- không phụ thuộc EF Core;
- không phụ thuộc ASP.NET;
- không đọc `HttpContext`;
- input là danh sách basket;
- output deterministic;
- có unit test độc lập.

### 9.3. Pipeline chuẩn

```text
Orders + OrderDetails
→ lọc đơn hợp lệ
→ chuẩn hóa mỗi order thành tập ProductId distinct
→ loại basket dưới 2 item
→ sinh L1
→ sinh C2 và prune
→ sinh L2
→ tiếp tục Ck/Lk
→ sinh association rules
→ tính support/confidence/lift
→ lọc threshold
→ lưu training run
→ lưu rules
→ storefront chỉ đọc rules đã lưu
```

### 9.4. Công thức cần kiểm thử

```text
support(A ∪ B) = số basket chứa A và B / tổng basket
confidence(A → B) = support(A ∪ B) / support(A)
lift(A → B) = confidence(A → B) / support(B)
```

### 9.5. Bảng mới

`CrossSellTrainingRuns`:

```text
RunId
StartedAt
CompletedAt
Status
FromDate
ToDate
BasketCount
ProductCount
CandidateCount
FrequentItemsetCount
RuleCount
SettingsJson
ErrorMessage
```

`CrossSellRules`:

```text
RuleId
RunId
AntecedentKey
ConsequentProductId
SupportCount
Support
Confidence
Lift
IsActive
CreatedAt
```

### 9.6. Golden dataset bắt buộc

```text
T1 = {A, B, C}
T2 = {A, B}
T3 = {A, C}
T4 = {B, C}
T5 = {A, B, C}
```

Test phải assert:

- support count của A;
- support count của AB;
- confidence A → B;
- lift A → B;
- candidate bị prune khi subset không frequent;
- kết quả giống nhau giữa nhiều lần chạy.

### 9.7. Test dữ liệu nghiệp vụ

- đơn hủy không đi vào training;
- một product xuất hiện nhiều dòng chỉ tính một lần trong basket;
- nhiều variant cùng product chỉ tạo một ProductId;
- hết hàng không làm thay đổi rule toán học, chỉ lọc ở inference;
- training mới lỗi không làm mất rules active;
- bundle discount không sửa support/confidence/lift;
- storefront không mining database khi request.

---

## 10. Error handling, logging và breakpoint

### 10.1. Exception hierarchy

```text
AppException
├── DomainException
│   ├── InvalidOrderTransitionException
│   ├── InsufficientStockException
│   ├── FlashSaleQuotaException
│   ├── DuplicateOperationException
│   └── PricingInvariantException
├── IntegrationException
│   ├── GhnApiException
│   ├── VnPayException
│   └── GoogleAnalyticsException
└── DataConsistencyException
```

### 10.2. Global exception handler

```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

app.UseExceptionHandler();
```

Global handler phải:

- log stack trace;
- tạo correlation ID;
- map exception sang HTTP status;
- không trả raw exception ở production;
- không lộ secret hoặc payload nhạy cảm.

### 10.3. Structured logging checkpoint

```csharp
_logger.LogDebug(
    OrderLogEvents.TransitionStarted,
    "Order transition started. OrderId={OrderId}, From={From}, To={To}",
    orderId,
    currentStatus,
    targetStatus);
```

Checkpoint bắt buộc:

| Vị trí | Điều kiện debug |
|---|---|
| `OrderLifecycleService.TransitionAsync` | `orderId` bằng đơn đang kiểm tra |
| `OrderPricingService.CalculateAsync` | `FinalTotal < 0` hoặc giá client khác server |
| `InventoryService.ApplyMovementAsync` | stock kết quả âm |
| `PaymentService.ConfirmAsync` | callback đã được xử lý |
| `ShippingService.CreateShipmentAsync` | GHN trả lỗi |
| `ShippingStatusMapper.Map` | provider status chưa map |
| `AprioriMiner.Mine` | sau mỗi tập `Lk` |
| `AssociationRuleGenerator.Generate` | confidence hoặc lift ngoài kỳ vọng |

### 10.4. IDE exception settings

Trong Visual Studio:

```text
Debug
→ Windows
→ Exception Settings
→ Common Language Runtime Exceptions
→ Add Exception
```

Bật `Break on thrown` cho namespace custom exception của project.

### 10.5. Assertion

`Debug.Assert` chỉ dành cho trạng thái bất khả thi khi debug.

Invariant nghiệp vụ thật phải `throw` custom exception.

---

## 11. Kế hoạch gom CSS không thay đổi giao diện

### 11.1. Tạo visual baseline

Chụp trước khi sửa:

- trang admin desktop;
- trang storefront desktop;
- tablet;
- mobile;
- sidebar đang cuộn;
- header sticky;
- modal mở;
- dropdown mở;
- form focus;
- validation error;
- empty state;
- bảng có dữ liệu dài;
- product detail;
- cart;
- checkout;
- orders;
- Flash Sale;
- Cross Sell.

Mỗi ảnh cần cố định:

- viewport;
- browser zoom;
- dữ liệu test;
- trạng thái component;
- font đã load xong.

### 11.2. Lập bản đồ CSS đang thắng

Với selector trùng:

1. mở DevTools;
2. xem `Computed`;
3. xác định file và dòng đang thắng;
4. ghi lại kết quả cuối;
5. di chuyển nguyên giá trị sang file đích;
6. xác nhận ảnh trước–sau;
7. mới xóa rule cũ.

### 11.3. Cấu trúc CSS mục tiêu

```text
wwwroot/css/
├── shared/
│   ├── reset.css
│   ├── tokens.css
│   └── utilities.css
├── admin/
│   ├── admin.tokens.css
│   ├── admin.layout.css
│   ├── admin.components.css
│   └── pages/
│       ├── dashboard.css
│       ├── products.css
│       ├── inventory.css
│       ├── orders.css
│       ├── shipping.css
│       ├── flash-sale.css
│       └── cross-sell.css
└── storefront/
    ├── storefront.tokens.css
    ├── storefront.layout.css
    ├── storefront.components.css
    └── pages/
        ├── home.css
        ├── catalog.css
        ├── product-detail.css
        ├── cart.css
        ├── checkout.css
        └── orders.css
```

### 11.4. Tránh đè Bootstrap

Không tiếp tục dùng tên custom quá chung:

```text
.nav-item
.modal-content
.modal-header
.modal-body
.modal-footer
.btn-close
.btn-dark
.form-control
```

Tên đích:

```css
.adm-nav-item {}
.adm-modal {}
.adm-modal__header {}
.adm-button--dark {}

.sf-card {}
.sf-modal {}
.sf-button--primary {}
```

Việc rename chỉ thực hiện sau khi giai đoạn di chuyển nguyên trạng hoàn tất.

### 11.5. Thứ tự refactor CSS

```text
Chụp baseline
→ inventory selector
→ di chuyển nguyên trạng
→ visual compare
→ xóa rule trùng
→ visual compare
→ rename class ở commit riêng
→ visual compare
```

### 11.6. Điều kiện pass CSS

```text
[ ] Không đổi font
[ ] Không đổi màu
[ ] Không đổi spacing
[ ] Không đổi kích thước component
[ ] Không đổi vị trí component
[ ] Không đổi breakpoint responsive
[ ] Không đổi hover/focus/active
[ ] Không đổi modal/dropdown behavior
[ ] Không xuất hiện layout shift mới
[ ] Không còn selector custom đè Bootstrap ngoài chủ đích
[ ] Ảnh trước–sau không có sai lệch ngoài vùng đã ghi nhận
```

---

## 12. Tách JavaScript khỏi Razor View

Cấu trúc mục tiêu:

```text
wwwroot/js/
├── admin/
│   ├── orders.js
│   ├── inventory.js
│   ├── shipping.js
│   ├── flash-sale.js
│   └── cross-sell.js
└── storefront/
    ├── analytics.js
    ├── search.js
    ├── product-detail.js
    ├── cart.js
    └── checkout.js
```

Trong View chỉ giữ:

- markup Razor;
- `data-*` attributes;
- URL được render từ server;
- section import CSS/JS;
- nội dung động cần thiết.

Không để một View vừa chứa hàng trăm dòng HTML, CSS và JavaScript.

---

## 13. Kế hoạch triển khai theo giai đoạn và commit

## Giai đoạn 0 — Khóa baseline

Branch triển khai đề xuất:

```text
refactor/stabilize-lac12
```

### Commit 1

```text
chore: remove generated files and exposed local artifacts
```

Nội dung:

- xóa build artifact;
- xóa dump file;
- xóa View đặt sai;
- cập nhật `.gitignore`;
- di chuyển secret;
- không thay đổi nghiệp vụ.

Checkpoint:

```text
[ ] git status sạch
[ ] không còn secret mới trong source
[ ] không còn bin/obj được track
[ ] project structure rõ ràng hơn
```

---

## Giai đoạn 1 — Build và database

### Commit 2

```text
fix: restore clean build baseline
```

- sửa compile error;
- đồng bộ package;
- xử lý namespace/ViewModel trùng;
- sửa lỗi reference sau khi dọn file.

### Commit 3

```text
fix: reconcile migrations and database snapshot
```

- kiểm tra migration history;
- repair hoặc tạo baseline mới;
- đồng bộ snapshot;
- tạo database sandbox sạch.

Checkpoint:

```bash
dotnet clean
dotnet restore
dotnet build
dotnet ef database update
```

Điều kiện qua cổng:

```text
[ ] 0 build errors
[ ] database mới migrate được từ đầu
[ ] không có duplicate column/index/FK
```

---

## Giai đoạn 2 — Inventory và Order lifecycle

### Commit 4

```text
refactor: introduce typed order payment shipping and return statuses
```

### Commit 5

```text
refactor: centralize inventory movements
```

### Commit 6

```text
refactor: centralize order lifecycle transitions
```

### Commit 7

```text
refactor: centralize pricing and checkout orchestration
```

Test bắt buộc:

```text
[ ] tạo COD
[ ] tạo VNPAY
[ ] VNPAY thất bại
[ ] khách hủy
[ ] admin hủy
[ ] hủy hai lần
[ ] hoàn quota Flash Sale
[ ] hoàn kho hai lần
[ ] hoàn thành hai lần
[ ] điểm thưởng chỉ cộng một lần
[ ] final total không âm
[ ] stock và tổng lot không lệch
```

---

## Giai đoạn 3 — Payment và refund

### Commit 8

```text
refactor: centralize payment lifecycle and callback idempotency
```

### Commit 9

```text
feat: persist gateway transaction and refund audit data
```

Test bắt buộc:

```text
[ ] VNPAY return hợp lệ
[ ] VNPAY IPN hợp lệ
[ ] callback lặp
[ ] sai checksum
[ ] sai amount
[ ] order không tồn tại
[ ] refund thành công
[ ] refund thất bại
[ ] refund không hoàn kho lặp
```

---

## Giai đoạn 4 — GHN

### Commit 10

```text
feat: add typed GHN client and master data endpoints
```

### Commit 11

```text
feat: add shipment lifecycle and provider order creation
```

### Commit 12

```text
feat: add GHN webhook status synchronization
```

Checkpoint:

```text
[ ] lấy tỉnh/quận/phường thật
[ ] tính phí thật
[ ] lưu phí vào order snapshot
[ ] tạo vận đơn sandbox
[ ] VNPAY có COD amount bằng 0
[ ] COD có tiền thu hộ đúng
[ ] webhook lặp không xử lý lặp
[ ] map đầy đủ trạng thái đang dùng
[ ] hủy trước pickup hủy được shipment
```

---

## Giai đoạn 5 — Apriori

### Commit 13

```text
refactor: implement deterministic Apriori domain engine
```

### Commit 14

```text
test: add golden dataset tests for Apriori
```

### Commit 15

```text
feat: persist cross-sell training runs and association rules
```

### Commit 16

```text
refactor: separate cross-sell inference from bundle promotions
```

Checkpoint:

```text
[ ] golden dataset pass
[ ] nhiều lần chạy cho kết quả giống nhau
[ ] storefront không mining trên request
[ ] rule active không mất khi training mới lỗi
[ ] đơn hủy không vào dataset
[ ] variant cùng product không bị đếm lặp
```

---

## Giai đoạn 6 — CSS và JavaScript

### Commit 17

```text
refactor: consolidate admin css without visual changes
```

### Commit 18

```text
refactor: consolidate storefront css without visual changes
```

### Commit 19

```text
refactor: extract page scripts from Razor views
```

Checkpoint:

```text
[ ] có baseline screenshot
[ ] layout admin giữ nguyên
[ ] storefront giữ nguyên
[ ] responsive giữ nguyên
[ ] modal và dropdown giữ nguyên
[ ] không còn khối style lớn trong layout
[ ] không còn file CSS trùng nội dung
[ ] không xóa file cũ trước khi visual pass
```

---

## Giai đoạn 7 — Test và hardening

### Commit 20

```text
test: add order lifecycle and inventory integration tests
```

### Commit 21

```text
test: add payment and shipping idempotency tests
```

### Commit 22

```text
chore: add structured logging health checks and ci gates
```

CI tối thiểu:

```text
restore
build
unit tests
integration tests
migration smoke test
secret scan
```

---

## 14. Ma trận ưu tiên

### P0 — Phải làm ngay

- secret;
- build;
- migration;
- file rác;
- compile error;
- hoàn kho bị viết nhiều nơi;
- Flash Sale quota hoàn không thống nhất;
- hard-code refund thành công.

### P1 — Phải hoàn thành trước trình bày nghiệp vụ

- Order lifecycle;
- InventoryService;
- Payment lifecycle;
- Shipping lifecycle;
- GHN tạo shipment;
- Apriori golden tests;
- persist Apriori rules;
- idempotency.

### P2 — Hoàn thiện chất lượng code và giao diện

- gom CSS;
- tách JavaScript;
- dọn ViewModel;
- tách upload service;
- tách email service;
- tối ưu query.

### P3 — Sau khi baseline ổn định

- outbox/event bus;
- background jobs;
- distributed cache;
- external search engine;
- recommendation nâng cao;
- scheduled shipment reconciliation.

---

## 15. Definition of Done trước khi trình bày

```text
[ ] Clone mới và build được
[ ] Database mới migrate từ đầu được
[ ] Không còn secret trong source hiện tại
[ ] Không còn bin/obj/file dump được track
[ ] Không còn View đặt trong Models
[ ] Không còn ViewModel trùng không cần thiết
[ ] Order transition có state machine rõ ràng
[ ] Mọi thay đổi kho đi qua InventoryService
[ ] Hủy đơn idempotent
[ ] Hoàn kho idempotent
[ ] Tích điểm idempotent
[ ] Payment callback idempotent
[ ] Refund có audit rõ ràng
[ ] GHN tính phí thật
[ ] GHN tạo shipment thật trong sandbox
[ ] Có shipping lifecycle
[ ] Có webhook hoặc polling đồng bộ trạng thái
[ ] Apriori pass golden dataset
[ ] Apriori persist rule đã train
[ ] Storefront không mining trên request
[ ] Admin CSS có nguồn định nghĩa rõ ràng
[ ] Storefront CSS có nguồn định nghĩa rõ ràng
[ ] Giao diện trước và sau refactor không thay đổi ngoài lỗi được phê duyệt
[ ] View không chứa khối CSS/JS hàng trăm dòng
[ ] Có correlation ID
[ ] Có custom exception
[ ] Có exception breakpoint
[ ] Có smoke test toàn bộ vòng đời đơn
```

---

## 16. Thứ tự bắt đầu thực tế

Không bắt đầu từ Apriori, GHN hoặc CSS ngay lập tức.

Thứ tự triển khai:

```text
1. Tạo branch refactor
2. Dọn repository và secret
3. Sửa compile
4. Sửa package
5. Sửa migration
6. Build + migrate baseline
7. Gom InventoryService
8. Gom OrderLifecycleService
9. Gom OrderPricingService
10. Gom PaymentService
11. Hoàn thiện ReturnService
12. Hoàn thiện GHN và ShippingService
13. Viết lại Apriori + test
14. Gom CSS bảo toàn giao diện
15. Tách JavaScript
16. Bổ sung CI và smoke test
```

Mỗi bước chỉ được bắt đầu khi checkpoint của bước trước đã pass.
