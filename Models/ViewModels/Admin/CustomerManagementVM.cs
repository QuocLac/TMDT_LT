using System;

namespace TMDT_LT.Models.ViewModels
{
    public class CustomerListItemVM
    {
        public int CustomerId { get; set; }
        public int AccountId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string CustomerType { get; set; } = "Newbie"; // Phân hạng thành viên (Newbie, Bạc, Vàng, Kim Cương)
        public bool IsActive { get; set; } // Trạng thái hoạt động của tài khoản
        public int TotalOrders { get; set; } // Tổng số đơn hàng đã đặt
        public decimal TotalSpent { get; set; } // Tổng số tiền đã chi tiêu (Chỉ tính các đơn "Hoàn thành")
        public DateTime CreatedAt { get; set; } // Ngày đăng ký tài khoản
    }
}
