$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet restore .\SiriusTools.sln
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore 失败。" }

    dotnet restore .\tests\Sirius.AssetTool.Tests\Sirius.AssetTool.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw "服务测试项目还原失败。" }

    dotnet build .\SiriusTools.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "解决方案构建失败。" }

    dotnet build .\tests\Sirius.AssetTool.Tests\Sirius.AssetTool.Tests.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "服务测试项目构建失败。" }

    dotnet run --project .\tests\Sirius.AssetTool.Tests\Sirius.AssetTool.Tests.csproj -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "服务回归测试失败。" }

    dotnet run --project .\src\Sirius.ToolboxUI\Sirius.ToolboxUI.csproj -c Release --no-build -- --self-test-ui
    if ($LASTEXITCODE -ne 0) { throw "ToolboxUI 自检失败。" }

    $toolboxUi = Join-Path $root 'src\Sirius.ToolboxUI\bin\Release\net10.0-windows\Sirius.ToolboxUI.exe'
    if (-not (Test-Path $toolboxUi)) {
        throw "Sirius.ToolboxUI build output was not found: $toolboxUi"
    }
}
finally {
    Pop-Location
}
