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
  mode de bitrate, canaux, fréquence d'échantillonnage, langue, nombre de
  pistes audio.
- Export **CSV** ou **Excel (.xlsx)**, au choix, avec les colonnes
  sélectionnées dans l'ordre choisi.
- Les dossiers, les colonnes choisies et le chemin vers `mediainfo.exe`
  sont mémorisés d'une session à l'autre.

## Prérequis

- **MediaInfo doit être installé sur la machine** (l'application ne
  l'embarque pas). Téléchargez la version "CLI" (ligne de commande) sur
  https://mediaarea.net/fr/MediaInfo/Download/Windows — vous obtenez un
  fichier `MediaInfo.exe` (ou `mediainfo.exe` selon le paquet).
  Au premier lancement de l'application, ouvrez **Paramètres** et indiquez
  le chemin vers cet exécutable.
- Pour **compiler** le projet : [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
  et Windows (le projet `MetaExtract.App` cible `net8.0-windows` / WPF).

## Structure du projet

```
MetaExtract.sln
├── MetaExtract.Core/     Logique métier, indépendante de l'UI (testable, multiplateforme)
│   ├── Models/           VideoFileRecord, MetadataFieldDefinition, AppSettings
│   └── Services/
│       ├── FieldCatalog.cs           Catalogue des champs + formatage d'affichage
│       ├── IMediaInfoProvider.cs     Abstraction de la source de métadonnées
│       ├── MediaInfoCliProvider.cs   Implémentation via mediainfo.exe --Output=JSON
│       ├── FolderScanner.cs          Parcours récursif des dossiers
│       ├── MediaScanOrchestrator.cs  Orchestration scan + parallélisme + progression
│       ├── ExportService.cs          Export CSV / Excel (ClosedXML)
│       └── SettingsService.cs        Persistance JSON (%AppData%\MetaExtract)
└── MetaExtract.App/      Interface WPF (.NET 8, Windows uniquement)
    ├── Views/            MainWindow, SettingsWindow
    └── ViewModels/       MainViewModel (CommunityToolkit.Mvvm)
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
