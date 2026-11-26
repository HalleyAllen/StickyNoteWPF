# Keep this script ASCII-only: Windows PowerShell 5.1 reads BOM-less files
# using the system code page. Non-ASCII comments can break parsing there.
# Managed DLLs must remain at the publish root for deps.json resolution.
param(
    [Parameter(Mandatory = $true)][string]$ProjectDir,
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [ValidateSet("", "win-x86", "win-x64", "win-arm64")][string]$Rid = ""
)

$ErrorActionPreference = "Stop"
$pub = if ([System.IO.Path]::IsPathRooted($PublishDir)) {
    [System.IO.Path]::GetFullPath($PublishDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $ProjectDir $PublishDir))
}
$exe = Join-Path $pub "StickyNote.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "publish dir not found: $pub" }

function Remove-PublishChild([string]$Name) {
    $publishRoot = $pub.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $target = [System.IO.Path]::GetFullPath((Join-Path $pub $Name))
    if (-not $target.StartsWith($publishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup path is outside the publish directory: $target"
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}

# Remove duplicate RID build output and the legacy lib directory.
if ($Rid) { Remove-PublishChild $Rid }
Remove-PublishChild "lib"

# Ensure tray icon resources are present even when RID publishing skips content.
$srcRes = Join-Path $ProjectDir "Resources"
$dstRes = Join-Path $pub "Resources"
if (Test-Path -LiteralPath $srcRes) {
    New-Item -ItemType Directory -Path $dstRes -Force | Out-Null
    Copy-Item (Join-Path $srcRes "*") $dstRes -Recurse -Force
}

Write-Output "publish-layout: cleaned, top=$((Get-ChildItem $pub -File).Count) files"
