# Verre flouté sous Windows 11 : la méthode de Manhwadle

> Retour d'expérience de Manhwadle (Electron), dont la version Windows affiche le bureau flouté derrière le jeu.
> Le rendu a été validé par le propriétaire sur un vrai Windows 11. Ce document sert de point de départ au
> prototype « verre flouté » de `docs/da.md` (section 13) ; ce qui n'a pas été vérifié est marqué **à vérifier**.

## 1. Le principe

On ne dessine pas le flou soi-même : on demande à **Windows (DWM, le compositeur)** de poser son propre matériau
flouté, l'**Acrylic**, derrière notre fenêtre, et on peint notre interface par-dessus avec un fond transparent ou
translucide.

- Tout se passe **dans notre propre fenêtre**, avec des API documentées : aucune injection, aucun pilote. C'est
  compatible avec les contraintes Vanguard de `CLAUDE.md`.
- Le flou est fait par le GPU via DWM : il ne coûte presque rien à notre processus.

## 2. Ce que fait Manhwadle (Electron)

```js
const { app, BrowserWindow, nativeTheme } = require('electron');

app.whenReady().then(() => {
  // Indispensable : le matériau suit le thème de l'appli. En thème clair, l'Acrylic devient gris délavé.
  nativeTheme.themeSource = 'dark';

  new BrowserWindow({
    backgroundColor: '#00000000',      // fond de la page transparent, sinon il masque le matériau
    backgroundMaterial: 'acrylic',     // le flou du système (Windows 11 22H2 et plus)
    titleBarStyle: 'hidden',           // pas de barre de titre système...
    titleBarOverlay: {                 // ...mais les boutons réduire/agrandir/fermer natifs restent
      color: '#00000000',
      symbolColor: '#a3a6b6',
      height: 44,
    },
  });
});
```

Côté page :

- `html` et `body` ont un fond **transparent** ; la couleur vient d'un voile translucide posé par-dessus le flou
  (dans Manhwadle : `--veil: rgba(10, 11, 18, .45)` quand la classe `is-windows` est sur `<html>`).
- L'en-tête de l'appli sert de barre de titre : zone de glissement en CSS (`-webkit-app-region: drag`, et `no-drag`
  sur les boutons), avec une marge à droite pour laisser la place aux boutons natifs, calculée par les variables
  CSS `env(titlebar-area-x)` et `env(titlebar-area-width)`.

## 3. Le piège : `transparent: true`

Sous Linux (Hyprland), Manhwadle utilise une fenêtre `transparent: true` et c'est le compositeur qui floute.
**Sous Windows, ça ne marche pas** : une fenêtre transparente perd son cadre natif. Elle n'est plus
redimensionnable, plus déplaçable par le système, elle a des coins carrés, et **aucun flou** n'est appliqué (le
bureau est visible net derrière).

Règle : sous Windows, on **garde le cadre natif** et on demande un matériau système. Jamais `transparent: true`.

## 4. Ce qu'il y a derrière (pour une appli native, sans Electron)

`backgroundMaterial` d'Electron est une enveloppe autour de l'API DWM. Une appli Win32, WinUI ou autre fait la même
chose directement sur son `HWND` :

