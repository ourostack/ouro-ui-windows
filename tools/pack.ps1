# Packs the WinUI app and the MCP server into one MSIX signed with a self-signed certificate made in this run, trusts
# that certificate on this machine, and installs the package. The public .cer stays beside the MSIX (a release attaches
# both); the private key never leaves the run's certificate store.
param([Parameter(Mandatory)][string]$AppDir, [Parameter(Mandatory)][string]$McpDir, [Parameter(Mandatory)][string]$Generated, [Parameter(Mandatory)][string]$Manifest, [Parameter(Mandatory)][string]$Out, [string]$Name = "Ouro.Shop", [string]$Subject = "CN=Ouro Shop Sample", [string]$File = "OuroShop.msix")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$stage = Join-Path $Out "stage"
New-Item -ItemType Directory -Force -Path $stage, "$stage\Assets\Actions", "$stage\Assets\Mcp" | Out-Null
Copy-Item -Recurse -Force "$AppDir\*" $stage
Copy-Item -Recurse -Force "$McpDir\*" $stage
# A packaged app reads its resources from resources.pri; the unpackaged publish may name it after the app.
$pris = @(Get-ChildItem $stage -Filter *.pri)
Write-Host "PRI files in the publish: $(($pris | ForEach-Object Name) -join ', ')"
# Only the app's own PRI becomes resources.pri; the framework PRIs (Microsoft.UI*.pri) stay as they are.
if (Test-Path "$stage\OuroShop.WinUI.pri") { Copy-Item -Force "$stage\OuroShop.WinUI.pri" "$stage\resources.pri" }
Copy-Item -Force $Manifest "$stage\AppxManifest.xml"
Copy-Item -Force "$Generated\app-actions.json" "$stage\Assets\Actions\app-actions.json"
Copy-Item -Force "$Generated\mcpb.manifest.json" "$stage\Assets\Mcp\manifest.json"
foreach ($logo in @(@{ n = "StoreLogo.png"; s = 50 }, @{ n = "Square150x150Logo.png"; s = 150 }, @{ n = "Square44x44Logo.png"; s = 44 })) {
  $bmp = New-Object System.Drawing.Bitmap $logo.s, $logo.s
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(255, 0, 102, 204)); $g.Dispose()
  $bmp.Save("$stage\Assets\$($logo.n)", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$kit = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\makeappx.exe" | Sort-Object FullName | Select-Object -Last 1
$bin = Split-Path $kit.FullName
Write-Host "Windows SDK tools: $bin"
$msix = Join-Path $Out $File
& "$bin\makeappx.exe" pack /d $stage /p $msix /o | Write-Host
if ($LASTEXITCODE) { throw "makeappx failed: $LASTEXITCODE" }
$cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature -FriendlyName "Ouro Shop sample (self-signed in CI)" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$cer = Join-Path $Out ([IO.Path]::ChangeExtension($File, ".cer"))
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
& "$bin\signtool.exe" sign /fd SHA256 /sha1 $cert.Thumbprint /s My $msix | Write-Host
if ($LASTEXITCODE) { throw "signtool failed: $LASTEXITCODE" }
Add-AppxPackage -Path $msix
$pkg = Get-AppxPackage -Name $Name
$pkg | Format-List Name, PackageFullName, PackageFamilyName, InstallLocation, Status | Out-String | Write-Host
[ordered]@{
  packageFullName = $pkg.PackageFullName; packageFamilyName = $pkg.PackageFamilyName; installLocation = $pkg.InstallLocation
  msixBytes = (Get-Item $msix).Length
  extensions = @((Get-AppxPackageManifest $pkg).Package.Applications.Application | ForEach-Object { $app = $_.Id; $_.Extensions.ChildNodes | ForEach-Object { [ordered]@{ app = $app; category = $_.Category; name = $_.AppExtension.Name; alias = $_.AppExecutionAlias.ExecutionAlias.Alias; protocol = $_.Protocol.Name } } })
} | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $Out "package.json")
Get-Content (Join-Path $Out "package.json") | Write-Host
