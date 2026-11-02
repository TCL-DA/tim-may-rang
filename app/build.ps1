# Build "OTL Tim May Rang.exe" bằng csc có sẵn trong .NET Framework 4.x, rồi đóng gói zip ở gốc repo.
# Chạy: powershell -ExecutionPolicy Bypass -File app\build.ps1
$ErrorActionPreference = 'Stop'
$goc = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$ban = '1.0'
$ra = Join-Path $PSScriptRoot 'build'
$thuMucGoi = Join-Path $ra "OTL-Tim-May-Rang-v$ban"
New-Item -ItemType Directory -Force $thuMucGoi | Out-Null
$exe = Join-Path $thuMucGoi 'OTL Tim May Rang.exe'

# Tên resource = đường dẫn trang gọi (index.html, tai-nguyen/...).
$tn = @(
  "/resource:$goc\index.html,index.html",
  "/resource:$goc\tai-nguyen\logo-otl.svg,tai-nguyen/logo-otl.svg",
  "/resource:$goc\tai-nguyen\may-rang.jpg,tai-nguyen/may-rang.jpg",
  "/resource:$goc\tai-nguyen\icon-otl.png,tai-nguyen/icon-otl.png",
  "/resource:$PSScriptRoot\may-rang-otl.ico,may-rang-otl.ico"
)
& $csc /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu `
  "/win32icon:$PSScriptRoot\may-rang-otl.ico" "/out:$exe" `
  /r:System.dll /r:System.Core.dll /r:System.Net.Http.dll @tn "$PSScriptRoot\TimMayRang.cs"
if ($LASTEXITCODE -ne 0) { throw "csc lỗi $LASTEXITCODE" }

Copy-Item (Join-Path $PSScriptRoot 'DOC-TRUOC-KHI-CHAY.txt') $thuMucGoi -Force
$zip = Join-Path $goc "OTL-Tim-May-Rang-v$ban.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $thuMucGoi -DestinationPath $zip
"Đã build: $exe"
"Đã đóng gói: $zip ($([math]::Round((Get-Item $zip).Length/1KB)) KB)"
