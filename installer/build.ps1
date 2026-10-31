# Builds the ZEIGARN installer (out\ZEIGARN-Setup-<version>.exe):
# a self-contained ZeiLand (no .NET install needed) plus PawnIO's official setup.
# Needs the .NET 10 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'ZeiLand\ZeiLand.csproj'
$version = (Select-Xml -Path $project -XPath '//Version').Node.InnerText

$app = Join-Path $PSScriptRoot 'out\app'
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -o $app -nologo
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# PawnIO's signed setup, unmodified (GPL-2.0, https://github.com/namazso/PawnIO), pinned to a known hash.
$pawnio = Join-Path $PSScriptRoot 'vendor\PawnIO_setup.exe'
$pawnioUrl = 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe'
$pawnioHash = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
if (-not (Test-Path $pawnio) -or (Get-FileHash $pawnio -Algorithm SHA256).Hash -ne $pawnioHash) {
    New-Item -ItemType Directory -Force (Split-Path $pawnio) | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest $pawnioUrl -OutFile $pawnio -UseBasicParsing
    if ((Get-FileHash $pawnio -Algorithm SHA256).Hash -ne $pawnioHash) {
        Remove-Item $pawnio
        throw 'PawnIO_setup.exe does not match the expected hash'
    }
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup)' }

& $iscc /Qp "/DAppVersion=$version" (Join-Path $PSScriptRoot 'ZeiLand.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }
Get-Item (Join-Path $PSScriptRoot "out\ZEIGARN-Setup-$version.exe")
