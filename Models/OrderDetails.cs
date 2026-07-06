using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class OrderDetails
{
    public int OrderDetailId { get; set; }

    public int? OrderId { get; set; }

    public int? VariantId { get; set; }

    public int? Quantity { get; set; }

    public decimal? UnitPrice { get; set; }

    // Ghi nhận dòng chi tiết nào được hưởng Flash Sale để hoàn suất khi hủy đơn trước khi giao.
    public bool IsFlashSaleItem { get; set; } = false;

    public int? FlashSaleItemId { get; set; }

    public virtual Orders? Order { get; set; }

    public virtual ProductVariants? Variant { get; set; }

    public virtual FlashSaleItems? FlashSaleItem { get; set; }

    // Cờ xác nhận khách hàng đã đánh giá sản phẩm này trong đơn hàng này chưa
    public bool IsReviewed { get; set; } = false;
}
