$ErrorActionPreference = 'Stop'
# Removes only this app's registrations, never images or the browser profile.
foreach ($extension in @('.png','.jpg','.jpeg','.bmp','.tif','.tiff')) { Remove-ItemProperty -Path "HKCU:\Software\Classes\$extension\OpenWithProgids" -Name 'VrcPhotoPost.Image' -ErrorAction SilentlyContinue }
Remove-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'VRC Photo Post' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\VrcPhotoPost.Image' -Recurse -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\VrcPhotoPost\Capabilities' -Recurse -ErrorAction SilentlyContinue
