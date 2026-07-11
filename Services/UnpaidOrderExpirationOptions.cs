namespace TMDT_LT.Services;

public sealed class UnpaidOrderExpirationOptions
{
    public const string SectionName = "UnpaidOrderExpiration";

    public bool Enabled { get; set; } = true;

    public int InitialDelaySeconds { get; set; } = 10;

    public int ScanIntervalSeconds { get; set; } = 60;

    public int VnPayTimeoutMinutes { get; set; } = 15;

    public int BankTransferTimeoutMinutes { get; set; } = 1440;

    public int BatchSize { get; set; } = 50;
}
