using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Brands
{
    public int BrandId { get; set; }

    public string BrandName { get; set; } = null!;

    public virtual ICollection<Products> Products { get; set; } = new List<Products>();
}
