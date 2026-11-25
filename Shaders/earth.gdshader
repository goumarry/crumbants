// Terre de la fourmilière : grains et graviers dessinés dans l'espace monde.
shader_type spatial;
render_mode diffuse_burley;

uniform vec3 dirt : source_color = vec3(0.47, 0.32, 0.19);
uniform vec3 dirt_dark : source_color = vec3(0.25, 0.16, 0.09);
uniform vec3 pebble : source_color = vec3(0.55, 0.47, 0.38);
uniform vec3 hole_center = vec3(0.0, 2.4, 47.0);
uniform float hole_r = 0.9;
varying vec3 v_wpos;

float h31(vec3 p) { p = fract(p * vec3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return fract((p.x + p.y) * p.z); }
float vn(vec3 p) {
	vec3 i = floor(p); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(h31(i), h31(i + vec3(1,0,0)), f.x), mix(h31(i + vec3(0,1,0)), h31(i + vec3(1,1,0)), f.x), f.y),
			   mix(mix(h31(i + vec3(0,0,1)), h31(i + vec3(1,0,1)), f.x), mix(h31(i + vec3(0,1,1)), h31(i + vec3(1,1,1)), f.x), f.y), f.z);
}

void vertex() { v_wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment() {
	vec3 w = v_wpos;
	float g = vn(w * 9.0), c = vn(w * 2.5);
	vec3 col = mix(dirt_dark, dirt, 0.45 + 0.55 * c) * (0.8 + 0.35 * g);
	float peb = smoothstep(0.84, 0.9, vn(w * 7.0 + 11.0));
	col = mix(col, pebble * (0.8 + 0.3 * g), peb);
	// l'entrée : un entonnoir qui s'enfonce dans le noir
	float hr = length(w.xz - hole_center.xz);
	float hole = 1.0 - smoothstep(hole_r * 0.55, hole_r * 1.5, hr);
	col *= mix(1.0, 0.04, hole);
	ALBEDO = col;
	ROUGHNESS = mix(0.95, 0.6, peb);
	AO = 1.0 - hole * 0.9;
}
