using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public partial class Reviews
    {
        [Key]
        public int ReviewId { get; set; }

        public int? ProductId { get; set; }

        public int? CustomerId { get; set; }

        // BỔ SUNG CỘT ORDERID ĐỂ KIỂM CHỨNG ĐƠN HÀNG
        public int? OrderId { get; set; }

        public int? Rating { get; set; }

        public string? Comment { get; set; }

        public DateTime? CreatedAt { get; set; }

        public bool IsHidden { get; set; } = false;

        public bool IsRead { get; set; } = false;

        public string? AdminReply { get; set; }

        public virtual Customer? Customer { get; set; }
        public virtual Products? Product { get; set; }

        [ForeignKey("OrderId")]
        public virtual Orders? Order { get; set; } // Liên kết ngược lại đơn gốc

        public virtual ICollection<ReviewDetails> ReviewDetails { get; set; } = new List<ReviewDetails>();
    }
}