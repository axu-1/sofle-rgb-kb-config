param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "artifacts/$Runtime"
dotnet test "$root/tests/Sofle.Status.Tests/Sofle.Status.Tests.csproj" --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
dotnet publish "$root/src/Sofle.Status.Windows/Sofle.Status.Windows.csproj" `
    --configuration Release --runtime $Runtime --self-contained true --output $output
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed." }
Copy-Item "$root/README.md" "$output/README.md"
Compress-Archive -Path "$output/*" -DestinationPath "$root/artifacts/SofleStatus-$Runtime.zip" -Force
Write-Host "Ready: $root/artifacts/SofleStatus-$Runtime.zip"
