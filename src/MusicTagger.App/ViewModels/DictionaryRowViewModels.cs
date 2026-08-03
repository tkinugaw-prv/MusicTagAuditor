using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagger.Core.Dictionary;

namespace MusicTagger.App.ViewModels;

/// <summary>
/// 別名の一覧と複数行テキストを相互変換する。
///
/// 区切りに改行を使うのは、<c>;</c> を値の区切りに使わないため（docs/TAGGING_POLICY.md 3.4）。
/// 読点やスラッシュは団体名や配役の値そのものに現れるので、これも使えない。
/// </summary>
public static class AliasText
{
    /// <summary>
    /// 複数行テキストを別名の一覧にする。空行は落とす。
    /// </summary>
    /// <param name="text">複数行テキスト。</param>
    /// <returns>別名の一覧。</returns>
    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return
        [
            .. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// 別名の一覧を複数行テキストにする。
    /// </summary>
    /// <param name="values">別名の一覧。</param>
    /// <returns>複数行テキスト。</returns>
    public static string Join(IReadOnlyList<string>? values)
    {
        return string.Join(Environment.NewLine, values ?? []);
    }
}

/// <summary>
/// 辞書タブの一覧で絞り込める編集行。
/// </summary>
public interface IDictionaryRow
{
    /// <summary>絞り込みの対象になる文字列をまとめたもの。</summary>
    string SearchText { get; }
}

/// <summary>
/// 作曲家 1 件の編集行。
/// </summary>
public sealed partial class ComposerRowViewModel : ObservableObject, IDictionaryRow
{
    /// <summary>正規形。</summary>
    [ObservableProperty]
    private string _canonical;

    /// <summary>ラテン文字の別名。1 行 1 件。</summary>
    [ObservableProperty]
    private string _aliasesText;

    /// <summary>日本語表記。1 行 1 件。</summary>
    [ObservableProperty]
    private string _aliasesJaText;

    /// <summary>
    /// 辞書のエントリから編集行を作る。
    /// </summary>
    /// <param name="entry">元のエントリ。</param>
    public ComposerRowViewModel(ComposerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _canonical = entry.Canonical;
        _aliasesText = AliasText.Join(entry.Aliases);
        _aliasesJaText = AliasText.Join(entry.AliasesJa);
    }

    /// <inheritdoc />
    public string SearchText => $"{Canonical}\n{AliasesText}\n{AliasesJaText}";

    /// <summary>
    /// 編集内容を辞書のエントリに戻す。
    /// </summary>
    /// <returns>エントリ。</returns>
    public ComposerEntry ToEntry()
    {
        return new ComposerEntry
        {
            Canonical = Canonical.Trim(),
            Aliases = AliasText.Split(AliasesText),
            AliasesJa = AliasText.Split(AliasesJaText),
        };
    }
}

/// <summary>
/// 人物 1 件の編集行。
/// </summary>
public sealed partial class PersonRowViewModel : ObservableObject, IDictionaryRow
{
    /// <summary>正規形。</summary>
    [ObservableProperty]
    private string _canonical;

    /// <summary>指揮者か。</summary>
    [ObservableProperty]
    private bool _isConductor;

    /// <summary>ソリストか（docs/TAGGING_POLICY.md 2.2）。</summary>
    [ObservableProperty]
    private bool _isSoloist;

    /// <summary>ラテン文字の別名。1 行 1 件。</summary>
    [ObservableProperty]
    private string _aliasesText;

    /// <summary>日本語表記。1 行 1 件。</summary>
    [ObservableProperty]
    private string _aliasesJaText;

    /// <summary>
    /// 辞書のエントリから編集行を作る。
    /// </summary>
    /// <param name="entry">元のエントリ。</param>
    public PersonRowViewModel(PersonEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _canonical = entry.Canonical;
        _isConductor = DictionaryIndex.HasRole(entry, PersonRole.Conductor);
        _isSoloist = DictionaryIndex.HasRole(entry, PersonRole.Soloist);
        _aliasesText = AliasText.Join(entry.Aliases);
        _aliasesJaText = AliasText.Join(entry.AliasesJa);
    }

    /// <inheritdoc />
    public string SearchText => $"{Canonical}\n{AliasesText}\n{AliasesJaText}";

