# Builds WallScape with the .NET Framework compiler that ships with Windows.
# No SDK install required.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    throw "csc.exe not found at $csc"
}

$sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) {
    throw "No source files found."
}

$output = Join-Path $root "WallScape.exe"
$manifest = Join-Path $root "app.manifest"

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/langversion:5",
    "/win32manifest:$manifest",
    "/out:$output",
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Xml.dll"
)

& $csc $arguments $sources
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$config = Join-Path $root "app.config"
if (Test-Path $config) {
    Copy-Item $config (Join-Path $root "WallScape.exe.config") -Force
}

$size = (Get-Item $output).Length
Write-Host ("Built {0} ({1:N0} bytes)" -f $output, $size)
