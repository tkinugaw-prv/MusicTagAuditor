<#
.SYNOPSIS
    MusicTagAuditor を安全に起動して動作確認する。

.DESCRIPTION
    実ライブラリを開かず、そこからコピーしたテスト用ライブラリを開く。
    利用者の settings.json は起動前に退避し、finally で必ず戻す
    （起動しただけで lastLibraryRoot が書き換わるため）。
    背景は docs/manual_verification.md を参照。

.EXAMPLE
    pwsh -NoProfile -File .claude/skills/verify-ui/verify-ui.ps1
    起動できることだけを確認する。

.EXAMPLE
    pwsh -NoProfile -File .claude/skills/verify-ui/verify-ui.ps1 -Click 検査 -Tab ファイル一覧
    検査を実行し、ファイル一覧タブへ切り替えて、それぞれの画面を撮る。
#>
[CmdletBinding()]
param(
    # コピー元。省略時は settings.json の lastLibraryRoot を使う。
    [string]$SourceLibrary,

    # テスト用ライブラリの置き場。リポジトリの外に置く（音源をコミット候補に入れない）。
    [string]$TestLibrary = (Join-Path $env:TEMP 'MusicTagAuditor-testlib'),

    # 既存のテスト用ライブラリを作り直さない。前回の適用結果が残る点に注意。
    [switch]$Reuse,

    # 押すボタンの表示名。指定順に InvokePattern で押す。
    [string[]]$Click,

    # 選ぶタブの表示名。Click をすべて終えたあとに指定順で選ぶ。
    [string[]]$Tab,

    # 手順を並べて実行する。Click / Tab では届かない導線（明細の行を選ぶ・ダイアログを押す）用。
    #
    #   click:検査        メインウィンドウのボタンを押す
    #   rule:1            ルール一覧の n 行目を選ぶ（1 始まり）
    #   change:1          明細の n 行目を選ぶ（1 始まり）
    #   rows:TrackGrid    一覧の行を番号付きで並べる（グリッドは x:Name で指す）
    #   dialog:キャンセル  開いているダイアログのボタンを押す
    #   tab:ファイル一覧   タブを選ぶ
    #   shot:任意の名前    その時点を撮る
    #
    # Click / Tab を併用した場合は、Steps → Click → Tab の順に実行する。
    [string[]]$Steps,

    # 画面の PNG を書き出す先。
    [string]$OutDir = (Join-Path $env:TEMP 'MusicTagAuditor-verify'),

    # 起動前に dotnet build を走らせる。
    [switch]$Build,

    # 起動してから操作を始めるまでの待ち時間（秒）。スキャンの完了を待つ。
    [int]$StartupWaitSeconds = 7,

    # 1 操作ごとの待ち時間（秒）。
    [int]$ActionWaitSeconds = 4
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class NativeWindow {
  delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lparam);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lparam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hwnd);
  [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hwnd);
  [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);
  public struct Rect { public int Left, Top, Right, Bottom; }

  /// <summary>ダイアログの子コントロール 1 つ分。</summary>
  public class Control {
    /// <summary>ウィンドウハンドル。</summary>
    public IntPtr Handle;
    /// <summary>ウィンドウクラス名。Button / Static など。</summary>
    public string ClassName;
    /// <summary>表示文字列。アクセラレータの &amp; を含む。</summary>
    public string Text;
    /// <summary>押せる状態か。</summary>
    public bool Enabled;
    /// <summary>コントロール ID。IDOK=1 / IDCANCEL=2 など。</summary>
    public int Id;
  }

  static string TextOf(IntPtr hwnd) {
    var text = new StringBuilder(1024);
    GetWindowTextW(hwnd, text, 1024);
    return text.ToString();
  }

  /// <summary>ウィンドウクラス名を返す。ネイティブのダイアログは #32770。</summary>
  public static string ClassNameOf(IntPtr hwnd) {
    var name = new StringBuilder(256);
    GetClassNameW(hwnd, name, 256);
    return name.ToString();
  }

  /// <summary>プロセスの可視トップレベルウィンドウを (HWND, タイトル) で返す。</summary>
  public static List<KeyValuePair<IntPtr, string>> VisibleWindows(uint processId) {
    var list = new List<KeyValuePair<IntPtr, string>>();
    EnumWindows((hwnd, lparam) => {
      uint pid;
      GetWindowThreadProcessId(hwnd, out pid);
      if (pid == processId && IsWindowVisible(hwnd)) {
        var text = TextOf(hwnd);
        if (text.Length > 0) { list.Add(new KeyValuePair<IntPtr, string>(hwnd, text)); }
      }
      return true;
    }, IntPtr.Zero);
    return list;
  }

  /// <summary>ウィンドウの子コントロールを並べる。</summary>
  public static List<Control> Children(IntPtr parent) {
    var list = new List<Control>();
    EnumChildWindows(parent, (hwnd, lparam) => {
      list.Add(new Control {
        Handle = hwnd,
        ClassName = ClassNameOf(hwnd),
        Text = TextOf(hwnd),
        Enabled = IsWindowEnabled(hwnd),
        Id = GetDlgCtrlID(hwnd),
      });
      return true;
    }, IntPtr.Zero);
    return list;
  }

  /// <summary>BM_CLICK を post してボタンを押す。</summary>
  /// <remarks>
  /// SendMessage にしない。押した先でさらにモーダルが開くと、
  /// 制御が戻らずこちらが止まる。
  /// </remarks>
  public static bool ClickButton(IntPtr hwnd) {
    const uint BM_CLICK = 0x00F5;
    return PostMessageW(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
  }
}
'@

