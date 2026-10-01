$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /out:"$PSScriptRoot\ComfyDrop.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Xml.Linq.dll "$PSScriptRoot\ComfyDrop.cs" "$PSScriptRoot\NetworkDrive.cs" "$PSScriptRoot\AppSettings.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
