// Bande de tapis roulant : caoutchouc sombre et UNE flèche pleine par case (opacité 50 %), pointée
// dans le sens du transport et qui avance avec lui (elle suit la courbe dans les virages), plus de
// fines rainures discrètes : plus d'effet d'optique comme avec les chevrons.
// UV.x : en travers (0..1) ; UV.y : le long du trajet, en cases, qui DÉCROÎT dans le sens du
// transport (modèles Blender : 1 à l'entrée, 1 − len à la sortie).
shader_type spatial;
render_mode cull_disabled;

uniform float speed = 2.0;
uniform float len = 1.0;          // longueur de la bande en UV.y (1 droit, π/4 virage)
uniform vec3 arrow_col : source_color = vec3(1.0, 0.82, 0.25);
uniform bool plain = false;       // raccord de jonction en T : caoutchouc seul, sans flèche
uniform bool frozen = false;      // tapis intégré à une machine non alimentée : à l'arrêt

// masque net (anticrénelé) d'un champ signé : < 0 dedans
float fill(float d) { float w = fwidth(d) * 0.75; return 1.0 - smoothstep(-w, w, d); }

void fragment() {
	bool powered = COLOR.r > 0.6 && !frozen;
	float t = (1.0 - UV.y) / len;      // 0 entrée → 1 sortie
	float u = UV.x - 0.5;              // −0,5 … 0,5 en travers
	float au = abs(u);
	// caoutchouc + rainures fines qui défilent (vitesse réelle du tapis, en cases/s)
	float s = UV.y * 8.0 + (powered ? TIME * speed * 8.0 : 0.0); // défile vers la sortie
	float groove = smoothstep(0.0, 0.08, fract(s)) * (1.0 - smoothstep(0.12, 0.2, fract(s)));
	vec3 col = vec3(0.17, 0.16, 0.16) * (1.0 - 0.18 * groove);
	// la flèche : une tige puis une pointe triangulaire, vers la sortie ; elle avance avec le tapis,
	// une case par 1/speed s (comme les objets), et passe d'une case à l'autre sans coupure
	float tt = fract(t - (powered ? TIME * speed : 0.0));
	float shaft = max(au - 0.075, max(0.2 - tt, tt - 0.56));
	float head = max(tt - 0.84, max(0.5 - tt, au - 0.25 * (0.84 - tt) / 0.34));
	float d = min(shaft, head);
	float edge = plain ? 0.0 : fill(d - 0.03); // liseré sombre autour (lisible sur le caoutchouc)
	float body = plain ? 0.0 : fill(d);
	vec3 ac = powered ? arrow_col : vec3(0.55, 0.53, 0.5);
	// à moitié transparente (50 %) : on voit le caoutchouc au travers
	col = mix(col, vec3(0.08, 0.06, 0.05), edge * 0.5);
	col = mix(col, ac, body * 0.5);
	col *= COLOR.rgb;
	// bords légèrement relevés
	col *= 1.0 - smoothstep(0.44, 0.5, au) * 0.4;
	ALBEDO = col;
	ROUGHNESS = mix(0.8, 0.45, body * 0.5);
	SPECULAR = 0.3;
	EMISSION = powered ? ac * body * 0.06 : vec3(0.0);
}
