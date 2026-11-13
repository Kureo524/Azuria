# Direction artistique « Encre »

> DA d'Azuria. Dérivée de la DA « Encre » d'Overquid (un overlay), étendue à un bureau complet : barre, panneaux
> latéraux, tableau de bord, lanceur, notifications, réglages.
> On garde l'essence : verre fumé **flouté**, liseré de lumière, un seul accent froid, formes souples.
> Quelques idées d'organisation viennent de Caelestia (tableau de bord à onglets, panneaux qui sortent des bords de
> l'écran) : ce sont des idées, pas son apparence ni son code. Rien n'est repris de son dépôt.
> Azuria n'a pas encore de code : ce document décrit **la cible**. Ce qui dépend de Windows et reste à prouver est
> en section 13.

## 1. L'idée en une phrase

Des **panneaux de verre fumé** posés sur le bureau, qui **floutent ce qu'il y a derrière eux**, comme une goutte
d'encre sur une vitre : froid, calme, mat. Un bureau qu'on oublie, pas une vitrine.

Le flou est la signature d'Azuria. Le fond n'est jamais maîtrisé : fond d'écran, fenêtres, jeu. Le verre doit donc
rester lisible sur du blanc comme sur du noir, et rien ne doit attirer l'œil hors du contenu.

## 2. Principes non négociables

1. **Un seul thème, sombre.** Pas de thème clair.
2. **Un seul accent à la fois.** Bleu encre `#8fa4cc` par défaut ; l'utilisateur peut en changer la teinte
   (section 4), jamais en avoir deux. Rouge, ambre et vert sont **réservés aux états**, jamais à la décoration.
3. **Le verre = fond fumé flouté + liseré clair en haut et à gauche** (lumière du haut-gauche). Bords bas et droit
   plus discrets.
4. **Formes souples** : grands rayons, rien d'anguleux. Tout ce qui flotte garde une marge de 12 avec les bords de
   l'écran : rien n'est collé au bord.
5. **Catégories plutôt que longue page** : onglets et contrôle segmenté pour découper un contenu dense.
6. **Fluide, sans en faire des caisses.** Chaque apparition, disparition et changement d'état a une transition
   courte et douce. Aucun rebond, aucune animation en boucle, aucun élément de fond animé (section 11).
7. **Les repères n'apparaissent qu'au besoin.** Rien de permanent autour des fenêtres ni des panneaux.

### Interdits

- Pas de glow ni d'ombre lumineuse colorée.
- Pas de dégradé décoratif sur boutons ou cartes.
- Pas de carte à bordure latérale colorée.
- Pas de petites capitales espacées (tracked caps), pas de letter-spacing.
- Pas d'emoji en guise d'icônes.
- Pas d'étoiles, de particules, de bruit animé ou de fond « ambiance ».
- Pas de rebond, d'élasticité ni de déformation dans les animations.

## 3. Couleurs (jetons)

Toutes les teintes claires translucides partent du **même blanc froid `rgb(230, 234, 242)`** à opacité variable ;
toutes les teintes sombres partent de **l'encre** (`rgb(11, 12, 17)` par défaut). C'est ce qui fait la cohérence.

Valeurs par défaut. La colonne « Suit » indique ce qui change quand l'utilisateur règle ses couleurs.

