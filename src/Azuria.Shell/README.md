# Azuria.Shell

Le shell d'Azuria : l'environnement de bureau (barre, panneaux, tableau de bord…). Squelette en cours.

Techno : **C# .NET 8 (hôte natif) + WebView2 (UI en HTML/CSS)**. Voir `docs/techno.md` pour le pourquoi.

## Comment c'est organisé

| Fichier | Rôle |
| ------- | ---- |
| `Program.cs` | Point d'entrée. Ouvre la barre (pour l'instant, seule fenêtre du squelette). |
| `GlassWindow.cs` | Fenêtre de verre réutilisable : sans cadre, au-dessus, hors barre des tâches, Acrylic DWM, monte un WebView2 **transparent** qui charge une page de `web/`. Gère le repli quand la transparence de Windows est coupée. |
| `BarWindow.cs` | La barre (DA §9) : en haut, pleine largeur, hauteur 40, à 12 px des bords. |
| `Native/Dwm.cs` | Le minimum de DWM pour le verre (mode sombre, cadre étendu, Acrylic, coins). API documentées, aucune injection. |
| `web/` | **L'interface**, en HTML/CSS : `encre.css` (jetons de la DA) et `bar.html`. C'est ici que vit la présentation. |

Le C# ne dessine aucune UI : il pose la fenêtre et le verre, le reste est du web.

## Compiler et lancer

Prérequis : SDK .NET 8, et le Runtime WebView2 (préinstallé sur Windows 11).

```sh
dotnet run --project src/Azuria.Shell/Azuria.Shell.csproj
```

La barre apparaît en haut de l'écran. (Jugement du verre et de la fluidité : sur l'écran physique,
pas via Parsec — voir `docs/contexte.md` §6.)

## Build de la CI

À chaque PR, la CI (`.github/workflows/ci.yml`) compile sur Windows et publie un exécutable autonome
(`azuria-shell-win-x64`) en artefact, à télécharger depuis la PR pour l'essayer sur une vraie machine.
