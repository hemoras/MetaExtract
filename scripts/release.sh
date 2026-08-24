#!/usr/bin/env bash
#
# Prepare et publie une nouvelle release de MetaExtract.
#
# Equivalent bash de l'ancien scripts/release.ps1 (conserve pour les
# personnes preferant PowerShell), pensee pour Git Bash (fournie avec
# Git pour Windows) afin d'eviter les soucis de Politique d'Execution
# PowerShell sur les scripts .ps1 non signes.
#
# Ce script :
#   1. Verifie qu'il n'y a pas de modifications non commitees.
#   2. Incremente le numero de version (Versioning Semantique - semver.org)
#      dans Directory.Build.props.
#   3. Commit ce changement de version (sauf si la version demandee est deja
#      la version actuelle : cas d'une premiere release qui tague l'existant).
#   4. Compile l'application en mode Release, autonome, fichier unique
#      (win-x64) - la meme commande dotnet publish utilisee jusqu'ici.
#   5. Empaquette le resultat dans releases/MetaExtract-vX.Y.Z-win-x64.zip
#      (dossier ignore par git, voir .gitignore).
#   6. Cree un tag git annote vX.Y.Z.
#   7. Pousse le commit et le tag vers origin.
#   8. Si l'outil GitHub CLI (gh) est installe et connecte, cree
#      automatiquement la Release GitHub et y attache le zip. Sinon,
#      affiche les instructions pour le faire manuellement sur GitHub.
#
# Usage :
#   scripts/release.sh                                    # incremente le PATCH
#   scripts/release.sh --bump minor                       # incremente le MINOR
#   scripts/release.sh --bump major                       # incremente le MAJOR
#   scripts/release.sh --version 1.0.0                     # force la version 1.0.0
#   scripts/release.sh --skip-push --skip-github-release   # test en local uniquement
#
set -euo pipefail

# --- Chemins ---------------------------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROPS_PATH="$REPO_ROOT/Directory.Build.props"
APP_PROJECT="$REPO_ROOT/MetaExtract.App"
PUBLISH_DIR="$APP_PROJECT/bin/Release/net8.0-windows/win-x64/publish"
RELEASES_DIR="$REPO_ROOT/releases"
GITHUB_REPO_URL="https://github.com/hemoras/MetaExtract"

BUMP="patch"
VERSION=""
SKIP_PUSH=false
SKIP_GH_RELEASE=false

usage() {
  cat <<EOF
Usage: $(basename "$0") [--bump patch|minor|major] [--version X.Y.Z] [--skip-push] [--skip-github-release]

  --bump TYPE             Partie du numero de version a incrementer :
                          patch (par defaut), minor ou major.
                          Ignore si --version est fourni.
  --version X.Y.Z         Force un numero de version precis au lieu
                          d'incrementer automatiquement.
  --skip-push             Ne pousse pas le commit/tag vers origin.
  --skip-github-release   Ne tente pas de creer la Release GitHub meme
                          si "gh" est disponible.

Exemples :
  $(basename "$0")
  $(basename "$0") --bump minor
  $(basename "$0") --version 1.0.0
  $(basename "$0") --skip-push --skip-github-release
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bump)
      [[ $# -ge 2 ]] || { echo "L'option --bump attend une valeur." >&2; exit 1; }
      BUMP="$2"; shift 2 ;;
    --version)
      [[ $# -ge 2 ]] || { echo "L'option --version attend une valeur." >&2; exit 1; }
      VERSION="$2"; shift 2 ;;
    --skip-push) SKIP_PUSH=true; shift ;;
    --skip-github-release) SKIP_GH_RELEASE=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Option inconnue : $1" >&2; usage; exit 1 ;;
  esac
done

step() { printf '\n\033[36m==> %s\033[0m\n' "$1"; }
die()  { printf '\n\033[31mERREUR : %s\033[0m\n' "$1" >&2; exit 1; }

cd "$REPO_ROOT"

# --- 1. Verifier l'etat du depot git ----------------------------------------

step "Verification de l'etat du depot git"

git rev-parse --is-inside-work-tree >/dev/null 2>&1 || die "Ce dossier n'est pas un depot git."

DIRTY="$(git status --porcelain)"
if [[ -n "$DIRTY" ]]; then
  die "Des modifications ne sont pas commitees. Commitez ou annulez-les avant de lancer une release :
$DIRTY"
fi

CURRENT_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
echo "Branche courante : $CURRENT_BRANCH"

# --- 2. Determiner le nouveau numero de version -----------------------------

step "Calcul du nouveau numero de version"

[[ -f "$PROPS_PATH" ]] || die "Introuvable : $PROPS_PATH"

CURRENT_VERSION="$(grep -oE '<VersionPrefix>[0-9]+\.[0-9]+\.[0-9]+</VersionPrefix>' "$PROPS_PATH" \
  | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' || true)"
[[ -n "$CURRENT_VERSION" ]] || die "Impossible de trouver <VersionPrefix>X.Y.Z</VersionPrefix> dans $PROPS_PATH"

if [[ -n "$VERSION" ]]; then
  [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "--version doit etre au format X.Y.Z (ex: 1.2.0), recu : $VERSION"
  NEW_VERSION="$VERSION"
else
  case "$BUMP" in
    major|minor|patch) ;;
    *) die "--bump doit etre major, minor ou patch (recu : $BUMP)" ;;
  esac

  IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT_VERSION"
  case "$BUMP" in
    major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
    minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
    patch) PATCH=$((PATCH + 1)) ;;
  esac
  NEW_VERSION="$MAJOR.$MINOR.$PATCH"
fi

echo "Version actuelle : $CURRENT_VERSION"
printf 'Nouvelle version : \033[32m%s\033[0m\n' "$NEW_VERSION"

