using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models
{
    public class Campaigns
    {
        [Key]
        public int CampaignId { get; set; }

        [Required(ErrorMessage = "Tên chiến dịch là bắt buộc")]
        [StringLength(255)]
        public string Name { get; set; } = string.Empty; // VD: Lễ hội Apple tháng 11

        public string? Description { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public virtual ICollection<CampaignRules> CampaignRules { get; set; } = new List<CampaignRules>();
    }
}