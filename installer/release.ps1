# Publishes a release on GitHub with the installer attached; installed copies pick it up and update themselves.
# Needs what build.ps1 needs, plus the GitHub CLI (winget install GitHub.cli) signed in with access to the repository.
# Bump <Version> in ZeiLand\ZeiLand.csproj, commit and push first: the release is made from the pushed commit.
param(
    [string]$Repository = 'kaandemirr/ZeiLand', # keep in step with UpdateService.Repository
    [string]$Notes = ''                         # empty: GitHub writes notes from the commits since the last release
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
if (git -C $root status --porcelain) { throw 'Uncommitted changes: commit (and push) them first, so the release matches its source.' }
$commit = git -C $root rev-parse HEAD
git -C $root fetch --quiet
if (-not (git -C $root branch -r --contains $commit)) { throw "Commit $commit is not pushed yet." }

$installer = & (Join-Path $PSScriptRoot 'build.ps1') | Select-Object -Last 1
$version = (Select-Xml -Path (Join-Path $root 'ZeiLand\ZeiLand.csproj') -XPath '//Version').Node.InnerText

$arguments = @('release', 'create', "v$version", $installer.FullName, '--repo', $Repository, '--target', $commit, '--title', "ZeiLand $version")
$arguments += if ($Notes) { @('--notes', $Notes) } else { @('--generate-notes') }
gh @arguments
if ($LASTEXITCODE) { throw 'gh release create failed' }
