// Sol de prairie : herbe rase vue de près (petits traits), trèfles,
// taches de terre et de lumière ; grandes nuances au loin.
shader_type spatial;
render_mode diffuse_burley;

uniform vec3 green1 : source_color = vec3(0.24, 0.50, 0.12);
uniform vec3 green2 : source_color = vec3(0.46, 0.70, 0.20);
uniform vec3 soil : source_color = vec3(0.45, 0.36, 0.20);
varying vec3 v_wpos;

float h21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1, 0)), f.x), mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) { return 0.5 * n2(p) + 0.3 * n2(p * 2.1 + 7.0) + 0.2 * n2(p * 4.3 + 13.0); }

void vertex() { v_wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment() {
	vec2 p = v_wpos.xz;
	float a = fbm(p * 0.04), b = fbm(p * 0.5);
	vec3 col = mix(green1, green2, smoothstep(0.25, 0.75, a));
	col *= 0.88 + 0.22 * b;
	// petits brins couchés (seulement de près)
	float near = 1.0 - smoothstep(0.08, 0.4, length(fwidth(p)));
	vec2 q = p * 3.0;
	vec2 id = floor(q);
	vec2 f = fract(q) - 0.5;
	float ang = h21(id) * 6.28;
	vec2 dir = vec2(cos(ang), sin(ang));
	float along = dot(f, dir), across = dot(f, vec2(-dir.y, dir.x));
	float stroke = (1.0 - smoothstep(0.03, 0.07, abs(across))) * (1.0 - smoothstep(0.25, 0.42, abs(along)));
	col = mix(col, col * (0.75 + 0.6 * h21(id + 3.0)), stroke * near);
	// trèfles (trois petites feuilles rondes)
	vec2 cq = p * 0.9; vec2 cid = floor(cq); vec2 cf = fract(cq) - 0.5;
	float has = step(0.86, h21(cid + 11.0));
	float clover = 0.0;
	for (int k = 0; k < 3; k++) {
		float an = float(k) * 2.094 + h21(cid) * 6.28;
		clover = max(clover, 1.0 - smoothstep(0.1, 0.13, length(cf - vec2(cos(an), sin(an)) * 0.12)));
	}
	col = mix(col, green1 * vec3(0.8, 1.15, 0.8), clover * has * 0.85);
	// taches de terre
	float dirt = smoothstep(0.68, 0.82, fbm(p * 0.15 + 3.0));
	col = mix(col, soil * (0.8 + 0.4 * b), dirt * 0.45);
	ALBEDO = col;
	ROUGHNESS = 0.92;
	SPECULAR = 0.15;
	BACKLIGHT = col * 0.15;
}
