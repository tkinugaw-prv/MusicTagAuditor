using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using MusicTagAuditor.App.Controls;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.Tests.Controls;

/// <summary>
/// セル編集の入力が保留中の手編集に入ることのテスト（docs/SPEC.md 5.2）。
///
/// **DataGrid はセルを 1 つ確定しただけでは値を書き戻さない。** 同じ行の中で別のセルへ
/// 移っただけでは、打った値がどこにも現れないまま残る。セルに見えている値と下段の保留が
/// 食い違うと、どちらが本当なのか確かめる手立てが無くなる。
///
/// 実際の DataGrid を立てて、利用者の操作（別のセルへ移る）と同じ経路で確かめる。
/// 列の作りは <c>MainWindow.xaml</c> のファイル一覧に合わせる。候補付きの 4 列は
/// <c>DataGridTemplateColumn</c> で、ここが最も落ちやすい（<see cref="CellEditCommit"/>）。
/// </summary>
public sealed class CellEditCommitTests
{
    /// <summary>候補付きの列（DataGridTemplateColumn + SuggestBox）の編集テンプレート。</summary>
    private const string COMPOSER_TEMPLATE = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:ctl="clr-namespace:MusicTagAuditor.App.Controls;assembly=MusicTagAuditor.App">
          <ctl:SuggestBox Text="{Binding Composer, UpdateSourceTrigger=LostFocus}" />
        </DataTemplate>
        """;

    /// <summary>
    /// 文字列の列（<c>DataGridTextColumn</c>）で、同じ行の別セルへ移っただけでも保留に入ることを確認する。
    /// </summary>
    [Fact]
    public void 文字列の列は別セルへ移った時点で保留に入る()
    {
        DispatcherTestRunner.Run(() =>
        {
            Harness harness = new();

            harness.Edit(harness.TitleColumn, "打ったタイトル");

            Assert.Equal("打ったタイトル", harness.Row.Title);
            Assert.Equal(1, harness.Edits.Count);

            harness.Close();

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 候補付きの列（<c>DataGridTemplateColumn</c>）でも同じであることを確認する。
    ///
    /// この列は <c>UpdateSourceTrigger=LostFocus</c> しか書き戻す手立てが無く、
    /// 別のセルをクリックして抜けると編集テンプレートのほうが先に外れて入力が消える。
    /// </summary>
    [Fact]
    public void 候補付きの列も別セルへ移った時点で保留に入る()
    {
        DispatcherTestRunner.Run(() =>
        {
            Harness harness = new();

            harness.Edit(harness.ComposerColumn, "打った作曲家");

            Assert.Equal("打った作曲家", harness.Row.Composer);
            Assert.Equal(1, harness.Edits.Count);

            harness.Close();

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 取り消し（Esc）では保留に入らないことを確認する。
    /// 捨てるつもりの入力が残ると、取り消しの意味が反転する。
    /// </summary>
    [Fact]
    public void 取り消した編集は保留に入らない()
    {
        DispatcherTestRunner.Run(() =>
        {
            Harness harness = new();

            harness.Edit(harness.TitleColumn, "打ったタイトル", cancel: true);

            Assert.Null(harness.Row.Title);
            Assert.Equal(0, harness.Edits.Count);

            harness.Close();

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// ファイル一覧と同じ作りの DataGrid を立てて、セル編集を再現する道具。
    /// </summary>
    private sealed class Harness
    {
        /// <summary>組み立てたウィンドウ。</summary>
        private readonly Window _window;

        /// <summary>組み立てた一覧。</summary>
        private readonly DataGrid _grid;

        /// <summary>
        /// ファイル一覧と同じ列構成の DataGrid を画面外に立てる。
        /// </summary>
        public Harness()
        {
            Row = new TrackRowViewModel(Track("01.m4a"), Edits);

            TitleColumn = new DataGridTextColumn { Header = "タイトル", Binding = new Binding("Title") };
            ComposerColumn = new DataGridTemplateColumn
            {
                Header = "作曲家",
                CellEditingTemplate = (DataTemplate)XamlReader.Parse(COMPOSER_TEMPLATE),
            };

            _grid = new DataGrid { AutoGenerateColumns = false };
            _grid.Columns.Add(TitleColumn);
            _grid.Columns.Add(ComposerColumn);

            // MainWindow.OnTrackCellEditEnding と同じ扱いにする。
            _grid.CellEditEnding += (_, e) =>
            {
                if (e.EditAction == DataGridEditAction.Commit)
                {
                    CellEditCommit.Flush(e.EditingElement);
                }
            };

            _grid.ItemsSource = new List<TrackRowViewModel> { Row };

            _window = new Window { Content = _grid, Width = 400, Height = 200, Left = -5000, Top = -5000 };
            _window.Show();
            _grid.UpdateLayout();
        }

        /// <summary>保留中の手編集。</summary>
        public ManualEditSet Edits { get; } = new();

        /// <summary>編集する行。</summary>
        public TrackRowViewModel Row { get; }

        /// <summary>文字列の列。</summary>
        public DataGridTextColumn TitleColumn { get; }

        /// <summary>候補付きの列。</summary>
        public DataGridTemplateColumn ComposerColumn { get; }

        /// <summary>
        /// セルを開いて打ち、**同じ行の別のセルへ移って**抜ける。
        /// </summary>
        /// <param name="column">編集する列。</param>
        /// <param name="text">打つ値。</param>
        /// <param name="cancel">取り消して抜けるなら true。</param>
        public void Edit(DataGridColumn column, string text, bool cancel = false)
        {
            _grid.CurrentCell = new DataGridCellInfo(Row, column);

            Assert.True(_grid.BeginEdit(), "セルの編集を開けなかった");

            TextBox box = FindEditingBox() ?? throw new InvalidOperationException("編集中の入力欄が見つからない");
            box.Text = text;

            if (cancel)
            {
                _ = _grid.CancelEdit(DataGridEditingUnit.Cell);
                return;
            }

            DataGridColumn other = ReferenceEquals(column, TitleColumn) ? ComposerColumn : TitleColumn;

            _grid.CurrentCell = new DataGridCellInfo(Row, other);
            _grid.UpdateLayout();
        }

        /// <summary>後片付け。</summary>
        public void Close()
        {
            _window.Close();
        }

        /// <summary>編集中のセルにある入力欄を探す。</summary>
        private TextBox? FindEditingBox()
        {
            return FindAll<DataGridCell>(_grid)
                .Where(cell => cell.IsEditing)
                .Select(cell => FindAll<TextBox>(cell).FirstOrDefault())
                .FirstOrDefault(box => box is not null);
        }

        /// <summary>ビジュアルツリーから T をすべて集める。</summary>
        private static IEnumerable<T> FindAll<T>(DependencyObject element)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(element);

            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(element, index);

                if (child is T match)
                {
                    yield return match;
                }

                foreach (T found in FindAll<T>(child))
                {
                    yield return found;
                }
            }
        }

        /// <summary>テスト用のタグ。値はすべて未設定。</summary>
        private static TrackTags Track(string relativePath)
        {
            return new TrackTags
            {
                RelativePath = relativePath,
                FullPath = System.IO.Path.Combine("C:/library", relativePath),
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields([]),
                RawTags = new Dictionary<string, string[]>(),
            };
        }
    }
}
