param(
    [string]$Configuration = "Debug"
)

$dotnet = "C:\Users\soulc\.dotnet\dotnet.exe"
$env:DOTNET_ROOT = "C:\Users\soulc\.dotnet"
$env:PATH = "C:\Users\soulc\.dotnet;C:\Users\soulc\.dotnet\tools;" + $env:PATH

Write-Host "Running MemoryGuard tests ($Configuration)..."
& $dotnet test MemoryGuard.sln -c $Configuration --verbosity normal --logger "console;verbosity=detailed"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}
Write-Host "All tests passed!"
