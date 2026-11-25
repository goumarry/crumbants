// ─────────────────────────────────────────────────────────────
// Traductions. Une ligne par texte : français | anglais | espagnol | allemand
// La colonne française est la clé : elle doit être identique, au caractère
// près, au texte écrit dans le code. {0}, {1}… sont remplacés à l'affichage.
// ─────────────────────────────────────────────────────────────
using System.Collections.Generic;

namespace Crumbants;

public static class Strings
{
    public static IEnumerable<string[]> Rows()
    {
        foreach (var raw in Table.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var cols = line.Split(" | ");
            for (int i = 0; i < cols.Length; i++) cols[i] = cols[i].Trim();
            yield return cols;
        }
    }

    const string Table = """
# ── Titre, menus, options ──
Un gâteau géant. Des millions de miettes. Une seule fève. | One giant cake. Millions of crumbs. Just one lucky charm. | Un pastel gigante. Millones de migas. Una sola haba. | Ein Riesenkuchen. Millionen Krümel. Nur eine Bohne.
Ta fourmi | Your ant | Tu hormiga | Deine Ameise
Son nom | Her name | Su nombre | Ihr Name
Fourmi | Ant | Hormiga | Ameise
{0} min | {0} min | {0} min | {0} Min.
Continuer | Continue | Continuar | Weiterspielen
Nouvelle partie | New game | Nueva partida | Neues Spiel
Sûr ? La partie sera effacée | Sure? Your game will be erased | ¿Seguro? La partida se borrará | Sicher? Der Spielstand wird gelöscht
Jouer | Play | Jugar | Spielen
Options | Options | Opciones | Optionen
Quitter | Quit | Salir | Beenden
{0} pour marcher · clic gauche pour croquer · {1} boutique · {2} aide | {0} to walk · left click to munch · {1} shop · {2} help | {0} para andar · clic izquierdo para morder · {1} tienda · {2} ayuda | {0} zum Laufen · Linksklick zum Knabbern · {1} Laden · {2} Hilfe
Pause | Paused | Pausa | Pause
Reprendre | Resume | Reanudar | Weiter
Sauvegarder et quitter | Save and quit | Guardar y salir | Speichern und beenden
Volume général | Master volume | Volumen general | Gesamtlautstärke
Musique | Music | Música | Musik
Effets sonores | Sound effects | Efectos de sonido | Soundeffekte
Sensibilité souris | Mouse sensitivity | Sensibilidad del ratón | Mausempfindlichkeit
Qualité | Quality | Calidad | Qualität
Rapide | Fast | Rápida | Schnell
Belle | Pretty | Bonita | Schön
Sublime | Stunning | Sublime | Prächtig
Plein écran | Fullscreen | Pantalla completa | Vollbild
Afficher les FPS (F3) | Show FPS (F3) | Mostrar FPS (F3) | FPS anzeigen (F3)
Langue | Language | Idioma | Sprache
Retour | Back | Volver | Zurück

# ── HUD ──
Gâteau 1 | Cake 1 | Pastel 1 | Kuchen 1
Gâteau {0} | Cake {0} | Pastel {0} | Kuchen {0}
Gâteau {0} · {1} | Cake {0} · {1} | Pastel {0} · {1} | Kuchen {0} · {1}
{0} miettes · ×{1} par miette | {0} crumbs · ×{1} per crumb | {0} migas · ×{1} por miga | {0} Krümel · ×{1} pro Krümel
Miettes récoltées | Crumbs collected | Migas recogidas | Gesammelte Krümel
Colonie | Colony | Colonia | Kolonie
Toi | You | Tú | Du
Ouvrières | Workers | Obreras | Arbeiterinnen
Grues | Cranes | Grúas | Kräne
Fèves | Charms | Habas | Bohnen
Bâtiments | Buildings | Edificios | Gebäude
Trophées | Trophies | Trofeos | Trophäen
Temps de jeu | Play time | Tiempo de juego | Spielzeit
Boutique | Shop | Tienda | Laden
Prix | Prices | Precios | Preise
Radiographie | X-ray | Rayos X | Röntgen
Son | Sound | Sonido | Ton
Aide | Help | Ayuda | Hilfe
Croquer | Munch | Morder | Knabbern
Récolter | Harvest | Recolectar | Ernten
Démolir (50 % remboursés) | Demolish (50% refunded) | Demoler (50 % reembolsado) | Abreißen (50 % zurück)
Verrouillé | Locked | Bloqueado | Gesperrt
{0} FPS · {1} ms/image | {0} FPS · {1} ms/frame | {0} FPS · {1} ms/imagen | {0} FPS · {1} ms/Bild

# ── Boutique ──
Ta fourmi | Your ant | Tu hormiga | Deine Ameise
Les ouvrières | Workers | Las obreras | Die Arbeiterinnen
L'usine | The factory | La fábrica | Die Fabrik
Commerce | Trade | Comercio | Handel
Trophées · {0} / 6 · +{1} % sur tous les gains | Trophies · {0} / 6 · +{1}% on all earnings | Trofeos · {0} / 6 · +{1} % en todas las ganancias | Trophäen · {0} / 6 · +{1} % auf alle Einnahmen
{0} % mangé | {0}% eaten | {0} % comido | {0} % gegessen
niv {0} | lvl {0} | nv {0} | Stufe {0}
Requiert : {0} | Requires: {0} | Requiere: {0} | Benötigt: {0}
acquis | owned | adquirido | erworben
Niveau maximum atteint | Max level reached | Nivel máximo alcanzado | Höchststufe erreicht
Morsure : {0} → {1} miettes | Bite: {0} → {1} crumbs | Mordisco: {0} → {1} migas | Biss: {0} → {1} Krümel
Sac : {0} → {1} miettes | Bag: {0} → {1} crumbs | Mochila: {0} → {1} migas | Rucksack: {0} → {1} Krümel
Entre deux morsures : {0} s → {1} s | Between bites: {0} s → {1} s | Entre mordiscos: {0} s → {1} s | Zwischen zwei Bissen: {0} s → {1} s
Vitesse : {0} → {1} | Speed: {0} → {1} | Velocidad: {0} → {1} | Tempo: {0} → {1}
Ouvrières : {0} → {1} | Workers: {0} → {1} | Obreras: {0} → {1} | Arbeiterinnen: {0} → {1}
Charge {0} → {1} · cadence {2}/s → {3}/s | Load {0} → {1} · rate {2}/s → {3}/s | Carga {0} → {1} · ritmo {2}/s → {3}/s | Last {0} → {1} · Takt {2}/s → {3}/s
Grues : {0}/s → {1}/s · portée {2} → {3} | Cranes: {0}/s → {1}/s · reach {2} → {3} | Grúas: {0}/s → {1}/s · alcance {2} → {3} | Kräne: {0}/s → {1}/s · Reichweite {2} → {3}
Tapis : {0} → {1} cases/s | Belts: {0} → {1} tiles/s | Cintas: {0} → {1} casillas/s | Bänder: {0} → {1} Felder/s
Usines : ×{0} → ×{1} | Factories: ×{0} → ×{1} | Fábricas: ×{0} → ×{1} | Fabriken: ×{0} → ×{1}
Tous les prix : +{0} % → +{1} % | All prices: +{0}% → +{1}% | Todos los precios: +{0} % → +{1} % | Alle Preise: +{0} % → +{1} %

# Améliorations : nom | effet | prérequis
Mandibules affûtées | Sharpened mandibles | Mandíbulas afiladas | Geschärfte Mandibeln
Miettes par morsure | Crumbs per bite | Migas por mordisco | Krümel pro Biss
Sac à dos | Backpack | Mochila | Rucksack
Capacité du sac | Bag capacity | Capacidad de la mochila | Rucksackgröße
Mâchoire nerveuse | Twitchy jaw | Mandíbula nerviosa | Flinker Kiefer
Morsures plus rapides | Faster bites | Mordiscos más rápidos | Schnellere Bisse
Pattes de sprinteuse | Sprinter legs | Patas de velocista | Sprinterbeine
Vitesse de course | Running speed | Velocidad de carrera | Laufgeschwindigkeit
Recruter une ouvrière | Recruit a worker | Reclutar una obrera | Arbeiterin anwerben
+1 ouvrière | +1 worker | +1 obrera | +1 Arbeiterin
Entraîner les ouvrières | Train the workers | Entrenar a las obreras | Arbeiterinnen trainieren
Ouvrières plus fortes | Stronger workers | Obreras más fuertes | Stärkere Arbeiterinnen
1 ouvrière | 1 worker | 1 obrera | 1 Arbeiterin
Ingénierie fourmi | Ant engineering | Ingeniería hormiga | Ameisen-Ingenieurwesen
Débloque tapis, grues, entrepôts et roues à fourmi | Unlocks belts, cranes, storehouses and ant wheels | Desbloquea cintas, grúas, almacenes y ruedas de hormiga | Schaltet Bänder, Kräne, Lager und Ameisenräder frei
Ingénierie | Engineering | Ingeniería | Ingenieurwesen
Grues turbo | Turbo cranes | Grúas turbo | Turbokräne
+50 % de cadence des grues, +1 de portée | +50% crane rate, +1 reach | +50 % de ritmo de las grúas, +1 de alcance | +50 % Krantakt, +1 Reichweite
Tapis huilés | Oiled belts | Cintas engrasadas | Geölte Bänder
+30 % de vitesse des tapis | +30% belt speed | +30 % de velocidad de las cintas | +30 % Bandgeschwindigkeit
Plan : Compacteur | Blueprint: Compactor | Plano: Compactadora | Bauplan: Verdichter
Débloque le compacteur | Unlocks the compactor | Desbloquea la compactadora | Schaltet den Verdichter frei
Plan : Four | Blueprint: Oven | Plano: Horno | Bauplan: Ofen
Débloque le four | Unlocks the oven | Desbloquea el horno | Schaltet den Ofen frei
Plan : Décorateur | Blueprint: Decorator | Plano: Decoradora | Bauplan: Verzierer
Débloque le décorateur | Unlocks the decorator | Desbloquea la decoradora | Schaltet den Verzierer frei
Chefs pâtissiers | Pastry chefs | Chefs pasteleros | Konditormeister
+40 % de vitesse des usines | +40% factory speed | +40 % de velocidad de las fábricas | +40 % Fabriktempo
Pub à la télé fourmi | Ant TV commercial | Anuncio en la tele hormiga | Werbung im Ameisen-TV
+25 % sur tous les prix | +25% on all prices | +25 % en todos los precios | +25 % auf alle Preise

# ── Prix ──
Miettes du gâteau : | Crumbs from cake: | Migas del pastel: | Krümel von Kuchen:
Multiplicateur : ×{0} | Multiplier: ×{0} | Multiplicador: ×{0} | Multiplikator: ×{0}
Gâteau ×{0} · Pub +{1} % · trophées +{2} % | Cake ×{0} · Ads +{1}% · trophies +{2}% | Pastel ×{0} · Anuncio +{1} % · trofeos +{2} % | Kuchen ×{0} · Werbung +{1} % · Trophäen +{2} %
Objet | Item | Objeto | Gegenstand
Valeur | Value | Valor | Wert
Miettes | Crumbs | Migas | Krümel
Par miette | Per crumb | Por miga | Pro Krümel
Les six gâteaux | The six cakes | Los seis pasteles | Die sechs Kuchen
Gâteau | Cake | Pastel | Kuchen
Fève | Charm | Haba | Bohne
trouvée | found | encontrada | gefunden
signalée | spotted | localizada | entdeckt
à chercher | to find | por buscar | gesucht
sous cloche | under glass | bajo la campana | unter der Glocke
Miette | Crumb | Miga | Krümel
Boulette de pâte | Dough ball | Bolita de masa | Teigkugel
Petit gâteau | Cupcake | Pastelito | Törtchen
Gâteau royal | Royal cake | Pastel real | Königskuchen
Galette frangipane | Frangipane galette | Roscón de frangipane | Frangipane-Galette
Forêt-noire | Black Forest | Selva Negra | Schwarzwälder
Fraisier | Strawberry cake | Tarta de fresas | Erdbeertorte
Matcha impérial | Imperial matcha | Matcha imperial | Kaiserlicher Matcha
Citron meringué | Lemon meringue | Limón merengado | Zitronen-Baiser
Myrtille cosmique | Cosmic blueberry | Arándano cósmico | Kosmische Blaubeere
Pépite de chocolat | Chocolate chip | Pepita de chocolate | Schokostückchen
Amande entière | Whole almond | Almendra entera | Ganze Mandel
Pépite | Chocolate chip | Pepita | Schokostück
Amande | Almond | Almendra | Mandel

# ── Aide ──
Le but | The goal | El objetivo | Das Ziel
Six gâteaux, six fèves. Croque, vends tes miettes à la fourmilière, agrandis ta colonie, et rapporte chaque fève au nid pour débloquer le gâteau suivant. | Six cakes, six lucky charms. Munch, sell your crumbs at the anthill, grow your colony, and bring each charm back to the nest to unlock the next cake. | Seis pasteles, seis habas. Muerde, vende tus migas en el hormiguero, haz crecer tu colonia y lleva cada haba al nido para desbloquear el siguiente pastel. | Sechs Kuchen, sechs Bohnen. Knabbere, verkaufe deine Krümel am Ameisenhaufen, vergrößere deine Kolonie und bring jede Bohne zum Nest, um den nächsten Kuchen freizuschalten.
Commandes | Controls | Controles | Steuerung
Touche | Key | Tecla | Taste
Action | Action | Acción | Aktion
flèches | arrows | flechas | Pfeiltasten
se déplacer (pousse contre un mur pour l'escalader) | move (push against a wall to climb it) | moverse (empuja contra una pared para trepar) | bewegen (gegen eine Wand drücken, um hochzuklettern)
Espace | Space | Espacio | Leertaste
sauter | jump | saltar | springen
Clic gauche maintenu | Hold left click | Mantener clic izquierdo | Linksklick halten
croquer · construire (glisser pour les tapis) · démolir (glisser : en chaîne) | munch · build (drag for belts) · demolish (drag: chain) | morder · construir (arrastra para las cintas) · demoler (arrastra: en cadena) | knabbern · bauen (ziehen für Bänder) · abreißen (ziehen: am Stück)
Clic droit + glisser | Right click + drag | Clic derecho + arrastrar | Rechtsklick + ziehen
orienter la caméra | turn the camera | girar la cámara | Kamera drehen
changer de vue : proche → large (construction) → fourmi | change view: close → wide (building) → ant | cambiar de vista: cerca → amplia (construcción) → hormiga | Ansicht wechseln: nah → weit (Bauen) → Ameise
ramasser la fève · parler à la gestionnaire | pick up the charm · talk to the manager | recoger el haba · hablar con la gestora | Bohne aufheben · mit der Verwalterin sprechen
1 · 2 à 8 · 9 | 1 · 2 to 8 · 9 | 1 · 2 a 8 · 9 | 1 · 2 bis 8 · 9
récolter · bâtiments · démolir | harvest · buildings · demolish | recolectar · edificios · demoler | ernten · Gebäude · abreißen
tourner un bâtiment | rotate a building | girar un edificio | Gebäude drehen
boutique · prix · aide · radiographie · son | shop · prices · help · x-ray · sound | tienda · precios · ayuda · rayos X · sonido | Laden · Preise · Hilfe · Röntgen · Ton
Échap | Esc | Esc | Esc
fermer · pause | close · pause | cerrar · pausa | schließen · Pause
Astuces | Tips | Consejos | Tipps
La gestionnaire, assise à côté du nid, répartit tes ouvrières entre les gâteaux. | The manager, sitting next to the nest, assigns your workers to the cakes. | La gestora, sentada junto al nido, reparte tus obreras entre los pasteles. | Die Verwalterin neben dem Nest teilt deine Arbeiterinnen auf die Kuchen auf.
Une grue posée au bord du gâteau grignote le dessus : relie-la au nid par des tapis, et alimente le tout avec une roue à fourmi. | A crane placed at the edge of a cake nibbles its top: link it to the nest with belts, and power it all with an ant wheel. | Una grúa al borde del pastel mordisquea la parte de arriba: conéctala al nido con cintas y aliméntalo todo con una rueda de hormiga. | Ein Kran am Kuchenrand knabbert die Oberseite an: Verbinde ihn mit Bändern zum Nest und versorge alles mit einem Ameisenrad.
Compacteur, puis four, puis décorateur : la même miette vaut jusqu'à 5 fois plus. | Compactor, then oven, then decorator: the same crumb is worth up to 5 times more. | Compactadora, luego horno, luego decoradora: la misma miga vale hasta 5 veces más. | Verdichter, dann Ofen, dann Verzierer: Derselbe Krümel ist bis zu 5-mal mehr wert.
Seule ta fourmi trouve des pépites et des amandes en croquant. | Only your ant finds chocolate chips and almonds while munching. | Solo tu hormiga encuentra pepitas y almendras al morder. | Nur deine Ameise findet beim Knabbern Schokostückchen und Mandeln.
Chaque gâteau mangé à 100 % rapporte un trophée : +10 % sur tous les gains. | Each cake eaten 100% earns a trophy: +10% on all earnings. | Cada pastel comido al 100 % da un trofeo: +10 % en todas las ganancias. | Jeder zu 100 % gegessene Kuchen bringt eine Trophäe: +10 % auf alle Einnahmen.
Recommencer de zéro | Start over from scratch | Empezar de cero | Ganz neu anfangen
Sûr ? Tout sera effacé | Sure? Everything will be erased | ¿Seguro? Se borrará todo | Sicher? Alles wird gelöscht

# ── Gestionnaire ──
Gestion des ouvrières | Worker management | Gestión de obreras | Arbeiterinnen-Verwaltung
{0} ouvrière | {0} worker | {0} obrera | {0} Arbeiterin
{0} ouvrières | {0} workers | {0} obreras | {0} Arbeiterinnen
{0} au repos | {0} resting | {0} descansando | {0} in Pause
« Dites-moi où envoyer tout ce petit monde. » | “Tell me where to send this little crowd.” | «Dime adónde mando a toda esta gente.» | „Sag mir, wohin ich all die Kleinen schicken soll.“
« Zzz… Un seul gâteau, rien à organiser. » | “Zzz… Just one cake, nothing to organize.” | «Zzz… Un solo pastel, nada que organizar.» | „Zzz… Nur ein Kuchen, nichts zu organisieren.“
Sous cloche | Under glass | Bajo la campana | Unter der Glocke
{0} restantes | {0} left | {0} restantes | {0} übrig
fève trouvée | charm found | haba encontrada | Bohne gefunden
fève signalée ! | charm spotted! | ¡haba localizada! | Bohne entdeckt!
fève cachée | charm hidden | haba escondida | Bohne versteckt
{0} en route | {0} on the way | {0} en camino | {0} unterwegs
ENRAGÉES | ENRAGED | FURIOSAS | WÜTEND
Toutes | All | Todas | Alle
recrues | recruits | reclutas | Neue
Répartir équitablement | Spread evenly | Repartir por igual | Gleichmäßig verteilen
Maj + clic : par 5 | Shift + click: by 5 | Mayús + clic: de 5 en 5 | Umschalt + Klick: 5 auf einmal
Elle dort profondément… Rapporte la fève du gâteau 1 pour la réveiller. | She's fast asleep… Bring back the charm from cake 1 to wake her up. | Duerme profundamente… Trae el haba del pastel 1 para despertarla. | Sie schläft tief und fest… Bring die Bohne aus Kuchen 1 zurück, um sie zu wecken.
Parler à la gestionnaire | Talk to the manager | Hablar con la gestora | Mit der Verwalterin sprechen
Elle dort · rapporte la fève n°1 | She's asleep · bring back charm #1 | Está dormida · trae el haba n.º 1 | Sie schläft · bring Bohne Nr. 1 zurück

# ── Bâtiments ──
Tapis roulant | Conveyor belt | Cinta transportadora | Förderband
Transporte 1 objet par case (électrique) | Carries 1 item per tile (electric) | Transporta 1 objeto por casilla (eléctrica) | Befördert 1 Gegenstand pro Feld (elektrisch)
Grue | Crane | Grúa | Kran
Grue pilotée par une fourmi (2 × 2 cases) : grignote le gâteau par le dessus et dépose les miettes sur son tapis de sortie (électrique) | Ant-operated crane (2 × 2 tiles): nibbles the cake from the top and drops the crumbs on its output belt (electric) | Grúa manejada por una hormiga (2 × 2 casillas): mordisquea el pastel por arriba y deja las migas en su cinta de salida (eléctrica) | Von einer Ameise gesteuerter Kran (2 × 2 Felder): knabbert den Kuchen von oben an und legt die Krümel auf sein Ausgangsband (elektrisch)
Entrepôt | Storehouse | Almacén | Lager
Les ouvrières y déposent leurs miettes s'il est plus près que le nid ; une magasinière les renvoie par le tapis de sortie (électrique) | Workers drop their crumbs here if it's closer than the nest; a storekeeper sends them out on the output belt (electric) | Las obreras dejan aquí sus migas si está más cerca que el nido; una almacenera las envía por la cinta de salida (eléctrico) | Arbeiterinnen bringen ihre Krümel hierher, wenn es näher als das Nest ist; eine Lageristin schickt sie über das Ausgangsband weiter (elektrisch)
Roue à fourmi | Ant wheel | Rueda de hormiga | Ameisenrad
Une fourmi court dans la roue (2 × 2 cases) : électricité pour les grues, tapis et usines à moins de 9 cases | An ant runs in the wheel (2 × 2 tiles): powers cranes, belts and factories within 9 tiles | Una hormiga corre en la rueda (2 × 2 casillas): electricidad para grúas, cintas y fábricas a menos de 9 casillas | Eine Ameise läuft im Rad (2 × 2 Felder): Strom für Kräne, Bänder und Fabriken im Umkreis von 9 Feldern
Compacteur | Compactor | Compactadora | Verdichter
25 miettes → 1 boulette (électrique) | 25 crumbs → 1 dough ball (electric) | 25 migas → 1 bolita (eléctrica) | 25 Krümel → 1 Teigkugel (elektrisch)
Four | Oven | Horno | Ofen
4 boulettes → 1 petit gâteau (électrique) | 4 dough balls → 1 cupcake (electric) | 4 bolitas → 1 pastelito (eléctrico) | 4 Teigkugeln → 1 Törtchen (elektrisch)
Décorateur | Decorator | Decoradora | Verzierer
3 petits gâteaux → 1 gâteau royal (électrique) | 3 cupcakes → 1 royal cake (electric) | 3 pastelitos → 1 pastel real (eléctrica) | 3 Törtchen → 1 Königskuchen (elektrisch)
Pas assez de place ({0} × {0} cases) | Not enough room ({0} × {0} tiles) | No hay sitio suficiente ({0} × {0} casillas) | Nicht genug Platz ({0} × {0} Felder)
Emplacement impossible | Can't build here | No se puede construir aquí | Hier kann nicht gebaut werden
Trop près d'une autre grue ({0} cases minimum) | Too close to another crane ({0} tiles minimum) | Demasiado cerca de otra grúa (mínimo {0} casillas) | Zu nah an einem anderen Kran (mindestens {0} Felder)
Pas assez de pièces | Not enough coins | No tienes suficientes monedas | Nicht genug Münzen
{0} : débloque d'abord « {1} » dans la boutique ({2}) | {0}: first unlock “{1}” in the shop ({2}) | {0}: primero desbloquea «{1}» en la tienda ({2}) | {0}: Schalte zuerst „{1}“ im Laden frei ({2})
Démoli · +{0} remboursés | Demolished · +{0} refunded | Demolido · +{0} reembolsado | Abgerissen · +{0} zurück
{0} constructions démolies · +{1} remboursés | {0} buildings demolished · +{1} refunded | {0} construcciones demolidas · +{1} reembolsado | {0} Bauten abgerissen · +{1} zurück
Grignote le gâteau par le dessus, la partie la plus proche d'abord, et dépose les miettes sur son tapis de sortie (à brancher). | Nibbles the cake from the top, nearest part first, and drops the crumbs on its output belt (connect it). | Mordisquea el pastel por arriba, primero la parte más cercana, y deja las migas en su cinta de salida (conéctala). | Knabbert den Kuchen von oben an, das Nächstgelegene zuerst, und legt die Krümel auf sein Ausgangsband (anschließen).
Stock : {0} / {1} miettes | Stock: {0} / {1} crumbs | Reserva: {0} / {1} migas | Vorrat: {0} / {1} Krümel
Stock : {0} / {1} {2} | Stock: {0} / {1} {2} | Reserva: {0} / {1} {2} | Vorrat: {0} / {1} {2}
G{0} | C{0} | P{0} | K{0}
Alimente grues, tapis et usines à moins de {0} cases. | Powers cranes, belts and factories within {0} tiles. | Alimenta grúas, cintas y fábricas a menos de {0} casillas. | Versorgt Kräne, Bänder und Fabriken im Umkreis von {0} Feldern.
Fournée : {0} × {1} du gâteau {2} · {3} % | Batch: {0} × {1} from cake {2} · {3}% | Hornada: {0} × {1} del pastel {2} · {3} % | Charge: {0} × {1} aus Kuchen {2} · {3} %
Transporte : {0} × {1} · gâteau {2} | Carrying: {0} × {1} · cake {2} | Transporta: {0} × {1} · pastel {2} | Befördert: {0} × {1} · Kuchen {2}
Vide | Empty | Vacía | Leer
Pas de courant : pose une roue à moins de {0} cases | No power: place a wheel within {0} tiles | Sin corriente: pon una rueda a menos de {0} casillas | Kein Strom: Stell ein Rad im Umkreis von {0} Feldern auf
Sortie bloquée | Output blocked | Salida bloqueada | Ausgang blockiert
Plus rien à portée | Nothing left in reach | No queda nada al alcance | Nichts mehr in Reichweite
En attente | Waiting | En espera | Wartet
En marche | Running | En marcha | In Betrieb
{0} : tourner · 9 : démolir | {0}: rotate · 9: demolish | {0}: girar · 9: demoler | {0}: drehen · 9: abreißen
Les machines font maintenant 3 × 3 cases : remboursées faute de place ({0}) | Machines are now 3 × 3 tiles: refunded for lack of room ({0}) | Las máquinas ocupan ahora 3 × 3 casillas: reembolsadas por falta de sitio ({0}) | Maschinen sind jetzt 3 × 3 Felder groß: mangels Platz erstattet ({0})

# ── Messages en jeu ──
Bon retour, {0} ! | Welcome back, {0}! | ¡Bienvenida de nuevo, {0}! | Willkommen zurück, {0}!
{0} fève trouvée | {0} charm found | {0} haba encontrada | {0} Bohne gefunden
{0} fèves trouvées | {0} charms found | {0} habas encontradas | {0} Bohnen gefunden
Bienvenue, {0} ! Croque le gâteau, vends au nid, trouve la fève | Welcome, {0}! Munch the cake, sell at the nest, find the charm | ¡Bienvenida, {0}! Muerde el pastel, vende en el nido, encuentra el haba | Willkommen, {0}! Knabber den Kuchen, verkaufe am Nest, finde die Bohne
Clic gauche maintenu sur le gâteau pour croquer, puis rapporte ton sac à la fourmilière. | Hold left click on the cake to munch, then bring your bag back to the anthill. | Mantén el clic izquierdo sobre el pastel para morder y luego lleva tu mochila al hormiguero. | Halte die linke Maustaste auf dem Kuchen gedrückt, um zu knabbern, und bring dann deinen Rucksack zum Ameisenhaufen.
Son coupé | Sound off | Sonido apagado | Ton aus
Son activé | Sound on | Sonido activado | Ton an
Ramasser la fève | Pick up the charm | Recoger el haba | Bohne aufheben
Au nid : {0} m | To the nest: {0} m | Al nido: {0} m | Zum Nest: {0} m
Fève signalée · {0} m | Charm spotted · {0} m | Haba localizada · {0} m | Bohne entdeckt · {0} m
Rapporte la fève à la fourmilière ! | Bring the charm back to the anthill! | ¡Lleva el haba al hormiguero! | Bring die Bohne zum Ameisenhaufen!
Amande entière : +{0} | Whole almond: +{0} | Almendra entera: +{0} | Ganze Mandel: +{0}
Sac plein ! Va vendre à la fourmilière | Bag full! Go sell at the anthill | ¡Mochila llena! Ve a vender al hormiguero | Rucksack voll! Ab zum Verkaufen am Ameisenhaufen
Une ouvrière a déterré la fève ! | A worker dug up the charm! | ¡Una obrera ha desenterrado el haba! | Eine Arbeiterin hat die Bohne ausgegraben!
Une grue a déterré la fève ! | A crane dug up the charm! | ¡Una grúa ha desenterrado el haba! | Ein Kran hat die Bohne ausgegraben!
Tu as déterré la fève ! | You dug up the charm! | ¡Has desenterrado el haba! | Du hast die Bohne ausgegraben!
Ramasse-la (E) et rapporte-la au nid | Pick it up (E) and bring it back to the nest | Recógela (E) y llévala al nido | Heb sie auf (E) und bring sie zum Nest
Va la ramasser (E) et rapporte-la au nid | Go pick it up (E) and bring it back to the nest | Ve a recogerla (E) y llévala al nido | Heb sie auf (E) und bring sie zum Nest
Tu portes la fève ! Rapporte-la à la fourmilière | You're carrying the charm! Bring it back to the anthill | ¡Llevas el haba! Llévala al hormiguero | Du trägst die Bohne! Bring sie zum Ameisenhaufen
Fève n°{0} trouvée ! | Charm #{0} found! | ¡Haba n.º {0} encontrada! | Bohne Nr. {0} gefunden!
une couronne pour la colonie | a crown for the colony | una corona para la colonia | eine Krone für die Kolonie
Nouveau gâteau débloqué : {0} | New cake unlocked: {0} | Nuevo pastel desbloqueado: {0} | Neuer Kuchen freigeschaltet: {0}
Trophée : {0} ! | Trophy: {0}! | ¡Trofeo: {0}! | Trophäe: {0}!
Gâteau {0} mangé à 100 % · +10 % sur tous les gains (total +{1} %) | Cake {0} eaten 100% · +10% on all earnings (total +{1}%) | Pastel {0} comido al 100 % · +10 % en todas las ganancias (total +{1} %) | Kuchen {0} zu 100 % gegessen · +10 % auf alle Einnahmen (insgesamt +{1} %)
Trophée du gâteau {0} : +10 % sur tous les gains | Cake {0} trophy: +10% on all earnings | Trofeo del pastel {0}: +10 % en todas las ganancias | Trophäe von Kuchen {0}: +10 % auf alle Einnahmen
Gâteau {0} : plus de galerie sûre, ses ouvrières sont ENRAGÉES ! Elles l'attaquent par le dessus jusqu'à la dernière miette. | Cake {0}: no safe tunnels left, its workers are ENRAGED! They attack it from the top down to the last crumb. | Pastel {0}: no quedan galerías seguras, ¡sus obreras están FURIOSAS! Lo atacan por arriba hasta la última miga. | Kuchen {0}: keine sicheren Gänge mehr, seine Arbeiterinnen sind WÜTEND! Sie greifen ihn von oben an bis zum letzten Krümel.
Les six fèves sont à toi ! | All six charms are yours! | ¡Las seis habas son tuyas! | Alle sechs Bohnen gehören dir!
La colonie est royale. Tu peux continuer à jouer. | The colony is royal. You can keep playing. | La colonia es real. Puedes seguir jugando. | Die Kolonie ist königlich. Du kannst weiterspielen.
Ça s'effondre ! | It's collapsing! | ¡Se derrumba! | Es stürzt ein!
Mange par le dessus, ou garde des piliers | Eat from the top, or leave pillars | Come por arriba o deja pilares | Iss von oben oder lass Säulen stehen
Écrasée par un éboulis ! -{0} miettes | Crushed by a cave-in! -{0} crumbs | ¡Aplastada por un derrumbe! -{0} migas | Von einem Einsturz erwischt! -{0} Krümel
Une ouvrière a trouvé la fève ! | A worker found the charm! | ¡Una obrera ha encontrado el haba! | Eine Arbeiterin hat die Bohne gefunden!
Gâteau {0} : suis le halo doré et déterre-la | Cake {0}: follow the golden glow and dig it up | Pastel {0}: sigue el halo dorado y desentiérrala | Kuchen {0}: Folge dem goldenen Schein und grab sie aus
{0} au stock | {0} stored | {0} al almacén | {0} eingelagert
Achat : {0} | Bought: {0} | Compra: {0} | Gekauft: {0}
Gâteau {0} entièrement mangé : ses ouvrières partent sur le gâteau {1} | Cake {0} fully eaten: its workers move on to cake {1} | Pastel {0} comido del todo: sus obreras se van al pastel {1} | Kuchen {0} ganz aufgegessen: Seine Arbeiterinnen ziehen zu Kuchen {1}

# ── Radiographie ──
Radiographie coupée | X-ray off | Rayos X desactivados | Röntgen aus
Radiographie : galeries | X-ray: tunnels | Rayos X: galerías | Röntgen: Gänge
Radiographie : fourmis | X-ray: ants | Rayos X: hormigas | Röntgen: Ameisen
Radiographie : galeries et fourmis | X-ray: tunnels and ants | Rayos X: galerías y hormigas | Röntgen: Gänge und Ameisen

# ── Décor ──
Fourmilière | Anthill | Hormiguero | Ameisenhaufen
×{0} par miette | ×{0} per crumb | ×{0} por miga | ×{0} pro Krümel
Rapporte la fève n°{0} | Bring back charm #{0} | Trae el haba n.º {0} | Bring Bohne Nr. {0} zurück
Fève trouvée · tout croqué ! | Charm found · all eaten! | Haba encontrada · ¡comido entero! | Bohne gefunden · alles aufgegessen!
Fève trouvée ! | Charm found! | ¡Haba encontrada! | Bohne gefunden!
Fève à trouver | Charm to find | Haba por encontrar | Bohne versteckt
""";
}