$UIA = [System.Windows.Automation.AutomationElement]
$TREE_SCOPE = [System.Windows.Automation.TreeScope]::Descendants

# PW_RENDERFULLCONTENT。WPF は既定のフラグだと黒い画像になる。
$PW_FULL = 2

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$exePath = Join-Path $repositoryRoot 'src\MusicTagAuditor.App\bin\Debug\net10.0-windows\MusicTagAuditor.App.exe'
$settingsPath = Join-Path $env:APPDATA 'MusicTagAuditor\settings.json'
$shotIndex = 0
$shots = @()

<#
.SYNOPSIS
    ウィンドウ単体を PNG に落とす。
.DESCRIPTION
    画面全体は撮らない。前面化には失敗することがあり、そのとき CopyFromScreen が
    撮るのは利用者が見ていた別のウィンドウになる。PrintWindow なら背面のままでも撮れる。
#>
function Save-WindowImage {
    param([IntPtr]$Handle, [string]$Label)

    $script:shotIndex++
    $safeLabel = ($Label -replace '[\\/:*?"<>|]', '_')
    $path = Join-Path $OutDir ('{0:D2}-{1}.png' -f $script:shotIndex, $safeLabel)

    $rect = New-Object NativeWindow+Rect
    [NativeWindow]::GetWindowRect($Handle, [ref]$rect) | Out-Null
    $bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [NativeWindow]::PrintWindow($Handle, $hdc, $PW_FULL) | Out-Null
    $graphics.ReleaseHdc($hdc)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()

    $script:shots += $path
    return $path
}

<#
.SYNOPSIS
    表示名で UI 要素を 1 つ探す。
.DESCRIPTION
    索引で選ぶと、ボタンが 1 つ増えただけで別のものを押してしまう。
    見つからないときは候補を並べて落とす。黙って何もしないより気づける。
#>
function Find-Element {
    param($Root, $ControlType, [string]$Name)

    $condition = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, $ControlType)
    $found = $Root.FindAll($TREE_SCOPE, $condition)

    foreach ($element in $found) {
        if ($element.Current.Name -eq $Name) { return $element }
    }

    $candidates = @()
    foreach ($element in $found) {
        if (-not [string]::IsNullOrWhiteSpace($element.Current.Name)) { $candidates += $element.Current.Name }
    }
    throw ("{0} '{1}' が見つからない。候補: {2}" -f $ControlType.ProgrammaticName, $Name, ($candidates -join ' / '))
}

<#
.SYNOPSIS
    引数をカンマで割る。
.DESCRIPTION
    **pwsh -File で起動すると配列が束縛されない。** `-Click A,B` も `-Click 'A','B'` も
    1 個の文字列として渡ってくるため、スクリプト側で割る。ここを用意する前は、
    表に出た症状が「ボタン 'A,B' が見つからない」で、原因が引数の渡し方だと分からなかった。

    **手順・ボタン名にカンマは使えない。** この app の表示名には無いので割り切る。
