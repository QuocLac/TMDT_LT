using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Favorites
{
    public int FavoriteId { get; set; }

    public int? CustomerId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<FavoriteDetails> FavoriteDetails { get; set; } = new List<FavoriteDetails>();
}
