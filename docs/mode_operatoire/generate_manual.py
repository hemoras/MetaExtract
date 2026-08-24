# -*- coding: utf-8 -*-
"""
Génère le mode opératoire de MetaExtract en PDF.

Les captures d'écran sont optionnelles : tant qu'un fichier n'existe pas
dans SCREENSHOTS_DIR, un encadré "capture à venir" est affiché à sa place.
Il suffit de déposer les fichiers PNG (mêmes noms que les clés du
dictionnaire SCREENSHOTS ci-dessous) dans le dossier "screenshots" et de
relancer ce script pour les voir apparaître dans le PDF.
"""

import os
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import cm
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_CENTER
from reportlab.lib import colors
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, Image,
    ListFlowable, ListItem, PageBreak, HRFlowable, KeepTogether
)
from reportlab.platypus.flowables import Flowable
from PIL import Image as PILImage

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SCREENSHOTS_DIR = os.path.join(BASE_DIR, "screenshots")
OUTPUT_PATH = os.path.join(BASE_DIR, "MetaExtract_Mode_Operatoire.pdf")

ACCENT = colors.HexColor("#2451B0")
ACCENT_DARK = colors.HexColor("#173A80")
TEXT_GRAY = colors.HexColor("#444444")
LIGHT_GRAY = colors.HexColor("#E8EBF2")
BORDER_GRAY = colors.HexColor("#B9C2D6")

# Nom de fichier (sans extension) -> légende affichée sous l'image / le placeholder.
SCREENSHOTS = {
    "fenetre_principale": "Fenêtre principale de MetaExtract",
    "parametres": "Fenêtre Paramètres (chemin vers mediainfo.exe)",
    "ajouter_dossier": "Dossier ajouté, affiché dans la liste « Dossiers à analyser »",
    "colonnes": "Sélection et ordre des colonnes",
    "resultats": "Tableau de résultats après un scan",
    "export": "Boîte de dialogue d'export",
}

styles = getSampleStyleSheet()

styles.add(ParagraphStyle(
    name="CoverTitle", fontName="Helvetica-Bold", fontSize=28,
    textColor=ACCENT_DARK, alignment=TA_CENTER, spaceAfter=10,
))
styles.add(ParagraphStyle(
    name="CoverSubtitle", fontName="Helvetica", fontSize=14,
    textColor=TEXT_GRAY, alignment=TA_CENTER, spaceAfter=6,
))
styles.add(ParagraphStyle(
    name="H1", fontName="Helvetica-Bold", fontSize=17,
    textColor=ACCENT_DARK, spaceBefore=22, spaceAfter=10,
))
styles.add(ParagraphStyle(
    name="H2", fontName="Helvetica-Bold", fontSize=12.5,
    textColor=ACCENT, spaceBefore=14, spaceAfter=6,
))
styles.add(ParagraphStyle(
    name="Body", fontName="Helvetica", fontSize=10.3, leading=15,
    textColor=colors.HexColor("#222222"), spaceAfter=6,
))
styles.add(ParagraphStyle(
    name="BodyBold", parent=styles["Body"], fontName="Helvetica-Bold",
))
styles.add(ParagraphStyle(
    name="Caption", fontName="Helvetica-Oblique", fontSize=9,
    textColor=TEXT_GRAY, alignment=TA_CENTER, spaceBefore=4, spaceAfter=16,
))
styles.add(ParagraphStyle(
    name="StepText", parent=styles["Body"], spaceAfter=4,
))
styles.add(ParagraphStyle(
    name="TipBody", parent=styles["Body"], textColor=ACCENT_DARK, spaceAfter=2,
))
styles.add(ParagraphStyle(
    name="TableHeader", fontName="Helvetica-Bold", fontSize=9.5,
    textColor=colors.white,
))
styles.add(ParagraphStyle(
    name="TableCell", fontName="Helvetica", fontSize=9.3, leading=12.5,
))


