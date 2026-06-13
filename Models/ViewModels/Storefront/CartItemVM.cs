using System;

namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class CartItemVM
    {
        public int VariantId { get; set; }
        public string ProductName { get; set; } = null!;
        public string Color { get; set; } = string.Empty;
        public string Storage { get; set; } = null!;
        public string ImageUrl { get; set; } = null!;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public int Stock { get; set; }
        public decimal TotalPrice => Price * Quantity;
    }
}