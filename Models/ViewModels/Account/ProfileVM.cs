using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels
{
    public class ProfileVM
    {
        // 1. Tab Hồ sơ cá nhân
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập họ tên")]
        public string FullName { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        public string? Phone { get; set; }

        public string? Gender { get; set; }
        public DateOnly? BirthDate { get; set; }

        public string CustomerType { get; set; } = "Newbie";
        public int RewardPoints { get; set; }
        public DateTime? CreatedAt { get; set; }

        // 2. Tab Đổi mật khẩu
        public ChangePasswordVM PasswordData { get; set; } = new ChangePasswordVM();

        // 3. Tab Địa chỉ
        public List<AddressVM> Addresses { get; set; } = new List<AddressVM>();
        public AddressVM NewAddress { get; set; } = new AddressVM(); // Form thêm mới

        // Cờ điều hướng để biết đang ở Tab nào sau khi load lại trang
        public string ActiveTab { get; set; } = "profile";
    }

    public class ChangePasswordVM
    {
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại")]
        public string OldPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
        [MinLength(6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu")]
        [Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận không khớp")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class AddressVM
    {
        public int AddressId { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn Tỉnh/Thành phố")]
        public string City { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng chọn Quận/Huyện")]
        public string District { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập địa chỉ cụ thể")]
        public string Street { get; set; } = string.Empty; // Lưu "Phường/Xã, Số nhà"

        public bool IsDefault { get; set; }
    }
}
