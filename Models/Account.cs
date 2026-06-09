using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Account
{
    public int AccountId { get; set; }

    public string Email { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string Role { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public bool? IsActive { get; set; }

    public string? Phone { get; set; }

    public virtual ICollection<Customer> Customer { get; set; } = new List<Customer>();

    public virtual ICollection<InventoryTransactions> InventoryTransactions { get; set; } = new List<InventoryTransactions>();

    public virtual ICollection<Notifications> Notifications { get; set; } = new List<Notifications>();

    public virtual ICollection<PurchaseOrders> PurchaseOrders { get; set; } = new List<PurchaseOrders>();
}
