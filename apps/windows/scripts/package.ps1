param(
  [string]$AppVersion = $(if ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME.TrimStart('v') } else { '0.1.0' }),
  [string]$Runtime = 'win-x64',
  [string]$SingBoxVersion = '1.13.11'
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Resolve-Path (Join-Path $ScriptDir '..\..\..')
$ProjectPath = Join-Path $RootDir 'apps\windows\src\Porkn.Windows\Porkn.Windows.csproj'
$ReleaseDir = Join-Path $RootDir 'release\windows'
$DepsDir = Join-Path $RootDir '.build\deps\windows'
$PublishDir = Join-Path $ReleaseDir "porkn-windows-x64"
$ZipPath = Join-Path $ReleaseDir 'porkn-windows-x64.zip'
$ShaPath = Join-Path $ReleaseDir 'SHA256SUMS-windows.txt'
$SingBoxZip = Join-Path $DepsDir "sing-box-$SingBoxVersion-windows-amd64.zip"
$SingBoxDir = Join-Path $DepsDir "sing-box-$SingBoxVersion-windows-amd64"
$SingBoxExe = Join-Path $SingBoxDir 'sing-box.exe'

New-Item -ItemType Directory -Force -Path $ReleaseDir, $DepsDir | Out-Null
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir, $ZipPath, $ShaPath

if (!(Test-Path $SingBoxExe)) {
  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $SingBoxDir
  if (!(Test-Path $SingBoxZip)) {
    $url = "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/sing-box-$SingBoxVersion-windows-amd64.zip"
    Write-Host "Downloading sing-box: $url"
    Invoke-WebRequest -Uri $url -OutFile $SingBoxZip
  }
  New-Item -ItemType Directory -Force -Path $SingBoxDir | Out-Null
  Expand-Archive -Path $SingBoxZip -DestinationPath $SingBoxDir -Force
  $found = Get-ChildItem -Path $SingBoxDir -Recurse -Filter 'sing-box.exe' | Select-Object -First 1
  if (!$found) { throw "sing-box.exe missing after extracting $SingBoxZip" }
  if ($found.FullName -ne $SingBoxExe) { Copy-Item $found.FullName $SingBoxExe -Force }
}

Write-Host "Publishing porkn Windows $AppVersion"
dotnet publish $ProjectPath `
  -c Release `
  -r $Runtime `
  --self-contained true `
  -p:Version=$AppVersion `
  -p:AssemblyVersion=$AppVersion `
  -p:FileVersion=$AppVersion `
  -o $PublishDir

$ResourceBin = Join-Path $PublishDir 'Resources\bin'
New-Item -ItemType Directory -Force -Path $ResourceBin | Out-Null
Copy-Item $SingBoxExe (Join-Path $ResourceBin 'sing-box.exe') -Force

Compress-Archive -Path $PublishDir -DestinationPath $ZipPath -Force
$hash = (Get-FileHash -Algorithm SHA256 $ZipPath).Hash.ToLowerInvariant()
"$hash  $ZipPath" | Set-Content -Path $ShaPath -Encoding utf8

Write-Host 'Windows release artifacts:'
Get-Item $ZipPath, $ShaPath | Format-Table FullName, Length