    /// <summary>役割の表示用文字列。</summary>
    public string RolesText => string.Join(" / ", BuildRoles().Select(role => role switch
    {
        nameof(PersonRole.Conductor) => "指揮者",
        _ => "ソリスト",
    }));

    /// <summary>
    /// 編集内容を辞書のエントリに戻す。
    /// </summary>
    /// <returns>エントリ。</returns>
    public PersonEntry ToEntry()
    {
        return new PersonEntry
        {
            Canonical = Canonical.Trim(),
            Roles = BuildRoles(),
            Aliases = AliasText.Split(AliasesText),
            AliasesJa = AliasText.Split(AliasesJaText),
        };
    }

    /// <summary>役割の表示を更新する。</summary>
    partial void OnIsConductorChanged(bool value)
    {
        OnPropertyChanged(nameof(RolesText));
    }

    /// <summary>役割の表示を更新する。</summary>
    partial void OnIsSoloistChanged(bool value)
    {
        OnPropertyChanged(nameof(RolesText));
    }

    /// <summary>
    /// チェック状態から役割の一覧を組み立てる。
    /// </summary>
    private IReadOnlyList<string> BuildRoles()
    {
        List<string> roles = [];

        if (IsConductor)
        {
            roles.Add(nameof(PersonRole.Conductor));
        }

        if (IsSoloist)
        {
            roles.Add(nameof(PersonRole.Soloist));
        }

        return roles;
    }
}

/// <summary>
/// 団体の時代区分 1 件の編集行（docs/TAGGING_POLICY.md 5.3.1）。
///
/// 年を文字列で持つのは、空欄（＝上限／下限なし）を表せるようにするため。
/// </summary>
public sealed partial class EnsembleEraRowViewModel : ObservableObject
{
    /// <summary>この年以降。空欄なら下限なし。</summary>
    [ObservableProperty]
    private string _fromText;

    /// <summary>この年より前。空欄なら上限なし。</summary>
    [ObservableProperty]
    private string _untilText;

    /// <summary>その期間の正規形。</summary>
    [ObservableProperty]
    private string _canonical;

    /// <summary>
    /// 時代区分から編集行を作る。
    /// </summary>
    /// <param name="era">元の時代区分。</param>
    public EnsembleEraRowViewModel(EnsembleEra era)
    {
        ArgumentNullException.ThrowIfNull(era);

        _fromText = era.From?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _untilText = era.Until?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _canonical = era.Canonical;
    }

    /// <summary>
    /// 編集内容を時代区分に戻す。年として読めない入力は「指定なし」として扱う。
    /// </summary>
    /// <returns>時代区分。</returns>
    public EnsembleEra ToEra()
    {
        return new EnsembleEra
        {
            From = ParseYear(FromText),
            Until = ParseYear(UntilText),
            Canonical = Canonical.Trim(),
        };
    }

    /// <summary>
    /// 年を読み取る。
    /// </summary>
    private static int? ParseYear(string text)
    {
        return int.TryParse(text.Trim(), CultureInfo.InvariantCulture, out int year) ? year : null;
    }
}

/// <summary>
/// 演奏団体 1 件の編集行。
/// </summary>
public sealed partial class EnsembleRowViewModel : ObservableObject, IDictionaryRow
{
    /// <summary>実体 ID。**同一性はこれで判断する**（docs/TAGGING_POLICY.md 5.3.1）。</summary>
    [ObservableProperty]
    private string _entityId;

    /// <summary>時代分割しない場合の正規形。</summary>
    [ObservableProperty]
    private string _canonical;

    /// <summary>時代分割を行わない個別例外か（5.3.2）。</summary>
    [ObservableProperty]
    private bool _noEraSplit;

    /// <summary>ラテン文字の別名。1 行 1 件。</summary>
    [ObservableProperty]
    private string _aliasesText;

    /// <summary>日本語表記。1 行 1 件。</summary>
    [ObservableProperty]
    private string _aliasesJaText;

