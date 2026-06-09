using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Products
{
    public int ProductId { get; set; }

    public string Name { get; set; } = null!;

    public int BrandId { get; set; }

    public int CategoryId { get; set; }

    public string? Chipset { get; set; }

    public string? OperatingSystem { get; set; }

    public short? BatteryCapacity { get; set; }

    public bool? ChargerIncluded { get; set; }

    public decimal? ScreenSize { get; set; }

    public string? ScreenTech { get; set; }

    public short? RefreshRate { get; set; }

    public string? RearCamera { get; set; }

    public string? FrontCamera { get; set; }

    public decimal? Weight { get; set; }

    public string? Dimensions { get; set; }

    public string? Description { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    public string? MainImage { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual Brands Brand { get; set; } = null!;

    public virtual Categories Category { get; set; } = null!;

    public virtual ICollection<ProductVariants> ProductVariants { get; set; } = new List<ProductVariants>();
}
