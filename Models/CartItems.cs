using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class CartItems
{
    public int CartItemId { get; set; }

    public int? CustomerId { get; set; }

    public int? VariantId { get; set; }

    public int? Quantity { get; set; }

    public DateTime? CreatedDate { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ProductVariants? Variant { get; set; }
}