| Jeton              | Valeur par défaut           | Suit           | Usage                                                       |
| ------------------ | --------------------------- | -------------- | ----------------------------------------------------------- |
| `--veil`           | `rgba(11, 12, 17, 0.38)`    | encre, voile   | Voile sur le bureau derrière une modale (session, réglages) |
| `--glass`          | `rgba(20, 22, 30, 0.72)`    | encre, opacité | Verre léger : en-têtes, navigation, fond du segmenté        |
| `--surface`        | `rgba(16, 18, 25, 0.84)`    | encre, opacité | Verre plein : barre, panneaux, menus, notifications         |
| `--solid`          | `rgb(16, 18, 25)`           | encre          | Verre opaque : quand le flou est indisponible               |
| `--input`          | `rgba(11, 12, 17, 0.7)`     | encre          | Champs de saisie                                            |
| `--surface-raised` | `rgba(230, 234, 242, 0.06)` |                | Boutons, cartes, touches, interrupteur éteint               |
| `--hover`          | `rgba(230, 234, 242, 0.05)` |                | Survol des lignes, onglets, boutons-icônes                  |
| `--border`         | `rgba(230, 234, 242, 0.08)` |                | Bordure par défaut                                          |
| `--border-strong`  | `rgba(230, 234, 242, 0.15)` |                | Bordure des boutons, séparateurs, champ en édition          |
| `--rim`            | `rgba(230, 234, 242, 0.2)`  |                | Liseré de lumière (haut + gauche)                           |
| `--text`           | `#e6eaf2`                   |                | Texte principal                                             |
| `--text-2`         | `#a2a6b4`                   |                | Libellés, texte secondaire                                  |
| `--text-3`         | `#868a98`                   |                | Aides, méta, icônes au repos                                |
| `--accent`         | `#8fa4cc`                   | accent         | Sélection, focus, bureau actif, bordure de la fenêtre active |
| `--accent-text`    | `#adbfe0`                   | accent         | Texte accentué                                              |
| `--accent-soft`    | `rgba(143, 164, 204, 0.14)` | accent         | Fond d'élément sélectionné ou actif                         |
| `--accent-tint`    | `rgba(143, 164, 204, 0.42)` | accent         | Bouton principal, interrupteur allumé                       |
| `--danger`         | `#d9646c`                   |                | Erreur, action destructrice, batterie critique              |
| `--warning`        | `#d9aa3a`                   |                | Alerte, batterie faible                                     |
| `--success`        | `#93b07e`                   |                | Réussite, en charge                                         |

Un message d'état a un fond de sa couleur à 13 % et une bordure à 30 %.

Comment les jetons suivent les réglages :

- `--accent-text` = l'accent, 10 points de luminosité en plus (90 % au maximum).
- `--accent-soft` et `--accent-tint` = l'accent à 14 % et 42 % d'opacité.
- `--surface` = l'encre éclaircie de `(5, 6, 8)`, à l'opacité choisie ; `--solid` = la même, opaque.
- `--glass` = l'encre éclaircie de `(9, 10, 13)`, à l'opacité choisie moins 0,12 (0,5 au minimum).
- `--veil` et `--input` = l'encre pure, à 0,38 (réglable) et 0,7.

## 4. Ce que l'utilisateur peut régler

Réglages › Apparence. **Seules les teintes changent** : la structure, le texte et les états ne bougent pas.

| Réglage          | Par défaut | Limites                             | Préréglages                                                                                         |
| ---------------- | ---------- | ----------------------------------- | --------------------------------------------------------------------------------------------------- |
| Accent           | `#8fa4cc`  | luminosité ramenée entre 55 et 85 % | Ink `#8fa4cc`, Steel `#8fb3c4`, Sage `#8fb5a0`, Amber `#c9a877`, Rose `#cf9bb3`, Amethyst `#a89ad1` |
| Encre            | `#0b0c11`  | luminosité ramenée entre 2 et 12 %  | Ink `#0b0c11`, Graphite `#101012`, Pine `#0a110f`, Wine `#140c0f`, Plum `#120d16`                   |
| Opacité du verre | 0,84       | 0,6 à 1                             |                                                                                                     |
| Voile            | 0,38       | 0 (aucun) à 0,8                     |                                                                                                     |
| Fond d'écran     | fond Encre | libre                               |                                                                                                     |

Une couleur hors limites n'est pas refusée : sa luminosité est ramenée dans la plage, la teinte est gardée.
C'est ce qui garantit un accent lisible et une encre qui reste sombre, quel que soit le choix.

Les préréglages d'accent sont tous **désaturés et de même clarté** : aucun ne doit ressembler à une couleur d'état.

**Accent automatique** (option, désactivée par défaut) : l'accent prend la teinte dominante du fond d'écran,
désaturée et ramenée dans la même plage de luminosité. Il n'y a toujours qu'un accent.

## 5. Typographie

| Rôle           | Police                             | Usage                                                |
| -------------- | ---------------------------------- | ---------------------------------------------------- |
| Texte courant  | **Gothic A1** Regular 12/1.5       | corps, libellés, champs, menus                       |
| Titres         | **Gothic A1** Bold                 | titre de panneau ou de carte 12, titres de page 15   |
| Onglets        | **Gothic A1** SemiBold 12          | onglets, bureaux                                     |
| Grands nombres | **Barlow Condensed** SemiBold 26   | horloge, minuteurs, mesures ; chiffres tabulaires    |
| Données        | **IBM Plex Mono** Regular 10–11    | touches, versions, valeurs, champs de couleur        |

