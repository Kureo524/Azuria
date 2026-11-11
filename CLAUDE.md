# Azuria

Projet en démarrage : pas encore de code. Dépôt public `Kureo524/Azuria`. Langue de travail : français (commits,
PR, docs).

Avant de commencer, lire `docs/contexte.md` : les décisions déjà prises, ce qui reste ouvert, et la prochaine étape.

## Cap du projet

Azuria est un **environnement de bureau pour Windows 11** qui donne le feeling de Caelestia (Arch + Hyprland) à une
machine qui reste un vrai Windows : fluide et agréable, sans surcharge. Le verre flouté est sa signature. Décision du propriétaire (11 novembre 2026) : **la priorité n° 1 est que Riot
Vanguard fonctionne** (League of Legends). Vanguard n'existe pas sous Linux, donc la base est Windows et Azuria est
une surcouche, pas une distribution Linux.

Ce que cette priorité impose, sans exception :

- Rien dans le noyau : aucun pilote.
- Aucune injection dans un autre processus (ni l'explorateur, ni le gestionnaire de fenêtres de Windows, ni un jeu).
- Ne jamais désactiver ni contourner Secure Boot, le TPM ou les protections de Windows dont Vanguard dépend.
- Rien qui dessine dans un jeu ou lise sa mémoire.

Un changement qui touche à l'un de ces points se discute avec le propriétaire avant d'être écrit.

## Git et pull requests

Les règles complètes sont dans `docs/git.md`, importées ci-dessous. Elles s'appliquent dès le premier commit.

@docs/git.md

Les réglages de la section 5 de `docs/git.md` sont appliqués sur GitHub, sauf les checks CI requis : à ajouter aux
protections de `dev` et `main` dès que la première CI existe. Si le mode auto refuse un réglage (protection de
branche, ruleset), donner la commande `gh` au propriétaire au lieu de contourner.

## Direction artistique

Toute interface suit `docs/da.md` (DA « Encre ») : la lire avant de toucher à l'UI et passer sa checklist
(section 14) avant de livrer. La section 13 liste ce qui reste à prouver sur Windows. Pour le verre flouté, partir de
`docs/flou-windows.md` (méthode éprouvée sur Manhwadle).

On s'inspire des idées de Caelestia (organisation, fonctionnalités), jamais de son code ni de ses fichiers : son
dépôt est sous GPL-3.0 et Azuria n'en reprend rien.