#>
function Split-Argument {
    param([string[]]$Values)

    $result = @()

    foreach ($value in $Values) {
        foreach ($part in ($value -split ',')) {
            if (-not [string]::IsNullOrWhiteSpace($part)) { $result += $part.Trim() }
        }
    }

    return $result
}

<#
.SYNOPSIS
    開いているダイアログを探す。
.DESCRIPTION
    **UIA のデスクトップ列挙（RootElement.FindAll(Children)）には出てこない。**
    所有ウィンドウのため子として並ばず、探しても見つからないので「開いていない」と
    読み違える。実際に 2026-08-22 にこれで 5 回遠回りした。EnumWindows で HWND を
    取り、FromHandle で掴む。

    **クラス名も返す。** ネイティブの MessageBox（#32770）と自前の Window では
    中身の読み方が違うので、ここで見分けて先を分ける。
#>
function Find-Dialog {
    param($Process, [int]$TimeoutSeconds = 10)

    for ($waited = 0; $waited -lt $TimeoutSeconds; $waited++) {
        foreach ($window in [NativeWindow]::VisibleWindows([uint32]$Process.Id)) {
            if ($window.Key -ne $Process.MainWindowHandle) {
                $className = [NativeWindow]::ClassNameOf($window.Key)
                return [pscustomobject]@{
                    Handle    = $window.Key
                    Title     = $window.Value
                    ClassName = $className
                    IsNative  = ($className -eq '#32770')
                }
            }
        }
        Start-Sleep -Seconds 1
    }

    return $null
}

<#
.SYNOPSIS
    ネイティブのボタン名の揺れを吸収する。
.DESCRIPTION
    MessageBox のボタンは OS が付けるので、日本語環境では「はい(&Y)」のように
    アクセラレータ付きで返る。**手順には見えているままの「はい」を書きたい**ので、
    生の文字列・& を抜いたもの・末尾の (Y) まで落としたもののどれでも拾う。
#>
function Get-ButtonLabel {
    param([string]$Text)

    $stripped = $Text -replace '&', ''
    return @($Text, $stripped, ($stripped -replace '\([A-Za-z]\)\s*$', '')) | Select-Object -Unique
}

<#
.SYNOPSIS
    ネイティブのダイアログのボタンを押す。
.DESCRIPTION
    **UIA では押せない。** Win32 コントロールを UIA に翻訳するクライアント側プロキシ
    （UIAutomationClientSideProviders）の登録が pwsh では失敗するため、MessageBox の子は
    Button ではなく Pane に見える。ControlType.Button で探すと 0 件になり、候補も空のまま
    落ちる。2026-08-22 に「選択行に一括入力」の確認ダイアログでこれに当たった。

    登録を反射で通す道も試したが、ProxyManager.LoadDefaultProxies が探す型名
    （UIAutomationClientSideProviders.…）と実際の型名（UIAutomationClientsideProviders.…）が
    食い違っていて NullReferenceException になる。**.NET 側の不整合なので手当てできない。**
    EnumChildWindows で子 HWND を拾い、BM_CLICK を post する。
#>
function Invoke-NativeDialogButton {
    param([IntPtr]$Handle, [string]$Name)

    $buttons = @([NativeWindow]::Children($Handle) | Where-Object { $_.ClassName -eq 'Button' })

    foreach ($button in $buttons) {
        if ((Get-ButtonLabel -Text $button.Text) -notcontains $Name) { continue }
        if (-not $button.Enabled) { throw "ダイアログのボタン '$Name' が無効になっている" }
        if (-not [NativeWindow]::ClickButton($button.Handle)) {
            throw "ダイアログのボタン '$Name' に BM_CLICK を送れなかった"
        }
        return
    }

    $candidates = @($buttons | ForEach-Object { $_.Text -replace '&', '' })
    throw ("ネイティブのボタン '{0}' が見つからない。候補: {1}" -f $Name, ($candidates -join ' / '))
}

<#
.SYNOPSIS
    ネイティブのダイアログに出ている文字を並べる。
.DESCRIPTION
    Read-Texts のネイティブ版。**出力の形は Read-Texts に合わせてある**ので、読む側は
    ダイアログの種類を意識しなくてよい。アイコンだけの Static は文字を持たないので落ちる。
