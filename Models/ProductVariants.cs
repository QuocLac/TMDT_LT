using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class ProductVariants
{
    public int VariantId { get; set; }

    public int ProductId { get; set; }

    public string? Color { get; set; }

    public string? Storage { get; set; }

    public string? Ram { get; set; }

    public decimal? Price { get; set; }

    public decimal? DiscountPrice { get; set; }

    public int? Stock { get; set; }

    public string? ImageUrl { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual ICollection<CartItems> CartItems { get; set; } = new List<CartItems>();

    public virtual ICollection<FavoriteDetails> FavoriteDetails { get; set; } = new List<FavoriteDetails>();

    public virtual ICollection<InventoryTransactions> InventoryTransactions { get; set; } = new List<InventoryTransactions>();

    public virtual ICollection<OrderDetails> OrderDetails { get; set; } = new List<OrderDetails>();

    public virtual Products Product { get; set; } = null!;

    public virtual ICollection<PurchaseOrderDetails> PurchaseOrderDetails { get; set; } = new List<PurchaseOrderDetails>();

    public virtual ICollection<ReviewDetails> ReviewDetails { get; set; } = new List<ReviewDetails>();
}
