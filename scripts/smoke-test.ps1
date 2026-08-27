$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet restore .\SiriusTools.sln
    dotnet build .\SiriusTools.sln -c Release --no-restore

    dotnet run --project .\src\Sirius.MasterTool\Sirius.MasterTool.csproj -c Release --no-build -- --help
    dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -c Release --no-build -- --help
    dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -c Release --no-build -- chart --help
    dotnet run --project .\src\Sirius.AssetTool\Sirius.AssetTool.csproj -c Release --no-build -- episode --help
}
finally {
    Pop-Location
}
