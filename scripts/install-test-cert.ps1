#Requires -RunAsAdministrator
# Self-signed test build. This is not a Microsoft Store release.
# Imports TermOrganize-Test.cer into LocalMachine\TrustedPeople, installs the
# Windows App Runtime package for this PC, then installs the app MSIX.

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

$runtimes = @(Get-ChildItem -LiteralPath $here -Filter *.msix |
    Where-Object { $_.Name -like "Microsoft.WindowsAppRuntime*-win-$arch.msix" } |
    Sort-Object Name)
if ($runtimes.Count -eq 0) {
    throw "No Microsoft.WindowsAppRuntime*-win-$arch.msix next to this script. Download the Windows App Runtime package for this PC into the same folder."
}

Write-Host "Importing the test certificate into LocalMachine\TrustedPeople."
Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

foreach ($runtime in $runtimes) {
    Write-Host "Installing Windows App Runtime $($runtime.Name)."
    try {
        Add-AppxPackage -Path $runtime.FullName
    } catch {
        $message = $_.Exception.Message
        if ($message -match '0x80073CFB|0x80073D06|higher version|already installed') {
            Write-Host "$($runtime.Name) is already installed."
        } else {
            throw
        }
    }
}

Write-Host "Installing $($msix.Name)."
Add-AppxPackage -Path $msix.FullName -ForceUpdateFromAnyVersion
Write-Host "Installed. This build is signed with a self-signed test certificate, not a public store certificate."
