#Requires -RunAsAdministrator
# Self-signed test build. This is not a Microsoft Store release.
# Imports TermOrganize-Test.cer into LocalMachine\TrustedPeople, then installs
# the MSIX for this PC that sits in the same folder.

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$cer = Join-Path $here 'TermOrganize-Test.cer'
if (-not (Test-Path -LiteralPath $cer)) {
    throw "TermOrganize-Test.cer was not found next to this script."
}

$arch = 'x64'
if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
    $arch = 'arm64'
}

$msix = Get-ChildItem -LiteralPath $here -Filter "TermOrganize-*-win-$arch.msix" | Select-Object -First 1
if (-not $msix) {
    throw "No TermOrganize-*-win-$arch.msix next to this script. Download the package for this PC into the same folder."
}

Write-Host "Importing the test certificate into LocalMachine\TrustedPeople."
Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

Write-Host "Installing $($msix.Name)."
Add-AppxPackage -Path $msix.FullName -ForceUpdateFromAnyVersion
Write-Host "Installed. This build is signed with a self-signed test certificate, not a public store certificate."
