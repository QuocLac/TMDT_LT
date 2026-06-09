using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Suppliers
{
    public int SupplierId { get; set; }

    public string SupplierName { get; set; } = null!;

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? TaxCode { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<PurchaseOrders> PurchaseOrders { get; set; } = new List<PurchaseOrders>();
}
