// Couverture de pique-nique en vichy : bandes filtrées (nettes de près,
// sans moiré au loin), tissage sergé, fils flammés, plis doux (normales
// seulement : le sol reste plat pour la marche), duvet de coton.
shader_type spatial;
render_mode diffuse_burley;

uniform vec3 red : source_color = vec3(0.82, 0.10, 0.13);
uniform vec3 cream : source_color = vec3(0.99, 0.95, 0.88);
uniform float square = 9.0;

varying vec3 v_wpos;

float h21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1, 0)), f.x), mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) { return 0.5 * n2(p) + 0.3 * n2(p * 2.1 + 7.0) + 0.2 * n2(p * 4.3 + 13.0); }

// créneau 50 % filtré sur la largeur du pixel (intégrale analytique)
float stripe(float x, float w) {
	w = max(w, 1e-4);
	float a = x - 0.5 * w, b = x + 0.5 * w;
	float ia = floor(a) * 0.5 + max(fract(a) - 0.5, 0.0);
	float ib = floor(b) * 0.5 + max(fract(b) - 0.5, 0.0);
	return (ib - ia) / w;
}

void vertex() { v_wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment() {
	vec2 wp = v_wpos.xz;
	// légères ondulations du tissu (les bandes ne sont pas parfaitement droites)
	vec2 p = wp / square;
	vec2 fw = fwidth(p);
	float bx = stripe(p.x, fw.x), bz = stripe(p.y, fw.y);
	float k = bx + bz;
	vec3 pink = mix(cream, red, 0.5);
	vec3 col = mix(cream, pink, clamp(k, 0.0, 1.0));
	col = mix(col, red, clamp(k - 1.0, 0.0, 1.0));

	// tissage sergé (diagonales fines) et fils flammés
	vec2 t = wp * 6.0;
	float far = smoothstep(0.1, 0.35, max(fwidth(t.x), fwidth(t.y)));
	float twill = sin((t.x + t.y) * 3.14159) * 0.5 + 0.5;
	float slub = n2(vec2(t.x * 0.15, floor(t.y))) * 0.5 + n2(vec2(floor(t.x), t.y * 0.15)) * 0.5;
	float weave = mix(twill * 0.6 + slub * 0.4, 0.5, far);
	col *= 0.95 + 0.06 * weave;
	// usure, taches de soleil, duvet
	col *= 0.95 + 0.07 * fbm(wp * 0.25);
	ALBEDO = col;
	ROUGHNESS = 0.9;
	SPECULAR = 0.25;
	SSS_STRENGTH = 0.0;
	// relief du tissage seulement (estompé au loin) ; pas de plis
	vec3 nrm = normalize(vec3(-(twill - 0.5) * 0.25 * (1.0 - far), 1.0, -(twill - 0.5) * 0.25 * (1.0 - far)));
	NORMAL = normalize((VIEW_MATRIX * vec4(nrm, 0.0)).xyz);
	// léger velours rasant
	RIM = 0.25;
	RIM_TINT = 0.6;
}