def screenshot_block(key, max_width_cm=15.5, max_height_cm=9.0):
    """Image réelle si le fichier existe dans screenshots/, sinon encadré placeholder."""
    caption = SCREENSHOTS[key]
    for ext in (".png", ".jpg", ".jpeg"):
        path = os.path.join(SCREENSHOTS_DIR, key + ext)
        if os.path.exists(path):
            with PILImage.open(path) as im:
                w, h = im.size
            max_w, max_h = max_width_cm * cm, max_height_cm * cm
            ratio = min(max_w / w, max_h / h, 1.0) if w and h else 1.0
            img = Image(path, width=w * ratio, height=h * ratio)
            img.hAlign = "CENTER"
            return KeepTogether([img, Paragraph(caption, styles["Caption"])])

    placeholder = Table(
        [[Paragraph(
            f"[ Capture d'écran à insérer ]<br/><br/><i>{caption}</i>",
            ParagraphStyle(name="Ph", parent=styles["Body"], alignment=TA_CENTER,
                           textColor=TEXT_GRAY),
        )]],
        colWidths=[max_width_cm * cm], rowHeights=[3.4 * cm],
    )
    placeholder.setStyle(TableStyle([
        ("BOX", (0, 0), (-1, -1), 1, BORDER_GRAY),
        ("BACKGROUND", (0, 0), (-1, -1), LIGHT_GRAY),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
    ]))
    return KeepTogether([placeholder, Spacer(1, 14)])


def numbered_steps(items):
    return ListFlowable(
        [ListItem(Paragraph(t, styles["StepText"]), spaceAfter=6) for t in items],
        bulletType="1", start=1, leftIndent=18, bulletFontName="Helvetica-Bold",
        bulletColor=ACCENT,
    )


def bullets(items):
    return ListFlowable(
        [ListItem(Paragraph(t, styles["StepText"]), spaceAfter=3) for t in items],
        bulletType="bullet", leftIndent=16, bulletColor=ACCENT,
    )


def tip_box(title, body_items):
    rows = [[Paragraph(f"💡 {title}", styles["TipBody"])]]
    for b in body_items:
        rows.append([Paragraph(b, styles["Body"])])
    t = Table(rows, colWidths=[16.2 * cm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#F1F5FC")),
        ("BOX", (0, 0), (-1, -1), 0.75, colors.HexColor("#C7D6F0")),
        ("LEFTPADDING", (0, 0), (-1, -1), 12),
        ("RIGHTPADDING", (0, 0), (-1, -1), 12),
        ("TOPPADDING", (0, 0), (-1, -1), 8),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
    ]))
    return KeepTogether([Spacer(1, 4), t, Spacer(1, 10)])


def section_title(num, text):
    return Paragraph(f"{num}. {text}", styles["H1"])


def rule():
    return HRFlowable(width="100%", thickness=0.6, color=BORDER_GRAY, spaceBefore=2, spaceAfter=14)


story = []

# ---------------------------------------------------------------- Couverture
story.append(Spacer(1, 5 * cm))
story.append(Paragraph("MetaExtract", styles["CoverTitle"]))
story.append(Spacer(1, 0.4 * cm))
story.append(Paragraph("Mode opératoire", styles["CoverSubtitle"]))
story.append(Paragraph(
    "Extraction des métadonnées de fichiers vidéo (résolution, FPS, codecs, bitrates…)",
    styles["CoverSubtitle"],
))
story.append(Spacer(1, 1 * cm))
story.append(HRFlowable(width="40%", thickness=1.2, color=ACCENT, spaceBefore=6, spaceAfter=6, hAlign="CENTER"))
story.append(PageBreak())

