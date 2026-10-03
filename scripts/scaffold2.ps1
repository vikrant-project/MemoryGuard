$dotnet = "C:\Users\soulc\.dotnet\dotnet.exe"
$env:DOTNET_ROOT = "C:\Users\soulc\.dotnet"

Write-Host "Creating remaining projects..."

# System, Security, Monitoring, Cleanup, UI
& $dotnet new classlib -n MemoryGuard.System -o "src/MemoryGuard.System" -f net8.0 --force
& $dotnet new classlib -n MemoryGuard.Security -o "src/MemoryGuard.Security" -f net8.0 --force
& $dotnet new classlib -n MemoryGuard.Monitoring -o "src/MemoryGuard.Monitoring" -f net8.0 --force
& $dotnet new classlib -n MemoryGuard.Cleanup -o "src/MemoryGuard.Cleanup" -f net8.0 --force
& $dotnet new classlib -n MemoryGuard.UI -o "src/MemoryGuard.UI" -f net8.0 --force

# App
& $dotnet new wpf -n MemoryGuard.App -o "src/MemoryGuard.App" -f net8.0 --force

# Tests & Harness
& $dotnet new xunit -n MemoryGuard.IntegrationTests -o "tests/MemoryGuard.IntegrationTests" -f net8.0 --force
& $dotnet new xunit -n MemoryGuard.SafetyTests -o "tests/MemoryGuard.SafetyTests" -f net8.0 --force
& $dotnet new console -n MemoryGuard.TestHarness -o "tests/MemoryGuard.TestHarness" -f net8.0 --force

# Add all to solution
Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object {
    & $dotnet sln MemoryGuard.sln add $_.FullName
}

Write-Host "All projects created and registered in solution."
