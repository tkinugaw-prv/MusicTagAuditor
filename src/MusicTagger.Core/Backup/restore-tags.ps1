#Requires -Version 7.0
<#
.SYNOPSIS
    musicTagger のスナップショットからタグを復元する。

.DESCRIPTION
    musicTagger アプリが無くても、このスクリプトと tags_snapshot.json だけでタグを巻き戻せる。
    同じフォルダに置かれた TagLibSharp.dll を使う。

    M4A の指揮者は必ず ©con (0xA9 63 6F 6E) に書く。TagLib# の Tag.Conductor は cond に書き、
    AIMP からは見えなくなるため使わない（docs/adr/0001-tag-io-library.md）。

.PARAMETER SnapshotPath
    tags_snapshot.json のパス。既定はこのスクリプトと同じフォルダ。

.PARAMETER LibraryRoot
    復元先のライブラリルート。既定はスナップショットに記録された値。

.PARAMETER TagLibPath
    TagLibSharp.dll のパス。既定はこのスクリプトと同じフォルダ。

.PARAMETER PathFilter
    復元対象を相対パスの部分一致で絞り込む。省略時は全件。

.PARAMETER DryRun
    書き込まずに差分だけを表示する。

.EXAMPLE
    ./restore-tags.ps1 -DryRun
    何が戻るのかを確認する。

.EXAMPLE
    ./restore-tags.ps1
    差分のあるファイルを復元する。

.NOTES
    差分表示は TagLib# で現在値を読むため、M4A では「1 値に ; を含む状態」と
    「複数値に分割済みの状態」を区別できない。書き込む内容はスナップショットの
    値そのものなので、復元結果には影響しない。
