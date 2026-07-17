using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public sealed class Warehouses
{
    public int WarehouseId { get; set; }

    public string WarehouseCode { get; set; } = string.Empty;

    public string WarehouseName { get; set; } = string.Empty;

    public string? Address { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<InventoryLots> InventoryLots { get; set; }
        = new List<InventoryLots>();

    public ICollection<PurchaseOrders> PurchaseOrders { get; set; }
        = new List<PurchaseOrders>();

    public ICollection<Orders> FulfillmentOrders { get; set; }
        = new List<Orders>();

    public ICollection<SalesOrders> DistributionOrders { get; set; }
        = new List<SalesOrders>();

    public ICollection<OrderInventoryAllocations> OrderInventoryAllocations { get; set; }
        = new List<OrderInventoryAllocations>();
}
