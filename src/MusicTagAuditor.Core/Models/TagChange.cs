namespace MusicTagAuditor.Core.Models;

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

    /// <summary>
    /// ✎ 人間が手で入れた値（段階 6）。
    ///
    /// ルールの重大度 3 段階とは別軸。ルールが出した案ではないので「原則違反の重さ」を持たない。
    /// エラー扱いにすると CSV や画面で「原則違反」に見えてしまうため、独立した区分にしてある。
    /// </summary>
    Manual,
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

    /// <summary>
    /// 作品エントリを同定できない（<c>HOLD_WORK_UNKNOWN</c>。docs/SPEC.md 7.4.4）。
    /// 未登録・候補が割れた・作曲家が単位内で複数のいずれか。
    /// 作品エントリを足すか <c>composer</c> を直せば再判定できる。
    /// </summary>
    WorkUnknown,

    /// <summary>
    /// <c>date</c> が未設定、または単位内で値が割れている（<c>HOLD_DATE_UNKNOWN</c>）。
    /// docs/TAGGING_POLICY.md 3.5 規則2。**最古年・最頻値のような機械的な選び方をしない。**
    /// </summary>
    DateUnknown,

    /// <summary>
    /// <c>artist</c> が単位内で一意に決まらない（<c>HOLD_ARTIST_UNKNOWN</c>）。
    /// </summary>
    ArtistUnknown,
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
/// <param name="ClearsValue">
/// 値を消すことが目的の変更か。**手編集だけが立てる**（段階 6）。
///
/// 修正案が空であること（<c>AfterValues</c> が空）は、ルールにとっては「決められなかった」を意味し、
/// 削除の指示ではない。両者は結果として同じ形になるので、意図を別のフラグで持つ。
/// タグを消すのは <c>TAGGING_POLICY.md</c> 7.4 が認める正当な操作である
/// （誤った値で埋めるより空欄のほうが後から対処できる）。
/// </param>
public sealed record TagChange(
    string RelativePath,
    TagField Field,
    IReadOnlyList<string> BeforeValues,
    IReadOnlyList<string> AfterValues,
    string RuleId,
    string Rationale,
    Severity Severity,
    HoldReason HoldReason = HoldReason.None,
    bool ClearsValue = false)
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
    /// 削除の意図は <see cref="ClearsValue"/> で表す。消すものが無ければ変更にならない。
    /// </summary>
    public bool HasFix => HoldReason == HoldReason.None
        && (ClearsValue
            ? BeforeValues.Count > 0
            : AfterValues.Count > 0 && !AfterValues.SequenceEqual(BeforeValues, StringComparer.Ordinal));

    /// <summary>表示用の現在値。</summary>
    public string BeforeText => string.Join(TrackTags.VALUE_JOIN_SEPARATOR, BeforeValues);

    /// <summary>表示用の修正後の値。値を消す変更は、空欄と区別できるよう明示する。</summary>
    public string AfterText => ClearsValue
        ? "（値を消す）"
        : HasFix ? string.Join(TrackTags.VALUE_JOIN_SEPARATOR, AfterValues) : string.Empty;

    /// <summary>
    /// 判定区分。差分プレビューの色分けに使う（docs/SPEC.md 5.3）。
    ///
    /// **<see cref="IsSelected"/> の既定値と同じ条件で判定する。**
    /// 「確定」なのにチェックが外れている、という状態を作らない。
    ///
    /// 修正値があっても ❓ のものは「要確認」になる。R-303（ファイル名からの補完）と
    /// R-403（文字化けの削除）が該当し、値は出せるが 1 件ずつ人間が見て決める必要がある。
    /// </summary>
    public string Classification => HoldReason != HoldReason.None
        ? "保留"
        : HasFix && Severity != Severity.Info ? "確定" : "要確認";
}
