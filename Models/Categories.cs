using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Categories
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public string? Description { get; set; }

    public bool? IsActive { get; set; }

    public virtual ICollection<Products> Products { get; set; } = new List<Products>();
}
