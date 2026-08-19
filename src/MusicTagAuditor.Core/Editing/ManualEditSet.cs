using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Editing;

/// <summary>
/// 保留中の手編集をまとめて持つ（docs/SPEC.md 5.2 / 12章 段階 6）。
///
/// **セルを直しても即座には書き込まない。** 一括処理ツールでありながら適用前に必ず差分を
/// 人間が確認できることが最重要方針であり（docs/SPEC.md 1章）、逐次保存にすると
/// 確認の機会もバックアップの機会も無くなる。編集はここに溜め、
/// <see cref="ToChanges"/> で差分にしてから適用フローへ渡す。
/// </summary>
public sealed class ManualEditSet
{
    /// <summary>ファイルとフィールドの組ごとの編集。</summary>
    private readonly Dictionary<(string RelativePath, TagField Field), ManualEditEntry> _edits = [];

    /// <summary>編集内容が変わったときに発火する。</summary>
    public event EventHandler? Changed;

    /// <summary>保留中の編集の数。</summary>
    public int Count => _edits.Count;

    /// <summary>保留中の編集があるか。</summary>
    public bool HasEdits => _edits.Count > 0;

    /// <summary>
    /// 1 ファイルの 1 フィールドを編集する。
    ///
    /// **元の値に戻したら編集そのものを取り消す。** 差分ゼロの行を残すと、
    /// 適用の確認画面で「何も変わらない項目」を数えることになる。
    /// </summary>
    /// <param name="track">対象ファイルのタグ。</param>
    /// <param name="field">対象フィールド。</param>
    /// <param name="value">入力値。空白のみなら値を消す指示として扱う。</param>
    /// <param name="rationale">根拠。省略時は「手編集」。</param>
    /// <returns>編集として記録したなら true。元の値と同じで取り消したなら false。</returns>
    public bool Set(TrackTags track, TagField field, string? value, string? rationale = null)
    {
        ArgumentNullException.ThrowIfNull(track);

        string text = (value ?? string.Empty).Trim();
        IReadOnlyList<string> before = track.GetValues(field);

        bool clears = text.Length == 0;
        bool unchanged = clears
            ? before.Count == 0
            : before.Count == 1 && before[0] == text;

        if (unchanged)
        {
            _ = Remove(track.RelativePath, field);
            return false;
        }

        _edits[Key(track.RelativePath, field)] = new ManualEditEntry(
            track.RelativePath,
            field,
            before,
            text,
            rationale ?? ManualEditConst.RATIONALE_SINGLE);

        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// 複数ファイルの同じフィールドに同じ値を入れる。
    /// docs/SPEC.md 5.2 が「アルバム単位の編集で必須」とする一括入力。
    /// </summary>
    /// <param name="tracks">対象ファイル。</param>
    /// <param name="field">対象フィールド。</param>
    /// <param name="value">入力値。空白のみなら値を消す指示として扱う。</param>
    /// <returns>編集として記録した件数。</returns>
    public int SetMany(IEnumerable<TrackTags> tracks, TagField field, string? value)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        TrackTags[] targets = [.. tracks];

        // 何件への一括入力だったかを根拠に残す。後から CSV を見ても経緯が分かる。
        string rationale = targets.Length > 1
            ? $"{ManualEditConst.RATIONALE_SINGLE}（{targets.Length:N0} ファイルに一括入力）"
            : ManualEditConst.RATIONALE_SINGLE;

        int applied = 0;

        foreach (TrackTags track in targets)
        {
            if (Set(track, field, value, rationale))
            {
                applied++;
            }
        }

        return applied;
    }

    /// <summary>
    /// 編集後の表示値を返す。編集していなければ元の値をそのまま返す。
    /// </summary>
    /// <param name="track">対象ファイルのタグ。</param>
    /// <param name="field">対象フィールド。</param>
    /// <returns>表示する値。値が無ければ null。</returns>
    public string? GetDisplayValue(TrackTags track, TagField field)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (_edits.TryGetValue(Key(track.RelativePath, field), out ManualEditEntry? edit))
        {
            return edit.Value.Length == 0 ? null : edit.Value;
        }

