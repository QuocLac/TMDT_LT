using System;

namespace TMDT_LT.Models
{
    public class AnalyticsEvent
    {
        public int EventId { get; set; }
        public string SessionKey { get; set; } = string.Empty;
        public string? VisitorKey { get; set; }
        public int? CustomerId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public int? ProductId { get; set; }
        public int? VariantId { get; set; }
        public int? TargetProductId { get; set; }
        public int? OrderId { get; set; }
        public string? SearchKeyword { get; set; }
        public string? PagePath { get; set; }
        public string? Referrer { get; set; }
        public decimal? EventValue { get; set; }
        public int? Quantity { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
