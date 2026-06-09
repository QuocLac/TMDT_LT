using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Address
{
    public int AddressId { get; set; }

    public int CustomerId { get; set; }

    public string Street { get; set; } = null!;

    public string? District { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? PostalCode { get; set; }

    public bool? IsDefault { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
