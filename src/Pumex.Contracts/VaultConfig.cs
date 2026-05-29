namespace Pumex.Contracts;

public record VaultConfig(
    string Name,
    DateTimeOffset Created,
    int Version,
    string? DailyFolder = null,
    string? DailyFormat = null,
    IReadOnlyList<string>? Formats = null,
    IReadOnlyList<string>? Ignore = null)
{
    public const int CurrentVersion = 1;
    public const string DefaultDailyFolder = "daily";
    public const string DefaultDailyFormat = "yyyy-MM-dd";

    public string EffectiveDailyFolder => string.IsNullOrWhiteSpace(DailyFolder) ? DefaultDailyFolder : DailyFolder;
    public string EffectiveDailyFormat => string.IsNullOrWhiteSpace(DailyFormat) ? DefaultDailyFormat : DailyFormat;

    /// <summary>
    /// Extra non-Markdown file extensions to index for this vault, normalised to
    /// lowercase with a leading dot (e.g. <c>.csv</c>). Markdown is always indexed
    /// and is not listed here. Absent/empty = Markdown only (legacy behaviour).
    /// </summary>
    public IReadOnlyList<string> EffectiveFormats =>
        Formats is null
            ? Array.Empty<string>()
            : Formats
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => "." + f.Trim().TrimStart('.').ToLowerInvariant())
                .Where(f => f != ".md")
                .Distinct()
                .ToArray();

    /// <summary>Ignore globs for this vault; absent = none.</summary>
    public IReadOnlyList<string> EffectiveIgnore =>
        Ignore?.Where(g => !string.IsNullOrWhiteSpace(g)).ToArray() ?? Array.Empty<string>();
}