# ---------------------------------------------------------------- 1. Présentation
story.append(section_title(1, "Présentation"))
story.append(Paragraph(
    "MetaExtract est une application Windows qui analyse un ou plusieurs dossiers de "
    "vidéos (y compris tous leurs sous-dossiers) et récupère automatiquement les "
    "informations techniques de chaque fichier : nom, résolution, images par seconde "
    "(FPS), formats et codecs audio/vidéo, débits (bitrates), durée, taille, etc.",
    styles["Body"],
))
story.append(Paragraph(
    "Les colonnes à afficher et leur ordre sont entièrement personnalisables, et le "
    "résultat peut être exporté au format CSV ou Excel.",
    styles["Body"],
))
story.append(tip_box("À qui s'adresse ce document ?", [
    "Ce guide s'adresse à toute personne devant utiliser MetaExtract, sans connaissance "
    "technique particulière. Il décrit chaque écran et chaque bouton de l'application.",
]))
story.append(rule())

# ---------------------------------------------------------------- 2. Prérequis
story.append(section_title(2, "Avant de commencer : prérequis"))
story.append(Paragraph(
    "MetaExtract s'appuie sur le logiciel <b>MediaInfo</b> pour lire les métadonnées des "
    "vidéos. MediaInfo n'est pas fourni avec l'application : il doit être installé "
    "séparément sur l'ordinateur.",
    styles["Body"],
))
story.append(numbered_steps([
    "Télécharger MediaInfo (édition « ligne de commande » / CLI) depuis le site officiel "
    "<b>mediaarea.net</b>.",
    "Installer ou décompresser MediaInfo sur l'ordinateur (noter l'emplacement du fichier "
    "<b>mediainfo.exe</b>, il sera demandé au premier lancement de MetaExtract).",
    "Lancer MetaExtract (double-clic sur son raccourci ou sur <b>MetaExtract.exe</b>).",
]))
story.append(tip_box("Important", [
    "Utilisez bien la version « ligne de commande » (CLI) de MediaInfo, et non la version "
    "avec interface graphique — sinon une fenêtre MediaInfo risque de s'ouvrir à chaque "
    "fichier analysé pendant un scan.",
]))
story.append(rule())

# ---------------------------------------------------------------- 3. Premier lancement
story.append(section_title(3, "Premier lancement : configurer MediaInfo"))
story.append(Paragraph(
    "Au premier lancement (ou si le chemin n'est pas encore configuré), indiquez à "
    "MetaExtract où se trouve MediaInfo :",
    styles["Body"],
))
story.append(numbered_steps([
    "Cliquer sur le bouton <b>« Paramètres... »</b> en haut à gauche de la fenêtre.",
    "Cliquer sur <b>« Parcourir... »</b> et sélectionner le fichier <b>mediainfo.exe</b> "
    "installé à l'étape précédente.",
    "Cliquer sur <b>« Enregistrer »</b>.",
]))
story.append(screenshot_block("parametres"))
story.append(Paragraph(
    "Ce réglage n'est à faire qu'une seule fois : il est mémorisé automatiquement pour "
    "les prochaines utilisations.",
    styles["Body"],
))
story.append(rule())

# ---------------------------------------------------------------- 4. Vue d'ensemble
story.append(section_title(4, "Vue d'ensemble de la fenêtre principale"))
story.append(Paragraph(
    "La fenêtre principale se compose de trois zones :",
    styles["Body"],
))
story.append(bullets([
    "En haut : les boutons d'action (Paramètres, Lancer le scan, Annuler, exports).",
    "À gauche : la liste des dossiers à analyser, et le choix des colonnes à afficher.",
    "Au centre : le tableau des résultats, une fois un scan effectué.",
]))
story.append(screenshot_block("fenetre_principale"))
story.append(rule())

# ---------------------------------------------------------------- 5. Ajouter des dossiers
story.append(section_title(5, "Ajouter un ou plusieurs dossiers à analyser"))
story.append(numbered_steps([
    "Cliquer sur <b>« Ajouter un dossier... »</b>.",
    "Dans la boîte de dialogue qui s'ouvre, sélectionner un dossier — ou plusieurs en "
    "maintenant la touche <b>Ctrl</b> (ou <b>Maj</b> pour une sélection continue) tout en "
    "cliquant sur chaque dossier.",
    "Valider. Les dossiers choisis apparaissent dans la liste de gauche.",
]))
story.append(screenshot_block("ajouter_dossier"))
story.append(Paragraph(
    "Chaque dossier ajouté sera parcouru <b>avec tous ses sous-dossiers</b> : il n'est "
    "donc pas nécessaire d'ajouter séparément les sous-dossiers d'un dossier déjà "
    "présent dans la liste.",
    styles["Body"],
))
story.append(Paragraph(
    "Pour retirer un dossier de la liste : le sélectionner puis cliquer sur "
    "<b>« Retirer »</b>.",
    styles["Body"],
))
story.append(rule())