#>
function Read-NativeTexts {
    param([IntPtr]$Handle)

    $labels = [ordered]@{ 'Static' = 'Text'; 'Button' = 'Button' }
    $children = [NativeWindow]::Children($Handle)

    foreach ($className in $labels.Keys) {
        foreach ($control in $children) {
            if ($control.ClassName -ne $className) { continue }
            if ([string]::IsNullOrWhiteSpace($control.Text)) { continue }
            "    [{0}] {1}" -f $labels[$className], ($control.Text -replace '&', '')
        }
    }
}

<#
.SYNOPSIS
    ウィンドウに出ている文字を並べる。
.DESCRIPTION
    **注意書きや選択肢は PNG を目で読むより確実に取れる。** 文言を変えたときの確認はここを見る。
    一覧の行を読むのは rows: 手順（Get-GridRows）のほう。
#>
function Read-Texts {
    param($Root)

    $types = @(
        [System.Windows.Automation.ControlType]::Text,
        [System.Windows.Automation.ControlType]::RadioButton,
        [System.Windows.Automation.ControlType]::CheckBox,
        [System.Windows.Automation.ControlType]::Button)

    foreach ($type in $types) {
        $label = $type.ProgrammaticName.Split('.')[-1]
        $condition = New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, $type)
        foreach ($element in $Root.FindAll($TREE_SCOPE, $condition)) {
            if (-not [string]::IsNullOrWhiteSpace($element.Current.Name)) {
                "    [{0}] {1}" -f $label, $element.Current.Name
            }
        }
    }
}

<#
.SYNOPSIS
    DataGrid を AutomationId で掴む。
.DESCRIPTION
    AutomationId は XAML の x:Name がそのまま出たもの。名前の無いグリッドは指せない。
#>
function Find-Grid {
    param($Root, [string]$AutomationId)

    $condition = New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, $AutomationId)
    $grid = $Root.FindFirst($TREE_SCOPE, $condition)
    if ($null -eq $grid) { throw "DataGrid '$AutomationId' が見つからない" }

    return $grid
}

<#
.SYNOPSIS
    DataGrid の行を並べて返す。
.DESCRIPTION
    **仮想化のため、画面に出ている行しか列挙されない。** 件数が合わないときはこれを疑う。

    行の名前は AutomationProperties.Name で振ってある（MainWindow.xaml の
    ChangeRowNameStyle ほか）。振ってあるのは検査結果の上段・下段、辞書に無い値、
    ファイル一覧、保留中の編集の 5 つだけで、**それ以外のグリッドは型名が返る。**
#>
function Get-GridRows {
    param($Grid)

    $rowCondition = New-Object System.Windows.Automation.PropertyCondition(
        $UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    return @($Grid.FindAll([System.Windows.Automation.TreeScope]::Children, $rowCondition))
}

<#
.SYNOPSIS
    DataGrid の n 行目を選ぶ（1 始まり）。
.DESCRIPTION
    行は番号で指すが、**選んだ結果は名前で確かめられる。** 並べ替えや絞り込みが挟まると
    番号と中身の対応は変わるので、出力に出た名前のほうを根拠にする。
#>
function Select-GridRow {
    param($Root, [string]$AutomationId, [int]$Index)

    $rows = @(Get-GridRows -Grid (Find-Grid -Root $Root -AutomationId $AutomationId))

    if ($Index -lt 1 -or $Index -gt $rows.Count) {
        throw "'$AutomationId' の $Index 行目は無い（列挙できたのは $($rows.Count) 行）"
    }

    $row = $rows[$Index - 1]
    $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()

    return [pscustomobject]@{ Count = $rows.Count; Name = $row.Current.Name }
}

<#
.SYNOPSIS
    実ライブラリから数フォルダをコピーしてテスト用ライブラリを作る。
.DESCRIPTION
    M4A と FLAC を 1 フォルダずつ拾う。形式で通る経路が違うため、片方だけだと取りこぼす。
#>
function New-TestLibrary {
    param([string]$Source, [string]$Destination)

    if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
    New-Item -ItemType Directory -Path $Destination | Out-Null

    foreach ($pattern in @('*.m4a', '*.flac')) {
        $sample = Get-ChildItem -Path $Source -Filter $pattern -Recurse -File | Select-Object -First 1
        if ($null -eq $sample) {
            Write-Warning "$pattern がコピー元に無い: $Source"
            continue
        }

        $target = Join-Path $Destination $sample.Directory.Name
        if (Test-Path $target) { continue }
        Copy-Item -Path $sample.Directory.FullName -Destination $target -Recurse
    }

    $count = (Get-ChildItem -Path $Destination -Recurse -File).Count
    if ($count -eq 0) { throw "テスト用ライブラリに 1 件もコピーできなかった: $Source" }
    "テスト用ライブラリ: $Destination（$count 件）"
}

# --- 事前確認 ---------------------------------------------------------------

if ($Build) {
    'ビルド中...'
    dotnet build (Join-Path $repositoryRoot 'MusicTagAuditor.slnx') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗した' }
}

if (-not (Test-Path $exePath)) { throw "exe が無い。先に dotnet build を実行する: $exePath" }
if (-not (Test-Path $settingsPath)) { throw "settings.json が無い: $settingsPath" }

$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($SourceLibrary)) { $SourceLibrary = $settings.lastLibraryRoot }
if ([string]::IsNullOrWhiteSpace($SourceLibrary)) { throw 'コピー元が決まらない。-SourceLibrary を指定する' }

