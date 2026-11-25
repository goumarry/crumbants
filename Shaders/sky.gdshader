// Ciel d'après-midi stylisé : dégradé zénith → horizon, halo chaud autour
// du soleil, gros nuages cotonneux (fbm seuillé, ombrés côté opposé au
// soleil, liseré lumineux), brume claire sur l'horizon.
shader_type sky;

uniform vec3 zenith : source_color = vec3(0.20, 0.47, 0.86);
uniform vec3 mid : source_color = vec3(0.45, 0.70, 0.95);
uniform vec3 horizon : source_color = vec3(0.86, 0.93, 0.97);
uniform vec3 ground : source_color = vec3(0.42, 0.56, 0.30);
uniform vec3 sun_tint : source_color = vec3(1.0, 0.93, 0.78);
uniform vec3 cloud_lit : source_color = vec3(1.0, 0.99, 0.96);
uniform vec3 cloud_shade : source_color = vec3(0.70, 0.78, 0.92);
uniform float cloud_cover = 0.47;
uniform float energy = 1.0;

float h21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1, 0)), f.x), mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) {
	float a = 0.5, s = 0.0;
	for (int i = 0; i < 5; i++) { s += a * n2(p); p = p * 2.03 + vec2(17.1, 9.3); a *= 0.5; }
	return s;
}
// densité de nuage : grosses masses arrondies (bosses « choux-fleur »)
float cloud(vec2 p) {
	float base = fbm(p * 0.55);
	float puffs = fbm(p * 1.6 + 4.0);
	return base * 0.75 + puffs * 0.35;
}

void sky() {
	vec3 d = EYEDIR;
	float y = d.y;
	// dégradé
	float t = clamp(y, 0.0, 1.0);
	vec3 col = mix(horizon, mid, smoothstep(0.0, 0.25, t));
	col = mix(col, zenith, smoothstep(0.2, 0.85, t));
	// sous l'horizon : on prolonge en douceur vers un vert prairie brumeux
	col = mix(col, mix(horizon, ground, 0.55), smoothstep(0.0, -0.12, y));

	vec3 sdir = LIGHT0_DIRECTION;
	float sd = max(dot(d, sdir), 0.0);
	// halo chaud + disque
	col += sun_tint * (pow(sd, 6.0) * 0.18 + pow(sd, 60.0) * 0.35);
	if (!AT_CUBEMAP_PASS) {
		float disc = smoothstep(0.9993, 0.99965, sd);
		col = mix(col, vec3(1.0, 0.98, 0.9) * 6.0, disc);
	}

	// nuages projetés sur une voûte
	if (y > 0.0) {
		// (nuages fixes : utiliser TIME ferait recalculer l'éclairage du ciel à CHAQUE image)
		vec2 uv = d.xz / (y + 0.12) * 1.6;
		float c = cloud(uv);
		float k = smoothstep(cloud_cover, cloud_cover + 0.1, c);
		// ombre : on regarde la densité un peu vers le soleil
		vec2 toward = normalize(sdir.xz + 1e-4) * 0.18;
		float c2 = cloud(uv + toward);
		float shade = smoothstep(cloud_cover - 0.02, cloud_cover + 0.28, c2);
		vec3 cc = mix(cloud_lit, cloud_shade, shade * 0.85);
		// liseré argenté sur les bords, plus fort près du soleil
		float rim = (1.0 - smoothstep(cloud_cover + 0.02, cloud_cover + 0.12, c)) * k;
		cc += sun_tint * rim * (0.25 + 0.9 * pow(sd, 4.0));
		// s'efface vers l'horizon (perspective aérienne)
		float fade = smoothstep(0.02, 0.22, y);
		col = mix(col, cc, k * fade);
	}
	COLOR = col * energy;
}
