#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Game2Path)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$version = '0.2.0'
$game = (Resolve-Path -LiteralPath $Game2Path).ProviderPath
& dotnet test (Join-Path $PSScriptRoot 'Tests\Tests.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
$project = Join-Path $PSScriptRoot 'ClientPlugin\FNSPaintColorPicker.csproj'
& dotnet build $project --configuration Release --nologo "-p:Game2Path=$game"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
$output = Join-Path $PSScriptRoot "artifacts\v$version"
$null = New-Item -ItemType Directory -Path $output -Force
$dll = Join-Path $PSScriptRoot 'ClientPlugin\bin\Release\net10.0\FNSPaintColorPicker.dll'
$packageFiles = @()
foreach ($file in @($dll, (Join-Path $PSScriptRoot 'FNSPaintColorPicker.xml'), (Join-Path $PSScriptRoot 'README.md'), (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md'))) {
    $destination = Join-Path $output (Split-Path -Leaf $file)
    Copy-Item -LiteralPath $file -Destination $destination -Force
    $packageFiles += $destination
}
$archive = Join-Path $output "FNSPaintColorPicker-v$version-win-x64.zip"
Compress-Archive -LiteralPath $packageFiles -DestinationPath $archive -Force
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$checksum  $(Split-Path -Leaf $archive)`n", $utf8)
Write-Output "Release candidate: $archive"
Write-Output 'No game/SDK binaries included. Nothing deployed or published.'