$resolvedSource = (Resolve-Path $SourceLibrary).Path
$resolvedTest = if (Test-Path $TestLibrary) { (Resolve-Path $TestLibrary).Path } else { $TestLibrary }

# **実ライブラリを開かせない。** 押し間違い 1 回で所蔵のタグが書き換わる。
if ($resolvedTest -eq $resolvedSource -or
    $resolvedTest.StartsWith($resolvedSource, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'テスト用ライブラリがコピー元と同じか、その配下にある。実ライブラリを開くことになるので中止する'
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

if ($Reuse -and (Test-Path $resolvedTest)) {
    "テスト用ライブラリを再利用: $resolvedTest"
} else {
    New-TestLibrary -Source $resolvedSource -Destination $resolvedTest
}

# --- 起動と操作 -------------------------------------------------------------

# 起動しただけで lastLibraryRoot が書き換わる。退避は起動前に、復元は finally で。
$settingsBackup = Join-Path $OutDir 'settings.json.bak'
Copy-Item $settingsPath $settingsBackup -Force
"設定を退避: $settingsBackup"

$process = $null
try {
    $process = Start-Process -FilePath $exePath -ArgumentList ('"{0}"' -f $resolvedTest) -PassThru
    Start-Sleep -Seconds $StartupWaitSeconds
    $process.Refresh()

    if ($process.HasExited) {
        throw ("起動直後に終了した。exitcode={0} ログ: {1}\MusicTagAuditor\logs" -f $process.ExitCode, $env:LOCALAPPDATA)
    }
    "起動 OK pid=$($process.Id) title='$($process.MainWindowTitle)'"

    $root = $UIA::FromHandle($process.MainWindowHandle)
    "撮影: $(Save-WindowImage -Handle $process.MainWindowHandle -Label 'startup')"

    # Click / Tab は Steps の糖衣。既存の呼び方をそのまま通しつつ、順序も前と同じにする。
    $plan = @()
    $plan += Split-Argument -Values $Steps
    foreach ($name in (Split-Argument -Values $Click)) { $plan += "click:$name" }
    foreach ($name in (Split-Argument -Values $Tab)) { $plan += "tab:$name" }

    foreach ($step in $plan) {
        if ([string]::IsNullOrWhiteSpace($step)) { continue }

        $verb, $argument = $step -split ':', 2
        $verb = $verb.Trim()

        switch ($verb) {
            'click' {
                $button = Find-Element -Root $root -ControlType ([System.Windows.Automation.ControlType]::Button) -Name $argument
                if (-not $button.Current.IsEnabled) { throw "ボタン '$argument' が無効になっている" }
                $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Seconds $ActionWaitSeconds
                "押した: $argument"

                # **ダイアログが開いたら黙って進まない。** 開いたことに気づかないまま
                # 次の手順へ行くと、メインウィンドウを撮り続けて「何も起きない」ように見える。
                $dialog = Find-Dialog -Process $process -TimeoutSeconds 3
                if ($null -ne $dialog) {
                    "ダイアログが開いた: $($dialog.Title)"
                    "撮影: $(Save-WindowImage -Handle $dialog.Handle -Label "dialog-$($dialog.Title)")"
                    if ($dialog.IsNative) {
                        Read-NativeTexts -Handle $dialog.Handle
                    } else {
                        Read-Texts -Root $UIA::FromHandle($dialog.Handle)
                    }
                } else {
                    "撮影: $(Save-WindowImage -Handle $process.MainWindowHandle -Label "click-$argument")"
                }
            }

            'dialog' {
                $dialog = Find-Dialog -Process $process -TimeoutSeconds 10
                if ($null -eq $dialog) { throw "ダイアログが開いていないのに 'dialog:$argument' が来た" }

                # ネイティブの MessageBox と自前の Window では押し方が違う。理由は
                # Invoke-NativeDialogButton に書いた。
                if ($dialog.IsNative) {
                    Invoke-NativeDialogButton -Handle $dialog.Handle -Name $argument
                } else {
                    $dialogRoot = $UIA::FromHandle($dialog.Handle)
                    $button = Find-Element -Root $dialogRoot -ControlType ([System.Windows.Automation.ControlType]::Button) -Name $argument
                    if (-not $button.Current.IsEnabled) { throw "ダイアログのボタン '$argument' が無効になっている" }
                    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                }
                Start-Sleep -Seconds $ActionWaitSeconds
                "ダイアログで押した: $argument"
                "撮影: $(Save-WindowImage -Handle $process.MainWindowHandle -Label "after-$argument")"
            }

            'rule' {
                $selected = Select-GridRow -Root $root -AutomationId 'RuleResultGrid' -Index ([int]$argument)
                Start-Sleep -Seconds $ActionWaitSeconds
                "ルールの $argument 行目を選んだ（列挙 $($selected.Count) 行）: $($selected.Name)"
            }

            'change' {
                $selected = Select-GridRow -Root $root -AutomationId 'InspectionChangeGrid' -Index ([int]$argument)
                Start-Sleep -Seconds $ActionWaitSeconds
                "明細の $argument 行目を選んだ（列挙 $($selected.Count) 行）: $($selected.Name)"
            }

            'rows' {
                # **撮らずに読む。** 行の中身は名前で取れるので、ここは PNG より確実。
                $rows = @(Get-GridRows -Grid (Find-Grid -Root $root -AutomationId $argument))
                "$argument の行（列挙 $($rows.Count) 行）"
                for ($index = 0; $index -lt $rows.Count; $index++) {
                    "    [{0}] {1}" -f ($index + 1), $rows[$index].Current.Name
                }
            }

            'tab' {
                $tabItem = Find-Element -Root $root -ControlType ([System.Windows.Automation.ControlType]::TabItem) -Name $argument
                $tabItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                Start-Sleep -Seconds $ActionWaitSeconds
                "選んだ: $argument"
                "撮影: $(Save-WindowImage -Handle $process.MainWindowHandle -Label "tab-$argument")"
            }

            'shot' {
                "撮影: $(Save-WindowImage -Handle $process.MainWindowHandle -Label $argument)"
            }

            default {
                throw "手順 '$step' が読めない。使えるのは click / dialog / rule / change / rows / tab / shot"
            }
        }
    }

    # **開いたままのダイアログを残さない。** CloseMainWindow が効かず、強制終了になる。
    $stray = Find-Dialog -Process $process -TimeoutSeconds 1
    if ($null -ne $stray) { throw "ダイアログ '$($stray.Title)' が開いたままになっている。dialog: で閉じる" }

    $process.CloseMainWindow() | Out-Null
    $process.WaitForExit(10000) | Out-Null
    if (-not $process.HasExited) { throw 'CloseMainWindow で終了しなかった' }
    "終了コード: $($process.ExitCode)"
}
finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            $process.Kill()
            Write-Warning 'プロセスが残っていたので強制終了した'
        }
    }

    # **ここを飛ばすと利用者の設定が壊れたままになる。**
    Copy-Item $settingsBackup $settingsPath -Force
    "設定を復元: $((Get-Content $settingsPath -Raw | ConvertFrom-Json).lastLibraryRoot)"
}

'--- 画面 ---'
$shots
