# Crumbants (Godot 4.7 · C#)

Un gâteau géant, des millions de miettes, une seule fève. Version PC (Steam) de *Crumbants* (anciennement « La Fève »), reprise de zéro sous Godot à partir du jeu web three.js (`~/jeu-fourmis`, dont le `GAME_DESIGN.md` reste la référence pour les règles et l'équilibrage).

## Ouvrir et lancer

1. Lancer **Godot 4.7.2 .NET** (`C:\Godot_v4.7.2-stable_mono_win64\…`).
2. *Importer* → choisir `\\wsl.localhost\Ubuntu\home\goumarry\jeu-fourmis-godot\project.godot`.
3. **F5** pour jouer (l'éditeur compile le C# automatiquement).

Le SDK .NET 10 doit être installé sur Windows (l'éditeur compile avec). Une compilation lancée depuis WSL (`dotnet build`) utilise son propre dossier `obj_wsl/` : les deux ne se gênent pas.

## Commandes

| Touche | Action |
|---|---|
| ZQSD / WASD, flèches | se déplacer (pousser contre un mur pour l'escalader) |
| Espace | sauter |
| Clic gauche maintenu | croquer le bloc visé (blanc = à portée, rouge = trop loin) · en mode construction : glisser pose des tapis, ou démolit en chaîne avec le marteau |
| Clic droit + glisser | orienter la caméra (pas de zoom libre) |
| V | changer de vue : 3e personne proche → 3e personne large (construction) → vue fourmi |
| E | ramasser la fève |
| B / H | boutique / aide |
| 1 · 2 à 8 · 9 | récolter · bâtiments (tapis, grue, entrepôt, roue, compacteur, four, décorateur) · démolir |
| R | tourner le bâtiment (survolé ou à poser) |
| E (près du bureau) | parler à la gestionnaire (répartition des ouvrières) |
| G | radiographie : galeries fermées (bleu clair en bas → bleu nuit en haut) → fourmis (silhouettes rouges à travers les murs) → les deux → coupé |
| M | couper le son |
| F3 | afficher / masquer les FPS (aussi dans Options) |
| Échap | fermer les panneaux, quitter le mode construction, pause |
| Manette | sticks, A sauter, gâchette droite croquer, Y boutique, X interagir, Start pause |

## Architecture

```
Scripts/
  Core/Cfg.cs           équilibrage et constantes (porté de config.js)
  Core/Sfx.cs           sons synthétisés au démarrage
  Sim/CakeSim.cs        un gâteau : blocs, croûte, solidité, éboulis, fève
  Sim/GameState.cs      argent, boutique, fourmi, fève, revenus, sauvegarde
  Render/CakeField.cs   champ de densité + surface lisse (surface nets), partagé avec les collisions
  Render/CakeView.cs    tranches 16×16 du gâteau, reconstruites en parallèle
  Render/WorldBuilder   ciel, soleil, couverture, herbe instanciée, fourmilière, cloches
  Render/AntModel.cs    fourmi articulée animée (marche en trépied)
  Render/Effects.cs     miettes, étincelles, textes flottants, visée, éboulis, fève
  Player/…              physique de la fourmi, visée DDA, caméras
  UI/Hud.cs             HUD, boutique, aide
  Main.cs               boucle : simulation à 20 Hz, événements, sauvegarde
  Render/PropField.cs    relief des objets vu de dessus (ouvrières, construction, objets posés)
  Render/PropPhysics.cs  collisions exactes des objets (maillages 3D) pour la fourmi et la caméra
  Render/CakeToppings.cs garnitures 3D des gâteaux (cerises, fraises, macarons, meringues, myrtilles), gobées au contact
  Render/NestDocksView.cs guichets de livraison du nid (tunnels Models/docks.glb, gardiennes, porteuses)
  Render/MatLib.cs       matériaux procéduraux appliqués aux modèles Blender (bois, osier, faïence, tissu, métal…)
Shaders/                gâteau (un style par gâteau), ciel à nuages, post-traitement (contour, netteté, vignette),
                        couverture vichy filtrée, herbe, prairie, verre réfractant, fourmi laquée, matériaux d'objets
```

La simulation (`Sim/`) ne dépend pas du rendu. Sauvegarde automatique toutes les 30 s et à la fermeture (`user://save_v1.json`).

## Arguments de développement

À passer après `--` : `Godot…console.exe --path <projet> -- --new --shot=C:\tmp\a.png`

- `--new` : nouvelle partie (ignore la sauvegarde)
- `--money=1000000` : pièces au départ
- `--demo` : creuse un cratère et une galerie dans le 1er gâteau
- `--cam=x,y,z,tx,ty,tz` : caméra imposée
- `--shot=<png>` et `--delay=<s>` : capture d'écran puis quitte
- `--press=move_forward` : maintient une action (test de marche)
- `--bite` : croque en continu
- `--env=tonemap_exposure:1.1,ssao_enabled:0` : régler l'environnement à la volée (propriétés de `Environment`)
- `--open=shop|help|prices` : ouvrir un panneau
- `--lang=fr|en|es|de` : langue imposée (non enregistrée) · `--options` : ouvrir l'écran des options
- `--feveview` : creuse un puits jusqu'à la fève du 1er gâteau et la filme
- `--feedcomp` : (avec `--factory`) alimente la chaîne compacteur → four → décorateur en miettes
- `--feed` : (avec `--factory`) pose des miettes sur le tapis du guichet nord, pour tester les livraisons
- `--pos=x,z` : poser la fourmi · `--yaw=a` : orienter la caméra (avec `--press=move_forward` : test d'escalade)
- `--camfollow=dx,dy,dz` : caméra fixe par rapport à la fourmi · `--plog` : trace l'escalade · `--wlog` : état des ouvrières toutes les 10 s
- `--camcrane` : filme la fourmi suspendue de la grue
- `--to2=<s>` : à t secondes de jeu, débloque le gâteau 2 et y envoie toutes les ouvrières (test de réaffectation)
- `--crop=x,y,l,h[,zoom]` : (avec `--shot`) n'enregistre que cette zone, agrandie (vérifier l'interface)
- `--items` : chaque objet (miette, boulette, petit gâteau, gâteau royal) de chaque gâteau, posé sur des tapis à l'arrêt (vérifier modèles et couleurs)
- `--store=x,z;x,z` : pose des entrepôts · choix du dépôt : par défaut vrai trajet parmi les 2 entrepôts les plus proches · `--straightdrop` : à vol d'oiseau · `--pathdrop` : vrai trajet vers tous · `--dooronly` : ancien calcul des trajets (tout depuis la porte), pour comparer · `--eatall` : 1er gâteau mangé d'un coup (trophée)
- `--mode=comp` : outil de construction sélectionné · `--hover=x,z` : case survolée imposée (voir l'aperçu de pose en capture)
- `--quality=0|1|2` : profil de qualité imposé (non enregistré) · `--novsync` : sans synchro verticale (mesures de FPS) · `--showfps` : compteur affiché · `--legacy` : ancien réglage « rapide » (comparaison) · `--save=<fichier>` : charger une autre sauvegarde
- `--signcam=k[,d]` : caméra face au panneau du gâteau k · `--demolishtest` : (avec `--factory`) démolition en chaîne simulée
- `--noclear` : désactive le déblaiement des éboulis par les ouvrières (comparaison)
- `--fp` : vue fourmi · `--workers=30` : ouvrières · `--factory` : usine de test · `--speed=10` : simulation accélérée · `--title` : forcer l'écran titre

## Feuille de route

- [x] **Étape 1 · socle** : monde, gâteaux voxel lisses, fourmi, caméra, croquer/vendre, boutique, fève, éboulements, sauvegarde
- [x] Étape 2 · ouvrières (essaim instancié, galeries 3D 2×2, gestionnaire)
- [x] Étape 3 · bâtiments : tapis à virages, grues animées, entrepôts, roues, usines, mode construction
- [x] Étape 4 · décor du pique-nique (Blender : `Models/props.glb`), panneaux, portique, serpentins (plus de chemin ni d'accélération : trajets directs, `Layout.Direct`)
- [x] Étape 5 · écran titre, pause, options, musique et ambiance générées, manette, radiographie
- [ ] Steam : compte Steamworks + App ID, puis GodotSteam (succès, sauvegarde cloud)
- [ ] Équilibrage à rejouer de bout en bout, tutoriel, effets de lumière des fours, plis de la couverture

Les tapis ne livrent qu'aux 4 guichets du nid (`Cfg.NestDocks`), en arrivant de face.

Les machines (entrepôt, compacteur, four, décorateur) occupent **3 × 3 cases** (la case survolée est le centre) : une entrée au milieu de chaque côté et la sortie au milieu de l'avant, chacune avec son tapis intégré qui défile quand la machine est alimentée. L'entrepôt n'a que son tapis de sortie (les ouvrières et la joueuse le remplissent), qui ne tourne qu'avec le courant. Les roues à fourmi font **2 × 2** (la case survolée et ses voisines est et sud) et alimentent à 9 cases de leur centre. Les grues font **2 × 2** : mât sur la case arrière droite, tapis de sortie sur la case avant gauche (leur seule sortie, à brancher comme celle des autres machines) ; leur flèche est faite de tronçons ajoutés selon la portée. Les tapis restent en 1 × 1. Les tapis intégrés ne défilent que s'ils sont alimentés ET branchés à un tapis ou une machine. Une ancienne sauvegarde dont une machine n'a plus la place est remboursée au chargement.

Modèles 3D : `Blender/buildings2.py` → `Models/buildings.glb` (machines, casque de chantier des opératrices, panneau des gâteaux ; les places des opératrices sont des repères `op_<type>_spot` lus par `ModelLib.Marker`), `Blender/props2.py` → `Models/props.glb` (décor, à exécuter dans Blender via le MCP ou en ligne de commande), `Blender/docks.py` → `Models/docks.glb` (lancé avec `blender.exe -b --python`), `Models/ant.glb` (pièces de la fourmi, pivots aux articulations), générés par script dans Blender (MCP).
