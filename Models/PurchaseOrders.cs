using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class PurchaseOrders
{
    public int Poid { get; set; }

    public int SupplierId { get; set; }

    public int AccountId { get; set; }

    public DateTime? OrderDate { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? Status { get; set; }

    public string? Note { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<PurchaseOrderDetails> PurchaseOrderDetails { get; set; } = new List<PurchaseOrderDetails>();

    public virtual Suppliers Supplier { get; set; } = null!;
}
