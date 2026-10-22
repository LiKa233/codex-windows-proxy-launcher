#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$SkillRepository)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path (Split-Path -Parent $scriptDirectory) 'src'
$skill = Join-Path ([IO.Path]::GetFullPath($SkillRepository)) 'skills\codex-windows-proxy'
if (-not (Test-Path -LiteralPath (Join-Path $skill 'SKILL.md'))) { throw 'Target skill entrypoint was not found.' }
$target = Join-Path $skill 'assets\launcher'
[void][IO.Directory]::CreateDirectory($target)
$hashes = [ordered]@{}
foreach ($name in @('Launcher.Core.psm1', 'Launcher.ps1', 'Codex Proxy Launcher.cmd', 'Configure Proxy.cmd', 'Check Proxy.cmd', 'launcher.config.example.json')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target $name) -Force
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{ repository = 'https://github.com/LiKa233/codex-windows-proxy-launcher'; version = '1.0.0-rc.1'; sourceDirectory = 'src'; sha256 = $hashes }
[IO.File]::WriteAllText((Join-Path $target 'UPSTREAM.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output 'Skill payload exported with SHA-256 provenance.'
