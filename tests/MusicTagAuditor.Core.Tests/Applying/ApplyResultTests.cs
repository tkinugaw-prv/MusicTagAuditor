using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Applying;

/// <summary>
/// <see cref="ApplyResult.GetSucceededFields"/> のテスト。
///
/// 適用後に検査結果タブから取り除いてよい項目（＝完全に成功した項目）を正しく判定できることを確認する。
/// </summary>
public sealed class ApplyResultTests
{
    /// <summary>
    /// 失敗・不一致・競合が無ければ、対象すべてが成功として返ることを確認する。
    /// </summary>
    [Fact]
    public void ReturnsAllTargetsWhenNothingFailed()
    {
        TagChange[] targets =
        [
            Change("01.m4a", TagField.Genre),
            Change("02.m4a", TagField.Composer),
        ];

        ApplyResult result = BuildResult();

        IReadOnlySet<TagChangeKey> succeeded = result.GetSucceededFields(targets);

        Assert.Equal(2, succeeded.Count);
        Assert.Contains(TagChangeKey.Of("01.m4a", TagField.Genre), succeeded);
        Assert.Contains(TagChangeKey.Of("02.m4a", TagField.Composer), succeeded);
    }

    /// <summary>
    /// 書き込みに失敗したファイルは、そのファイルの全フィールドが除外されることを確認する。
    /// </summary>
    [Fact]
    public void ExcludesAllFieldsOfFailedFile()
    {
        TagChange[] targets =
        [
            Change("壊れた.m4a", TagField.Genre),
            Change("壊れた.m4a", TagField.Composer),
            Change("02.m4a", TagField.Genre),
        ];

        ApplyResult result = BuildResult(failures: [new ApplyFailure("壊れた.m4a", "書き込み失敗")]);

        IReadOnlySet<TagChangeKey> succeeded = result.GetSucceededFields(targets);

        Assert.Equal([TagChangeKey.Of("02.m4a", TagField.Genre)], succeeded);
    }

    /// <summary>
    /// 読み戻し不一致になった組だけ除外され、同じファイルの他フィールドは成功のまま残ることを確認する。
    /// </summary>
    [Fact]
    public void ExcludesOnlyMismatchedFieldNotWholeFile()
    {
        TagChange[] targets =
        [
            Change("01.m4a", TagField.AlbumArtist),
            Change("01.m4a", TagField.Genre),
        ];

        ApplyResult result = BuildResult(
            mismatches: [new VerificationMismatch("01.m4a", TagField.AlbumArtist, ["A"], ["B"])]);

        IReadOnlySet<TagChangeKey> succeeded = result.GetSucceededFields(targets);

        Assert.Equal([TagChangeKey.Of("01.m4a", TagField.Genre)], succeeded);
    }

    /// <summary>
    /// 競合になった組は、手編集で解消済み（<see cref="ApplyConflict.IsResolved"/>）でも
    /// 成功に含めないことを確認する。捨てた案があったことを利用者が確認できるよう検査結果に残すため。
    /// </summary>
    [Fact]
    public void ExcludesResolvedConflicts()
    {
        TagChange[] targets =
        [
            Change("01.m4a", TagField.Conductor),
            Change("01.m4a", TagField.Genre),
        ];

        ApplyResult result = BuildResult(
            conflicts:
            [
                new ApplyConflict(
                    "01.m4a",
                    TagField.Conductor,
                    [("R-202", "A"), (ManualEditConst.RULE_ID, "B")],
                    AdoptedValue: "B"),
            ]);

        IReadOnlySet<TagChangeKey> succeeded = result.GetSucceededFields(targets);

        Assert.Equal([TagChangeKey.Of("01.m4a", TagField.Genre)], succeeded);
    }

    /// <summary>
    /// 相対パスの大文字小文字違いを同一視することを確認する（Windows のパスに合わせる）。
    /// </summary>
    [Fact]
    public void TreatsRelativePathCaseInsensitively()
    {
        TagChange[] targets = [Change("Folder/01.m4a", TagField.Genre)];

        ApplyResult result = BuildResult(failures: [new ApplyFailure("FOLDER/01.M4A", "書き込み失敗")]);

        IReadOnlySet<TagChangeKey> succeeded = result.GetSucceededFields(targets);

        Assert.Empty(succeeded);
    }

    /// <summary>
    /// <c>targets</c> に含まれない組は、結果に現れないことを確認する。
    /// </summary>
    [Fact]
    public void OnlyConsidersGivenTargets()
    {
        TagChange[] targets = [Change("01.m4a", TagField.Genre)];

        ApplyResult result = BuildResult();

        IReadOnlySet<TagChangeKey> succeeded = result.GetSucceededFields(targets);

        Assert.DoesNotContain(TagChangeKey.Of("02.m4a", TagField.Composer), succeeded);
    }

    /// <summary>
    /// テスト用の修正案を作る。
    /// </summary>
    private static TagChange Change(string relativePath, TagField field)
    {
        return new TagChange(relativePath, field, [], ["変更後"], "R-000", "テスト用", Severity.Error);
    }

    /// <summary>
    /// テスト用の適用結果を作る。
    /// </summary>
    private static ApplyResult BuildResult(
        IReadOnlyList<ApplyFailure>? failures = null,
        IReadOnlyList<VerificationMismatch>? mismatches = null,
        IReadOnlyList<ApplyConflict>? conflicts = null)
    {
        return new ApplyResult(
            "backup",
            AttemptedFiles: 1,
            SucceededFiles: 1,
            AppliedChanges: 1,
            failures ?? [],
            mismatches ?? [],
            conflicts ?? []);
    }
}
