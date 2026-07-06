using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Orders
{
    public int OrderId { get; set; }

    public int? CustomerId { get; set; }

    public DateTime? OrderDate { get; set; }

    public string? Status { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? ShippingFullName { get; set; }

    public string? ShippingPhone { get; set; }

    public string? ShippingStreet { get; set; }

    public string? ShippingDistrict { get; set; }

    public string? ShippingCity { get; set; }

    public string? ShippingCountry { get; set; }

    // Cờ kho chuẩn vận hành TMĐT:
    // true = đơn này đã giữ/trừ tồn vật lý, khi hủy/hoàn cần hoàn lại đúng một lần.
    public bool IsStockDeducted { get; set; } = false;

    public DateTime? StockDeductedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<OrderDetails> OrderDetails { get; set; } = new List<OrderDetails>();

    public virtual ICollection<Payments> Payments { get; set; } = new List<Payments>();

    public virtual ICollection<Shipping> Shipping { get; set; } = new List<Shipping>();
    public virtual ICollection<OrderHistory> OrderHistories { get; set; } = new List<OrderHistory>();

    // THÊM DÒNG NÀY ĐỂ LIÊN KẾT NGƯỢC
    public virtual ICollection<OrderReturns> OrderReturns { get; set; } = new List<OrderReturns>();

    public string? CancellationReason { get; set; }
    public string? CancellationRequestedBy { get; set; }

    // Mốc thời gian để tính hạn 3 tháng đánh giá và 7 ngày hoàn trả
    public DateTime? CompletedDate { get; set; }
}
