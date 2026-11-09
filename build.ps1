# Builds CursorBeacon.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
#   ./build.ps1                                   -> local build (auto-update disabled)
#   ./build.ps1 -Version 1.0.7 -Repo owner/name   -> what GitHub Actions runs
param(
    [string]$Version = "0.0.0",
    [string]$Repo = ""
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "csc.exe (.NET Framework 4.x) not found" }

$src   = Join-Path $here 'CursorBeacon.cs'
$out   = Join-Path $here 'CursorBeacon.exe'
$ico   = Join-Path $here 'CursorBeacon.ico'
$info  = Join-Path $here 'obj\BuildInfo.cs'
$stage = Join-Path $here 'obj\IconStage.exe'
New-Item -ItemType Directory -Force (Join-Path $here 'obj') | Out-Null

# Version + repo are baked into the exe so it knows where to look for updates.
@"
[assembly: System.Reflection.AssemblyVersion("$Version")]
[assembly: System.Reflection.AssemblyFileVersion("$Version")]
namespace CursorBeacon {
    internal static class BuildInfo {
        public const string Version = "$Version";
        public const string Repo = "$Repo";
    }
}
"@ | Set-Content -Encoding utf8 $info

$common = @('/nologo', '/optimize+', '/target:winexe', '/platform:anycpu',
            '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')

# Stage 1: build once to render the icon.
& $csc @common "/out:$stage" $src $info
if ($LASTEXITCODE -ne 0) { throw "Compile failed (stage 1)" }
$p = Start-Process -FilePath $stage -ArgumentList '--make-icon', "`"$ico`"" -Wait -PassThru
if ($p.ExitCode -ne 0 -or -not (Test-Path $ico)) { throw "Icon generation failed" }

# Stage 2: final exe with the icon embedded.
Get-Process CursorBeacon -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $out } | Stop-Process -Force -ErrorAction SilentlyContinue
& $csc @common "/win32icon:$ico" "/out:$out" $src $info
if ($LASTEXITCODE -ne 0) { throw "Compile failed (stage 2)" }

$kb = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "Built $out ($kb KB, version $Version)"
