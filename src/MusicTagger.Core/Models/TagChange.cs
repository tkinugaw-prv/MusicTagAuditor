namespace MusicTagger.Core.Models;

/// <summary>
/// 検出した問題の重大度（docs/SPEC.md 6章）。
/// </summary>
public enum Severity
{
    /// <summary>⛔ 原則違反が明確で、修正値も一意に決まる。</summary>
    Error,

    /// <summary>⚠ 原則違反だが、修正値の決定に文脈が要る。</summary>
    Warning,

    /// <summary>❓ 人間の確認が必要。自動修正しない。</summary>
    Info,
}

/// <summary>
/// 修正を保留する理由。
///
/// 重大度の 3 段階とは別の軸。保留は「エラーでも警告でもなく、条件が揃えば自動で再判定できる状態」を指す
/// （docs/TAGGING_POLICY.md 7.5）。
/// </summary>
public enum HoldReason
{
    /// <summary>保留していない。</summary>
    None,

    /// <summary>
    /// <c>date</c> が空欄で、収録時点の団体名を決められない（<c>HOLD_ERA_UNKNOWN</c>）。
    /// <c>date</c> が埋まった時点で自動的に再判定できる。
    /// </summary>
    EraUnknown,
}

/// <summary>
/// 1 ファイル 1 フィールドの修正案。差分プレビューの 1 行に対応する。
/// </summary>
/// <param name="RelativePath">ライブラリルートからの相対パス。</param>
/// <param name="Field">対象フィールド。</param>
/// <param name="BeforeValues">現在の値。</param>
/// <param name="AfterValues">修正後の値。修正案が無い場合は現在の値と同じ。</param>
/// <param name="RuleId">検出したルールの ID。</param>
/// <param name="Rationale">
/// 判定の根拠。**UI に必ず出す**（docs/SPEC.md 5.3）。
/// 根拠が読めない自動判定は承認できない。
/// </param>
/// <param name="Severity">重大度。</param>
/// <param name="HoldReason">保留の理由。</param>
public sealed record TagChange(
    string RelativePath,
    TagField Field,
    IReadOnlyList<string> BeforeValues,
    IReadOnlyList<string> AfterValues,
    string RuleId,
    string Rationale,
    Severity Severity,
    HoldReason HoldReason = HoldReason.None)
{
    /// <summary>利用者が明示的に切り替えたチェック状態。未設定なら既定値を使う。</summary>
    private bool? _isSelected;

    /// <summary>
    /// 適用対象にするかどうか（docs/SPEC.md 9章）。
    ///
    /// **修正値が決まっている ⛔ と ⚠ を既定でチェックする。** ❓ と保留は既定で外す。
    /// ⚠ を含めるのは、該当する R-102 / R-103 / R-104 がいずれも修正値を一意に決められるため
    /// （genre は必ず Classic、単一ディスクなら 1/1、ISO 形式からは年を抽出できる）。
    /// 修正値を決められなかったものは重大度によらずチェックしない。
    ///
    /// 既定値をプロパティ初期化子で書くと、レコードの位置指定パラメータが代入される前に
    /// 評価されてしまい常に true になる。遅延評価にして取り違えを防ぐ。
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected ?? (Severity != Severity.Info && HasFix);
        set => _isSelected = value;
    }

    /// <summary>
    /// 修正値を持つか。持たないものは一覧に出すだけで適用できない。
    /// 修正案が空なのは「決められなかった」という意味であり、削除の指示ではない。
    /// </summary>
    public bool HasFix => HoldReason == HoldReason.None
        && AfterValues.Count > 0
        && !AfterValues.SequenceEqual(BeforeValues, StringComparer.Ordinal);

    /// <summary>表示用の現在値。</summary>
    public string BeforeText => string.Join(TrackTags.VALUE_JOIN_SEPARATOR, BeforeValues);

    /// <summary>表示用の修正後の値。</summary>
    public string AfterText => HasFix ? string.Join(TrackTags.VALUE_JOIN_SEPARATOR, AfterValues) : string.Empty;

    /// <summary>
    /// 判定区分。差分プレビューの色分けに使う（docs/SPEC.md 5.3）。
    ///
    /// チェック状態と区分がずれないよう、修正値の有無で判定する。
    /// 「確定」なのにチェックが外れている、という状態を作らない。
    /// </summary>
    public string Classification => HoldReason != HoldReason.None
        ? "保留"
        : HasFix ? "確定" : "要確認";
}