| Appel                                                                     | Effet                                                    |
| ------------------------------------------------------------------------- | -------------------------------------------------------- |
| `DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, …)`               | Choisit le matériau (valeurs ci-dessous)                 |
| `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, TRUE)`        | Matériau et cadre en sombre (l'équivalent de `themeSource`) |
| `DwmExtendFrameIntoClientArea(hwnd, {-1,-1,-1,-1})`                       | Étend le matériau à toute la fenêtre, pas seulement au cadre |
| `DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, …)`          | Coins arrondis, petits arrondis ou carrés                |
| `DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, …)`                      | Couleur de la bordure de la fenêtre                      |

Valeurs de `DWMWA_SYSTEMBACKDROP_TYPE` (`DWM_SYSTEMBACKDROP_TYPE`) :

| Valeur                    | Matériau  | Rendu                                                              |
| ------------------------- | --------- | ------------------------------------------------------------------ |
| `DWMSBT_TRANSIENTWINDOW`  | Acrylic   | **Ce qu'on veut** : ce qui est derrière la fenêtre, flouté         |
| `DWMSBT_MAINWINDOW`       | Mica      | Le fond d'écran flouté, pas les fenêtres derrière : effet plus plat |
| `DWMSBT_TABBEDWINDOW`     | Mica Alt  | Variante plus teintée de Mica                                      |
| `DWMSBT_NONE` / `AUTO`    | aucun / choix du système |                                                     |

Le contenu doit ensuite être peint avec un alpha (DirectComposition, Direct2D, ou le moteur de l'interface) pour
laisser voir le matériau ; un fond opaque le cache entièrement.

À éviter : `SetWindowCompositionAttribute` (`ACCENT_ENABLE_ACRYLICBLURBEHIND`), qu'on voit dans de vieux tutos.
L'API n'est pas documentée, son comportement a changé selon les versions de Windows, et elle laggait au
déplacement des fenêtres. On ne s'en sert qu'en dernier recours, après discussion avec le propriétaire.

## 5. Quand le flou disparaît

Dans ces cas, Windows remplace le matériau par une couleur unie. **L'interface doit rester lisible sans flou** (même
règle que `docs/da.md`) :

- Windows 10, ou Windows 11 avant **22H2** (build 22621) : `DWMWA_SYSTEMBACKDROP_TYPE` n'existe pas.
- **Effets de transparence** désactivés (Paramètres › Personnalisation › Couleurs).
- **Économiseur de batterie** actif sur un portable.
- Fenêtre inactive : Mica passe en couleur unie quand la fenêtre perd le focus ; **à vérifier** pour l'Acrylic posé
  par DWM.

## 6. Ce que ça ne permet pas (pour Azuria)

- **Le rayon des coins** d'une fenêtre est celui de Windows (environ 8 px) ou carré. Un rayon libre (18, 22…) sur une
  fenêtre flouée par DWM n'est pas prévu par l'API : **à vérifier** (région de fenêtre, ou fenêtre plein écran
  transparente avec des panneaux dessinés dedans, mais alors le flou DWM ne s'applique plus).
- **Le rayon du flou** n'est pas réglable : c'est celui de l'Acrylic de Windows.
- **Les fenêtres des autres applications** : on ne peut leur poser ni matériau ni bordure sans s'injecter dans leur
  processus (interdit, voir `CLAUDE.md`). `DwmSetWindowAttribute` sur un `HWND` d'un autre processus : **à vérifier**,
  et sans doute refusé.

## 7. Tester sans PC Windows

Le PC de développement est sous Linux. Pour Manhwadle :

1. La CI construit une version Windows à chaque PR (runner `windows-latest`), publiée en **artefact** du job.
2. Le propriétaire la télécharge depuis la PR (Checks › build Windows › Artifacts) et la lance sur un vrai Windows 11.
3. Il valide le rendu sur un fond clair **et** sur un fond sombre (navigateur blanc, jeu sombre derrière).

Wine ne sert à rien ici : il ne reproduit pas DWM, donc pas l'Acrylic. Une machine virtuelle sans accélération GPU
désactive aussi souvent la transparence.

## 8. Checklist du prototype

- [ ] Thème sombre forcé avant d'afficher la fenêtre.
- [ ] Cadre natif conservé (redimensionnement, Aero Snap, coins arrondis de Windows).
- [ ] Acrylic visible derrière la fenêtre, avec les fenêtres d'autres applis floutées derrière elle.
- [ ] Fond de l'interface transparent, teinte apportée par un voile translucide.
- [ ] Lisible avec les effets de transparence désactivés.
- [ ] Fluide pendant un déplacement de fenêtre (à mesurer aussi sur carte graphique intégrée).
- [ ] Mettre à jour le tableau de la section 13 de `docs/da.md` avec le résultat.
