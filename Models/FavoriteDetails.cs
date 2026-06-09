using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class FavoriteDetails
{
    public int FavoriteDetailId { get; set; }

    public int? FavoriteId { get; set; }

    public int? VariantId { get; set; }

    public virtual Favorites? Favorite { get; set; }

    public virtual ProductVariants? Variant { get; set; }
}
