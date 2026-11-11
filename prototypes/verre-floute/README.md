# Prototype — verre flouté (Windows 11)

Premier prototype d'Azuria (voir `docs/contexte.md` §4). But : une fenêtre à nous qui floute ce
qui est derrière elle, avec la recette de `docs/da.md` §6, et répondre aux quatre questions de
`docs/contexte.md` §4 **par l'essai**, pas par la documentation.

Ce n'est **pas** le squelette du projet : c'est un banc d'essai jetable. Le choix de technologie
qu'il motive est expliqué plus bas et dans la PR.

## Technologie retenue

**C# .NET 8 + Win32 brut (P/Invoke) + Windows.UI.Composition (WinRT).**

- La fenêtre est une fenêtre Win32 classique (`CreateWindowExW`). Le flou est le matériau **Acrylic**
  du compositeur de Windows (DWM), demandé sur notre propre `HWND` via `DwmSetWindowAttribute`
  (`DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW`). Aucun pilote, aucune injection : conforme à
  `CLAUDE.md`.
- `Windows.UI.Composition` sert à peindre le contenu (voile translucide, cartes, bordure) avec un
  canal alpha et à **découper en rectangle arrondi antialiasé**, et à animer le glissé de panneau
  (offset + opacité) côté compositeur.
- Pourquoi pas Electron (comme Manhwadle) : on veut mesurer finement (temps d'image, API DWM
  directes) et garder le prototype léger. La recette reste transposable à Electron
  (`backgroundMaterial: 'acrylic'` = le même `DWMSBT_TRANSIENTWINDOW`).

Le prototype n'engage pas le projet : il montre seulement que la recette tient en natif.

## Compiler

Prérequis : SDK .NET 8 (`winget install Microsoft.DotNet.SDK.8`).

```sh
dotnet build prototypes/verre-floute/VerreFloute.csproj -c Debug
```

L'exécutable : `prototypes/verre-floute/bin/Debug/net8.0-windows10.0.22621.0/VerreFloute.exe`.

## Lancer et regarder

Chaque scénario ouvre une fenêtre de verre et répond à une question. **Le jugement final se fait
devant l'écran physique** (une image via Parsec est compressée, cf. `docs/contexte.md` §6).

```sh
# fenêtre d'arrière-plan contrastée (blanc | noir | bandes), à lancer d'abord dans un autre terminal
VerreFloute.exe fond

# recette de référence : cadre conservé + Acrylic DWM
VerreFloute.exe acrylic

# Q1 — rayon libre : popup + Acrylic + découpe par région (SetWindowRgn)
VerreFloute.exe region

# Q1 bis — flou composé par nous (HostBackdropBrush) + découpe arrondie antialiasée
VerreFloute.exe compo

# Q2 — fenêtre sans cadre, toujours au-dessus, hors barre des tâches
VerreFloute.exe sans-cadre

# Q3 — panneau 380 px au bord droit ; Espace = glissé composition, W = glissé fenêtre mesuré,
#      X = balayage 300 px / 1 s (les temps d'image sont écrits dans la console)
VerreFloute.exe glisse

# Q4 — suit l'activation / la coupure de « Effets de transparence » de Windows
VerreFloute.exe repli

# contradiction DA/flou : couleur de bordure sur la fenêtre d'une AUTRE appli (Bloc-notes)
VerreFloute.exe bordure
```

### Touches dans une fenêtre de verre

| Touche | Effet |
| ------ | ----- |
| `1`..`5` | rayon 8 / 14 / 18 / 22 / 30 |
| `T` | opacité du voile : 0,84 → 0,6 → 0,45 → 0 (pour voir l'Acrylic nu) |
| `R` | active/retire la découpe par région (`SetWindowRgn`) |
| `C` | active/retire la découpe arrondie du contenu composé |
| `S` | force le repli `--solid` (sans toucher à Windows) |
| `M` | cycle des matériaux DWM : none → Mica → Acrylic → Mica Alt |
| `E` | bascule le mode sombre DWM |
| `A` | cycle d'accents **non documentés** (blur-behind…) — pour l'essai seulement |
| `D` | bascule le matériau DWM on/off |
| `W` / `X` | glissé fenêtre mesuré (dans `glisse`) |
| `Espace` | glissé composition du panneau (dans `glisse`) |
| `Échap` | quitter |

## Observé sur cette machine (11 nov. 2026)

Résumé des constats ; le détail est dans la PR et le tableau `docs/da.md` §13. Machine : Windows 11
26H2 (build 26300), AMD Radeon RX 6600, **avec un adaptateur d'affichage virtuel Parsec** — ce
dernier point est suspect pour le flou (voir la PR).
