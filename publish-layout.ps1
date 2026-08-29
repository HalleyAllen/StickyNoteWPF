# 发布后布局整理脚本（由 StickyNoteWPF.csproj 的 AfterTargets=Publish 自动调用）
# 作用：把运行时 dll 按包结构收进 lib\ 子目录，原生库留在顶层，使 publish 目录整洁
param(
    [Parameter(Mandatory = $true)][string]$ProjectDir,
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [string]$Rid = ""
)

$ErrorActionPreference = "Stop"
$pub = [System.IO.Path]::GetFullPath((Join-Path $ProjectDir $PublishDir))
$exe = Join-Path $pub "StickyNote.exe"
if (-not (Test-Path $exe)) { throw "publish dir not found: $pub" }

$lib = Join-Path $pub "lib"
if (Test-Path $lib) { Remove-Item $lib -Recurse -Force }
New-Item -ItemType Directory -Path $lib -Force | Out-Null

$depsPath = Join-Path $pub "StickyNote.deps.json"
$deps = Get-Content $depsPath -Raw | ConvertFrom-Json

# 1) 把运行时托管程序集按 {packageId}/{version} 结构移进 lib\
#    注意：deps.json 可能包含多个 target（含空 target），需遍历全部
$moved = 0
foreach ($target in $deps.targets.PSObject.Properties) {
    if ($null -eq $target.Value) { continue }
    foreach ($pkg in $target.Value.PSObject.Properties) {
        $idVer = $pkg.Name -split '/'
        if ($idVer.Count -ne 2) { continue }
        if ($idVer[0] -eq "StickyNote") { continue }  # 应用主程序集留顶层
        if ($null -eq $pkg.Value.runtime) { continue }
        foreach ($rt in $pkg.Value.runtime.PSObject.Properties) {
            $name = $rt.Name
            # System.Private.CoreLib 是 host 从 app 目录硬加载的运行时核心，不能收进 lib\（否则启动即崩）
            if ($name -eq "System.Private.CoreLib.dll") { continue }
            $src = Join-Path $pub $name
            if (-not (Test-Path $src)) { continue }
            $destDir = Join-Path (Join-Path $lib $idVer[0]) $idVer[1]
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            Move-Item $src (Join-Path $destDir $name) -Force
            $moved++
        }
    }
}

# 2) 原生库（deps 的 native 资产）移回顶层，与 apphost 同级
foreach ($target in $deps.targets.PSObject.Properties) {
    if ($null -eq $target.Value) { continue }
    foreach ($pkg in $target.Value.PSObject.Properties) {
        if ($null -eq $pkg.Value.native) { continue }
        foreach ($n in $pkg.Value.native.PSObject.Properties) {
            $f = [System.IO.Path]::GetFileName($n.Name)
            $src = Join-Path $lib $f
            if (Test-Path $src) { Move-Item $src (Join-Path $pub $f) -Force }
        }
    }
}

# 3) runtimeconfig.json 增加 lib 探测路径（插在 configProperties 块之后）
$rcPath = Join-Path $pub "StickyNote.runtimeconfig.json"
$content = [System.IO.File]::ReadAllText($rcPath)
if ($content -notmatch "additionalProbingPaths") {
    $pattern = '("configProperties"\s*:\s*\{[^}]*\})'
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $m.Value + ",`n    `"additionalProbingPaths`": [`"lib`"]" }
    $content = [regex]::Replace($content, $pattern, $evaluator)
    [System.IO.File]::WriteAllText($rcPath, $content)
}

# 4) 清理带 RID 的 build 中间产物目录（如 win-x86\），避免整套重复副本留在根目录
if ($Rid) {
    $ridDir = Join-Path $pub $Rid
    if (Test-Path $ridDir) { Remove-Item $ridDir -Recurse -Force }
}

Write-Output "publish-layout: moved=$moved, top=$((Get-ChildItem $pub -File).Count) files"
