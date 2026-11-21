# 发布后布局整理脚本（由 StickyNoteWPF.csproj 的 AfterTargets=Publish 自动调用）
# 作用：清理带 RID 的 build 中间产物目录（如 win-x86\）和残留的 lib\，避免重复副本留在根目录。
# 注意：自包含 WPF 的托管 dll 必须平铺在发布根目录（deps.json 的加载路径指向根目录），不能收进子目录。
param(
    [Parameter(Mandatory = $true)][string]$ProjectDir,
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [string]$Rid = ""
)

$ErrorActionPreference = "Stop"
$pub = [System.IO.Path]::GetFullPath((Join-Path $ProjectDir $PublishDir))
$exe = Join-Path $pub "StickyNote.exe"
if (-not (Test-Path $exe)) { throw "publish dir not found: $pub" }

# 1) 清理带 RID 的 build 中间产物目录（如 win-x86\），避免整套重复副本留在根目录
if ($Rid) {
    $ridDir = Join-Path $pub $Rid
    if (Test-Path $ridDir) { Remove-Item $ridDir -Recurse -Force }
}

# 2) 清理历史方案遗留的 lib\ 子目录（托管 dll 必须平铺在根目录）
$libDir = Join-Path $pub "lib"
if (Test-Path $libDir) { Remove-Item $libDir -Recurse -Force }

# 3) 确保 Resources\（托盘图标等）复制到发布目录：Content 复制在带 RID 发布时可能被跳过
$srcRes = Join-Path $ProjectDir "Resources"
$dstRes = Join-Path $pub "Resources"
if (Test-Path $srcRes) {
    New-Item -ItemType Directory -Path $dstRes -Force | Out-Null
    Copy-Item (Join-Path $srcRes "*") $dstRes -Recurse -Force
}

Write-Output "publish-layout: cleaned, top=$((Get-ChildItem $pub -File).Count) files"
