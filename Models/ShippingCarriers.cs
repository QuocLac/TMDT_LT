using System;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models
{
    public class ShippingCarriers
    {
        [Key]
        public int CarrierId { get; set; }

        [Required(ErrorMessage = "Tên đơn vị vận chuyển không được để trống")]
        [StringLength(100)]
        public string CarrierName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Đường dẫn API gốc (Base URL) không được để trống")]
        [StringLength(255)]
        public string ApiUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã Token xác thực kết nối không được để trống")]
        [StringLength(255)]
        public string ApiToken { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}