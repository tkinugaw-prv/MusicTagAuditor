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
using System.Runtime.InteropServices;
public static class NativeWindow {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
  public struct Rect { public int Left, Top, Right, Bottom; }
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

    foreach ($name in $Click) {
        $button = Find-Element -Root $root -ControlType ([System.Windows.Automation.ControlType]::Button) -Name $name
        if (-not $button.Current.IsEnabled) { throw "ボタン '$name' が無効になっている" }
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds $ActionWaitSeconds
        "押した: $name"
        "撮影: $(Save-WindowImage -Handle $process.MainWindowHandle -Label "click-$name")"
    }

    foreach ($name in $Tab) {
        $tabItem = Find-Element -Root $root -ControlType ([System.Windows.Automation.ControlType]::TabItem) -Name $name
        $tabItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Seconds $ActionWaitSeconds
        "選んだ: $name"
        "撮影: $(Save-WindowImage -Handle $process.MainWindowHandle -Label "tab-$name")"
    }

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
