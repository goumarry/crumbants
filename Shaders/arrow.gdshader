// Flèches d'entrée / de sortie des machines : couleur vive, légère lueur,
// et un petit va-et-vient dans le sens de la flèche pour qu'on la repère d'un coup d'œil.
shader_type spatial;
render_mode cull_disabled, shadows_disabled;

uniform vec3 color : source_color = vec3(0.2, 0.9, 0.3);
uniform float glow = 0.2;

void vertex() {
	// la flèche pointe vers −Z (repère du modèle) : elle avance et recule doucement
	VERTEX.z -= (sin(TIME * 4.0) * 0.5 + 0.5) * 0.08;
}

void fragment() {
	// bord plus clair sur le dessus, flanc plus sombre : lisible même de loin
	float top = step(0.5, NORMAL.y * 0.5 + 0.5);
	ALBEDO = mix(color * 0.6, color, top);
	EMISSION = color * glow;
	ROUGHNESS = 0.5;
}
