using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Notifications
{
    public int NotificationId { get; set; }

    public int? AccountId { get; set; }

    public string? Message { get; set; }

    public string? Type { get; set; }

    public bool? IsRead { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Account? Account { get; set; }
}