TAG_NAME="v$NEW_VERSION"

if git tag --list "$TAG_NAME" | grep -q .; then
  die "Le tag $TAG_NAME existe deja."
fi

read -r -p "Confirmer la creation de la release $TAG_NAME ? (o/N) " CONFIRM
case "$CONFIRM" in
  o|O|y|Y) ;;
  *) echo "Release annulee."; exit 0 ;;
esac

# --- 3. Mettre a jour Directory.Build.props et commiter ---------------------

if [[ "$NEW_VERSION" == "$CURRENT_VERSION" ]]; then
  # Cas d'une premiere release qui tague simplement la version deja en place
  # (ex: --version 1.0.0 alors que Directory.Build.props vaut deja 1.0.0) :
  # rien a modifier ni a commiter, on passe directement au build.
  step "Directory.Build.props deja a jour ($NEW_VERSION) — pas de commit necessaire"
else
  step "Mise a jour de Directory.Build.props"
  sed -i -E "s#<VersionPrefix>[0-9]+\.[0-9]+\.[0-9]+</VersionPrefix>#<VersionPrefix>$NEW_VERSION</VersionPrefix>#" "$PROPS_PATH"
  git add "$PROPS_PATH"
  git commit -m "chore(release): bump version to $NEW_VERSION" || die "Le commit de version a echoue."
fi

# --- 4. Compilation Release (autonome, fichier unique, win-x64) ------------

step "Compilation de l'application (dotnet publish)"

if [[ -d "$PUBLISH_DIR" ]]; then
  rm -rf "$PUBLISH_DIR"
fi

dotnet publish "$APP_PROJECT" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  || die "dotnet publish a echoue."

[[ -d "$PUBLISH_DIR" ]] || die "Dossier de publication introuvable : $PUBLISH_DIR"

# --- 5. Empaquetage en zip ---------------------------------------------------

step "Creation du zip de release"

mkdir -p "$RELEASES_DIR"
ZIP_NAME="MetaExtract-$TAG_NAME-win-x64.zip"
ZIP_PATH="$RELEASES_DIR/$ZIP_NAME"
rm -f "$ZIP_PATH"

if command -v powershell >/dev/null 2>&1 || command -v powershell.exe >/dev/null 2>&1; then
  # Depuis Git Bash (MSYS), on passe par Compress-Archive (integre a Windows,
  # PowerShell 5.1+) plutot qu'un binaire zip externe pas toujours present.
  # Appel via -Command (et non -File) : non soumis a la Politique d'Execution
  # des scripts .ps1, donc aucun souci de signature ici.
  PS_BIN="$(command -v powershell.exe || command -v powershell)"
  WIN_PUBLISH_DIR="$(cygpath -w "$PUBLISH_DIR" 2>/dev/null || echo "$PUBLISH_DIR")"
  WIN_ZIP_PATH="$(cygpath -w "$ZIP_PATH" 2>/dev/null || echo "$ZIP_PATH")"
  "$PS_BIN" -NoProfile -Command "Compress-Archive -Path '$WIN_PUBLISH_DIR\\*' -DestinationPath '$WIN_ZIP_PATH'" \
    || die "La creation du zip via Compress-Archive a echoue."
elif command -v zip >/dev/null 2>&1; then
  # Environnement sans PowerShell (ex: WSL, Linux) : on utilise zip directement.
  (cd "$PUBLISH_DIR" && zip -rq "$ZIP_PATH" .) || die "La creation du zip via 'zip' a echoue."
else
  die "Ni 'powershell' ni 'zip' n'ont ete trouves pour creer l'archive. Installez l'un des deux, ou zippez manuellement le contenu de $PUBLISH_DIR."
fi

echo "Archive creee : $ZIP_PATH"

# --- 6. Tag git annote --------------------------------------------------------

step "Creation du tag git $TAG_NAME"
git tag -a "$TAG_NAME" -m "Release $TAG_NAME" || die "La creation du tag a echoue."

# --- 7. Push vers origin -------------------------------------------------------

if $SKIP_PUSH; then
  printf '\033[33mPush ignore (--skip-push).\033[0m\n'
else
  step "Envoi vers origin"
  git push origin "$CURRENT_BRANCH" || die "Le push du commit a echoue."
  git push origin "$TAG_NAME" || die "Le push du tag a echoue."
fi

# --- 8. Release GitHub (optionnelle, via gh CLI) -------------------------------

step "Release GitHub"

if $SKIP_GH_RELEASE; then
  printf '\033[33mCreation de la Release GitHub ignoree (--skip-github-release).\033[0m\n'
elif command -v gh >/dev/null 2>&1; then
  if gh release create "$TAG_NAME" "$ZIP_PATH" --title "$TAG_NAME" --generate-notes; then
    printf '\033[32mRelease GitHub creee : %s\033[0m\n' "$TAG_NAME"
  else
    printf '\033[33mLa creation automatique de la Release GitHub a echoue. Vous pouvez la creer manuellement :\033[0m\n'
    echo "  1. Allez sur $GITHUB_REPO_URL/releases/new"
    echo "  2. Choisissez le tag '$TAG_NAME'"
    echo "  3. Attachez le fichier : $ZIP_PATH"
  fi
else
  printf "\033[33mL'outil GitHub CLI (gh) n'est pas installe sur cette machine.\033[0m\n"
  echo "Pour creer la Release GitHub manuellement :"
  echo "  1. Allez sur $GITHUB_REPO_URL/releases/new"
  echo "  2. Choisissez le tag '$TAG_NAME'"
  echo "  3. Attachez le fichier : $ZIP_PATH"
fi

step "Termine"
printf '\033[32mRelease %s prete.\033[0m\n' "$TAG_NAME"
