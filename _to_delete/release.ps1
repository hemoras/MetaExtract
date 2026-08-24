<#
.SYNOPSIS
    Prepare et publie une nouvelle release de MetaExtract.

.DESCRIPTION
    Ce script automatise le cycle de release complet :
      1. Verifie qu'il n'y a pas de modifications non commitees.
      2. Incremente le numero de version (Versioning Semantique - semver.org)
         dans Directory.Build.props.
      3. Commit ce changement de version.
      4. Compile l'application en mode Release, autonome, fichier unique
         (win-x64) — la meme commande dotnet publish utilisee jusqu'ici.
      5. Empaquette le resultat dans releases\MetaExtract-vX.Y.Z-win-x64.zip
         (dossier ignore par git, voir .gitignore).
      6. Cree un tag git annote vX.Y.Z.
      7. Pousse le commit et le tag vers origin.
      8. Si l'outil GitHub CLI (gh) est installe et connecte, cree
         automatiquement la Release GitHub et y attache le zip. Sinon,
         affiche les instructions pour le faire manuellement sur GitHub.

.PARAMETER Bump
    Partie du numero de version a incrementer : Major, Minor ou Patch.
    Ignore si -Version est fourni. Par defaut : Patch.

.PARAMETER Version
    Force un numero de version precis (ex: "1.2.0") au lieu d'incrementer
    automatiquement.

.PARAMETER SkipPush
    Ne pousse pas le commit/tag vers origin (utile pour tester en local).

.PARAMETER SkipGitHubRelease
    Ne tente pas de creer la Release GitHub meme si "gh" est disponible.

.EXAMPLE
    .\scripts\release.ps1
    Incremente le PATCH (ex: 1.0.0 -> 1.0.1) et livre la release complete.

.EXAMPLE
    .\scripts\release.ps1 -Bump Minor
    Incremente le MINOR (ex: 1.0.1 -> 1.1.0).

.EXAMPLE
    .\scripts\release.ps1 -Version 2.0.0
    Force la version 2.0.0.

.EXAMPLE
    .\scripts\release.ps1 -SkipPush -SkipGitHubRelease
    Fait tout en local (version, build, zip, tag) sans rien envoyer sur
    GitHub. Utile pour verifier que tout fonctionne avant de livrer pour de
    vrai.
#>

[CmdletBinding()]
param(
    [ValidateSet("Major", "Minor", "Patch")]
    [string]$Bump = "Patch",

    [string]$Version,

    [switch]$SkipPush,

    [switch]$SkipGitHubRelease
)

$ErrorActionPreference = "Stop"

# --- Chemins -------------------------------------------------------------

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$PropsPath  = Join-Path $RepoRoot "Directory.Build.props"
$AppProject = Join-Path $RepoRoot "MetaExtract.App"
$PublishDir = Join-Path $AppProject "bin\Release\net8.0-windows\win-x64\publish"
$ReleasesDir = Join-Path $RepoRoot "releases"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Exit-WithError {
    param([string]$Message)
    Write-Host ""
    Write-Host "ERREUR : $Message" -ForegroundColor Red
    exit 1
}

