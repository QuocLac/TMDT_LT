using System;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models
{
    public class ShippingCarriers
    {
        [Key]
        public int CarrierId { get; set; }

        // MÃ HÃNG: Dùng để mapping với appsettings.json ở tầng Code (Ví dụ: "GHN", "GHTK")
        [Required(ErrorMessage = "Mã hãng không được để trống")]
        [StringLength(50)]
        public string CarrierCode { get; set; } = string.Empty;

        // TÊN HÃNG: Hiển thị trên giao diện cho Admin và Khách hàng (Ví dụ: "Giao Hàng Nhanh")
        [Required(ErrorMessage = "Tên đơn vị vận chuyển không được để trống")]
        [StringLength(100)]
        public string CarrierName { get; set; } = string.Empty;

        // LOGO: Link ảnh hiển thị trên giao diện Grid UI cho trực quan
        [StringLength(255)]
        public string? LogoUrl { get; set; }

        // TRẠNG THÁI: Công tắc Bật/Tắt hãng vận chuyển
        public bool IsActive { get; set; } = true;

        // MẶC ĐỊNH: Hãng được chọn sẵn khi khách vào trang thanh toán
        public bool IsDefault { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}