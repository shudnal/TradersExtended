# Updates only the local Thunderstore manifest version; never uploads a package.
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

if (!(Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Thunderstore manifest was not found: $ManifestPath"
}

$AssemblyVersion = $null
if (![Version]::TryParse($Version, [ref]$AssemblyVersion) -or
    $AssemblyVersion.Build -lt 0 -or $AssemblyVersion.Revision -gt 0) {
    throw "Expected a three-part package version or a four-part assembly version ending in .0: $Version"
}
$PackageVersion = $AssemblyVersion.ToString(3)
$Manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($null -eq $Manifest -or $Manifest -isnot [pscustomobject] -or
    $null -eq $Manifest.PSObject.Properties['version_number']) {
    throw "Thunderstore manifest must be a JSON object containing version_number: $ManifestPath"
}

$OldVersion = [string]$Manifest.version_number
if ($OldVersion -eq $PackageVersion) {
    Write-Host "Thunderstore manifest version is already $PackageVersion"
    return
}

$Manifest.version_number = $PackageVersion
$ManifestPath = (Resolve-Path -LiteralPath $ManifestPath).ProviderPath
$Content = ($Manifest | ConvertTo-Json -Depth 100) + [Environment]::NewLine
[IO.File]::WriteAllText($ManifestPath, $Content, ([Text.UTF8Encoding]::new($false)))
Write-Host "Thunderstore manifest version updated: $OldVersion -> $PackageVersion"
