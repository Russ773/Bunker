<#
  Packs the published app into an MSIX for the Microsoft Store.
  The Store signs it after certification, so the package is left unsigned.

  Example:
    .\msix\build-msix.ps1 -PublishDir publish -Version 0.0.10 `
      -IdentityName "12345Name.Bunker" -Publisher "CN=00000000-0000-0000-0000-000000000000" `
      -PublisherDisplayName "Your Name"
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $IdentityName,
    [Parameter(Mandatory)] [string] $Publisher,
    [Parameter(Mandatory)] [string] $PublisherDisplayName,
    [string] $Output = "dist\Bunker-$Version.msix"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Store packages need a first version part above 0 and a last part of 0, so 1.0.1 becomes 1.0.1.0.
$v = [Version]$Version
if ($v.Major -lt 1) { throw "Store packages need a version of 1.0.0 or higher (got $Version)." }
$msixVersion = "{0}.{1}.{2}.0" -f $v.Major, $v.Minor, [Math]::Max($v.Build, 0)

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe (Windows SDK) was not found.' }

$stage = Join-Path ([IO.Path]::GetTempPath()) ("bunker-msix-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage, "$stage\Assets" | Out-Null
try {
    Copy-Item (Join-Path $PublishDir '*') $stage -Recurse -Force

    # Logos for the Start menu and the Store, scaled from the app logo.
    Add-Type -AssemblyName System.Drawing
    $src = [Drawing.Image]::FromFile((Join-Path $root 'assets\logo-512.png'))
    foreach ($logo in @{ 'StoreLogo' = 50; 'Square44x44Logo' = 44; 'Square150x150Logo' = 150 }.GetEnumerator()) {
        $bmp = New-Object Drawing.Bitmap $logo.Value, $logo.Value
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.DrawImage($src, 0, 0, $logo.Value, $logo.Value)
        $g.Dispose()
        $bmp.Save("$stage\Assets\$($logo.Key).png", [Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
    $src.Dispose()

    $manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
    $manifest = $manifest.Replace('{{IDENTITY_NAME}}', $IdentityName).Replace('{{PUBLISHER}}', $Publisher).
        Replace('{{VERSION}}', $msixVersion).Replace('{{PUBLISHER_DISPLAY_NAME}}', $PublisherDisplayName)
    Set-Content (Join-Path $stage 'AppxManifest.xml') $manifest -Encoding UTF8

    New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
    & $makeappx.FullName pack /d $stage /p $Output /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE" }
    Write-Host "Created $Output (package version $msixVersion)"
}
finally { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
