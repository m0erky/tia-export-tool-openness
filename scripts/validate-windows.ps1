param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "[1/3] Restoring..."
dotnet restore

Write-Host "[2/3] Building solution ($Configuration)..."
dotnet build TiaProjectExporter.sln -c $Configuration

Write-Host "[3/3] Running tests ($Configuration)..."
dotnet test tests/TiaProjectExporter.Tests/TiaProjectExporter.Tests.csproj -c $Configuration --no-build -v minimal

Write-Host "Validation pre-checks completed. Launch UI for end-to-end Openness validation."
