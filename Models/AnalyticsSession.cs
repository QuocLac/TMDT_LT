using System;

namespace TMDT_LT.Models
{
    public class AnalyticsSession
    {
        public int AnalyticsSessionId { get; set; }
        public string SessionKey { get; set; } = string.Empty;
        public string? VisitorKey { get; set; }
        public int? CustomerId { get; set; }
        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
        public string? LandingPage { get; set; }
        public string? Referrer { get; set; }
        public string? UserAgent { get; set; }
        public string? IpHash { get; set; }
        public string? Source { get; set; }
        public string? Medium { get; set; }
        public string? Campaign { get; set; }
        public bool IsAuthenticated { get; set; }
        public int PageViewCount { get; set; }
        public int EventCount { get; set; }
        public bool HasAddToCart { get; set; }
        public bool HasCheckout { get; set; }
        public bool HasPurchase { get; set; }
        public decimal TotalRevenue { get; set; }
    }
}
