using System;

namespace TMDT_LT.Models
{
    public class SearchQueryLog
    {
        public int SearchQueryLogId { get; set; }
        public string? SessionKey { get; set; }
        public string? VisitorKey { get; set; }
        public int? CustomerId { get; set; }
        public string Keyword { get; set; } = string.Empty;
        public string NormalizedKeyword { get; set; } = string.Empty;
        public int ResultCount { get; set; }
        public int? ClickedProductId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
