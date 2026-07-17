using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class PurchaseOrders
{
    public int WarehouseId { get; set; } = 1;

    public string? InvoiceNumber { get; set; }

    public DateTime? InvoiceDate { get; set; }

    public decimal GoodsSubtotal { get; set; }

    public decimal InputVatAmount { get; set; }

    public decimal InboundShippingFee { get; set; }

    public decimal OtherCost { get; set; }

    public decimal InventoryCapitalizedCost { get; set; }

    public bool InputVatDeductible { get; set; } = true;

    public Warehouses? Warehouse { get; set; }
}

public partial class PurchaseOrderDetails
{
    public decimal TaxRate { get; set; }

    public decimal InputVatAmount { get; set; }

    public decimal AllocatedInboundCost { get; set; }

    public decimal LandedUnitCost { get; set; }

    public decimal CapitalizedLineCost { get; set; }
}

public partial class Orders
{
    public int? FulfillmentWarehouseId { get; set; }

    public decimal MerchandiseNetRevenueAmount { get; set; }

    public decimal CogsAmount { get; set; }

    public decimal GrossProfitAmount { get; set; }

    public DateTime? CostCalculatedAt { get; set; }

    public Warehouses? FulfillmentWarehouse { get; set; }

    public ICollection<OrderInventoryAllocations> OrderInventoryAllocations { get; set; }
        = new List<OrderInventoryAllocations>();
}

public partial class OrderDetails
{
    public decimal NetRevenueAmount { get; set; }

    public decimal CogsAmount { get; set; }

    public decimal GrossProfitAmount { get; set; }

    public DateTime? CostCalculatedAt { get; set; }

    public ICollection<OrderInventoryAllocations> InventoryAllocations { get; set; }
        = new List<OrderInventoryAllocations>();
}

public partial class InventoryTransactions
{
    public int? WarehouseId { get; set; }

    public int? LotId { get; set; }

    public decimal? UnitCostSnapshot { get; set; }

    public decimal? TotalCostSnapshot { get; set; }

    public Warehouses? Warehouse { get; set; }

    public InventoryLots? Lot { get; set; }
}
