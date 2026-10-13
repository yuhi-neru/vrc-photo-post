$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
dotnet build "$projectRoot\src\VrcPhotoPost.csproj" -o "$projectRoot\artifacts\check"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$probeDirectory = Join-Path $projectRoot 'artifacts\probe-final'
$resultFile = Join-Path $probeDirectory 'results.json'
if (Test-Path -LiteralPath $resultFile) { Remove-Item -LiteralPath $resultFile }
$previousValue = $env:VRC_PHOTO_POST_PROBE_EXIT
try {
  $env:VRC_PHOTO_POST_PROBE_EXIT = '1'
  $probeProcess = Start-Process -FilePath "$projectRoot\artifacts\check\VrcPhotoPost.exe" -ArgumentList '--probe',('"'+$probeDirectory+'"') -WindowStyle Hidden -PassThru
  if (!$probeProcess.WaitForExit(60000)) { $probeProcess.Kill(); throw 'Probe timed out' }
  if (!(Test-Path -LiteralPath $resultFile)) { throw 'No probe result' }
  $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
  if (!$result.passed) { throw $result.error }
  $result.tests | Format-Table test,passed
  Write-Host "WebView2 runtime: $($result.runtime). Real X attachment remains a separate check."
} finally { $env:VRC_PHOTO_POST_PROBE_EXIT = $previousValue }
