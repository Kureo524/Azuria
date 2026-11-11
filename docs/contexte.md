# Contexte et décisions

> Passage de relais : ce qui a été décidé avant le premier code, pourquoi, et par où commencer.
> À lire après `CLAUDE.md`. On le met à jour quand une décision change ; quand il n'apprend plus rien que le
> dépôt ne dise déjà, on le supprime.
> État au 11 novembre 2026.

## 1. D'où vient le projet

L'idée de départ du propriétaire : un OS avec le feeling et les performances d'Arch Linux + Caelestia, et la
simplicité de Windows (jouer à League of Legends, lancer un `.exe`, ne pas vivre dans un terminal).

## 2. Décisions prises, dans l'ordre

1. **Pas une distribution Linux.** La première piste était une distribution basée sur Arch. Elle a été abandonnée :
   League of Legends exige Riot Vanguard, un anti-triche au niveau du noyau Windows, que Riot ne porte pas sur
   Linux. Le propriétaire a tranché : **Vanguard est la priorité n° 1**, donc la base est Windows 11 et Azuria est
   une surcouche. Conséquences : les `.exe` et les jeux marchent d'office ; les performances d'Arch sont perdues.
2. **Le terme juste est « environnement de bureau pour Windows 11 »**, pas « OS ».
3. **DA Encre, pas Caelestia.** Une DA reprise à l'identique de Caelestia a été rédigée puis écartée : son dépôt
   est sous GPL-3.0, et le propriétaire ne veut pas d'un clone. On garde ses idées (tableau de bord à onglets,
   panneaux à gauche et à droite), jamais son code ni ses fichiers.
4. **Le verre flouté est la signature.** C'est ce que le propriétaire trouve le plus réussi ; il passe avant le
   reste de l'apparence.
5. **Fluide et agréable, sans surcharge** : transitions courtes, aucun rebond (section 11 de `docs/da.md`).
6. **Dépôt public**, destiné à être montré sur un CV : historique git propre, règles de `docs/git.md`.

## 3. Ce qui n'est pas décidé

- **La technologie** (langage, bibliothèque d'interface). Rien n'est choisi. Le prototype du verre doit orienter
  ce choix : on prend ce qui donne le flou voulu et reste fluide.
- **La disposition du shell** de la section 9 de `docs/da.md` (barre en haut, contenu des panneaux) : proposition
  de Claude, pas encore validée par le propriétaire.
- **La licence** du dépôt, et le README.
- **Le gestionnaire de fenêtres** (pavage) : en écrire un, ou s'appuyer sur un existant dont la licence le permet.
- **Les règles anti-triche de `CLAUDE.md`** (aucun pilote, aucune injection, ne pas toucher à Secure Boot ni au
  TPM) : déduction prudente de Claude, pas une liste publiée par Riot. Le propriétaire ne les a pas contestées.

## 4. Première étape : le prototype du verre flouté

Objectif : une fenêtre à nous, sur un vrai Windows 11, qui floute ce qui est derrière elle, avec la recette de la
section 6 de `docs/da.md`. Point de départ : `docs/flou-windows.md`.

Questions auxquelles le prototype doit répondre, dans cet ordre :

1. **Rayon libre.** La DA demande des rayons de 14 à 22 ; l'Acrylic de DWM n'offre que les coins de Windows
   (environ 8). Pistes à essayer : découpe par région de fenêtre ; ou composer le flou nous-mêmes avec
   `Windows.UI.Composition` (pinceau d'arrière-plan de l'hôte + effet de flou gaussien, découpé en rectangle
   arrondi), ce qui donnerait aussi un rayon de flou réglable. Aucune de ces pistes n'a été testée.
2. **Fenêtres sans cadre.** La barre et les panneaux ne sont pas des fenêtres classiques : pas de barre de titre,
   toujours au-dessus, hors de la barre des tâches. `docs/flou-windows.md` montre que le flou DWM tient au cadre
   natif : il faut vérifier ce qui reste possible pour ce type de fenêtre.
3. **Fluidité** pendant un glissé de panneau de 0,20 s, flou actif, y compris sur carte graphique intégrée.
4. **Repli** quand les effets de transparence de Windows sont coupés.

Si le rayon libre s'avère impossible, on ne bricole pas : on le dit au propriétaire, et on ajuste la DA (rayons)
plutôt que le flou, qui est prioritaire.

Les deux documents se contredisent sur un point à trancher par un essai : `docs/da.md` (section 13) suppose qu'on
peut colorer la bordure de la fenêtre active d'une autre application ; `docs/flou-windows.md` (section 6) pense
que Windows le refusera.

À chaque résultat, mettre à jour le tableau de la section 13 de `docs/da.md`.

## 5. Ensuite

Ordre envisagé, à revoir après le prototype :

1. Choix de la technologie, squelette du projet et CI (build Windows à chaque PR, en artefact).
2. Barre.
3. Panneaux gauche et droit, tableau de bord.
4. Pavage des fenêtres et bureaux.
5. Réglages, puis installeur.

Le test avec League of Legends demande une **vraie machine** : Vanguard refuse les machines virtuelles. Il est à
faire dès que la barre existe, pour confirmer tôt qu'Azuria ne gêne pas l'anti-triche.

## 6. Conditions de travail

- Le PC de développement d'origine est sous Arch Linux + Hyprland + Caelestia : rien de ce qui touche à Windows
  n'a pu y être testé. Tout ce que les documents disent de Windows est une hypothèse tant qu'un essai ne l'a pas
  confirmé.
- Le travail continue sur un PC Windows 11, où l'on peut compiler, lancer et regarder le résultat.
- Une image vue à travers un bureau à distance (Parsec) est compressée : le jugement final sur le flou et la
  fluidité se fait devant l'écran de la machine.
- Les commits du dépôt utilisent l'adresse « noreply » GitHub du propriétaire, pas son adresse personnelle. Sur
  une nouvelle machine : `git config user.email` dans le dépôt, avec cette même adresse (visible dans
  `git log`).
