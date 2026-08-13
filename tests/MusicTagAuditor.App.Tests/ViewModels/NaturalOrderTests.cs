using MusicTagAuditor.App.ViewModels;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 自然順比較のテスト。
///
/// 素の文字列比較だと <c>Symphony No. 10</c> が <c>No. 4</c> より前に来る。
/// **作品名は番号で呼ぶものが大半**なので、それでは辞書の一覧を目で追えない。
/// </summary>
public sealed class NaturalOrderTests
{
    /// <summary>
    /// 番号が数として並ぶことを確認する。文字列比較なら 10, 11, 12, 15, 4, 5, 6 になる。
    /// </summary>
    [Fact]
    public void 作品番号は数として並ぶ()
    {
        string[] works =
        [
            "Dmitri Shostakovich: Symphony No. 10",
            "Dmitri Shostakovich: Symphony No. 4",
            "Dmitri Shostakovich: Symphony No. 15",
            "Dmitri Shostakovich: Symphony No. 5",
            "Dmitri Shostakovich: Symphony No. 6",
        ];

        string[] sorted = [.. works.Order(Comparer<string>.Create(NaturalOrder.Compare))];

        Assert.Equal(
            [
                "Dmitri Shostakovich: Symphony No. 4",
                "Dmitri Shostakovich: Symphony No. 5",
                "Dmitri Shostakovich: Symphony No. 6",
                "Dmitri Shostakovich: Symphony No. 10",
                "Dmitri Shostakovich: Symphony No. 15",
            ],
            sorted);
    }

    /// <summary>
    /// 作曲家が違えば作品番号より先に作曲家で分かれることを確認する。
    /// </summary>
    [Fact]
    public void 作曲家が違えば作曲家で分かれる()
    {
        Assert.True(NaturalOrder.Compare("Anton Bruckner: Symphony No. 9", "Dmitri Shostakovich: Symphony No. 1") < 0);
    }

    /// <summary>
    /// 数字を含まない名前は、今までどおりカルチャの文字列比較で並ぶことを確認する。
    ///
    /// **アクセント付きの名前が末尾に飛ばないこと。** 序数比較では <c>András</c> が
    /// すべての ASCII 名の後ろに回り、重複を見つけるという目的に合わない。
    /// </summary>
    [Fact]
    public void アクセント付きの名前は素の位置に並ぶ()
    {
        Assert.True(NaturalOrder.Compare("Alicia de Larrocha", "András Schiff") < 0);
        Assert.True(NaturalOrder.Compare("András Schiff", "Anton Nanut") < 0);
        Assert.True(NaturalOrder.Compare("Günter Wand", "Hans Knappertsbusch") < 0);
    }

    /// <summary>
    /// 桁数の多い数（年号・作品番号）でも破綻しないことを確認する。
    /// </summary>
    [Fact]
    public void 桁数が多くても数として比べる()
    {
        Assert.True(NaturalOrder.Compare("Op. 9", "Op. 132") < 0);
        Assert.True(NaturalOrder.Compare("1812", "20000") < 0);

        // long に収まらない桁数でも数として扱えること（数値変換していない証拠）。
        Assert.True(NaturalOrder.Compare("99999999999999999999", "100000000000000000000") < 0);
    }

    /// <summary>
    /// 0 詰めの違いを吸収しつつ、並びが揺れないことを確認する。
    /// </summary>
    [Fact]
    public void ゼロ詰めは数として同じに扱う()
    {
        Assert.True(NaturalOrder.Compare("Disc 04", "Disc 5") < 0);

        // 数として同じなら 0 詰めの多いほうを後ろに置く。0 を返すと並びが実行ごとに揺れうる。
        Assert.True(NaturalOrder.Compare("Disc 4", "Disc 04") < 0);
    }

    /// <summary>
    /// 数字で始まる名前と文字で始まる名前が混ざっても順序が決まることを確認する。
    /// </summary>
    [Fact]
    public void 数字始まりと文字始まりが混ざっても並ぶ()
    {
        Assert.True(NaturalOrder.Compare("1812 Overture", "Also sprach Zarathustra") < 0);
    }

    /// <summary>
    /// 空文字と null を落ちずに扱えることを確認する。行の途中の値は空になりうる。
    /// </summary>
    [Fact]
    public void 空の値でも落ちない()
    {
        Assert.Equal(0, NaturalOrder.Compare(null, null));
        Assert.Equal(0, NaturalOrder.Compare("", null));
        Assert.True(NaturalOrder.Compare(null, "Anton Bruckner") < 0);
        Assert.True(NaturalOrder.Compare("Anton Bruckner", "") > 0);
    }

    /// <summary>
    /// 同じ文字列は 0 を返すことを確認する。
    /// </summary>
    [Fact]
    public void 同じ文字列は等しい()
    {
        Assert.Equal(0, NaturalOrder.Compare("Symphony No. 9", "Symphony No. 9"));
    }
}