#>
[CmdletBinding()]
param(
    [string]$SnapshotPath = (Join-Path $PSScriptRoot 'tags_snapshot.json'),
    [string]$LibraryRoot,
    [string]$TagLibPath = (Join-Path $PSScriptRoot 'TagLibSharp.dll'),
    [string[]]$PathFilter,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 日本語のパスやフィールド名を出力するため、コンソールの文字コードを UTF-8 に固定する。
# 既定のままだと環境によっては ???? に化ける。
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
try
{
    [Console]::OutputEncoding = $OutputEncoding
}
catch
{
    # 出力がリダイレクトされていて設定できない場合がある。表示だけの問題なので続行する。
    Write-Verbose "コンソールの文字コードを設定できませんでした: $($_.Exception.Message)"
}

# --- 論理フィールドと格納先の対応（docs/TAGGING_POLICY.md 4.1） ---

$MP4_ATOM_BY_FIELD = @{
    Title       = "$([char]0xA9)nam"
    Artist      = "$([char]0xA9)ART"
    AlbumArtist = 'aART'
    Composer    = "$([char]0xA9)wrt"
    Conductor   = "$([char]0xA9)con"
    Album       = "$([char]0xA9)alb"
    Genre       = "$([char]0xA9)gen"
    Date        = "$([char]0xA9)day"
}

$VORBIS_FIELD_BY_FIELD = @{
    Title       = 'TITLE'
    Artist      = 'ARTIST'
    AlbumArtist = 'ALBUMARTIST'
    Composer    = 'COMPOSER'
    Conductor   = 'CONDUCTOR'
    Album       = 'ALBUM'
    Genre       = 'GENRE'
    Date        = 'DATE'
    TrackNumber = 'TRACKNUMBER'
    DiscNumber  = 'DISCNUMBER'
}

$ID3_FRAME_BY_FIELD = @{
    Title       = 'TIT2'
    Artist      = 'TPE1'
    AlbumArtist = 'TPE2'
    Composer    = 'TCOM'
    Conductor   = 'TPE3'
    Album       = 'TALB'
    Genre       = 'TCON'
    Date        = 'TDRC'
    TrackNumber = 'TRCK'
    DiscNumber  = 'TPOS'
}

function ConvertTo-Identifier {
    # バイト列を TagLib の識別子に変換する。
    #
    # 先頭の , は必須。ByteVector は IEnumerable のため、付けないと PowerShell が
    # 戻り値を 1 バイトずつに展開してしまい Object[] になる。
    param([byte[]]$Bytes)

    return ,[TagLib.ReadOnlyByteVector]::new($Bytes)
}

function ConvertTo-AtomIdentifier {
    # atom 名を 4 バイトの識別子に変換する。© は 0xA9 にする。
    param([string]$Name)

    $bytes = [byte[]]::new(4)
    for ($i = 0; $i -lt 4; $i++)
    {
        $c = [int][char]$Name[$i]
        $bytes[$i] = if ($c -eq 0xA9) { 0xA9 } else { [byte]$c }
    }

    return ,(ConvertTo-Identifier $bytes)
}

function ConvertTo-FrameIdentifier {
    # ID3v2 のフレーム ID を識別子に変換する。
    # 文字列を直接渡すと ReadOnlyByteVector(int size) の方に解決されてしまう。
    param([string]$FrameId)

    return ,(ConvertTo-Identifier ([System.Text.Encoding]::ASCII.GetBytes($FrameId)))
}

function Get-JoinedValue {
    # 比較用に複数値を 1 つの文字列へまとめる。
    param([string[]]$Values)

    if ($null -eq $Values -or $Values.Count -eq 0)
    {
        return ''
    }

    return ($Values -join '; ')
}

function Get-CurrentFieldValue {
    # 現在ファイルに入っている値を、比較用の 1 文字列として取り出す。
    param($File, [string]$Format, [string]$Field)

    switch ($Format)
    {
        'M4a'
        {
            $tag = $File.GetTag([TagLib.TagTypes]::Apple, $true)
            if ($Field -eq 'TrackNumber')
            {
                if ($tag.Track -eq 0) { return '' }
                if ($tag.TrackCount -gt 0) { return "$($tag.Track)/$($tag.TrackCount)" }
                return "$($tag.Track)"
            }
            if ($Field -eq 'DiscNumber')
            {
                if ($tag.Disc -eq 0) { return '' }
                if ($tag.DiscCount -gt 0) { return "$($tag.Disc)/$($tag.DiscCount)" }
                return "$($tag.Disc)"
            }
            if (-not $MP4_ATOM_BY_FIELD.ContainsKey($Field)) { return '' }
            # メソッド呼び出しを他のコマンドの引数に直接書くと、PowerShell は
            # 「$tag.GetText」と「(...)」を別々の引数として解釈する。必ず変数に取る。
            $values = $tag.GetText((ConvertTo-AtomIdentifier $MP4_ATOM_BY_FIELD[$Field]))
            return Get-JoinedValue $values
        }

        'Flac'
        {
            $tag = $File.GetTag([TagLib.TagTypes]::Xiph, $true)
            if (-not $VORBIS_FIELD_BY_FIELD.ContainsKey($Field)) { return '' }
            $values = $tag.GetField($VORBIS_FIELD_BY_FIELD[$Field])
            return Get-JoinedValue $values
        }

        'Id3'
        {
            $tag = $File.GetTag([TagLib.TagTypes]::Id3v2, $true)
            if (-not $ID3_FRAME_BY_FIELD.ContainsKey($Field)) { return '' }
            $ident = ConvertTo-FrameIdentifier $ID3_FRAME_BY_FIELD[$Field]
            $values = @()
            foreach ($frame in $tag.GetFrames($ident))
            {
                if ($frame -is [TagLib.Id3v2.TextInformationFrame]) { $values += $frame.Text }
            }
            return Get-JoinedValue $values
        }
    }

    return ''
}

function Set-FieldValue {
    # スナップショットの値を書き込む。1 値に ; が含まれていても分割しない。
    param($File, [string]$Format, [string]$Field, [string[]]$Values)

    switch ($Format)
    {
        'M4a'
        {
            $tag = $File.GetTag([TagLib.TagTypes]::Apple, $true)

            if ($Field -eq 'TrackNumber' -or $Field -eq 'DiscNumber')
            {
                $parts = if ($Values.Count -gt 0) { $Values[0] -split '/' } else { @() }
                $number = if ($parts.Count -gt 0) { [uint32]($parts[0].Trim()) } else { [uint32]0 }
                $total = if ($parts.Count -gt 1) { [uint32]($parts[1].Trim()) } else { [uint32]0 }

                if ($Field -eq 'TrackNumber') { $tag.Track = $number; $tag.TrackCount = $total }
                else { $tag.Disc = $number; $tag.DiscCount = $total }
                return
            }

            if (-not $MP4_ATOM_BY_FIELD.ContainsKey($Field)) { return }
            $ident = ConvertTo-AtomIdentifier $MP4_ATOM_BY_FIELD[$Field]

            if ($Values.Count -eq 0) { $tag.ClearData($ident) }
            else { $tag.SetText($ident, [string[]]$Values) }
        }

        'Flac'
        {
            $tag = $File.GetTag([TagLib.TagTypes]::Xiph, $true)
            if (-not $VORBIS_FIELD_BY_FIELD.ContainsKey($Field)) { return }
            $name = $VORBIS_FIELD_BY_FIELD[$Field]

            if ($Values.Count -eq 0) { $tag.RemoveField($name) }
            else { $tag.SetField($name, [string[]]$Values) }
        }

        'Id3'
        {
            $tag = $File.GetTag([TagLib.TagTypes]::Id3v2, $true)
            if (-not $ID3_FRAME_BY_FIELD.ContainsKey($Field)) { return }
            $ident = ConvertTo-FrameIdentifier $ID3_FRAME_BY_FIELD[$Field]

            if ($Values.Count -eq 0) { $tag.RemoveFrames($ident) }
            else { $tag.SetTextFrame($ident, [string[]]$Values) }
        }
    }
}

# --- 準備 ---

if (-not (Test-Path -LiteralPath $SnapshotPath))
{
    throw "スナップショットが見つかりません: $SnapshotPath"
}

if (-not (Test-Path -LiteralPath $TagLibPath))
{
    throw "TagLibSharp.dll が見つかりません: $TagLibPath`nアプリの実行フォルダからコピーするか -TagLibPath で指定してください。"
}

Add-Type -LiteralPath $TagLibPath

$snapshot = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 32

if (-not $LibraryRoot)
{
    $LibraryRoot = $snapshot.libraryRoot
}

if (-not (Test-Path -LiteralPath $LibraryRoot))
{
    throw "ライブラリが見つかりません: $LibraryRoot"
}

Write-Host "スナップショット: $SnapshotPath"
Write-Host "取得日時        : $($snapshot.createdAt)"
Write-Host "ライブラリ      : $LibraryRoot"
Write-Host "記録件数        : $($snapshot.trackCount)"
Write-Host ''

# --- 差分の算出と復元 ---

$changed = 0
$restored = 0
$missing = 0
$failed = 0

foreach ($track in $snapshot.tracks)
{
    if ($PathFilter -and -not ($PathFilter | Where-Object { $track.path -like "*$_*" }))
    {
        continue
    }

    $fullPath = Join-Path $LibraryRoot $track.path

    if (-not (Test-Path -LiteralPath $fullPath))
    {
        Write-Warning "ファイルが見つかりません: $($track.path)"
        $missing++
        continue
    }

    try
    {
        $file = [TagLib.File]::Create($fullPath)
    }
    catch
    {
        Write-Warning "開けません: $($track.path) — $($_.Exception.Message)"
        $failed++
        continue
    }

    try
    {
        $differences = @()

        foreach ($field in $track.fields.PSObject.Properties)
        {
            $expected = Get-JoinedValue ([string[]]$field.Value)
            $actual = Get-CurrentFieldValue $file $track.format $field.Name

            if ($expected -ne $actual)
            {
                $differences += [pscustomobject]@{
                    Field  = $field.Name
                    Before = $actual
                    After  = $expected
                    Values = [string[]]$field.Value
                }
            }
        }

        if ($differences.Count -eq 0)
        {
            continue
        }

        $changed++
        Write-Host $track.path -ForegroundColor Cyan

        foreach ($difference in $differences)
        {
            Write-Host ("  {0,-12} 「{1}」 → 「{2}」" -f $difference.Field, $difference.Before, $difference.After)
        }

        if ($DryRun)
        {
            continue
        }

        foreach ($difference in $differences)
        {
            Set-FieldValue $file $track.format $difference.Field $difference.Values
        }

        $file.Save()
        $restored++
    }
    catch
    {
        Write-Warning "復元に失敗: $($track.path) — $($_.Exception.Message)"
        $failed++
    }
    finally
    {
        $file.Dispose()
    }
}

Write-Host ''

if ($DryRun)
{
    Write-Host "差分あり $changed 件（-DryRun のため書き込んでいません）"
}
else
{
    Write-Host "差分あり $changed 件 / 復元 $restored 件"
}

if ($missing -gt 0) { Write-Host "見つからないファイル $missing 件" }
if ($failed -gt 0) { Write-Host "失敗 $failed 件" -ForegroundColor Red }
