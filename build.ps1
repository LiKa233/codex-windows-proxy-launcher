#requires -Version 5.1
[CmdletBinding()]
param([string]$OutputDirectory)
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $scriptDirectory 'dist' }
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$version = '1.0.0-rc.1'
$source = Join-Path $scriptDirectory 'src'
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows x64 .NET Framework C# compiler is required to build. No system changes were made.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$portable = Join-Path $output 'portable'
[void][IO.Directory]::CreateDirectory($portable)
$files = @('Launcher.Core.psm1', 'Launcher.ps1', 'Codex Proxy Launcher.cmd', 'Configure Proxy.cmd', 'Check Proxy.cmd', 'launcher.config.example.json')
foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $portable $name) -Force }
$exe = Join-Path $portable 'CodexProxyLauncher.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ('/out:' + $exe) ('/resource:' + (Join-Path $portable 'Launcher.Core.psm1') + ',Launcher.Core.psm1') ('/resource:' + (Join-Path $portable 'Launcher.ps1') + ',Launcher.ps1') (Join-Path $source 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed: $LASTEXITCODE" }
$payloadCheck = Start-Process -FilePath $exe -ArgumentList '--verify-payload' -WindowStyle Hidden -Wait -PassThru
if ($payloadCheck.ExitCode -ne 0) { throw 'EXE embedded payload verification failed.' }
foreach ($name in @('README.md', 'LICENSE', 'VALIDATION.md')) { Copy-Item -LiteralPath (Join-Path $scriptDirectory $name) -Destination $portable -Force }
$zip = Join-Path $output ('codex-proxy-launcher-' + $version + '-windows-x64.zip')
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $portable -File | Select-Object -ExpandProperty FullName) -DestinationPath $zip -CompressionLevel Optimal
Copy-Item -LiteralPath $exe -Destination (Join-Path $output 'CodexProxyLauncher.exe') -Force
$checksums = foreach ($name in @('CodexProxyLauncher.exe', [IO.Path]::GetFileName($zip))) {
    $hash = Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256
    $hash.Hash.ToLowerInvariant() + '  ' + $name
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
Write-Output "Built $version. No Codex client was started."
Write-Output $zip
