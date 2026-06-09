using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Reviews
{
    public int ReviewId { get; set; }

    public int? CustomerId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<ReviewDetails> ReviewDetails { get; set; } = new List<ReviewDetails>();
}
