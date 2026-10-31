# Publishes a release on GitHub with the installer attached; installed copies pick it up and update themselves.
# Needs what build.ps1 needs, plus the GitHub CLI (winget install GitHub.cli) signed in with access to the repository.
# Bump <Version> in ZeiLand\ZeiLand.csproj, commit and push first: the release is made from the pushed commit.
param(
    [string]$Repository = 'zeigarn/ZeiLand',    # keep in step with UpdateService.Repository
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

# The setup isn't code-signed, so SmartScreen stops its first run: the notes start with how to get past it.
$installNote = @'
**Kurulum:** Windows "Windows kişisel bilgisayarınızı korudu" derse **Ek bilgi → Yine de çalıştır**'a basın. Kurulum dosyası kod imzalı olmadığı için bu uyarı ilk kurulumda çıkar; virüs uyarısı değildir. Güncellemeler sessizce kurulur.

**Installing:** if Windows says "Windows protected your PC", click **More info → Run anyway**. The setup isn't code-signed, so this shows on the first install; it isn't a virus warning. Updates install silently.
'@

$arguments = @('release', 'create', "v$version", $installer.FullName, '--repo', $Repository, '--target', $commit, '--title', "ZeiLand $version")
# The notes go through a file: Windows PowerShell splits an argument with quotes in it on the way to gh.
# With --generate-notes, they go above GitHub's list of changes.
$notesFile = Join-Path ([IO.Path]::GetTempPath()) "zeiland-notes-$version.md"
[IO.File]::WriteAllText($notesFile, $(if ($Notes) { "$installNote`n$Notes" } else { $installNote }), (New-Object Text.UTF8Encoding $false))
$arguments += @('--notes-file', $notesFile)
if (-not $Notes) { $arguments += '--generate-notes' }
try { gh @arguments } finally { Remove-Item $notesFile -ErrorAction SilentlyContinue }
if ($LASTEXITCODE) { throw 'gh release create failed' }