Tailles utilisées : 10 / 11 / 12 / 15 / 26, en pixels logiques (elles suivent la mise à l'échelle de l'écran).
Pas de capitales forcées. L'horloge de l'écran de session peut monter à 52, seule exception.

Les trois polices (licence OFL) sont **embarquées** avec Azuria : rendu identique partout et hors ligne.

## 6. Le verre

Recette de référence, exprimée en CSS :

```css
.glass {
  border: 1px solid var(--border);
  border-top-color: var(--rim);
  border-left-color: var(--rim);
  background: var(--surface);
  border-radius: 18px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.35);
  backdrop-filter: blur(20px) saturate(1.1);
}
```

- **Le flou porte sur tout ce qui est derrière** : fond d'écran et fenêtres des applications, pas seulement le
  contenu d'Azuria. Flou de 20, saturation légèrement relevée.
- **Tout ce qui flotte est du verre plein** : barre, panneaux, tableau de bord, lanceur, menus, notifications,
  réglages. Même recette, seul le rayon change.
- **Le verre léger** (`--glass`) ne sert qu'à l'intérieur d'un verre plein, pour sa structure : en-tête,
  colonne de navigation, fond du contrôle segmenté.
- **Les cartes** posées dans un panneau sont en `--surface-raised` avec une bordure `--border` : elles ne
  refloutent pas et n'ont pas d'ombre.
- L'ombre portée est neutre et douce, jamais colorée.
- **Repli sans flou** : quand le flou est indisponible (transparence coupée dans Windows, économie d'énergie), le
  verre passe en `--solid`. C'est aussi la raison du minimum de 0,6 sur l'opacité réglable : le rendu doit tenir
  même avec un flou dégradé.

## 7. Le bureau

**Fond d'écran** — libre. Le fond par défaut est l'encre unie avec une seule tache d'encre diffuse en haut à
gauche. Aucun fond animé.

**Fenêtres des applications** — elles se partagent l'écran sans se chevaucher (pavage), avec **12 d'écart** entre
elles et avec la barre et les bords. La fenêtre active a une bordure de 1 en `--accent` ; les autres en
`--border`. Une fenêtre peut être détachée (flottante) ou mise en plein écran.

**Plein écran** — la barre et les panneaux s'effacent. Un jeu en plein écran ne voit rien d'Azuria.

**Bureaux** — plusieurs bureaux virtuels ; on en change par raccourci, à la molette sur la barre ou au clic.

## 8. Rayons

| Rayon | Éléments                                                                    |
| ----- | --------------------------------------------------------------------------- |
| 6     | touches de raccourci                                                        |
| 8     | boutons-icônes, items de menu, pastilles de couleur                         |
| 9     | éléments de la barre (bureaux, onglets), interrupteur                       |
| 10    | boutons, champs, items de segmenté, onglets des réglages                    |
| 12    | menus, cartes, lignes de réglage, messages                                  |
| 14    | barre, groupe segmenté, notifications                                       |
| 18    | panneaux latéraux, tableau de bord, lanceur                                 |
| 22    | fenêtre de réglages, écran de session                                       |

## 9. Le shell

Organisation inspirée de Caelestia (les idées, jamais son code ni ses fichiers), dans les couleurs et la typo
de cette DA. Les deux écrans forment **une seule zone** : chaque panneau s'attache au bord extérieur de
l'ensemble des écrans (ou à l'écran du curseur pour le haut et le bas).

**Cadre** — un **contour permanent** de verre, épaisseur 8, entoure la zone de travail de chaque écran (au-dessus
de la barre des tâches), coins intérieurs arrondis à 18. Sa place est réservée : les fenêtres agrandies s'arrêtent
à son bord intérieur. Il se masque quand une appli passe en plein écran (un jeu), et aucun tiroir ne s'ouvre alors.

**Tiroirs** — tout panneau est un **tiroir qui sort du cadre** : bord collé droit, **congés concaves** (rayon 18)
là où il rejoint le cadre, coins arrondis côté libre. Cadre et tiroirs sont un même verre (voile `--surface` sur
le flou), d'un seul tenant, sans trait de contour. Un tiroir latéral pleine hauteur remplace les coins du cadre de
son côté par ses propres congés. Un tiroir ne fait que **glisser** depuis son bord, de dessous le cadre
(section 11) : sa forme et son contenu ne bougent pas. Masqué au repos.

**Barre** — verticale, collée au bord gauche, pleine hauteur, largeur 56. Révélée quand le curseur touche le bord
gauche. De haut en bas :