        return track.GetSingle(field);
    }

    /// <summary>
    /// 指定フィールドが編集されているかを返す。
    /// </summary>
    /// <param name="relativePath">対象ファイル。</param>
    /// <param name="field">対象フィールド。</param>
    /// <returns>編集されていれば true。</returns>
    public bool IsEdited(string relativePath, TagField field)
    {
        return _edits.ContainsKey(Key(relativePath, field));
    }

    /// <summary>
    /// 指定ファイルにいずれかの編集があるかを返す。
    /// </summary>
    /// <param name="relativePath">対象ファイル。</param>
    /// <returns>編集されていれば true。</returns>
    public bool IsEdited(string relativePath)
    {
        return _edits.Keys.Any(key => key.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 編集を 1 件取り消す。
    ///
    /// **間違えて入れた 1 項目だけを捨てるための入口。** これが無いと、誤入力を消す手段が
    /// 全件破棄か、元の値を思い出して打ち直すかしか無くなる（後者は元の値を覚えていないと使えない）。
    /// </summary>
    /// <param name="relativePath">対象ファイル。</param>
    /// <param name="field">対象フィールド。</param>
    /// <returns>取り消したなら true。元から編集が無ければ false。</returns>
    public bool Remove(string relativePath, TagField field)
    {
        if (!_edits.Remove(Key(relativePath, field)))
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// 指定ファイルの編集をすべて取り消す。
    /// </summary>
    /// <param name="relativePath">対象ファイル。</param>
    /// <returns>取り消した件数。</returns>
    public int Reset(string relativePath)
    {
        (string RelativePath, TagField Field)[] keys =
        [
            .. _edits.Keys.Where(key => key.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)),
        ];

        foreach ((string _, TagField field) in keys)
        {
            _ = _edits.Remove(Key(relativePath, field));
        }

        if (keys.Length > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return keys.Length;
    }

    /// <summary>
    /// すべての編集を捨てる。
    /// </summary>
    public void Clear()
    {
        if (_edits.Count == 0)
        {
            return;
        }

        _edits.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 保留中の編集を差分にする。適用フローへはこの形で渡す。
    /// </summary>
    /// <returns>差分。パスとフィールドの順に並べる。</returns>
    public IReadOnlyList<TagChange> ToChanges()
    {
        return
        [
            .. _edits.Values
                .OrderBy(edit => edit.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edit => edit.Field)
                .Select(edit => edit.ToChange()),
        ];
    }

    /// <summary>
    /// 辞書のキーを作る。パスの大文字小文字は Windows に合わせて無視する。
    /// </summary>
    private static (string RelativePath, TagField Field) Key(string relativePath, TagField field)
    {
        return (relativePath.ToUpperInvariant(), field);
    }
}

/// <summary>
/// 手編集 1 件。
/// </summary>
/// <param name="RelativePath">対象ファイル。</param>
/// <param name="Field">対象フィールド。</param>
/// <param name="BeforeValues">編集前の値。</param>
/// <param name="Value">入力された値。空文字なら値を消す。</param>
/// <param name="Rationale">根拠。</param>
public sealed record ManualEditEntry(
    string RelativePath,
    TagField Field,
    IReadOnlyList<string> BeforeValues,
    string Value,
    string Rationale)
{
    /// <summary>値を消す編集か。</summary>
    public bool ClearsValue => Value.Length == 0;

    /// <summary>
    /// 適用フローに渡す差分にする。
    /// </summary>
    /// <returns>差分。</returns>
    public TagChange ToChange()
    {
        return new TagChange(
            RelativePath,
            Field,
            BeforeValues,
            ClearsValue ? [] : [Value],
            ManualEditConst.RULE_ID,
            Rationale,
            Severity.Manual,
            HoldReason.None,
            ClearsValue);
    }
}