# ---------------------------------------------------------------- 6. Choisir les colonnes
story.append(section_title(6, "Choisir les informations à afficher"))
story.append(Paragraph(
    "La liste des informations récupérées (colonnes) est entièrement personnalisable, "
    "ainsi que leur ordre d'affichage :",
    styles["Body"],
))
story.append(numbered_steps([
    "La colonne <b>« Champs disponibles »</b> (à gauche) liste toutes les informations "
    "que MetaExtract peut récupérer (résolution, FPS, codecs, bitrates, durée...).",
    "Pour ajouter un champ à afficher : le sélectionner puis cliquer sur <b>« &gt; »</b> "
    "(ou double-cliquer dessus). Il passe dans la colonne <b>« Champs sélectionnés »</b>.",
    "Pour retirer un champ affiché : le sélectionner dans « Champs sélectionnés » puis "
    "cliquer sur <b>« &lt; »</b>.",
    "Pour changer l'ordre des colonnes : sélectionner un champ dans « Champs sélectionnés » "
    "puis utiliser les boutons <b>▲</b> / <b>▼</b> pour le déplacer.",
]))
story.append(screenshot_block("colonnes"))
story.append(tip_box("Bon à savoir", [
    "Le <b>nom du fichier</b> est toujours affiché en première colonne et ne peut pas "
    "être retiré : c'est le seul champ obligatoire.",
    "Ce choix de colonnes est mémorisé automatiquement d'une utilisation à l'autre.",
]))
story.append(rule())

# ---------------------------------------------------------------- 7. Lancer un scan
story.append(section_title(7, "Lancer l'analyse (scan)"))
story.append(numbered_steps([
    "Une fois les dossiers et les colonnes choisis, cliquer sur <b>« Lancer le scan »</b>.",
    "Une barre de progression et un message indiquent l'avancement (nombre de fichiers "
    "traités sur le total, nom du fichier en cours).",
    "Pour interrompre une analyse en cours, cliquer sur <b>« Annuler »</b>.",
    "À la fin de l'analyse, le tableau se remplit avec un résultat par fichier vidéo "
    "trouvé.",
]))
story.append(tip_box("Si un fichier pose problème", [
    "Si MetaExtract ne parvient pas à lire les informations d'un fichier (fichier "
    "corrompu, format non reconnu...), ce fichier reste tout de même visible dans le "
    "tableau, avec un message dans la colonne <b>« Erreur »</b> si celle-ci a été "
    "sélectionnée. Cela n'interrompt pas le reste de l'analyse.",
]))
story.append(rule())

# ---------------------------------------------------------------- 8. Résultats
story.append(section_title(8, "Consulter les résultats"))
story.append(Paragraph(
    "Le tableau central affiche une ligne par fichier vidéo trouvé, avec une colonne "
    "pour chaque information choisie à l'étape 6, dans l'ordre choisi.",
    styles["Body"],
))
story.append(screenshot_block("resultats"))
story.append(rule())

# ---------------------------------------------------------------- 9. Export
story.append(section_title(9, "Exporter les résultats"))
story.append(Paragraph(
    "Une fois l'analyse terminée, les résultats peuvent être exportés dans un fichier :",
    styles["Body"],
))
story.append(numbered_steps([
    "Cliquer sur <b>« Exporter CSV... »</b> ou <b>« Exporter Excel... »</b> selon le "
    "format souhaité.",
    "Choisir l'emplacement et le nom du fichier dans la boîte de dialogue, puis valider.",
]))
story.append(screenshot_block("export"))
story.append(Paragraph(
    "Le fichier exporté contient exactement les colonnes actuellement sélectionnées, "
    "dans le même ordre que celui affiché à l'écran.",
    styles["Body"],
))
story.append(rule())

