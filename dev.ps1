# 開発用統一コマンド。使い方は `dev help`。
# このファイルは UTF-8 (BOM 付き) で保存する。グローバルの dev ディスパッチャーが
# Windows PowerShell 5.1 から呼ぶことがあり、BOM がないと日本語を読めずパースに失敗するため。
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Command = 'help',
    # 対象作品の番号 (例: 001)。省略時はカレントディレクトリが games/NNN-* 配下ならその作品。
    [Parameter(Position = 1)][string]$Game,
    # test: Unity 上でテストを実行する
    [switch]$Unity,
    # test -Unity: EditMode または PlayMode
    [ValidateSet('EditMode', 'PlayMode')][string]$Mode = 'EditMode',
    # new: 2d または 3d (URP テンプレート)
    [ValidateSet('2d', '3d')][string]$Template = '2d'
)

# 本体は PowerShell 7 前提。Windows PowerShell 5.1 から呼ばれた場合は pwsh で実行し直す。
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $forward = @($Command)
    if ($Game) { $forward += $Game }
    if ($Unity) { $forward += '-Unity' }
    & pwsh -NoProfile -File $PSCommandPath @forward -Mode $Mode -Template $Template
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$GamesDir = Join-Path $RepoRoot 'games'
$Solution = Join-Path $RepoRoot 'unity-100games.slnx'
$BuildTarget = 'StandaloneWindows64'
$GeneratedDirs = 'Library', 'Temp', 'Logs', 'obj', 'Builds'

