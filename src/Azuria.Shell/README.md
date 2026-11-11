# Azuria.Shell

Le shell d'Azuria : l'environnement de bureau (barre, panneaux, tableau de bord…). Squelette en cours.

Techno : **C# .NET 8 (hôte natif) + WebView2 (UI en HTML/CSS)**. Voir `docs/techno.md` pour le pourquoi.

## Comment c'est organisé

| Fichier | Rôle |
| ------- | ---- |
| `Program.cs` | Point d'entrée : force le DPI PerMonitorV2, lance `ShellContext`. |
| `ShellContext.cs` | Tient les fenêtres du shell (barre, tableau de bord, panneau droit, lanceur) et relie le logo de la barre au lanceur. |
| `GlassWindow.cs` | Fenêtre de verre réutilisable : sans cadre, au-dessus, Acrylic DWM, WebView2 **transparent** ; forme de **tiroir**, glissé depuis un bord (au survol ou sur commande), repli sans flou. |
| `SidePanelWindow.cs` | Tiroir latéral pleine hauteur : barre à gauche (moniteur le plus à gauche), panneau à droite (le plus à droite). |
| `DashboardWindow.cs` | Tableau de bord collé en haut, descend sur l'écran du curseur. |
| `LauncherWindow.cs` | Lanceur collé en bas, ouvert par le logo, prend le focus, se range sur Échap / clic ailleurs. |
| `Native/Dwm.cs` | Le minimum de DWM pour le verre (mode sombre, cadre étendu, Acrylic, coins, cadence). |
| `Native/Shaping.cs` | Découpe de la fenêtre en tiroir (congés concaves au bord, coins arrondis côté libre). |
| `web/` | **L'interface**, en HTML/CSS : `encre.css` (jetons de la DA), `drawer.js` (dessine la forme du tiroir), et une page par fenêtre (`left`, `dashboard`, `right`, `launcher`). |

Le C# ne dessine aucune UI : il pose la fenêtre, sa forme et le verre ; le reste est du web.
Toutes les API sont documentées, aucune injection (voir `CLAUDE.md`).

## Compiler et lancer

Prérequis : SDK .NET 8, et le Runtime WebView2 (préinstallé sur Windows 11).

```sh
dotnet run --project src/Azuria.Shell/Azuria.Shell.csproj
```

Rien n'est visible au lancement : tout est rangé. Curseur au **bord gauche** → barre ; **haut** d'un écran →
tableau de bord ; **bord droit** → panneau ; clic sur le **logo** de la barre → lanceur. (Jugement du verre
et de la fluidité : sur l'écran physique, pas via Parsec — voir `docs/contexte.md` §6.)

## Build de la CI

À chaque PR, la CI (`.github/workflows/ci.yml`) compile sur Windows et publie un exécutable autonome
(`azuria-shell-win-x64`) en artefact, à télécharger depuis la PR pour l'essayer sur une vraie machine.