    /// <summary>
    /// 辞書のエントリから編集行を作る。
    /// </summary>
    /// <param name="entry">元のエントリ。</param>
    public EnsembleRowViewModel(EnsembleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entityId = entry.EntityId;
        _canonical = entry.Canonical ?? string.Empty;
        _noEraSplit = entry.NoEraSplit;
        _aliasesText = AliasText.Join(entry.Aliases);
        _aliasesJaText = AliasText.Join(entry.AliasesJa);

        foreach (EnsembleEra era in entry.Eras ?? [])
        {
            Eras.Add(new EnsembleEraRowViewModel(era));
        }
    }

    /// <summary>時代区分。</summary>
    public ObservableCollection<EnsembleEraRowViewModel> Eras { get; } = [];

    /// <summary>一覧に出す名前。時代分割エントリは最初の区分名を出す。</summary>
    public string DisplayName => Canonical.Length > 0
        ? Canonical
        : Eras.FirstOrDefault()?.Canonical ?? EntityId;

    /// <inheritdoc />
    public string SearchText =>
        $"{EntityId}\n{Canonical}\n{AliasesText}\n{AliasesJaText}\n"
        + string.Join('\n', Eras.Select(era => era.Canonical));

    /// <summary>
    /// 編集内容を辞書のエントリに戻す。
    /// </summary>
    /// <returns>エントリ。</returns>
    public EnsembleEntry ToEntry()
    {
        return new EnsembleEntry
        {
            EntityId = EntityId.Trim(),
            Canonical = Canonical.Trim().Length == 0 ? null : Canonical.Trim(),
            NoEraSplit = NoEraSplit,
            Eras = [.. Eras.Select(era => era.ToEra())],
            Aliases = AliasText.Split(AliasesText),
            AliasesJa = AliasText.Split(AliasesJaText),
        };
    }

    /// <summary>一覧の表示を更新する。</summary>
    partial void OnCanonicalChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>一覧の表示を更新する。</summary>
    partial void OnEntityIdChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}

/// <summary>
/// 楽語の誤記 1 件の編集行（docs/TAGGING_POLICY.md 5.4）。
/// </summary>
public sealed partial class TypoRowViewModel : ObservableObject, IDictionaryRow
{
    /// <summary>検出する正規表現。</summary>
    [ObservableProperty]
    private string _pattern;

    /// <summary>置換後の文字列。</summary>
    [ObservableProperty]
    private string _replacement;

    /// <summary>由来のメモ。</summary>
    [ObservableProperty]
    private string _note;

    /// <summary>
    /// 辞書のエントリから編集行を作る。
    /// </summary>
    /// <param name="entry">元のエントリ。</param>
    public TypoRowViewModel(TypoEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _pattern = entry.Pattern;
        _replacement = entry.Replacement;
        _note = entry.Note ?? string.Empty;
    }

    /// <inheritdoc />
    public string SearchText => $"{Pattern}\n{Replacement}\n{Note}";

    /// <summary>正規表現として妥当か。保存前に画面で分かるようにする（docs/SPEC.md 7.3）。</summary>
    public bool IsValidPattern => DictionaryIndex.IsValidPattern(Pattern);

    /// <summary>妥当性の表示。</summary>
    public string ValidityMark => IsValidPattern ? "OK" : "不正";

    /// <summary>
    /// 編集内容を辞書のエントリに戻す。
    /// </summary>
    /// <returns>エントリ。</returns>
    public TypoEntry ToEntry()
    {
        return new TypoEntry
        {
            Pattern = Pattern.Trim(),
            Replacement = Replacement,
            Note = Note.Trim().Length == 0 ? null : Note.Trim(),
        };
    }

    /// <summary>妥当性の表示を更新する。</summary>
    partial void OnPatternChanged(string value)
    {
        OnPropertyChanged(nameof(IsValidPattern));
        OnPropertyChanged(nameof(ValidityMark));
    }
}

/// <summary>
/// 保護対象の <c>albumartist</c> 1 件（docs/TAGGING_POLICY.md 2.3）。
/// </summary>
public sealed partial class ProtectedValueRowViewModel : ObservableObject, IDictionaryRow
{
    /// <summary>保護する値。</summary>
    [ObservableProperty]
    private string _value;

    /// <summary>
    /// 値から編集行を作る。
    /// </summary>
    /// <param name="value">保護する値。</param>
    public ProtectedValueRowViewModel(string value)
    {
        _value = value;
    }

    /// <inheritdoc />
    public string SearchText => Value;
}
