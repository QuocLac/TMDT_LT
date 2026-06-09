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

    public virtual Orders? Order { get; set; }

    public virtual ProductVariants? Variant { get; set; }
}
