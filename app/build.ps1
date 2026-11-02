# Build "OTL Tim May Rang.exe" bằng csc có sẵn trong .NET Framework 4.x, rồi đóng gói zip ở gốc repo.
# Chạy: powershell -ExecutionPolicy Bypass -File app\build.ps1
# Tên zip không mang số bản để link tải trên trang và README không phải đổi theo.
$ErrorActionPreference = 'Stop'
$goc = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$ra = Join-Path $PSScriptRoot 'build'
$thuMucGoi = Join-Path $ra 'OTL-Tim-May-Rang'
New-Item -ItemType Directory -Force $thuMucGoi | Out-Null
$exe = Join-Path $thuMucGoi 'OTL Tim May Rang.exe'

& $csc /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu `
  "/win32icon:$PSScriptRoot\may-rang-otl.ico" "/out:$exe" `
  /r:System.dll /r:System.Core.dll /r:System.Management.dll "/resource:$PSScriptRoot\may-rang-otl.ico,may-rang-otl.ico" "/resource:$PSScriptRoot\lich-su.html,lich-su.html" `
  "$PSScriptRoot\TimMayRang.cs"
if ($LASTEXITCODE -ne 0) { throw "csc lỗi $LASTEXITCODE" }

Copy-Item (Join-Path $PSScriptRoot 'DOC-TRUOC-KHI-CHAY.txt') $thuMucGoi -Force
$zip = Join-Path $goc 'OTL-Tim-May-Rang.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $thuMucGoi -DestinationPath $zip
"Đã build: $exe"
"Đã đóng gói: $zip ($([math]::Round((Get-Item $zip).Length/1KB)) KB)"