# ---------------------------------------------------------------- 10. Dépannage
story.append(section_title(10, "Dépannage"))

depannage = [
    ("Rien ne se passe au lancement de l'application",
     "Vérifier qu'aucune fenêtre d'erreur ne s'affiche derrière une autre fenêtre. "
     "Si le problème persiste, contacter la personne en charge de l'application."),
    ("Une fenêtre MediaInfo s'ouvre pendant le scan",
     "Le chemin configuré dans « Paramètres » pointe probablement vers la version avec "
     "interface graphique de MediaInfo. Réinstaller/pointer vers la version « ligne de "
     "commande » (CLI)."),
    ("Certaines colonnes restent vides (FPS, résolution, codecs...)",
     "Vérifier que le chemin vers mediainfo.exe est correctement configuré dans "
     "« Paramètres », et que le fichier vidéo n'est pas corrompu (voir la colonne "
     "« Erreur »)."),
    ("Un dossier n'apparaît pas dans les résultats",
     "Si le dossier n'est pas accessible (droits insuffisants), il est ignoré "
     "automatiquement et l'analyse continue sur les autres dossiers."),
    ("Le bouton « Lancer le scan » est grisé",
     "Aucun dossier n'a encore été ajouté à la liste : utiliser « Ajouter un dossier... » "
     "au préalable."),
]

rows = [[Paragraph("Situation", styles["TableHeader"]), Paragraph("Solution", styles["TableHeader"])]]
for situation, solution in depannage:
    rows.append([Paragraph(situation, styles["TableCell"]), Paragraph(solution, styles["TableCell"])])

t = Table(rows, colWidths=[6.2 * cm, 10.0 * cm], repeatRows=1)
t.setStyle(TableStyle([
    ("BACKGROUND", (0, 0), (-1, 0), ACCENT_DARK),
    ("GRID", (0, 0), (-1, -1), 0.5, BORDER_GRAY),
    ("VALIGN", (0, 0), (-1, -1), "TOP"),
    ("TOPPADDING", (0, 0), (-1, -1), 6),
    ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
    ("LEFTPADDING", (0, 0), (-1, -1), 8),
    ("RIGHTPADDING", (0, 0), (-1, -1), 8),
    ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT_GRAY]),
]))
story.append(t)
story.append(PageBreak())

# ---------------------------------------------------------------- 11. Annexe
story.append(section_title(11, "Annexe : liste complète des informations disponibles"))
story.append(Paragraph(
    "Voici l'ensemble des champs pouvant être sélectionnés comme colonnes (étape 6) :",
    styles["Body"],
))

