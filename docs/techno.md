# Choix de technologie

> Décision prise après le prototype du verre flouté (`prototypes/verre-floute`, PR #2).
> État au 11 novembre 2026.

## La décision

**Le shell d'Azuria est écrit en C# .NET 8. Le cœur natif (fenêtres, verre DWM, système, et plus
tard le pavage) est en C#/Win32 ; l'interface est rendue en HTML/CSS dans un WebView2.**

- **Hôte natif : C# .NET 8 + Win32 (P/Invoke).** Il crée les fenêtres, pose l'Acrylic de DWM (recette
  du prototype), gère le DPI, et gérera les raccourcis globaux et le pavage des fenêtres — par des API
  Win32 documentées, **sans pilote ni injection** (CLAUDE.md).
- **Interface : WebView2.** La DA « Encre » est déjà écrite en CSS ; WebView2 la rend telle quelle. Le
  WebView2 est **transparent**, posé sur le verre de DWM (le voile et le contenu se peignent par-dessus
  le flou). WinForms ne sert que de fenêtre hôte : **aucune UI n'est dessinée en C#**.

## Pourquoi (et pas autre chose)

Le critère retenu par le propriétaire : **optimisé avant tout**, et un code que le propriétaire peut
suivre. Les contraintes qui tranchent : la DA est en CSS, le pavage doit bouger les fenêtres d'autres
applis sans injection, et le shell tourne en permanence (donc empreinte faible, iGPU).

| Option                | Verdict |
| --------------------- | ------- |
| **C# .NET 8 + WebView2** | **Retenu.** WebView2 = composant système (pas un navigateur embarqué), donc léger et fidèle à la DA CSS ; Win32 direct pour le verre et le futur pavage ; un seul process pour plusieurs fenêtres ; build sans Visual Studio ; lisible. |
| Rust + WebView2 (Tauri) | Le plus léger (pas de GC) et le plus flatteur sur un CV, mais plus exigeant : moins tenable à deux, pavage verbeux. Écarté pour la vélocité et la relecture. |
| Electron              | DA en CSS directe et précédent Manhwadle, mais Chromium résident (~150–300 Mo) : contraire au « sans surcharge ». Écarté. |
| WPF / WinUI pur (sans web) | Natif léger, mais il faut retraduire toute la DA de CSS en XAML, plus verbeux. Écarté au profit de WebView2. |

Empreinte au repos, en ordre de grandeur : Electron ~150–300 Mo ; C# + WebView2 ~20–40 Mo ;
Rust + WebView2 ~quelques Mo. C# + WebView2 est le bon compromis « opti / tenable ».

## Le point à confirmer sur l'écran physique

Un WebView2 **transparent** posé sur l'Acrylic fonctionne (vu sur la barre du squelette : le fond
d'écran transparaît, teinté). Mais, comme le prototype l'a montré, la machine de dev a un **affichage
virtuel Parsec** qui semble empêcher le flou des **fenêtres d'autres applis derrière** ; le fond
d'écran, lui, est bien flouté. À **revérifier sur l'écran physique** avant de s'engager davantage
(voir `docs/da.md` §13 et `docs/contexte.md` §6).

## Structure du dépôt

- `src/Azuria.Shell/` — le shell (hôte C#) ; l'UI est dans `src/Azuria.Shell/web/`.
- `prototypes/` — bancs d'essai jetables (le prototype du verre y reste comme référence).
- `Azuria.sln` — la solution (shell + prototype), ce que compile la CI.