Push-Location $RepoRoot
try {
    # --- 1. Verifier l'etat du depot git ---------------------------------

    Write-Step "Verification de l'etat du depot git"

    $gitStatus = git status --porcelain
    if ($LASTEXITCODE -ne 0) {
        Exit-WithError "Impossible de lire l'etat git. Ce dossier est-il bien un depot git ?"
    }
    if ($gitStatus) {
        Exit-WithError "Des modifications ne sont pas commitees. Commitez ou annulez-les avant de lancer une release :`n$gitStatus"
    }

    $currentBranch = git rev-parse --abbrev-ref HEAD
    Write-Host "Branche courante : $currentBranch"

    # --- 2. Determiner le nouveau numero de version ----------------------

    Write-Step "Calcul du nouveau numero de version"

    if (-not (Test-Path $PropsPath)) {
        Exit-WithError "Introuvable : $PropsPath"
    }

    $propsContent = Get-Content $PropsPath -Raw
    $match = [regex]::Match($propsContent, "<VersionPrefix>(\d+)\.(\d+)\.(\d+)</VersionPrefix>")
    if (-not $match.Success) {
        Exit-WithError "Impossible de trouver <VersionPrefix>X.Y.Z</VersionPrefix> dans $PropsPath"
    }

    $currentVersion = "$($match.Groups[1].Value).$($match.Groups[2].Value).$($match.Groups[3].Value)"

    if ($Version) {
        if ($Version -notmatch "^\d+\.\d+\.\d+$") {
            Exit-WithError "Le parametre -Version doit etre au format X.Y.Z (ex: 1.2.0), recu : $Version"
        }
        $newVersion = $Version
    }
    else {
        $major = [int]$match.Groups[1].Value
        $minor = [int]$match.Groups[2].Value
        $patch = [int]$match.Groups[3].Value

        switch ($Bump) {
            "Major" { $major++; $minor = 0; $patch = 0 }
            "Minor" { $minor++; $patch = 0 }
            "Patch" { $patch++ }
        }

        $newVersion = "$major.$minor.$patch"
    }

    Write-Host "Version actuelle : $currentVersion"
    Write-Host "Nouvelle version : $newVersion" -ForegroundColor Green

    $tagName = "v$newVersion"

    $existingTag = git tag --list $tagName
    if ($existingTag) {
        Exit-WithError "Le tag $tagName existe deja."
    }

    $confirmation = Read-Host "Confirmer la creation de la release $tagName ? (o/N)"
    if ($confirmation -notin @("o", "O", "y", "Y")) {
        Write-Host "Release annulee."
        exit 0
    }

    # --- 3. Mettre a jour Directory.Build.props et commiter --------------

    if ($newVersion -eq $currentVersion) {
        # Cas d'une premiere release qui tague simplement la version deja en
        # place (ex: -Version 1.0.0 alors que Directory.Build.props vaut deja
        # 1.0.0) : rien a modifier ni a commiter, on passe directement au build.
        Write-Step "Directory.Build.props deja a jour ($newVersion) — pas de commit necessaire"
    }
    else {
        Write-Step "Mise a jour de Directory.Build.props"

        $newPropsContent = $propsContent -replace "<VersionPrefix>\d+\.\d+\.\d+</VersionPrefix>", "<VersionPrefix>$newVersion</VersionPrefix>"
        Set-Content -Path $PropsPath -Value $newPropsContent -NoNewline -Encoding UTF8

        git add $PropsPath
        git commit -m "chore(release): bump version to $newVersion"
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "Le commit de version a echoue."
        }
    }

    # --- 4. Compilation Release (autonome, fichier unique, win-x64) ------

    Write-Step "Compilation de l'application (dotnet publish)"

    if (Test-Path $PublishDir) {
        Remove-Item -Path $PublishDir -Recurse -Force
    }

    dotnet publish $AppProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        Exit-WithError "dotnet publish a echoue."
    }

    if (-not (Test-Path $PublishDir)) {
        Exit-WithError "Dossier de publication introuvable : $PublishDir"
    }

    # --- 5. Empaquetage en zip --------------------------------------------

    Write-Step "Creation du zip de release"

    if (-not (Test-Path $ReleasesDir)) {
        New-Item -ItemType Directory -Path $ReleasesDir | Out-Null
    }

    $zipName = "MetaExtract-$tagName-win-x64.zip"
    $zipPath = Join-Path $ReleasesDir $zipName

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $zipPath

    Write-Host "Archive creee : $zipPath" -ForegroundColor Green

    # --- 6. Tag git annote -------------------------------------------------

    Write-Step "Creation du tag git $tagName"

    git tag -a $tagName -m "Release $tagName"
    if ($LASTEXITCODE -ne 0) {
        Exit-WithError "La creation du tag a echoue."
    }

    # --- 7. Push vers origin -------------------------------------------------

    if ($SkipPush) {
        Write-Host "Push ignore (-SkipPush)." -ForegroundColor Yellow
    }
    else {
        Write-Step "Envoi vers origin"

        git push origin $currentBranch
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "Le push du commit a echoue."
        }

        git push origin $tagName
        if ($LASTEXITCODE -ne 0) {
            Exit-WithError "Le push du tag a echoue."
        }
    }

    # --- 8. Release GitHub (optionnelle, via gh CLI) ------------------------

    Write-Step "Release GitHub"

    $ghAvailable = Get-Command gh -ErrorAction SilentlyContinue

    if ($SkipGitHubRelease) {
        Write-Host "Creation de la Release GitHub ignoree (-SkipGitHubRelease)." -ForegroundColor Yellow
    }
    elseif ($ghAvailable) {
        gh release create $tagName $zipPath --title $tagName --generate-notes
        if ($LASTEXITCODE -ne 0) {
            Write-Host "La creation automatique de la Release GitHub a echoue. Vous pouvez la creer manuellement (voir ci-dessous)." -ForegroundColor Yellow
            Write-Host "  1. Allez sur https://github.com/hemoras/MetaExtract/releases/new"
            Write-Host "  2. Choisissez le tag '$tagName'"
            Write-Host "  3. Attachez le fichier : $zipPath"
        }
        else {
            Write-Host "Release GitHub creee : $tagName" -ForegroundColor Green
        }
    }
    else {
        Write-Host "L'outil GitHub CLI (gh) n'est pas installe sur cette machine." -ForegroundColor Yellow
        Write-Host "Pour creer la Release GitHub manuellement :"
        Write-Host "  1. Allez sur https://github.com/hemoras/MetaExtract/releases/new"
        Write-Host "  2. Choisissez le tag '$tagName'"
        Write-Host "  3. Attachez le fichier : $zipPath"
    }

    Write-Step "Termine"
    Write-Host "Release $tagName prete." -ForegroundColor Green
}
finally {
    Pop-Location
}
