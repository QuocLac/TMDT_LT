using System;
using System.Collections.Generic;

namespace TMDT_LT.Models
{
    public class SalesOrders
    {
        public int SOId { get; set; }
        public string SOCode { get; set; } = null!;
        public int StoreId { get; set; }
        public int FromWarehouseId { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.Now;
        public string Status { get; set; } = "Completed";
        public decimal TotalAmount { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal COGSTotal { get; set; }
        public decimal ProfitTotal { get; set; }
        public string? InvoiceNumber { get; set; }
        public int AccountId { get; set; }
        public string? Notes { get; set; }

        public virtual Stores? Store { get; set; }
        public virtual Warehouses? FromWarehouse { get; set; }
        public virtual Account? Account { get; set; }
        public virtual ICollection<SalesOrderDetails> SalesOrderDetails { get; set; }
            = new List<SalesOrderDetails>();
    }
}
