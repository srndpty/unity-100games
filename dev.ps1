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
    [ValidateSet('2d', '3d')][string]$Template = '2d',
    # lint / check: git にステージした変更が影響する作品だけを対象にする (pre-commit 用)
    [switch]$Staged
)

# 本体は PowerShell 7 前提。Windows PowerShell 5.1 から呼ばれた場合は pwsh で実行し直す。
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $forward = @($Command)
    if ($Game) { $forward += $Game }
    if ($Unity) { $forward += '-Unity' }
    if ($Staged) { $forward += '-Staged' }
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
$CaptureTimeoutSeconds = 120
# 変更されると全作品の検証結果が変わりうる共通ファイル (-Staged でもこれらが含まれれば全作品を対象にする)
$SharedPathPattern = '^(build/|\.editorconfig$|global\.json$|unity-100games\.slnx$|dev\.ps1$)'

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

# lint / check の対象作品。-Staged ならステージした変更が影響する作品だけ (共通ファイルの変更なら全作品)、
# 番号指定ならその作品、どちらもなければ全作品。
function Get-TargetGameDirs {
    if ($Staged) {
        $files = @(git -C $RepoRoot diff --cached --name-only)
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        if ($files -match $SharedPathPattern) { return Get-GameDirs }
        $names = @($files | ForEach-Object { if ($_ -match '^games/(\d{3}-[^/]+)/') { $Matches[1] } } | Sort-Object -Unique)
        return @(Get-GameDirs | Where-Object Name -In $names)
    }
    if ($Game) { return @(Find-GameDir) }
    Get-GameDirs
}

