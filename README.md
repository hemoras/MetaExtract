# MetaExtract

Application Windows (client lourd, WPF / .NET 8) pour extraire les
métadonnées d'un ou plusieurs dossiers de vidéos, à l'aide de **MediaInfo**
(l'exécutable `mediainfo.exe` déjà installé sur votre machine).

## Fonctionnalités

- Sélection d'un ou plusieurs dossiers, parcourus **récursivement**.
- Liste de champs de métadonnées **dynamique** : choisissez les colonnes à
  afficher/exporter et leur **ordre**. Le **nom du fichier est obligatoire
  et toujours en première position**.
- Champs disponibles : nom du fichier, dossier, chemin complet, extension,
  taille, date de modification, format du conteneur, durée, bitrate
  global, format vidéo, codec vidéo, profil, largeur, hauteur, résolution,
  FPS, bitrate vidéo, profondeur de couleur, ratio d'affichage, type de
  scan, sous-échantillonnage, format audio, codec audio, bitrate audio,
  mode de bitrate, canaux, fréquence d'échantillonnage, langue, chaîne,
  nombre de pistes audio, ainsi que saison, manche, Grand Prix et type de
  séance déduits du **nom de fichier** (voir « Personnaliser la
  reconnaissance des noms de fichiers » ci-dessous).
- La **langue audio** est affichée avec son nom complet (ex: « Français »
  plutôt que « fr »), via `langues.json` — voir plus bas.
- Si le format vidéo/audio brut renvoyé par MediaInfo est peu lisible, il
  est simplifié à l'affichage (ex: « MPEG Video » → « MPEG », « MPEG
  Audio » → « MP3 »). Le bitrate vidéo se rabat sur le bitrate global du
  fichier quand MediaInfo ne renvoie pas de bitrate spécifique à la piste
  vidéo (fréquent en MPEG-TS).
