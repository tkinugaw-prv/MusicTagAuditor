using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using MusicTagAuditor.App.Controls;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App.Tests.Controls;

/// <summary>
/// 候補の確定に関するキー操作のテスト（docs/SPEC.md 5.2）。
///
/// 自由記述の欄（<see cref="SuggestBox.RequiresExplicitSelection"/>）では、打った値を
/// 選んでいない候補で置き換えない。キーは実際のルーティングイベントとして流す。
/// </summary>
public sealed class SuggestBoxTests
{
    /// <summary>候補一覧の部品だけを持つテンプレート。テーマに依存させない。</summary>
    private const string TEMPLATE = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:ctl="clr-namespace:MusicTagAuditor.App.Controls;assembly=MusicTagAuditor.App"
                         TargetType="ctl:SuggestBox">
          <Grid>
            <ScrollViewer x:Name="PART_ContentHost" />
            <Popup x:Name="PART_Popup"><ListBox x:Name="PART_Suggestions" /></Popup>
          </Grid>
        </ControlTemplate>
        """;

    /// <summary>未選択の Enter は打った値を残し、キーも食べないことを確認する。</summary>
    [Fact]
    public void 未選択のEnterは打った値を残して通す()
    {
        Run(harness =>
        {
            harness.Open("Adagio");

            bool handled = harness.Press(Key.Enter);

            Assert.False(handled);
            Assert.Equal("Adagio", harness.Box.Text);
        });
    }

    /// <summary>未選択の Tab は打った値を残すことを確認する。</summary>
    [Fact]
    public void 未選択のTabは打った値を残す()
    {
        Run(harness =>
        {
            harness.Open("Adagio");

            bool handled = harness.Press(Key.Tab);

            Assert.False(handled);
            Assert.Equal("Adagio", harness.Box.Text);
        });
    }

    /// <summary>↓ で選ぶと Enter で正規形が入り、1 回目のキーはセルへ渡さないことを確認する。</summary>
    [Fact]
    public void 下キーで選ぶとEnterで正規形が入る()
    {
        Run(harness =>
        {
            harness.Open("Adagio");

            _ = harness.Press(Key.Down);
            bool handled = harness.Press(Key.Enter);

            Assert.True(handled);
            Assert.Equal("Adagio for Strings", harness.Box.Text);
        });
    }

    /// <summary>↑ で選んでも Tab で正規形が入ることを確認する。</summary>
    [Fact]
    public void 上キーで選ぶとTabで正規形が入る()
    {
        Run(harness =>
        {
            harness.Open("Adagio");

            _ = harness.Press(Key.Up);
            bool handled = harness.Press(Key.Tab);

            Assert.False(handled);
            Assert.Equal("Adagio for Strings", harness.Box.Text);
        });
    }

    /// <summary>候補が複数あるとき、選んだ行の正規形が入ることを確認する。</summary>
    /// <param name="downCount">↓ を押す回数。</param>
    /// <param name="upCount">そのあと ↑ を押す回数。</param>
    /// <param name="expected">入る値。</param>
    [Theory]
    [InlineData(2, 0, "Adagio in G minor")]
    [InlineData(2, 1, "Adagio for Strings")]
    [InlineData(5, 0, "Adagio in G minor")]
    [InlineData(1, 5, "Adagio for Strings")]
    public void 複数の候補から選んだ行の正規形が入る(int downCount, int upCount, string expected)
    {
        Run(harness =>
        {
            harness.Open("Adagio");

            for (int count = 0; count < downCount; count++)
            {
                _ = harness.Press(Key.Down);
            }

            for (int count = 0; count < upCount; count++)
            {
                _ = harness.Press(Key.Up);
            }

            bool handled = harness.Press(Key.Enter);

            Assert.True(handled);
            Assert.Equal(expected, harness.Box.Text);
        });
    }

    /// <summary>↓ で 2 件目を選んだ状態の Tab でも、その候補が入ることを確認する。</summary>
    [Fact]
    public void 二件目を選んだTabでその候補が入る()
    {
        Run(harness =>
        {
            harness.Open("Adagio");

            _ = harness.Press(Key.Down);
            _ = harness.Press(Key.Down);
            bool handled = harness.Press(Key.Tab);

            Assert.False(handled);
            Assert.Equal("Adagio in G minor", harness.Box.Text);
        });
    }

    /// <summary>既定のモードは先頭を選んで開く従来の動作のままであることを確認する。</summary>
    [Fact]
    public void 既定のモードはEnterで先頭の候補が入る()
    {
        Run(harness =>
        {
            harness.Box.RequiresExplicitSelection = false;
            harness.Open("Adagio");

            bool handled = harness.Press(Key.Enter);

            Assert.True(handled);
            Assert.Equal("Adagio for Strings", harness.Box.Text);
        });
    }

    /// <summary>道具を組み立てて試験を流す。</summary>
    private static void Run(Action<Harness> test)
    {
        DispatcherTestRunner.Run(() =>
        {
            Harness harness = new();

            try
            {
                test(harness);
            }
            finally
            {
                harness.Close();
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 入力欄を画面外に立て、キーを流す道具。
    /// </summary>
    private sealed class Harness
    {
        /// <summary>組み立てたウィンドウ。</summary>
        private readonly Window _window;

        /// <summary>
        /// 自由記述の設定で入力欄を立てる。
        /// </summary>
        public Harness()
        {
            Box = new SuggestBox
            {
                Template = (ControlTemplate)XamlReader.Parse(TEMPLATE),
                RequiresExplicitSelection = true,
                Suggestions = [new SuggestionEntry("Adagio for Strings", ["Adagio for Strings"]), new SuggestionEntry("Adagio in G minor", ["Adagio in G minor"])],
            };

            _window = new Window { Content = Box, Width = 400, Height = 200, Left = -5000, Top = -5000 };
            _window.Show();
            Box.UpdateLayout();
        }

        /// <summary>入力欄。</summary>
        public SuggestBox Box { get; }

        /// <summary>
        /// 値を入れて候補を開く。
        ///
        /// 打鍵で開く経路はキーボードフォーカスを要し、画面外のウィンドウでは当てにならないので ↓ で開く。
        /// </summary>
        /// <param name="text">打った値。</param>
        public void Open(string text)
        {
            Box.Text = text;

            Assert.True(Press(Key.Down), "候補が開かなかった");
        }

        /// <summary>
        /// キーを 1 つ流す。
        /// </summary>
        /// <param name="key">押すキー。</param>
        /// <returns>入力欄がキーを処理したら true。</returns>
        public bool Press(Key key)
        {
            KeyEventArgs args = new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(Box), 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };

            Box.RaiseEvent(args);

            return args.Handled;
        }

        /// <summary>後片付け。</summary>
        public void Close()
        {
            _window.Close();
        }
    }
}
