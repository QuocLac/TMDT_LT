using System;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels
{
    public class ProfileVM
    {
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập họ tên")]
        public string FullName { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        public string? Phone { get; set; }

        public string? Gender { get; set; }

        public DateOnly? BirthDate { get; set; }

        // Các thông tin chỉ đọc (Read-only) hiển thị cho khách xem
        public string CustomerType { get; set; } = "Newbie";
        public int RewardPoints { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}