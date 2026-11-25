// Sol des galeries (fond du gâteau, sur l'assiette) : pâte tassée par les
// passages, miettes et grains de sucre éparpillés, traces de pattes.
shader_type spatial;
render_mode diffuse_burley;

uniform vec3 base : source_color = vec3(0.55, 0.36, 0.18);
uniform vec3 crumb : source_color = vec3(0.86, 0.66, 0.36);
varying vec3 v_w;

float h21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float n2(vec2 p) { vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1, 0)), f.x), mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), f.x), f.y); }

void vertex() { v_w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment() {
	vec2 p = v_w.xz;
	float px = length(fwidth(p));
	float big = n2(p * 0.7) * 0.6 + n2(p * 2.3) * 0.4;
	vec3 col = base * (0.8 + 0.35 * big);
	float h = big * 0.4;
	// miettes : petites pastilles claires en relief (estompées au loin)
	vec2 g = p * 6.0; vec2 gi = floor(g);
	float r = h21(gi);
	vec2 c = vec2(h21(gi + 3.1), h21(gi + 7.7)) * 0.6 + 0.2;
	vec2 q = fract(g) - c;
	float ang = atan(q.y, q.x);
	// miettes irrégulières : contour bosselé, tailles très variées, peu nombreuses
	float sz = (0.04 + 0.13 * pow(h21(gi + 1.3), 2.0)) * (0.75 + 0.25 * sin(ang * 3.0 + r * 20.0) + 0.12 * sin(ang * 7.0 + r * 9.0));
	float d = length(q);
	float m = (1.0 - smoothstep(sz * 0.5, sz, d)) * step(0.68, r) * (1.0 - smoothstep(0.03, 0.12, px));
	vec3 cc = mix(base * 1.25, crumb, h21(gi + 9.0)) * (0.8 + 0.25 * n2(q * 40.0));
	col = mix(col, cc, m);
	col *= 1.0 - 0.25 * (1.0 - smoothstep(sz, sz * 1.6, d)) * step(0.68, r) * (1.0 - m); // ombre au pied de la miette
	h += m * 0.6 * (1.0 - d / max(sz, 1e-3));
	// grains de sucre qui brillent
	vec2 s2 = p * 18.0; vec2 si = floor(s2);
	float sug = step(0.96, h21(si)) * (1.0 - smoothstep(0.05, 0.14, length(fract(s2) - 0.5))) * (1.0 - smoothstep(0.02, 0.06, px));
	col = mix(col, vec3(1.0, 0.98, 0.92), sug);
	ALBEDO = col;
	ROUGHNESS = mix(0.85, 0.2, sug);
	SPECULAR = 0.5 + sug * 0.5;
	EMISSION = vec3(1.0, 0.95, 0.85) * sug * 0.25;
	// relief par dérivées écran
	float dhx = dFdx(h * 0.06), dhy = dFdy(h * 0.06);
	vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
	vec3 r1 = cross(dpdy, NORMAL), r2 = cross(NORMAL, dpdx);
	float det = dot(dpdx, r1);
	NORMAL = normalize(abs(det) * NORMAL - sign(det) * (dhx * r1 + dhy * r2));
}
