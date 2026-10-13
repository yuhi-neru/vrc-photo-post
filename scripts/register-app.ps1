param([string]$AppPath)
$ErrorActionPreference = 'Stop'
if (!$AppPath) {
  $packageRoot = Split-Path $PSScriptRoot -Parent
  $AppPath = Join-Path $packageRoot 'VrcPhotoPost.exe'
  if (!(Test-Path -LiteralPath $AppPath)) { $AppPath = Join-Path $packageRoot 'artifacts\release\VrcPhotoPost.exe' }
}
$AppPath = (Resolve-Path -LiteralPath $AppPath).Path
$class = 'HKCU:\Software\Classes\VrcPhotoPost.Image'
New-Item -Path "$class\shell\open\command" -Force | Out-Null
Set-Item -Path $class -Value 'Photo Post Image'
Set-Item -Path "$class\shell\open\command" -Value ('"' + $AppPath + '" "%1"')
New-Item -Path "$class\DefaultIcon" -Force | Out-Null
Set-Item -Path "$class\DefaultIcon" -Value ('"' + $AppPath + '",0')
foreach ($extension in @('.png','.jpg','.jpeg','.bmp','.tif','.tiff')) {
  $association = "HKCU:\Software\Classes\$extension\OpenWithProgids"
  New-Item -Path $association -Force | Out-Null
  New-ItemProperty -Path $association -Name 'VrcPhotoPost.Image' -PropertyType String -Value '' -Force | Out-Null
}
$capabilities = 'HKCU:\Software\VrcPhotoPost\Capabilities'
New-Item -Path "$capabilities\FileAssociations" -Force | Out-Null
New-ItemProperty -Path $capabilities -Name ApplicationName -Value 'VRC Photo Post' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $capabilities -Name ApplicationDescription -Value 'Photo library and X post composer' -PropertyType String -Force | Out-Null
foreach ($extension in @('.png','.jpg','.jpeg','.bmp','.tif','.tiff')) { New-ItemProperty -Path "$capabilities\FileAssociations" -Name $extension -Value 'VrcPhotoPost.Image' -PropertyType String -Force | Out-Null }
New-Item -Path 'HKCU:\Software\RegisteredApplications' -Force | Out-Null
New-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'VRC Photo Post' -Value 'Software\VrcPhotoPost\Capabilities' -PropertyType String -Force | Out-Null
Write-Host 'Registered. Select VRC Photo Post in Windows Settings > Apps > Default apps, or Open with > Choose another app. Defaults were not forced.'