fields = [
    ("Fichier / dossier", [
        ("Nom du fichier", "Nom du fichier vidéo (toujours affiché, en première colonne)"),
        ("Dossier", "Dossier contenant le fichier"),
        ("Chemin complet", "Chemin complet du fichier sur le disque"),
        ("Extension", "Extension du fichier (.mp4, .mkv...)"),
        ("Taille du fichier", "Taille du fichier (Ko/Mo/Go)"),
        ("Date de modification", "Date de dernière modification du fichier"),
    ]),
    ("Conteneur / général", [
        ("Format du conteneur", "Format du fichier (MP4, Matroska, AVI...)"),
        ("Durée", "Durée totale de la vidéo (hh:mm:ss)"),
        ("Bitrate global", "Débit binaire global du fichier"),
    ]),
    ("Vidéo", [
        ("Format vidéo", "Nom du codec vidéo, sous sa forme la plus parlante (ex. x264 pour "
         "de l'AVC/H.264, x265 pour du HEVC/H.265, divx pour du MPEG-4 Visual) — cohérent "
         "quel que soit le format du fichier (MKV, MP4, TS...)"),
        ("Codec vidéo", "Identique à Format vidéo (les deux affichent la même valeur fiable, "
         "quel que soit le conteneur du fichier)"),
        ("Profil codec vidéo", "Profil du codec (High, Main...)"),
        ("Largeur (px)", "Largeur de l'image en pixels"),
        ("Hauteur (px)", "Hauteur de l'image en pixels"),
        ("Résolution", "Largeur x Hauteur combinées"),
        ("FPS", "Nombre d'images par seconde"),
        ("Bitrate vidéo", "Débit binaire de la piste vidéo"),
        ("Profondeur de couleur", "Nombre de bits par composante de couleur"),
        ("Ratio d'affichage", "Format d'image, au format lisible (ex. 16:9, 4:3, 2.35:1)"),
        ("Type de scan", "Progressif ou entrelacé"),
        ("Sous-échantillonnage", "Chroma subsampling (ex. 4:2:0)"),
    ]),
    ("Audio", [
        ("Format audio", "Format de la piste audio (AAC, AC-3...) — cohérent quel que soit "
         "le conteneur du fichier"),
        ("Codec audio", "Identique à Format audio (les deux affichent la même valeur fiable, "
         "quel que soit le conteneur du fichier)"),
        ("Bitrate audio", "Débit binaire de la piste audio"),
        ("Mode bitrate audio", "Mode de débit (CBR, VBR...)"),
        ("Canaux audio", "Nombre de canaux (2 = stéréo, 6 = 5.1...)"),
        ("Fréquence d'échantillonnage", "Fréquence d'échantillonnage audio (kHz)"),
        ("Langue audio", "Langue(s) audio du fichier. Si plusieurs pistes audio ont des "
         "langues différentes, toutes les langues distinctes sont affichées (séparées par "
         "une virgule) ; le champ reste vide si aucune langue n'est renseignée"),
        ("Chaîne", "Nom de la chaîne TV, déduit du titre de la piste audio en retirant un "
         "éventuel commentaire entre parenthèses (ex. « TF1 (José Rosinski) » devient "
         "« TF1 »). Si plusieurs pistes audio ont des chaînes différentes, elles sont "
         "toutes affichées (séparées par une virgule)"),
        ("Nb pistes audio", "Nombre de pistes audio présentes dans le fichier"),
    ]),
    ("Divers", [
        ("Erreur", "Message d'erreur si l'analyse du fichier a échoué"),
    ]),
]

for group_name, group_fields in fields:
    story.append(Paragraph(group_name, styles["H2"]))
    rows = [[Paragraph("Champ", styles["TableHeader"]), Paragraph("Description", styles["TableHeader"])]]
    for name, desc in group_fields:
        rows.append([Paragraph(name, styles["TableCell"]), Paragraph(desc, styles["TableCell"])])
    t = Table(rows, colWidths=[5.2 * cm, 11.0 * cm], repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), ACCENT),
        ("GRID", (0, 0), (-1, -1), 0.5, BORDER_GRAY),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("TOPPADDING", (0, 0), (-1, -1), 5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
        ("LEFTPADDING", (0, 0), (-1, -1), 8),
        ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT_GRAY]),
    ]))
    story.append(t)
    story.append(Spacer(1, 10))


def add_page_number(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica", 8.5)
    canvas.setFillColor(TEXT_GRAY)
    canvas.drawString(2 * cm, 1.3 * cm, "MetaExtract — Mode opératoire")
    canvas.drawRightString(A4[0] - 2 * cm, 1.3 * cm, f"Page {doc.page}")
    canvas.restoreState()


doc = SimpleDocTemplate(
    OUTPUT_PATH, pagesize=A4,
    topMargin=2.2 * cm, bottomMargin=2.2 * cm, leftMargin=2.2 * cm, rightMargin=2.2 * cm,
    title="MetaExtract — Mode opératoire",
)
doc.build(story, onFirstPage=add_page_number, onLaterPages=add_page_number)
print(f"PDF généré : {OUTPUT_PATH}")
