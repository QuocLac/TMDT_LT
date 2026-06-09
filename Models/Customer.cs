using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Customer
{
    public int CustomerId { get; set; }

    public int AccountId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Gender { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string? CustomerType { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<Address> Address { get; set; } = new List<Address>();

    public virtual ICollection<CartItems> CartItems { get; set; } = new List<CartItems>();

    public virtual ICollection<Favorites> Favorites { get; set; } = new List<Favorites>();

    public virtual ICollection<Orders> Orders { get; set; } = new List<Orders>();

    public virtual ICollection<Reviews> Reviews { get; set; } = new List<Reviews>();
}