- le logo Azuria (ouvre le lanceur) ;
- les bureaux, empilés (32 × 32, rayon 9, SemiBold ; actif : fond `--accent-soft`, texte `--accent`) ;
- l'heure en vertical (heures au-dessus des minutes, Barlow Condensed) et la date courte ;
- les icônes d'état (réseau, son, batterie) ;
- le bouton de session (prend `--danger` au survol seulement).
- Séparateurs horizontaux 24 × 1 en `--border-strong` entre les groupes.

**Tableau de bord** — tiroir collé au bord haut, centré, rayon 18, largeur 640 (720 au plus). Il descend quand le
curseur touche le haut d'un écran, **sur cet écran**. Un contrôle segmenté en tête, quatre onglets :

- **Accueil** : utilisateur, horloge en grand, calendrier, aperçu météo, média en cours.
- **Média** : pochette, titre, progression, commandes ; sortie audio.
- **Performances** : processeur, carte graphique, mémoire, disques, réseau, batterie.
- **Météo** : conditions actuelles, prévisions par heure et par jour.

**Lanceur** — tiroir collé au bas de la zone de travail (au-dessus de la barre des tâches de Windows), centré,
largeur 560, rayon 18. S'ouvre par le logo Azuria de la barre (plus tard aussi par la touche Windows), sur l'écran
du curseur ; pas d'ouverture au survol, le bas de l'écran étant pris par la barre des tâches. Champ de recherche
en tête (il a le focus à l'ouverture), puis les applications. La même recherche trouvera les applications, les
fichiers, les réglages et les actions (éteindre, verrouiller…). Se range sur Échap ou au clic ailleurs.

**Panneau droit** — notifications et réglages rapides. Tiroir collé au bord droit, pleine hauteur, largeur 200,
révélé quand le curseur touche le bord droit. En haut les réglages rapides (Wi-Fi, Bluetooth, mode jeu, ne pas
déranger, curseurs de volume et de luminosité), en dessous les notifications groupées par application, avec
« tout effacer ».

**Notification** — apparaît en haut à droite : largeur 360, rayon 14. Icône, titre Bold, texte
`--text-2`, actions en boutons compacts. Disparaît après 5 s et reste dans le panneau droit.

**Volume, luminosité** — pastille en bas au centre, rayon 14 : icône, barre de niveau en `--accent`, valeur en
mono. Se referme après 2 s.

**Session** — éteindre, redémarrer, veille, verrouiller, se déconnecter. Verre plein rayon 22, centré, sur le
voile `--veil`. L'action destructrice ne prend `--danger` qu'au survol.

**Réglages** — une vraie fenêtre, en verre plein rayon 22, 960 × 640 par défaut. Colonne de navigation de 200 en
verre léger : titre 15 Bold, onglets hauteur 30, rayon 10 (sélectionné : `--accent-soft`, texte `--text`).
Contenu : titres de section 15 Bold, lignes de réglage (hauteur 40 au moins, rayon 12, survol `--hover`) avec
libellé et aide 11 en `--text-3` à gauche, contrôle à droite. L'objectif est qu'aucun réglage courant n'oblige à
ouvrir un terminal ni les réglages de Windows.

**Menus et infobulles** — verre plein, rayon 12, marge intérieure 4, largeur minimale 170. Item : rayon 8,
`--text-2`, survol `--hover` + `--text`. Coche en `--accent`. Item indisponible à 40 %.

Un seul tiroir ouvert à la fois par bord. Un tiroir révélé au survol se range quand le curseur s'en éloigne ; un
tiroir ouvert sur commande (lanceur) se range sur Échap ou au clic ailleurs.

## 10. Composants

- **Bouton-icône** : 24 × 24, rayon 8, sans fond, icône `--text-3`. Survol : `--hover` + `--text`. Activé : fond
  `--accent-soft`, icône `--accent`. Désactivé : opacité 0,35.
- **Bouton** : hauteur 28, marge 0 14, rayon 10, `--surface-raised`, bordure `--border-strong` + liseré ; survol
  `rgba(230,234,242,0.10)`. Principal : `--accent-tint`, bordure `rgba(255,255,255,0.28)`, texte `#fff`.
  Désactivé à 45 %.
- **Bouton compact** (dans une carte ou une notification) : hauteur 24 à 26, rayon 8, fond
  `rgba(230,234,242,0.04)`, bordure `--border`. Principal : fond `--accent-soft`, texte `--accent`.
- **Interrupteur** : 32 × 18, rayon 9. Éteint : `--surface-raised`, pastille `--text-2`. Allumé : `--accent-tint`,
  pastille blanche.
- **Segmenté** : groupe en verre léger rayon 14, marge 3 ; items hauteur 24, rayon 10 ; actif `--accent-soft` +
  `--text`.
- **Champ** : hauteur 26, rayon 10, `--input`, bordure `--border` ; focus : bordure `--accent`, sans anneau.
  Le champ de recherche des panneaux fait 36 de haut.
- **Curseur** : piste `--surface-raised`, partie remplie et poignée en `--accent`.
- **Pastille de couleur** : 22 × 22, rayon 8, contour `--border-strong` ; choisie : bordure 2 en `--text`.
- **Touche** : mono 11, `--surface-raised`, rayon 6, bordure `--border`.
- **Ligne de liste** : rayon 8 à 10, survol `--hover`, élément courant `--accent-soft`.
- **Message** : rayon 12. Note : fond `--accent-soft`, texte `--text-2`. États : voir section 3.
- **Icônes** : un seul jeu, au trait, 16 dans la barre et les boutons, 20 dans les listes d'applications.
- **Mesures et graphiques** (Performances, disques, batterie) : **une seule teinte, l'accent, à intensité
  variable**, sur une grille `rgba(255,255,255,0.07)`. Une couleur d'état n'apparaît que si le seuil est franchi
  (température, batterie). Grands nombres en Barlow Condensed.

## 11. Mouvement

Le but : que tout paraisse immédiat et doux. Une transition se remarque à peine ; son absence, si.

| Quoi                                    | Durée  | Effet                                               |
| --------------------------------------- | ------ | --------------------------------------------------- |
| Survol, focus, interrupteur             | 0,12 s | fond, bordure, couleur                              |
| Menu, infobulle                         | 0,12 s | fondu                                               |
| Ouverture d'un panneau, d'une notification | 0,20 s | fondu + glissé de 12 depuis son bord             |
| Fermeture                               | 0,14 s | l'inverse                                           |
| Changement d'onglet, item de segmenté   | 0,18 s | l'indicateur glisse, le contenu se fond             |
| Fenêtre qui change de place (pavage)    | 0,18 s | glissé                                              |
| Changement d'accent ou d'encre          | 0,30 s | fondu des couleurs                                  |

- Une seule courbe d'entrée, en décélération : `cubic-bezier(0.2, 0, 0, 1)`. Sortie : `cubic-bezier(0.3, 0, 1, 1)`.
  **Jamais de dépassement** : rien ne rebondit.
- On anime l'opacité, la position et — pour les panneaux et la barre en dynamic island — la **taille**. On n'anime
  jamais le rayon du flou. Le mouvement doit rester fluide à 60 images par seconde et plus, flou actif (à vérifier
  sur la machine la plus modeste visée) : si une animation de taille accroche, on la rend par position/opacité.
- Une animation interrompue repart de là où elle est, sans saut.
- Rien ne bouge sans action de l'utilisateur, hormis l'arrivée d'une notification.
- Animations réduites (réglage de Windows ou d'Azuria) : tout passe en fondu simple de 0,12 s.

## 12. Plus tard

Intentions gardées de la direction d'origine, non prioritaires :

- **Reflet du curseur** : un unique halo blanc de 320, `rgba(255,255,255,0.06)`, dans le verre survolé.
- **Graphiques multi-séries** : palette catégorielle `#3987e5 #d95926 #199e70 #c98500 #d55181 #008300 #9085e9
  #e66767`, seulement quand une teinte ne suffit plus.
- **Écran de verrouillage** aux couleurs d'Azuria.

## 13. Sur Windows : ce qui reste à prouver

Azuria dessine son shell ; les fenêtres des autres applications sont dessinées par Windows, et Azuria s'interdit
de s'injecter dans d'autres processus (voir `CLAUDE.md`). **Ces points sont des hypothèses tant qu'un prototype
ne les a pas confirmés** ; on met ce tableau à jour à chaque résultat.

La colonne « Constaté » vient du prototype `prototypes/verre-floute` (11 novembre 2026). Machine d'essai :
Windows 11 26H2 (build 26300), AMD Radeon RX 6600, **avec un adaptateur d'affichage virtuel Parsec**. Ce dernier
point est important : le flou des fenêtres d'autres applications ne s'est **pas** reproduit sur cette machine (ni
par notre fenêtre, ni par l'Acrylic du menu Démarrer de Windows lui-même), ce qui pointe vers l'affichage virtuel
plutôt qu'une limite de Windows. À **re-vérifier devant l'écran physique** (cf. `docs/contexte.md` §6).

| Cible                                               | Attendu sur Windows 11                                              | Constaté (prototype, 11 nov. 2026)                                                                 |
| --------------------------------------------------- | ------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| Verre flouté sur le **fond d'écran**, rayon libre   | Possible pour nos fenêtres ; **premier prototype à faire**          | **Oui.** Acrylic DWM floute le fond d'écran ; rayon libre 14→30 via `SetWindowRgn` (coins crénelés, non lissés). **13 nov. :** dans le shell, l'Acrylic DWM ignore la région (congés remplis en rectangle) et devient gris plat sur une fenêtre jamais active. **Solution retenue :** flou composé par nous (`CreateHostBackdropBrush` + `DWMWA_USE_HOSTBACKDROPBRUSH`), découpé par une géométrie Direct2D (`IGeometrySource2DInterop`) : forme libre, congés concaves, bords lissés |
| Verre flouté sur les **fenêtres d'autres applis derrière** | (implicite dans la DA §6)                                    | **Non reproduit ici** (Acrylic, blur-behind, HostBackdrop : aucun). Suspect Parsec → à juger sur l'écran physique. **13 nov., portable sans Parsec** (Radeon intégrée + RTX 3060) : **Oui**, une autre appli derrière la barre est floutée |
| Flou fluide pendant un glissé de panneau            | À mesurer, y compris sur carte graphique intégrée                   | **Fluide** sur RX 6600 : ~8 ms/image, 0 image > 20 ms (glissé 100 px/0,2 s et 300 px/1 s). iGPU non testable ici |
| Bordure d'accent sur la fenêtre active              | Possible : Windows laisse choisir la couleur de bordure             | **Non** pour une fenêtre d'une **autre** appli : `DWMWA_BORDER_COLOR` renvoie S_OK mais est ignoré. OK pour nos propres fenêtres |
| Rayon des fenêtres des applications                 | Imposé par Windows (environ 8) ; on ne le change pas                | Non testé (hors prototype)                                                                          |
| Fenêtres d'applications translucides et floutées    | Non : seul le shell est en verre                                    | Non testé (hors prototype)                                                                          |
| Fenêtre sans cadre, toujours au-dessus, hors barre des tâches, floutée | (découlait de la DA §9)                          | **Oui.** `WS_POPUP` + topmost + tool-window + `DwmExtendFrameIntoClientArea(-1)` : l'Acrylic s'applique, pas de barre de titre |
| Repli sans flou (transparence Windows coupée)       | Verre → `--solid` (DA §6)                                           | **Oui.** La fenêtre détecte la coupure (`WM_SETTINGCHANGE`) et passe en `--solid` opaque, rayon conservé |
| Pavage, écarts, bureaux                             | Possible, par un gestionnaire de fenêtres en espace utilisateur     | Non testé (hors prototype)                                                                          |
| Animations d'ouverture et de fermeture des fenêtres | Celles de Windows                                                   | Non testé (hors prototype)                                                                          |
| Écran de verrouillage                               | Celui de Windows                                                    | Non testé (hors prototype)                                                                          |

## 14. Checklist avant de livrer une UI

- [ ] Lisible posé sur un fond blanc **et** sur un fond noir (fond d'écran clair, navigateur clair, jeu sombre).
- [ ] Lisible avec chaque préréglage d'accent et d'encre, avec le verre à 0,6, et **sans flou** (repli `--solid`).
- [ ] Aucune couleur écrite en dur là où un jeton existe : l'accent et l'encre doivent suivre les réglages.
- [ ] Chaque élément flottant est un verre plein avec son liseré haut/gauche, à 12 des bords.
- [ ] Une seule couleur d'accent ; rouge/ambre/vert uniquement pour des états.
- [ ] Aucun élément décoratif animé ; aucune étoile, particule ou motif de fond.
- [ ] Rayons pris dans l'échelle (6/8/9/10/12/14/18/22).
- [ ] Aucun glow, dégradé décoratif, bordure latérale décorative ni tracked caps.
- [ ] Contenu dense découpé en catégories.
- [ ] Chaque apparition et changement d'état a sa transition de la section 11 ; aucun rebond.
- [ ] Fluide pendant les animations, flou actif, sur la machine la plus modeste visée.
- [ ] Animations réduites respectées.
