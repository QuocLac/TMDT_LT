using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels
{
    public class PurchaseOrderVM
    {
        [Required(ErrorMessage = "Vui lòng chọn Nhà cung cấp")]
        public int SupplierId { get; set; }

        public string? Note { get; set; }

        // Danh sách các mặt hàng được nhập trong phiếu này
        public List<PurchaseOrderDetailVM> Details { get; set; } = new List<PurchaseOrderDetailVM>();
    }

    public class PurchaseOrderDetailVM
    {
        public int VariantId { get; set; }
        public int Quantity { get; set; }
        public decimal ImportPrice { get; set; }
    }
}