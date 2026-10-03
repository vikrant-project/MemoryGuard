$dotnet = "C:\Users\soulc\.dotnet\dotnet.exe"
$env:DOTNET_ROOT = "C:\Users\soulc\.dotnet"
$env:PATH = "C:\Users\soulc\.dotnet;C:\Users\soulc\.dotnet\tools;" + $env:PATH

Write-Host "Publishing MemoryGuard Portable Release (x64 self-contained)..."

$outDir = "C:\memory-cleaner\artifacts\portable"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $dotnet publish "src/MemoryGuard.App/MemoryGuard.App.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $outDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Publish succeeded! Portable executable ready at $outDir\MemoryGuard.App.exe"

# Copy icon if needed
Copy-Item "src/MemoryGuard.App/Resources/app.ico" "$outDir\app.ico" -Force -ErrorAction SilentlyContinue
