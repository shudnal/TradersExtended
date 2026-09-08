# Validate build provenance before manual upload. This script never contacts a publishing service.
param(
    [string]$ProjectRoot = $PSScriptRoot,
    [switch]$WriteReceipt
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$receiptPath = Join-Path $ProjectRoot 'package/release-receipt.json'
$entries = @(Get-ChildItem -LiteralPath $ProjectRoot -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($ProjectRoot.Length + 1).Replace('\', '/')
    if ($relative -match '(^|/)(\.git|bin|obj|tests|\.github)(/|$)' -or
        $relative.StartsWith('package/images/') -or $relative -eq 'package/release-receipt.json') { return }
    if (($_.Extension -in @('.cs', '.csproj', '.sln', '.props', '.targets', '.ps1', '.cmd', '.md')) -or
        $relative -match '^Configs/' -or $relative -match '^Libs/.*\.dll$' -or
        $relative -match '^package/.*/manifest\.json$' -or
        $relative -eq 'package/thunderstore/TradersExtended/icon.png') {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        "$relative=$hash"
    }
} | Sort-Object)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $sourceHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($entries -join "`n")))).Replace('-', '')
} finally { $sha.Dispose() }
$artifacts = @('package/thunderstore/TradersExtended.zip', 'package/nexus/TradersExtended.zip')
$artifactHashes = [ordered]@{}
foreach ($relative in $artifacts) {
    $path = Join-Path $ProjectRoot $relative
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package '$relative'. Rebuild the project first." }
    $artifactHashes[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
if ($WriteReceipt) {
    [ordered]@{ SourceHash = $sourceHash; Artifacts = $artifactHashes } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $receiptPath -Encoding UTF8
    Write-Host 'Recorded package build provenance.'
    return
}
if (!(Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'No package build receipt exists. Rebuild before publishing.' }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.SourceHash -ne $sourceHash) { throw 'Sources or package metadata changed after the last build. Rebuild before publishing.' }
foreach ($relative in $artifacts) {
    if ($receipt.Artifacts.PSObject.Properties[$relative].Value -ne $artifactHashes[$relative]) {
        throw "Package '$relative' changed after the last build. Rebuild before publishing."
    }
}
Write-Host 'Package build provenance verified.'
