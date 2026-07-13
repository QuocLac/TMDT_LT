namespace TMDT_LT.Models.AI;

public sealed class KingPhoneAiChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? PagePath { get; set; }
    public string? PageTitle { get; set; }
    public List<KingPhoneAiHistoryMessage> History { get; set; } = new();
}

public sealed class KingPhoneAiHistoryMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class KingPhoneAiChatResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ResponseId { get; set; }
    public string? ErrorCode { get; set; }
    public List<string> QuickReplies { get; set; } = new();
    public List<KingPhoneAiProductCard> Products { get; set; } = new();
    public List<string> ToolsUsed { get; set; } = new();
}

public sealed class KingPhoneAiProductCard
{
    public int ProductId { get; set; }
    public int VariantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? OriginalPrice { get; set; }
    public bool IsFlashSale { get; set; }
    public int Stock { get; set; }
    public string StockText { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}
