# ============================================================
# GenerateComponents.ps1
# Publish フォルダの全ファイル/サブフォルダを走査して、
# WiX v4 形式の Components.wxs を生成する。
# ============================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

# コンソール出力を UTF-8 に設定（コマンドプロンプトの文字化け防止）
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $PublishDir)) {
    Write-Error "Publish ディレクトリが存在しません: $PublishDir"
    exit 1
}

$publishDirFull = (Resolve-Path $PublishDir).Path.TrimEnd('\')

Write-Host "  Publish フォルダ: $publishDirFull"
Write-Host "  Components.wxs 出力先: $OutputPath"

# 全ファイル収集
$files = Get-ChildItem -Path $publishDirFull -File -Recurse

# サブディレクトリ一覧を作成 (空ディレクトリも含むため別途列挙)
$subdirs = Get-ChildItem -Path $publishDirFull -Directory -Recurse

$sb = New-Object System.Text.StringBuilder
$null = $sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
$null = $sb.AppendLine('<!-- このファイルは build-installer.bat により自動生成されます。手動編集しないでください。 -->')
$null = $sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$null = $sb.AppendLine('  <Fragment>')

# ----------------------------------------------------------
# DirectoryRef ツリーを構築
# ----------------------------------------------------------
$null = $sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

# 既出 Directory Id 管理 (ID は安全な文字列に変換)
$dirIdMap = @{}
$dirIdMap[''] = 'INSTALLFOLDER'  # ルート

function Get-SafeId {
    param([string]$name, [string]$prefix = 'Dir')
    # 英数字とアンダースコアだけにする
    $safe = ($name -replace '[^A-Za-z0-9_]', '_')
    if ($safe -match '^\d') { $safe = "_$safe" }
    if ($safe.Length -eq 0) { $safe = 'Empty' }
    if ($safe.Length -gt 60) { $safe = $safe.Substring(0, 60) }
    return "${prefix}_${safe}"
}

# サブディレクトリを階層的に出力
# Path → DirectoryId のマップを構築
$dirsByDepth = $subdirs | ForEach-Object {
    $rel = $_.FullName.Substring($publishDirFull.Length).TrimStart('\')
    [PSCustomObject]@{
        FullName = $_.FullName
        RelPath  = $rel
        Depth    = ($rel.Split('\').Count)
        Name     = $_.Name
    }
} | Sort-Object Depth, RelPath

# ID 衝突防止用のカウンタ
$idCounter = 0

foreach ($d in $dirsByDepth) {
    $idCounter++
    $dirId = "Dir_{0:D4}_{1}" -f $idCounter, ((Get-SafeId $d.Name 'X') -replace '^X_', '')
    $dirIdMap[$d.RelPath] = $dirId
}

# 階層的に Directory 要素を生成 (ネストして出力)
function Write-DirTree {
    param(
        [string]$ParentRel,
        [int]$IndentLevel
    )
    $indent = '    ' + ('  ' * $IndentLevel)
    $children = $subdirs | Where-Object {
        $rel = $_.FullName.Substring($publishDirFull.Length).TrimStart('\')
        $parent = Split-Path $rel -Parent
        if ($parent -eq $null) { $parent = '' }
        $parent -eq $ParentRel
    } | Sort-Object Name
    foreach ($c in $children) {
        $rel = $c.FullName.Substring($publishDirFull.Length).TrimStart('\')
        $dirId = $dirIdMap[$rel]
        $null = $sb.AppendLine("$indent  <Directory Id=`"$dirId`" Name=`"$($c.Name)`">")
        Write-DirTree -ParentRel $rel -IndentLevel ($IndentLevel + 1)
        $null = $sb.AppendLine("$indent  </Directory>")
    }
}

Write-DirTree -ParentRel '' -IndentLevel 0

$null = $sb.AppendLine('    </DirectoryRef>')

# ----------------------------------------------------------
# ComponentGroup
# ----------------------------------------------------------
$null = $sb.AppendLine('    <ComponentGroup Id="ApplicationComponents">')

$fileIdx = 0
foreach ($f in $files) {
    $fileIdx++
    $rel = $f.FullName.Substring($publishDirFull.Length).TrimStart('\')
    $relDir = Split-Path $rel -Parent
    if ($null -eq $relDir) { $relDir = '' }
    $directoryId = $dirIdMap[$relDir]
    if (-not $directoryId) {
        Write-Warning "ディレクトリIDが見つかりません: $relDir → INSTALLFOLDER にフォールバック"
        $directoryId = 'INSTALLFOLDER'
    }

    $cmpId  = "Cmp_{0:D5}" -f $fileIdx
    $fileId = "Fil_{0:D5}" -f $fileIdx
    $guid   = [guid]::NewGuid().ToString().ToUpper()

    # PdfStudio.exe を主要KeyPathコンポーネントとしてマーク（ショートカット用）
    $isMainExe = ($f.Name -eq 'PdfStudio.exe' -and $relDir -eq '')

    $null = $sb.AppendLine("      <Component Id=`"$cmpId`" Directory=`"$directoryId`" Guid=`"$guid`" Bitness=`"always64`">")
    $null = $sb.AppendLine("        <File Id=`"$fileId`" Source=`"$($f.FullName)`" KeyPath=`"yes`" />")
    $null = $sb.AppendLine("      </Component>")
}

$null = $sb.AppendLine('    </ComponentGroup>')
$null = $sb.AppendLine('  </Fragment>')
$null = $sb.AppendLine('</Wix>')

# UTF-8 (BOM なし) で書き出し
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($OutputPath, $sb.ToString(), $utf8)

Write-Host "  生成完了: $fileIdx ファイル / $($subdirs.Count) サブディレクトリ"
exit 0
