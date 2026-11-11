# Règles git et pull requests

> Règles du dépôt Azuria, reprises de celles de Manhwadle. Elles valent pour tout le monde, humains comme agents
> (Claude Code compris).

## 1. Branches

| Branche         | Rôle                                                         | Comment on y écrit             |
| --------------- | ------------------------------------------------------------ | ------------------------------ |
| `dev`           | Branche **par défaut**. Tout le travail y arrive.            | PR seulement                   |
| `main`          | Jalons validés, chacun avec son tag de version.              | PR `dev` → `main` seulement    |
| `<type>/<sujet>` | Une branche par sujet, créée depuis `dev` à jour.           | Commits de l'auteur            |

Préfixes : `feature/`, `fix/`, `docs/`, `refactor/`, `test/`, `ci/`, `chore/`.
Sujet en français, minuscules et tirets : `feature/perso-du-jour-serveur`, `fix/suggestions-defilement`.

Règles :

1. **Jamais de commit direct** sur `dev` ni `main` : les deux sont protégées, une PR est obligatoire.
2. **Toujours partir de `dev` à jour, toujours viser `dev`** :
   ```sh
   git switch dev && git pull
   git switch -c feature/mon-sujet
   ```
3. **Pas de PR empilées.** Une PR ne prend jamais pour base la branche d'une autre PR : sinon elle est fusionnée
   dans cette branche et n'atteint jamais `dev`. Si un travail dépend d'une PR ouverte, on attend sa fusion, puis on
   repart de `dev`.
4. Une branche = un sujet.
5. Les branches fusionnées sont supprimées automatiquement.

## 2. Commits

Format Conventional Commits, **en français** : `type(portée): sujet`. La portée est facultative.

```
feat(serveur): perso du jour joué côté serveur
fix(pwa): l'appli installée détecte les mises à jour et le propose dans le jeu
refactor(jeu): extrait le tableau des essais (grid.js)
ci: réessaie le build en cas d'échec de téléchargement
```

Types : `feat`, `fix`, `refactor`, `test`, `docs`, `ci`, `chore`.

- Le sujet décrit le résultat visible, au présent, sans majuscule ni point final.
- Plusieurs petits commits par branche sont acceptés : ils sont conservés tels quels à la fusion.
- On ne réécrit pas l'historique de `dev` ni de `main` : pas de `push --force`.
- Pas de secret ni de jeton dans un commit, même temporairement.

## 3. Pull requests

### Titre

Même format qu'un commit : `fix(connexion): montre le code avant d'ouvrir Discord`.
Si la PR change la version, on l'indique entre parenthèses : `fix: la liste suit le champ au défilement (0.1.10)`.

### Description

Pas de modèle imposé, mais toujours ces informations, en français :

```markdown
## Contenu        (ou « Signalé » + « Correctif » pour un bug)
Ce qui change, et pourquoi.

## Limites connues
Ce qui n'est pas fait ou reste fragile.

## Déploiement     (seulement si besoin)
Ordre à respecter : migrations, release avant le serveur, etc.

## Vérifié
- `npm test` : 34 réussis.
- Ce qui a été essayé à la main, et sur quelle plateforme.

## Non vérifié
Ce qu'on n'a pas pu tester (vrai téléphone, macOS…).
```

« Vérifié » et « Non vérifié » sont obligatoires : on préfère une limite annoncée à une surprise après la fusion.
Pas de pied « Generated with Claude Code » ni de lien de session.

### Fusion

- La CI doit être **verte** : les checks (tests, lint, builds de chaque plateforme) sont requis sur `dev` et `main`.
- **C'est le propriétaire du dépôt qui fusionne**, avec un **merge commit** (« Create a merge commit ») : pas de squash
  ni de rebase. Les commits de la branche sont gardés, et `main` reste un ancêtre de `dev`.

## 4. Releases

Une release = un jalon que le propriétaire a validé. Rien ne part sur `main` sans son accord.

1. **Bumper `version` dans la PR qui justifie la release** (le correctif ou la fonctionnalité), pas après coup.
   Sans changement de numéro, les applis installées ne voient pas la mise à jour.
   Semver à **trois chiffres** : `0.1.10`, `0.2.0`.
2. Ouvrir une PR `dev` → `main`, intitulée `vX.Y.Z — résumé` (ou un résumé en clair pour un petit jalon).
   La description liste les PR incluses, ce qui doit être fusionné avant, et l'ordre de sortie.
3. Après fusion, poser un **tag annoté sur le commit de fusion dans `main`** :
   ```sh
   git switch main && git pull
   git tag -a vX.Y.Z <sha-du-merge> -m "vX.Y.Z"
   git push origin vX.Y.Z
   ```
   Les tags `v*` sont protégés (ruleset « Tags de version protégés »). La CI de release se lance sur le tag et
   publie les builds de chaque plateforme dans la release GitHub.
4. Vérifier que la CI de release est verte et que tous les fichiers sont attachés (y compris ceux des mises à jour
   automatiques).
5. Ce qui dépend de la nouvelle version (serveur, migrations, site) se déploie **après** la release, jamais avant :
   une ancienne version du client doit pouvoir fonctionner jusqu'à sa mise à jour.

## 5. Réglages GitHub du dépôt

- Branche par défaut : `dev`.
- Protection de `dev` et `main` : PR obligatoire (0 relecture requise), checks CI requis, pas de force-push ni de
  suppression.
- Ruleset « Tags de version protégés » sur `v*`.
- Suppression automatique des branches fusionnées.
- Sécurité : secret scanning + push protection, alertes et correctifs Dependabot, signalement privé, approbation des
  workflows pour tous les contributeurs externes.

## 6. Pour les agents (Claude Code)

- Brancher depuis `dev` à jour, viser `dev`, ne jamais empiler.
- Vérifier la sortie de `git push` (ne pas la masquer) : une PR peut sinon être fusionnée sans son dernier commit.
- Ne jamais fusionner de PR, pousser de tag, modifier une protection de branche ni faire de `push --force` :
  donner la commande au propriétaire.
- Lancer les tests et le lint avant d'ouvrir la PR, et remplir « Vérifié » / « Non vérifié » honnêtement.
- Déployer un serveur seulement avec l'accord du propriétaire, et après la release.
