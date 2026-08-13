using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App.ViewModels;

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

    /// <summary>
    /// 一覧の並び順に使う文字列。一覧に出ている見出しと同じものにする。
    /// 画面に出ていない値で並べると、並び順の理由が読み取れない。
    /// </summary>
    string SortKey { get; }
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

    /// <inheritdoc />
    public string SortKey => Canonical;

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

    /// <inheritdoc />
    public string SortKey => Canonical;

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

    /// <inheritdoc />
    public string SortKey => DisplayName;

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
/// 作品 1 件の編集行（docs/SPEC.md 7.3.1 / 7.4）。
///
/// **作曲家は辞書の作曲家から選ばせる。** 自由入力にすると、綴りが正規形と 1 文字でも違えば
/// 索引に載らず、登録しても引けない作品が黙って増える（7.4.1）。
/// </summary>
public sealed partial class WorkRowViewModel : ObservableObject, IDictionaryRow
{
    /// <summary>この作品の作曲家。</summary>
    private string _composer;

    /// <summary>作品名。アルバム名にそのまま入る値。</summary>
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
    public WorkRowViewModel(WorkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _composer = entry.Composer;
        _canonical = entry.Canonical;
        _aliasesText = AliasText.Join(entry.Aliases);
        _aliasesJaText = AliasText.Join(entry.AliasesJa);
    }

    /// <summary>
    /// この作品の作曲家。<c>composers</c> の正規形と一致させる。
    ///
    /// **null を受けても空文字に寄せる。** 選択欄の候補が作り直されると WPF は選択なしとして
    /// null を書き込んでくる。素通しすると、以後この行を保存するたびに落ちる。
    /// </summary>
    public string Composer
    {
        get => _composer;

        set
        {
            if (SetProperty(ref _composer, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    /// <inheritdoc />
    public string SearchText => $"{Composer}\n{Canonical}\n{AliasesText}\n{AliasesJaText}";

    /// <inheritdoc />
    public string SortKey => DisplayName;

    /// <summary>一覧に出す名前。作曲家が違えば別の作品なので、作曲家も添える（7.4.1）。</summary>
    public string DisplayName => Composer.Length == 0
        ? Canonical
        : $"{Composer}: {Canonical}";

    /// <summary>
    /// 編集内容を辞書のエントリに戻す。
    /// </summary>
    /// <returns>エントリ。</returns>
    public WorkEntry ToEntry()
    {
        return new WorkEntry
        {
            Composer = Composer.Trim(),
            Canonical = Canonical.Trim(),
            Aliases = AliasText.Split(AliasesText),
            AliasesJa = AliasText.Split(AliasesJaText),
        };
    }

    /// <summary>一覧の表示を更新する。</summary>
    partial void OnCanonicalChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}

/// <summary>
/// アルバム単位の個別例外 1 件の編集行（docs/SPEC.md 7.3.1 / 7.4.5）。
///
/// **フォルダは検査結果から埋める前提**で、ここでは編集・削除のためだけに持つ（7.3.2）。
/// 手で相対パスを打つと、打ち間違えても例外が黙って効かなくなるだけで原因が画面から分からない。
/// </summary>
public sealed partial class AlbumOverrideRowViewModel : ObservableObject, IDictionaryRow
{
    /// <summary>ライブラリルートからの相対パス。</summary>
    [ObservableProperty]
    private string _folder;

    /// <summary>対象のディスク番号。空欄はそのフォルダの全ディスク（7.4.5）。</summary>
    [ObservableProperty]
    private string _discText;

    /// <summary>単位の作曲家を明示する。主作品 + カップリング（3.5 規則5）で使う。</summary>
    [ObservableProperty]
    private string _composer;

    /// <summary>作品名を明示する。版の違い（規則4）・同一演奏の別リリース（規則7）で使う。</summary>
    [ObservableProperty]
    private string _workName;

    /// <summary>アルバム名の対象外にするか。本物のコンピレーション（規則6）で使う。</summary>
    [ObservableProperty]
    private bool _exclude;

    /// <summary>例外の理由。**書く運用とする。** 理由の無い例外は後から消せない。</summary>
    [ObservableProperty]
    private string _note;

    /// <summary>
    /// 辞書のエントリから編集行を作る。
    /// </summary>
    /// <param name="entry">元のエントリ。</param>
    public AlbumOverrideRowViewModel(AlbumOverrideEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _folder = entry.Folder;
        _discText = entry.Disc?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _composer = entry.Composer ?? string.Empty;
        _workName = entry.WorkName ?? string.Empty;
        _exclude = entry.Exclude;
        _note = entry.Note ?? string.Empty;
    }

    /// <inheritdoc />
    public string SearchText => $"{Folder}\n{Composer}\n{WorkName}\n{Note}";

    /// <inheritdoc />
    public string SortKey => Folder;

    /// <summary>
    /// 編集内容を辞書のエントリに戻す。ディスク番号として読めない入力は「全ディスク」として扱う。
    /// </summary>
    /// <returns>エントリ。</returns>
    public AlbumOverrideEntry ToEntry()
    {
        return new AlbumOverrideEntry
        {
            Folder = Folder.Trim(),
            Disc = int.TryParse(DiscText.Trim(), CultureInfo.InvariantCulture, out int disc) ? disc : null,
            Composer = Blank(Composer),
            WorkName = Blank(WorkName),
            Exclude = Exclude,
            Note = Blank(Note),
        };
    }

    /// <summary>
    /// 空欄を null にする。JSON に空文字を書くと「指定した」ように見える。
    /// </summary>
    private static string? Blank(string value)
    {
        return value.Trim().Length == 0 ? null : value.Trim();
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

    /// <inheritdoc />
    public string SortKey => Pattern;

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

    /// <inheritdoc />
    public string SortKey => Value;
}
