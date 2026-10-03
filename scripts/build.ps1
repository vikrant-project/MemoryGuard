param(
    [string]$Configuration = "Debug"
)

$dotnet = "C:\Users\soulc\.dotnet\dotnet.exe"
$env:DOTNET_ROOT = "C:\Users\soulc\.dotnet"
$env:PATH = "C:\Users\soulc\.dotnet;C:\Users\soulc\.dotnet\tools;" + $env:PATH

Write-Host "Building MemoryGuard solution ($Configuration)..."
& $dotnet build MemoryGuard.sln -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}
Write-Host "Build succeeded!"
