namespace Titan.Abstractions;

/// <summary>
/// Configuration options for item history tracking.
/// </summary>
public class ItemHistoryOptions
{
    public const string SectionName = "ItemHistory";

    /// <summary>
    /// Maximum number of history entries to keep per item.
    /// Older entries are pruned when this limit is exceeded.
    /// </summary>
    public int MaxEntriesPerItem { get; set; } = 100;

    /// <summary>
    /// Number of days to retain history entries.
    /// Entries older than this are eligible for cleanup.
    /// </summary>
    public int RetentionDays { get; set; } = 90;
}
