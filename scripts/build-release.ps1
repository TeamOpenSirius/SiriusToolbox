[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$FrameworkDependent,
    [switch]$ReadyToRun
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj'
$artifactRoot = Join-Path $root 'artifacts'
$artifactName = "ToolboxUI-$RuntimeIdentifier"
if ($FrameworkDependent) {
    $artifactName += '-framework-dependent'
}
$publishDirectory = Join-Path $artifactRoot $artifactName

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "找不到 ToolboxUI 项目：$project"
}

$artifactRootFull = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$publishDirectoryFull = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $publishDirectoryFull.StartsWith($artifactRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "发布目录不在 artifacts 目录内：$publishDirectoryFull"
}

if (Test-Path -LiteralPath $publishDirectoryFull) {
    Remove-Item -LiteralPath $publishDirectoryFull -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

$selfContained = (-not $FrameworkDependent).ToString().ToLowerInvariant()
$readyToRunValue = $ReadyToRun.ToString().ToLowerInvariant()
$publishArguments = @(
    'publish',
    $project,
    '--configuration', 'Release',
    '--runtime', $RuntimeIdentifier,
    '--self-contained', $selfContained,
    '--output', $publishDirectoryFull,
    '-p:PublishSingleFile=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    "-p:PublishReadyToRun=$readyToRunValue",
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)
if (-not $FrameworkDependent) {
    $publishArguments += '-p:GenerateRuntimeConfigurationFiles=false'
}

Write-Host "正在发布 ToolboxUI ($RuntimeIdentifier，单文件，自包含=$selfContained)..." -ForegroundColor Cyan
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

$executable = Join-Path $publishDirectoryFull 'Sirius.ToolboxUI.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "发布完成但没有找到单文件程序：$executable"
}

$file = Get-Item -LiteralPath $executable
Write-Host "发布完成：$($file.FullName)" -ForegroundColor Green
Write-Host "文件大小：$([Math]::Round($file.Length / 1MB, 2)) MB"
