[🇮🇶 العربية](README.md) | [🇬🇧 English](README.en.md) | [🇷🇺 Русский](README.ru.md) | **🇫🇷 Français**

# Mod ArabicSupport

Un mod léger pour RimWorld qui corrige l'affichage du texte arabe nécessitant une écriture de droite à gauche (RTL), permet de saisir du texte arabe et prend en charge les polices personnalisées.

## Fonctionnalités

- **Saisie de texte arabe** – Vous pouvez saisir des caractères arabes directement dans le jeu (RimWorld 1.5 et 1.6, voir [Versions de RimWorld](#versions-de-rimworld)).
- **Affichage RTL correct** – Les caractères arabes sont affichés dans le bon ordre, avec une mise en forme et une liaison correctes des lettres.
- **Prise en charge des polices personnalisées** – Remplacez la police du jeu par la police arabe de votre choix. La police par défaut est **Cairo Bold**.
- **Mod léger et optimisé** – Utilise les correctifs Harmony tout en consommant un minimum de ressources.
- **Traduction incluse** – La traduction est fournie dans un dossier `Data` inclus dans l'archive téléchargée, à côté du mod.

## Versions de RimWorld

| RimWorld | Ce qui fonctionne |
|---|---|
| 1.5 et 1.6 | Tout : saisie de l'arabe, affichage RTL, harakat, polices personnalisées |
| 1.7 (nouveau moteur Waylith) – expérimental | Affichage RTL et retour à la ligne, placement des harakat, polices personnalisées |

**Ce que la version 1.7 n'inclut pas :** la correction de la saisie de l'arabe dans les champs de texte et la recherche en arabe. Dans RimWorld 1.7, c'est le jeu qui gère la saisie ; l'arabe saisi peut donc apparaître avec des lettres séparées ou dans l'ordre inverse, selon ce que fait le nouveau moteur.

Le mod charge automatiquement les fichiers adaptés à votre version du jeu (`LoadFolders.xml`). Notes pour les développeurs : [docs/RimWorld-1.7.md](docs/RimWorld-1.7.md).

## Installation

**Installation manuelle** :

1. Téléchargez la dernière version depuis [GitHub](https://github.com/MushussuBabylon/RimWorld-ar) (version `Data-ArabicSupport`).
2. Extrayez l'archive dans le dossier du jeu RimWorld. Le mod sera placé automatiquement dans le dossier `Mods`, et la traduction dans le dossier `Data`.
3. Activez le mod dans le menu des mods du jeu.

## Polices personnalisées

La police **Cairo Bold** est définie comme police par défaut du mod. Pour utiliser une autre police, suivez ces étapes :

1. Accédez au dossier `ArabicSupport/ExtraFonts/Fonts` de ce dépôt.
2. Téléchargez le fichier de police (`.fontbundle`) souhaité. (Dans RimWorld 1.7, vos propres fichiers de police `.ttf` ou `.otf` fonctionnent aussi.)
3. Copiez le fichier téléchargé dans `ArabicSupport/Fonts`, en remplaçant les fichiers existants ou en ajoutant la nouvelle police.

## Compatibilité

- En cas de problème, essayez de charger ce mod **après** les autres mods d'interface ou de traduction.
- **Conflits connus** – Si vous rencontrez un conflit avec un autre mod, veuillez ouvrir une issue dans ce dépôt.

## Remerciements

- **Auteur** : Mushussu Babylon
- **Remerciements particuliers** : à la bibliothèque Harmony, utilisée pour le modding de jeux, ainsi qu'à la communauté des moddeurs de RimWorld.

## Licence

Ce mod est publié sous la [licence MIT](LICENSE). Vous êtes libre de l'utiliser, de le modifier et de le distribuer, à condition d'en mentionner correctement la source.

---

*Ce travail est dédié à tous les joueurs arabophones de RimWorld. Bon jeu !*