# Assets 配下の全ファイル/フォルダに .meta があり、孤立した .meta がないことを検証する。
function Test-MetaFiles($GameDirs) {
    $problems = foreach ($gameDir in $GameDirs) {
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

# 作品の Assets 配下の .cs 全体 (Unity 層・PlayMode テストを含む) の空白整形を、1 回の起動でまとめて行う。
# コンパイル不要なので速い。
function Invoke-WhitespaceFormat($GameDirs, [switch]$Verify) {
    if (-not $GameDirs) { return }
    [string[]]$includes = $GameDirs | ForEach-Object { "$($_.Name)/Assets/" }
    [string[]]$verifyArgs = if ($Verify) { '--verify-no-changes' } else { @() }
    Invoke-Native dotnet format whitespace $GamesDir --folder --include @includes @verifyArgs
}

# lint はコンパイルしない検査 (.meta と空白) だけを行う。
# コードスタイル (IDE*) とアナライザー (CA*) は、check の dotnet build が EnforceCodeStyleInBuild と
# TreatWarningsAsErrors で同じ規則を検査するため、ここでは重ねない (dotnet format でのコンパイルが二重になり遅い)。
function Invoke-Lint {
    $targets = @(Get-TargetGameDirs)
    Test-MetaFiles $targets
    Test-EvidenceLinks $targets
    Invoke-WhitespaceFormat $targets -Verify
}

# evidence/README.md の相対リンク (画像など) を検証する。
# - リンク先が存在すること。
# - 証跡 (captures/<画像世代>/、runs/<撮影 ID>/) へのリンクは、公開中のもの (current.json が指すもの) であること。
#   画像は内容で決まる世代フォルダに置くため、画像が変わって撮り直すとリンク先が変わる。古い世代が消せずに残っていても見逃さない。
function Test-EvidenceLinks($GameDirs) {
    $problems = foreach ($gameDir in $GameDirs) {
        $evidence = Join-Path $gameDir.FullName 'evidence'
        $readme = Join-Path $evidence 'README.md'
        if (-not (Test-Path $readme)) { continue }
        $pointerPath = Join-Path $evidence 'current.json'
        $current = if (Test-Path $pointerPath) { Get-Content $pointerPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{ Generation = '(未公開)'; Run = '(未公開)' } }
        foreach ($match in [regex]::Matches((Get-Content $readme -Raw), '\]\((?!https?:|#)([^)\s#]+)')) {
            $link = $match.Groups[1].Value
            if (-not (Test-Path -LiteralPath (Join-Path $evidence $link))) { "リンク先がない ($readme): $link"; continue }
            if ($link -match '^captures/([^/]+)/' -and $Matches[1] -ne $current.Generation) {
                "公開中でない画像世代へのリンク ($readme): $link (公開中: $($current.Generation))"
            }
            if ($link -match '^runs/([^/]+)/' -and $Matches[1] -ne $current.Run) {
                "公開中でない撮影記録へのリンク ($readme): $link (公開中: $($current.Run)。撮影記録は撮影ごとに変わるので current.json にリンクする)"
            }
        }
    }
    if ($problems) {
        $problems | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        exit 1
    }
}

function Invoke-Check {
    if (-not $Staged -and -not $Game) {
        Invoke-Native dotnet build $Solution
        return
    }
    # 対象作品のテストプロジェクトをビルドすれば、参照している Core / App も一緒にビルドされる。
    foreach ($gameDir in Get-TargetGameDirs) {
        foreach ($project in Get-ChildItem (Join-Path $gameDir.FullName 'dotnet') -Recurse -Filter '*.Tests.csproj') {
            Invoke-Native dotnet build $project.FullName
        }
    }
}

# 整形の適用。スタイル・アナライザーの自動修正もかけるため、こちらは dotnet format をソリューション全体に使う。
function Invoke-Format {
    Invoke-Native dotnet format $Solution
    Invoke-WhitespaceFormat (Get-GameDirs)
}

function Invoke-New {
    if (-not $Game) { throw '作品番号を指定してください (例: dev new 001)。' }
    $number = Format-GameNumber $Game
    $entry = Import-Csv (Join-Path $RepoRoot 'plan/progress.csv') | Where-Object { [int]$_.No -eq [int]$number }
    $spec = if ($entry) { Get-Item (Join-Path $RepoRoot "plan/$($entry.仕様書)") } else { $null }
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
  capture [NNN]                 ビルドを撮影モードで起動し、証跡を evidence/ に公開 (画像は captures/、撮影記録は runs/。自動で終了)
  test                          全作品の Core / App テストを dotnet で実行 (高速、Unity 不要)
  test [NNN] -Unity [-Mode EditMode|PlayMode]
                                Unity 上でテストを実行
  lint [NNN] [-Staged]          .meta・証跡 README のリンク・空白整形の検証 (変更しない、コンパイル不要で速い)
  format                        コードを整形 (スタイル・アナライザーの自動修正を含む)
  check [NNN] [-Staged]         Core / App のビルド。型・コードスタイル・アナライザーを検査 (警告はエラー扱い)
                                -Staged: git にステージした変更が影響する作品だけ (pre-commit 用)
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
    'capture' {
        $gameDir = Get-RequiredGameDir
        $exe = Get-BuildExe $gameDir
        if (-not (Test-Path $exe)) { throw "ビルドがありません。先に dev build を実行してください: $exe" }
        $evidence = Join-Path $gameDir.FullName 'evidence'
        $log = Join-Path $gameDir.FullName 'Logs/capture.log'
        # ゲーム側の撮影台本 (ICaptureScenario) が画面を進めて撮影し、evidence/captures/<世代>/ に一式を公開して
        # evidence/current.json をその世代に切り替え、自分で終了する。撮影中はキー入力を無視する。
        # ゲームが終了しないときに待ち続けないよう、時間で打ち切る。
        $pointer = Join-Path $evidence 'current.json'
        $previous = if (Test-Path $pointer) { (Get-Content $pointer -Raw | ConvertFrom-Json).Generation } else { $null }
        $process = Start-Process $exe -PassThru -ArgumentList @(
            '-capture', "`"$evidence`"", '-logFile', "`"$log`"",
            '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720'
        )
        if (-not $process.WaitForExit($CaptureTimeoutSeconds * 1000)) {
            $process.Kill()
            throw "撮影が $CaptureTimeoutSeconds 秒で終わらないため打ち切りました。ログ: $log"
        }
        if ($process.ExitCode -ne 0) { throw "撮影に失敗しました (終了コード $($process.ExitCode))。ログ: $log" }
        $current = Get-Content $pointer -Raw | ConvertFrom-Json
        $generation = $current.Generation
        Write-Host "撮影しました: 画像 evidence/captures/$generation/、撮影記録 evidence/runs/$($current.Run)/manifest.json"
        if ($previous -and $previous -ne $generation) {
            Write-Host "画像が変わり、画像世代が $previous から $generation になりました。evidence/README.md の画像リンクを更新してください (dev lint が古いリンクを検出します)。" -ForegroundColor Yellow
        }
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
    'lint' { Invoke-Lint }
    'format' { Invoke-Format }
    'check' { Invoke-Check }
    'clean' { Invoke-Clean }
    default {
        Show-Help
        throw "不明なコマンド: $Command"
    }
}
