using System.Globalization;
using MusicTagAuditor.Core.Backup;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// バックアップ履歴の 1 行。
/// </summary>
/// <param name="entry">元になるバックアップ。</param>
public sealed class BackupEntryViewModel(BackupEntry entry)
{
    /// <summary>取得理由の表示名。</summary>
    private static readonly Dictionary<string, string> REASON_LABELS = new(StringComparer.Ordinal)
    {
        [nameof(SnapshotReason.Manual)] = "手動",
        [nameof(SnapshotReason.BeforeApply)] = "適用前（自動）",
        [nameof(SnapshotReason.BeforeRestore)] = "復元前（自動）",
    };

    /// <summary>元になるバックアップ。</summary>
    public BackupEntry Entry { get; } = entry;

    /// <summary>バックアップフォルダの絶対パス。</summary>
    public string DirectoryPath => Entry.DirectoryPath;

    /// <summary>取得日時の表示文字列。</summary>
    public string CreatedAtText
    {
        get
        {
            DateTimeOffset? createdAt = Entry.Manifest?.CreatedAt
                ?? SnapshotService.ParseTimestamp(Entry.DirectoryName);

            return createdAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
                ?? Entry.DirectoryName;
        }
    }

    /// <summary>取得理由の表示文字列。</summary>
    public string ReasonText
    {
        get
        {
            string? reason = Entry.Manifest?.Reason;

            if (reason is null)
            {
                return "不明";
            }

            return REASON_LABELS.TryGetValue(reason, out string? label) ? label : reason;
        }
    }

    /// <summary>記録件数。</summary>
    public string TrackCountText => Entry.Manifest?.TrackCount.ToString("N0", CultureInfo.CurrentCulture) ?? "-";

    /// <summary>補足。</summary>
    public string? Note => Entry.Manifest?.Note;
}