# 外部コマンドを実行し、失敗したらその終了コードで即終了する。
# `--option` 形式の引数をそのまま渡すため、param ブロックを使わず $args で受ける。
function Invoke-Native {
    $exe = $args[0]
    [string[]]$arguments = $args | Select-Object -Skip 1
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Get-GameDirs {
    if (-not (Test-Path $GamesDir)) { return @() }
    @(Get-ChildItem $GamesDir -Directory | Where-Object Name -Match '^\d{3}-')
}

function Format-GameNumber([string]$Value) {
    if ($Value -notmatch '^\d{1,3}$') { throw "作品番号は 1〜100 の数字で指定してください: '$Value'" }
    '{0:d3}' -f [int]$Value
}

# 明示指定 → カレントディレクトリの順で対象作品を決める。決まらなければ $null。
function Find-GameDir {
    if ($Game) {
        $number = Format-GameNumber $Game
        $dir = Get-GameDirs | Where-Object Name -Like "$number-*"
        if (-not $dir) { throw "games/$number-* が見つかりません。`dev new $number` で作成してください。" }
        return $dir
    }
    $current = (Get-Location).Path
    Get-GameDirs | Where-Object { "$current$([IO.Path]::DirectorySeparatorChar)".StartsWith("$($_.FullName)$([IO.Path]::DirectorySeparatorChar)") }
}

function Get-RequiredGameDir {
    $dir = Find-GameDir
    if (-not $dir) { throw "対象作品を指定してください (例: dev $Command 001)。" }
    $dir
}

function Get-BuildExe($GameDir) {
    Join-Path $GameDir.FullName "Builds/Windows/$($GameDir.Name).exe"
}

# Assets 配下の全ファイル/フォルダに .meta があり、孤立した .meta がないことを検証する。
function Test-MetaFiles {
    $problems = foreach ($gameDir in Get-GameDirs) {
        $assets = Join-Path $gameDir.FullName 'Assets'
        foreach ($item in Get-ChildItem $assets -Recurse -Force) {
            if ($item.Extension -eq '.meta') {
                $target = $item.FullName.Substring(0, $item.FullName.Length - '.meta'.Length)
                if (-not (Test-Path -LiteralPath $target)) { "対象のない .meta: $($item.FullName)" }
            }
            elseif (-not (Test-Path -LiteralPath "$($item.FullName).meta")) {
                ".meta がない (Unity で一度開いて生成する): $($item.FullName)"
            }
        }
    }
    if ($problems) {
        $problems | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        exit 1
    }
}

function Invoke-Format([switch]$Verify) {
    [string[]]$verifyArgs = if ($Verify) { '--verify-no-changes' } else { @() }
    Invoke-Native dotnet format $Solution @verifyArgs
    # Core 以外 (App / Unity 層) は dotnet のプロジェクトに含まれないため、空白整形のみ適用する。
    foreach ($gameDir in Get-GameDirs) {
        Invoke-Native dotnet format whitespace (Join-Path $gameDir.FullName 'Assets') --folder @verifyArgs
    }
}

function Invoke-New {
    if (-not $Game) { throw '作品番号を指定してください (例: dev new 001)。' }
    $number = Format-GameNumber $Game
    $spec = Get-ChildItem (Join-Path $RepoRoot 'plan/games') -Filter "$number-*.md"
    if (-not $spec) { throw "plan/games/$number-*.md が見つかりません。" }
    $gameId = $spec.BaseName
    $gameDir = Join-Path $GamesDir $gameId
    if (Test-Path $gameDir) { throw "すでに存在します: $gameDir" }

    New-Item -ItemType Directory -Force $GamesDir | Out-Null
    Invoke-Native unity projects create $gameId --output-dir $GamesDir --template $Template --no-cloud --non-interactive

    # テンプレート付属のチュートリアル (Welcome 画面) と、約4MBのサムネイルを埋め込んだ
    # シーンテンプレートは不要なので除去する。
    $unneeded = @(
        'Assets/Welcome', 'Assets/Welcome.meta'
        'Assets/Settings/Lit2DSceneTemplate.scenetemplate', 'Assets/Settings/Lit2DSceneTemplate.scenetemplate.meta'
        'Assets/Settings/Scenes', 'Assets/Settings/Scenes.meta'
        'ProjectSettings/Packages/com.unity.learn.iet-framework'
    )
    $unneeded | ForEach-Object { Join-Path $gameDir $_ } | Where-Object { Test-Path $_ } | Remove-Item -Recurse -Force
    $manifestPath = Join-Path $gameDir 'Packages/manifest.json'
    $manifest = Get-Content $manifestPath | Where-Object { $_ -notmatch '"com\.unity\.learn\.iet-framework"' }
    [IO.File]::WriteAllText($manifestPath, ($manifest -join "`n") + "`n")

    $unityVersion = (Get-Content (Join-Path $gameDir 'ProjectSettings/ProjectVersion.txt') -TotalCount 1) -replace '^m_EditorVersion:\s*', ''
    $tokens = @{
        '{{NNN}}'           = $number
        '{{GAME_ID}}'       = $gameId
        '{{TITLE}}'         = (Get-Content $spec.FullName -TotalCount 1) -replace '^#\s*', ''
        '{{UNITY_VERSION}}' = $unityVersion
    }
    $expand = {
        param([string]$Text)
        foreach ($token in $tokens.GetEnumerator()) { $Text = $Text.Replace($token.Key, $token.Value) }
        $Text
    }
    $templateRoot = Join-Path $RepoRoot 'templates/game'
    foreach ($file in Get-ChildItem $templateRoot -Recurse -File) {
        $relative = & $expand ([IO.Path]::GetRelativePath($templateRoot, $file.FullName))
        $destination = Join-Path $gameDir $relative
        New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
        [IO.File]::WriteAllText($destination, (& $expand ([IO.File]::ReadAllText($file.FullName))))
    }

    foreach ($project in Get-ChildItem (Join-Path $gameDir 'dotnet') -Recurse -Filter '*.csproj') {
        Invoke-Native dotnet sln $Solution add $project.FullName --solution-folder $gameId
    }

    Write-Host "作成しました: games/$gameId"
    Write-Host "次の手順: dev test $number -Unity (.meta 生成と疎通確認) → dev gui $number"
}

function Invoke-Clean {
    $target = Find-GameDir
    $gameDirs = if ($target) { @($target) } else { Get-GameDirs }
    foreach ($gameDir in $gameDirs) {
        $paths = @($GeneratedDirs | ForEach-Object { Join-Path $gameDir.FullName $_ })
        $paths += @(Get-ChildItem (Join-Path $gameDir.FullName 'dotnet') -Directory -Recurse |
                Where-Object Name -In 'bin', 'obj' | ForEach-Object FullName)
        foreach ($path in $paths | Where-Object { Test-Path $_ }) {
            Write-Host "削除: $path"
            Remove-Item $path -Recurse -Force
        }
    }
}

function Show-Help {
    @'
使い方: dev <command> [作品番号] [オプション]

  new <NNN> [-Template 2d|3d]   plan/games/NNN-*.md に対応する Unity プロジェクトを games/ に作成
  gui [NNN]                     Unity エディタで開く
  build [NNN]                   Windows 向けにビルド (games/NNN-*/Builds/Windows/)
  run [NNN]                     ビルド済みのゲームを起動
  test                          全作品の Core テストを dotnet で実行 (高速、Unity 不要)
  test [NNN] -Unity [-Mode EditMode|PlayMode]
                                Unity 上でテストを実行
  lint                          整形・アナライザー・.meta の検証 (変更しない)
  format                        コードを整形
  check                         全作品の Core を型チェック (警告はエラー扱い)
  clean [NNN]                   生成物 (Library, Builds, bin, obj など) を削除。番号省略時は全作品
  help                          このヘルプ

作品番号を省略すると、カレントディレクトリが games/NNN-* 配下ならその作品を対象にする。
'@ | Write-Host
}

switch ($Command) {
    'help' { Show-Help }
    'new' { Invoke-New }
    'gui' { Invoke-Native unity open (Get-RequiredGameDir).FullName }
    'build' {
        $gameDir = Get-RequiredGameDir
        # 開発中のビルドが目的なので、未コミットの変更があっても止めない。
        Invoke-Native unity build $gameDir.FullName --target $BuildTarget --output (Get-BuildExe $gameDir) --allow-dirty-build
    }
    'run' {
        $exe = Get-BuildExe (Get-RequiredGameDir)
        if (-not (Test-Path $exe)) { throw "ビルドがありません。先に dev build を実行してください: $exe" }
        Start-Process $exe
    }
    'test' {
        if ($Unity) {
            $gameDir = Get-RequiredGameDir
            $report = Join-Path $gameDir.FullName "Logs/test-results-$Mode.xml"
            Invoke-Native unity test $gameDir.FullName --mode $Mode --output $report
        }
        else {
            Invoke-Native dotnet test $Solution
        }
    }
    'lint' {
        Test-MetaFiles
        Invoke-Format -Verify
    }
    'format' { Invoke-Format }
    'check' { Invoke-Native dotnet build $Solution }
    'clean' { Invoke-Clean }
    default {
        Show-Help
        throw "不明なコマンド: $Command"
    }
}
