namespace TMDT_LT.Services;

public sealed class ReturnIntakeOptions
{
    public const string SectionName = "ReturnIntake";

    public int ReturnWindowDays { get; set; } = 7;

    public int MaxFiles { get; set; } = 5;

    public long MaxFileSizeBytes { get; set; }
        = 10L * 1024 * 1024;

    public long MaxTotalFileSizeBytes { get; set; }
        = 25L * 1024 * 1024;

    public bool RequireEvidence { get; set; }

    public string[] AllowedExtensions { get; set; }
        =
        [
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
            ".mp4",
            ".mov"
        ];
}
