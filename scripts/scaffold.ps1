$dotnet = "C:\Users\soulc\.dotnet\dotnet.exe"
$env:DOTNET_ROOT = "C:\Users\soulc\.dotnet"

Write-Host "Scaffolding MemoryGuard projects..."

# Ensure directories
New-Item -ItemType Directory -Force -Path "src", "tests", "installer", "scripts", "docs", "artifacts" | Out-Null

# 1. MemoryGuard.Core
& $dotnet new classlib -n MemoryGuard.Core -o "src/MemoryGuard.Core" -f net8.0 --force

# 2. MemoryGuard.Settings
& $dotnet new classlib -n MemoryGuard.Settings -o "src/MemoryGuard.Settings" -f net8.0 --force

# 3. MemoryGuard.System
& $dotnet new classlib -n MemoryGuard.System -o "src/MemoryGuard.System" -f net8.0-windows --force

# 4. MemoryGuard.Security
& $dotnet new classlib -n MemoryGuard.Security -o "src/MemoryGuard.Security" -f net8.0-windows --force

# 5. MemoryGuard.Monitoring
& $dotnet new classlib -n MemoryGuard.Monitoring -o "src/MemoryGuard.Monitoring" -f net8.0-windows --force

# 6. MemoryGuard.Cleanup
& $dotnet new classlib -n MemoryGuard.Cleanup -o "src/MemoryGuard.Cleanup" -f net8.0-windows --force

# 7. MemoryGuard.UI
& $dotnet new classlib -n MemoryGuard.UI -o "src/MemoryGuard.UI" -f net8.0-windows --force

# 8. MemoryGuard.App
& $dotnet new wpf -n MemoryGuard.App -o "src/MemoryGuard.App" -f net8.0-windows --force

# 9. Tests
& $dotnet new xunit -n MemoryGuard.UnitTests -o "tests/MemoryGuard.UnitTests" -f net8.0 --force
& $dotnet new xunit -n MemoryGuard.IntegrationTests -o "tests/MemoryGuard.IntegrationTests" -f net8.0-windows --force
& $dotnet new xunit -n MemoryGuard.SafetyTests -o "tests/MemoryGuard.SafetyTests" -f net8.0-windows --force
& $dotnet new console -n MemoryGuard.TestHarness -o "tests/MemoryGuard.TestHarness" -f net8.0-windows --force

# Add all to solution
& $dotnet sln MemoryGuard.sln add (Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object { $_.FullName })

Write-Host "Scaffolding complete!"
