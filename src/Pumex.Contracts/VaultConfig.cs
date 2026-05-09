namespace Pumex.Contracts;

public record VaultConfig(
    string Name,
    DateTimeOffset Created,
    int Version,
    string? DailyFolder = null,
    string? DailyFormat = null)
{
    public const int CurrentVersion = 1;
    public const string DefaultDailyFolder = "daily";
    public const string DefaultDailyFormat = "yyyy-MM-dd";

    public string EffectiveDailyFolder => string.IsNullOrWhiteSpace(DailyFolder) ? DefaultDailyFolder : DailyFolder;
    public string EffectiveDailyFormat => string.IsNullOrWhiteSpace(DailyFormat) ? DefaultDailyFormat : DailyFormat;
}
