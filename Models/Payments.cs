using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Payments
{
    public int PaymentId { get; set; }

    public int? OrderId { get; set; }

    public string? PaymentMethod { get; set; }

    public DateTime? PaymentDate { get; set; }

    public string? PaymentStatus { get; set; }

    public virtual Orders? Order { get; set; }
}
