using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class PurchaseOrderDetails
{
    public int PodetailId { get; set; }

    public int Poid { get; set; }

    public int VariantId { get; set; }

    public int Quantity { get; set; }

    public decimal ImportPrice { get; set; }

    public virtual PurchaseOrders Po { get; set; } = null!;

    public virtual ProductVariants Variant { get; set; } = null!;
}
