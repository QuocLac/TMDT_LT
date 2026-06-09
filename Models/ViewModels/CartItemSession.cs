namespace TMDT_LT.Models.ViewModels
{
    public class CartItemSession
    {
        public int VariantId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string Storage { get; set; } = string.Empty;
        public string RAM { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public int MaxStock { get; set; } // Giới hạn số lượng khách được phép mua

        // Tính thành tiền của dòng này
        public decimal TotalPrice => UnitPrice * Quantity;
    }
}