- Export **CSV** ou **Excel (.xlsx)**, au choix, avec les colonnes
  sélectionnées dans l'ordre choisi (boutons **Export CSV...** / **Export
  Excel...**), ou **Export complet...** : un troisième bouton qui exporte
  systématiquement un ensemble fixe de colonnes (Nom du fichier, Saison,
  Manche, Grand Prix, Type, Durée, Taille du fichier, Chaîne, Langue
  audio, Résolution, FPS, Codec vidéo, Bitrate vidéo, Codec audio, Bitrate
  audio, Type de scan, Ratio d'affichage, Dossier, Date de modification),
  quelles que soient les colonnes actuellement sélectionnées à l'écran —
  pratique pour un export complet et reproductible. Le format (CSV ou
  Excel) se choisit via l'extension dans la boîte de dialogue. C'est aussi
  cet ensemble de colonnes qui est présélectionné par défaut au tout
  premier lancement de l'application.
- Les dossiers, les colonnes choisies et le chemin vers `mediainfo.exe`
  sont mémorisés d'une session à l'autre (aucun dossier n'est présélectionné
  au tout premier lancement : la liste démarre vide).

## Prérequis

- **MediaInfo doit être installé sur la machine** (l'application ne
  l'embarque pas). Téléchargez la version "CLI" (ligne de commande) sur
  https://mediaarea.net/fr/MediaInfo/Download/Windows — vous obtenez un
  fichier `MediaInfo.exe` (ou `mediainfo.exe` selon le paquet).
  Au premier lancement de l'application, ouvrez **Paramètres** et indiquez
  le chemin vers cet exécutable.
- Pour **compiler** le projet : [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
  et Windows (le projet `MetaExtract.App` cible `net8.0-windows` / WPF).

## Personnaliser la reconnaissance des noms de fichiers

En plus des métadonnées lues par MediaInfo, MetaExtract déduit six
informations du **nom de fichier** lui-même : Saison, Manche, Grand Prix,
Type (de séance), Chaîne et Langue, via quatre fichiers JSON en texte
clair, **dans le même dossier que `MetaExtract.App.exe`** (facile à
retrouver, et compatible avec une utilisation "portable"). Seul
`filename_rules.json` est propre à la convention de nommage de chacun
(généré avec un exemple minimal au premier lancement) ; `grands_prix.json`,
`chaines_langues.json` et `langues.json` sont des données de référence
maintenues dans le projet et livrées déjà complètes à chaque build (voir le
détail de chacun plus bas) :

```
<dossier de MetaExtract.App.exe>\
├── settings.json           Paramètres généraux (chemin mediainfo.exe, dossiers, colonnes...)
├── filename_rules.json     Règles de reconnaissance (regex nommées)
├── grands_prix.json        (saison, manche) -> nom du Grand Prix
├── chaines_langues.json    chaîne -> langue principale
└── langues.json            code de langue -> nom complet (affichage)
```

Accès rapide à ce dossier : bouton **« Ouvrir le dossier de
configuration... »** dans **Paramètres**.

> Avant la 1.1.0, ces fichiers étaient stockés dans `%AppData%\MetaExtract`.
> S'il en existe déjà à cet ancien emplacement, ils sont automatiquement
> recopiés au nouvel emplacement au premier lancement d'une version ≥ 1.1.0
> (sans rien supprimer de l'ancien dossier).

### `filename_rules.json`

Chaque règle est une expression régulière (.NET) avec des groupes nommés
optionnels `manche`, `saison`, `type`, `chaine`. Les règles sont essayées
dans l'ordre du fichier ; la première qui correspond au nom de fichier
(sans son extension) est utilisée. Une règle peut aussi fournir `types`,
une table qui traduit la valeur brute captée par le groupe `type` (telle
qu'écrite dans le nom de fichier) vers le libellé à afficher — recherche
insensible à la casse ; si `types` est absent, ou si la valeur captée n'y
figure pas, elle est utilisée telle quelle :

```json
[
  {
    "nom": "Course F1 : \"<manche> GP <nom> <saison>[ - <type>][ (<chaine>)]\" ou \"<manche> GP <nom> <saison> <chaine>\"",
    "pattern": "^(?<manche>\\d{1,2})\\s+GP\\s+.+?\\s+(?<saison>\\d{4})(?:\\s*-\\s*(?<type>[^()]+?)\\s*(?:\\((?<chaine>[^()]+)\\))?|\\s*\\((?<chaine>[^()]+)\\)|\\s+(?<chaine>[^()\\s].*?))?\\s*$",
    "type_par_defaut": "Course"
  },
  {
    "nom": "Format \"Formula1.<saison>.Round<manche>.<nom>.<type>.<chaine>...\"",
    "pattern": "^Formula1\\.(?<saison>\\d{4})\\.Round(?<manche>\\d{1,2})\\..+?\\.(?<type>Race\\.Highlights|Qualifying\\.Reports|Qualifying\\.Highlights|Race\\.Preview|Post-Race|Qualifying|Preview|Quali1|Quali2|Race|Q1|Q2|FP1|FP2|FP3|FP4)\\.(?<chaine>[^.]+)",
    "types": {
      "Race": "Course",
      "Qualifying": "Qualifications",
      "Q1": "Qualifications 1",
      "Quali1": "Qualifications 1",
      "Q2": "Qualifications 2",
      "Quali2": "Qualifications 2",
      "Race.Highlights": "Résumé",
      "Qualifying.Reports": "Résumé",
      "Qualifying.Highlights": "Résumé Qualifications",
      "Preview": "Pre-Course",
      "Race.Preview": "Pre-Course",
      "Post-Race": "Post-Course",
      "FP1": "Essais Libres 1",
      "FP2": "Essais Libres 2",
      "FP3": "Essais Libres 3",
      "FP4": "Essais Libres 4"
    }
  }
]
```

Comportement de la première règle (ci-dessus) — trois formes possibles
après la saison, une seule s'applique par fichier :
- `01 GP Australie 1998 (TF1)` → chaîne entre parenthèses, sans type →
  `Saison=1998, Manche=1, Type=Course, Chaîne=TF1`.
- `01 GP Australie 1998 - Qualifications (TF1)` → tiret + type, chaîne
  entre parenthèses en option → `Type=Qualifications`.
- `08 GP Canada 2006 RAI` → chaîne "en clair", sans tiret ni parenthèses →
  `Saison=2006, Manche=8, Type=Course, Chaîne=RAI`.
- Si la chaîne contient « Multi » (ex: `(Multi 5)`), elle n'est **pas**
  calculée depuis le nom de fichier, quelle que soit la forme utilisée —
  dans ce cas, les pistes audio du fichier renseignent normalement chacune
  leur propre chaîne, déjà gérée par les métadonnées (voir plus bas).

Comportement de la seconde règle (format Kodi/scène, ex:
`Formula1.1998.Round01.Australia.Race.TF1.1080p.H264.French`) :
- → `Saison=1998, Manche=1, Type=Course, Chaîne=TF1`.
- Le nom du Grand Prix (`Australia` ici) n'est pas utilisé : comme pour
  toute règle, il est résolu séparément via `grands_prix.json`.
- `types` traduit les codes de séance du nom de fichier (`Race`, `FP1`,
  `Qualifying.Highlights`...) vers leur libellé français.

**Repli si aucune règle ne correspond** : plutôt que de renvoyer des
métadonnées vides, MetaExtract tente alors une détection minimale :
- **Saison** : premier nombre à 4 chiffres isolé du nom de fichier compris
  entre 1950 et l'année en cours (ex. un « 2160 » de résolution 4K, hors de
  cette plage, est ignoré). Si le nom de fichier n'en contient aucun, la
  même recherche est refaite dans le chemin du dossier parent.
- **Chaîne** : si le nom de fichier se termine par une expression entre
  parenthèses (parenthèse fermante optionnelle), cette expression — ou
  uniquement sa partie après un tiret si elle en contient un (ex.
  `(AFAVA - Motors TV)` → `Motors TV`) — est recherchée telle quelle dans
  `chaines_langues.json` ; si elle y figure, elle devient la chaîne
  détectée (la langue associée est alors résolue normalement, voir
  ci-dessous).
- Manche et Type restent vides dans ce cas.

### `grands_prix.json`

Le nom du Grand Prix n'est volontairement **pas** extrait du nom de
fichier (il peut varier pour une même course selon les sources : ex.
« Hollande » / « Pays-Bas »). Il est résolu à partir de la saison et de la
manche, via cette table :

```json
[
  { "saison": 1998, "manche": 1, "grand_prix": "Australie" }
]
```

Contrairement à `filename_rules.json` et `chaines_langues.json` (propres à
la convention de nommage de chacun), ce fichier est **une donnée de
référence maintenue dans le projet** (`MetaExtract.App\grands_prix.json`,
livrée avec l'historique complet des saisons F1) et copiée à côté de
l'exécutable à chaque build : elle est donc déjà présente et à jour dès le
premier lancement, sans action de l'utilisateur. Une nouvelle saison est
ajoutée en éditant ce fichier dans le projet et en publiant une nouvelle
version (`scripts/release.sh`), pas en modifiant le fichier déployé.

### `chaines_langues.json`

Donne la langue principale d'une chaîne, utilisée uniquement en repli
(voir ci-dessous). Comme `grands_prix.json`, c'est une donnée de référence
maintenue dans le projet (`MetaExtract.App\chaines_langues.json`, livrée
avec la table complète chaîne → langue) et copiée à côté de l'exécutable à
chaque build :

```json
[
  { "chaine": "TF1", "langue": "fr" }
]
```

### `langues.json`

Donne le nom complet affiché pour chaque code de langue (ex: « fr » →
« Français »), quelle que soit l'origine du code (métadonnées MediaInfo ou
repli via `chaines_langues.json`/nom de fichier). Comme les deux fichiers
précédents, c'est une donnée de référence maintenue dans le projet
(`MetaExtract.App\langues.json`) et copiée à côté de l'exécutable à chaque
build. Un code absent de ce fichier est affiché tel quel (sans planter) :

```json
[
  { "langue": "fr", "nom_langue": "Français" },
  { "langue": "en", "nom_langue": "Anglais" }
]
```

### Colonnes Chaîne et Langue : priorité aux métadonnées

Les colonnes **Chaîne** et **Langue audio** utilisent en priorité les
métadonnées déjà lues par MediaInfo (titre et langue de piste audio). Le
nom de fichier n'est utilisé qu'en repli, si MediaInfo n'a rien pu
déterminer :
- **Chaîne** : repli sur la chaîne extraite du nom de fichier
  (`filename_rules.json`).
- **Langue** : repli sur `chaines_langues.json`, à partir de la chaîne
  déjà déterminée (métadonnées ou, à défaut, nom de fichier).

**Cas particulier d'une piste audio unique dont le titre ne correspond à
aucune chaîne connue** (absente de `chaines_langues.json`) : ce titre n'est
alors pas jugé fiable (souvent un texte générique du type « Nom de chaine
inconnu », pas un vrai nom de chaîne) et il est ignoré au profit de la
chaîne trouvée dans le nom de fichier (`filename_rules.json`). Si cette
dernière est trouvée, la **langue lue dans les métadonnées est elle aussi
remplacée**, par celle associée à cette chaîne dans `chaines_langues.json`.
Exemple : piste audio unique en langue « en », titre « Nom de chaine
inconnu » (inconnu de `chaines_langues.json`), mais nom de fichier
contenant « RAI » → `Chaîne = RAI`, `Langue = it` (et non plus « en »).
Cette règle ne s'applique qu'avec une seule piste audio ; à partir de deux
pistes, le comportement standard (priorité aux métadonnées, repli simple
si absentes) s'applique normalement.

Un fichier JSON absent (parmi les quatre ci-dessus) est recréé avec ses
valeurs par défaut au lancement suivant. Un fichier présent mais invalide
(JSON mal formé) ne bloque jamais un scan : les valeurs par défaut sont
utilisées pour cette exécution, sans écraser votre fichier sur disque.

## Structure du projet

```
MetaExtract.sln
├── MetaExtract.Core/     Logique métier, indépendante de l'UI (testable, multiplateforme)
│   ├── Models/           VideoFileRecord, MetadataFieldDefinition, AppSettings,
│   │                     FilenameParsingRule, GrandPrixEntry, ChaineLangueEntry, LangueEntry
│   └── Services/
│       ├── FieldCatalog.cs             Catalogue des champs + formatage d'affichage
│       ├── IMediaInfoProvider.cs       Abstraction de la source de métadonnées
│       ├── MediaInfoCliProvider.cs     Implémentation via mediainfo.exe --Output=file://...
│       ├── FilenameMetadataService.cs  Saison/Manche/Type/Chaîne/Grand Prix/Langue déduits du nom de fichier
│       ├── LangueNameResolver.cs       Code de langue -> nom complet (affichage), via langues.json
│       ├── FolderScanner.cs            Parcours récursif des dossiers
│       ├── MediaScanOrchestrator.cs    Orchestration scan + parallélisme + progression
│       ├── ExportService.cs            Export CSV / Excel (ClosedXML)
│       ├── AppPaths.cs                 Emplacement des fichiers de config (à côté de l'exe)
│       └── SettingsService.cs          Persistance JSON (settings.json, à côté de l'exe)
└── MetaExtract.App/      Interface WPF (.NET 8, Windows uniquement)
    ├── Views/                 MainWindow, SettingsWindow
    ├── ViewModels/            MainViewModel (CommunityToolkit.Mvvm)
    ├── grands_prix.json       Donnée de référence (historique F1), copiée à côté de l'exe au build
    ├── chaines_langues.json   Donnée de référence (chaîne -> langue), copiée à côté de l'exe au build
    └── langues.json           Donnée de référence (code langue -> nom complet), copiée à côté de l'exe au build
```

`IMediaInfoProvider` est une interface : l'implémentation fournie
(`MediaInfoCliProvider`) invoque l'exécutable CLI de MediaInfo. Si vous
souhaitez plus tard basculer vers un binding direct de `MediaInfo.dll`
(plus rapide pour de très gros volumes), il suffit d'ajouter une nouvelle
implémentation de cette interface sans toucher au reste de l'application.

## Compiler et lancer (sous Windows)

```powershell
cd MetaExtract
dotnet restore
dotnet build -c Release
dotnet run --project MetaExtract.App
```

## Publier un exécutable autonome

```powershell
dotnet publish MetaExtract.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

L'exécutable sera généré dans
`MetaExtract.App\bin\Release\net8.0-windows\win-x64\publish\`.

## Publier une release

Le numéro de version (Versioning Sémantique, `MAJOR.MINOR.PATCH`) est
centralisé dans `Directory.Build.props` à la racine du dépôt. Le script
`scripts/release.sh` automatise tout le cycle : mise à jour de la
version, build Release autonome (la commande ci-dessus, avec en plus
`-p:IsReleaseBuild=true`), création d'un zip dans `releases/` (ignoré par
git), tag git `vX.Y.Z`, push vers `origin`, et création de la Release
GitHub si l'outil `gh` est installé.

Ce numéro de version est affiché dans le titre de la fenêtre principale de
l'application, à côté de son nom : uniquement "1.0.0" pour un exécutable
publié via `scripts/release.sh` (grâce à `-p:IsReleaseBuild=true`), ou
"1.0.0-dev+20260824-1533" (version + date/heure de build) pour tout autre
build (`dotnet build`, `dotnet run`, ou un `dotnet publish` manuel) — ce qui
permet d'identifier au premier coup d'œil un exécutable de test.

Il s'exécute depuis **Git Bash** (fourni avec Git pour Windows — clic
droit dans le dossier du dépôt dans l'Explorateur → « Git Bash Here »,
ou onglet "Git Bash" dans Windows Terminal), à la racine du dépôt
(working tree propre, sans modification non commitée) :

```bash
# Première release, à partir de la version actuelle (1.0.0) sans l'incrémenter :
./scripts/release.sh --version 1.0.0

# Releases suivantes : incrémente automatiquement PATCH (1.0.0 -> 1.0.1)...
./scripts/release.sh

# ...ou MINOR / MAJOR :
./scripts/release.sh --bump minor
./scripts/release.sh --bump major

# Pour tester sans rien envoyer sur GitHub :
./scripts/release.sh --skip-push --skip-github-release
```

Si `./scripts/release.sh` n'est pas reconnu comme exécutable (erreur
« Permission denied »), lancez-le via `bash scripts/release.sh ...`, ou
rendez-le exécutable une bonne fois pour toutes avec
`chmod +x scripts/release.sh`.

Si l'outil `gh` (GitHub CLI) n'est pas installé, le script affiche le
lien direct vers `https://github.com/hemoras/MetaExtract/releases/new`
pour créer la Release manuellement en y attachant le zip généré dans
`releases/`.

## Remarques

- Le parcours des dossiers ignore silencieusement les sous-dossiers
  inaccessibles (droits insuffisants) — un avertissement est journalisé en
  interne mais n'interrompt pas le scan.
- Si l'extraction MediaInfo échoue pour un fichier précis (fichier
  corrompu, format non reconnu...), le fichier reste listé avec un message
  dans la colonne "Erreur" plutôt que d'interrompre tout le scan.
- Le nombre de fichiers traités en parallèle est borné au nombre de
  cœurs du processeur (un `mediainfo.exe` est lancé par fichier).
- Extensions vidéo prises en compte par défaut : mp4, mkv, avi, mov, wmv,
  flv, webm, m4v, mpg, mpeg, ts, m2ts, mts, vob, 3gp, ogv, divx
  (modifiable dans `AppSettings.VideoExtensions`, persisté dans
  `settings.json`).

## Validation effectuée dans cet environnement

Le SDK .NET 8 a été installé et `MetaExtract.Core` (modèles + services,
hors export Excel/ClosedXML pour lequel NuGet n'était pas joignable
depuis cet environnement) a été compilé avec succès pour valider la
logique métier. Le projet `MetaExtract.App` (WPF) nécessite Windows pour
être compilé et n'a donc pas pu être buildé ici — il a été relu avec
attention mais **une première compilation sur votre poste Windows est
recommandée avant toute utilisation en production**.
