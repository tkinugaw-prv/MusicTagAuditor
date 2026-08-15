using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Inspection;

/// <summary>
/// <see cref="InspectionConst"/> のテスト。
///
/// 全フィールドを走査するルールはこの一覧を回す。分類の漏れをここで機械的に検出する
/// （フィールドを足した人が <c>FREE_TEXT_FIELDS</c> の判断を忘れても、
/// 検査対象に入るという安全側の既定になっていることを確かめる）。
/// </summary>
public sealed class InspectionConstTests
{
    /// <summary>
    /// 検査対象に自由記述のフィールドが混ざらないことを確認する。
    /// </summary>
    [Fact]
    public void ExcludesFreeTextFields()
    {
        Assert.DoesNotContain(TagField.Comment, InspectionConst.INSPECTED_FIELDS);
    }

    /// <summary>
    /// 検査対象と自由記述で全フィールドを覆っていることを確認する。
    /// **どちらにも入らないフィールドがあれば、それは分類の取りこぼしである。**
    /// </summary>
    [Fact]
    public void CoversEveryTagField()
    {
        foreach (TagField field in Enum.GetValues<TagField>())
        {
            Assert.True(
                InspectionConst.INSPECTED_FIELDS.Contains(field) || TagFieldConst.IsFreeText(field),
                $"{field} がどちらにも分類されていません。");
        }
    }

    /// <summary>
    /// 検査対象が重複しないことを確認する。
    /// </summary>
    [Fact]
    public void HasNoDuplicates()
    {
        Assert.Equal(InspectionConst.INSPECTED_FIELDS.Count, InspectionConst.INSPECTED_FIELDS.Distinct().Count());
    }
}
