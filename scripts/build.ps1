$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
dotnet publish "$projectRoot\src\VrcPhotoPost.csproj" -c Release -r win-x64 --self-contained true -o "$projectRoot\artifacts\release"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "App: $projectRoot\artifacts\release\VrcPhotoPost.exe"
