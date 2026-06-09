using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class ReviewDetails
{
    public int ReviewDetailId { get; set; }

    public int? ReviewId { get; set; }

    public int? VariantId { get; set; }

    public int? Rating { get; set; }

    public string? Comment { get; set; }

    public virtual Reviews? Review { get; set; }

    public virtual ProductVariants? Variant { get; set; }
}
