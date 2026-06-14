using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Address
{
    public int AddressId { get; set; }
    public int CustomerId { get; set; }

    // --- 3 TRƯỜNG BỔ SUNG CHO NGHIỆP VỤ NHẬN HÀNG ---
    public string? ReceiverName { get; set; }
    public string? ReceiverPhone { get; set; }
    public string? Ward { get; set; } // Phường/Xã

    public string Street { get; set; } = null!; // Số nhà, tên đường
    public string? District { get; set; } // Quận/Huyện
    public string? City { get; set; } // Tỉnh/Thành phố
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public bool? IsDefault { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}