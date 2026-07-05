using System;
using System.Collections.Generic;

namespace TMDT_LT.Models
{
    public class Stores
    {
        public int StoreId { get; set; }
        public string StoreCode { get; set; } = null!; // ST001, AG001...
        public string StoreName { get; set; } = null!;
        public string StoreType { get; set; } = null!; // Direct (Trực thuộc), Agent (Đại lý)
        public string? TaxCode { get; set; }
        public string Address { get; set; } = null!;
        public string Phone { get; set; } = null!;
        public string? Email { get; set; }
        public string? ContactPerson { get; set; }
        public string? ContractNumber { get; set; }
        public DateTime? ContractDate { get; set; }
        public DateTime? ContractExpiry { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? Notes { get; set; }

        public virtual ICollection<SalesOrders> SalesOrders { get; set; } = new List<SalesOrders>();
    }
}