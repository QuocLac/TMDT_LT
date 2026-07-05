using System;
using System.Collections.Generic;

namespace TMDT_LT.Models
{
    public class SalesOrders
    {
        public int SOId { get; set; }
        public string SOCode { get; set; } = null!; // SO001, SO002...
        public int StoreId { get; set; }
        public int FromWarehouseId { get; set; } // Liên kết đến kho chính xuất đi
        public DateTime OrderDate { get; set; } = DateTime.Now;
        public string Status { get; set; } = "Completed"; // Draft, Confirmed, Completed
        public decimal TotalAmount { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal COGSTotal { get; set; } // Tổng giá vốn đích thực cộng dồn theo FIFO
        public decimal ProfitTotal { get; set; } // Tổng lợi nhuận gộp thực tế của phiếu xuất
        public string? InvoiceNumber { get; set; }
        public int AccountId { get; set; }
        public string? Notes { get; set; }

        public virtual Stores? Store { get; set; }
        public virtual Account? Account { get; set; }
        public virtual ICollection<SalesOrderDetails> SalesOrderDetails { get; set; } = new List<SalesOrderDetails>();
    }
}