[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$rootPrefix = $root + [System.IO.Path]::DirectorySeparatorChar
$generatedDirectories = [System.Collections.Generic.List[string]]::new()

foreach ($parent in @((Join-Path $root 'src'), (Join-Path $root 'tests'))) {
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        continue
    }

    foreach ($projectDirectory in Get-ChildItem -LiteralPath $parent -Directory -Force) {
        foreach ($name in @('bin', 'obj')) {
            $generatedDirectories.Add((Join-Path $projectDirectory.FullName $name))
        }
    }
}

$generatedDirectories.Add((Join-Path $root 'artifacts'))

foreach ($path in $generatedDirectories) {
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }

    $resolved = (Resolve-Path -LiteralPath $path).Path
    if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理工作区外路径：$resolved"
    }

    if ($PSCmdlet.ShouldProcess($resolved, '删除生成目录')) {
        try {
            Remove-Item -LiteralPath $resolved -Recurse -Force
            Write-Host "已清理：$resolved"
        }
        catch {
            Write-Warning "跳过正在使用的目录：$resolved ($($_.Exception.Message))"
        }
    }
}

$legacyDirectory = Join-Path $root 'src\Sirius.MasterUI'
if (Test-Path -LiteralPath $legacyDirectory -PathType Container) {
    $sourceFiles = Get-ChildItem -LiteralPath $legacyDirectory -File -Recurse -Force
    if ($sourceFiles.Count -eq 0 -and $PSCmdlet.ShouldProcess($legacyDirectory, '删除迁移后遗留的空目录')) {
        try {
            Remove-Item -LiteralPath $legacyDirectory -Recurse -Force
            Write-Host "已清理：$legacyDirectory"
        }
        catch {
            Write-Warning "跳过正在使用的旧目录：$legacyDirectory ($($_.Exception.Message))"
        }
    }
}

Write-Host '仓库生成文件清理完成。' -ForegroundColor Green
