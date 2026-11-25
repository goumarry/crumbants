// Surlignage du bloc visé : arêtes lumineuses + voile léger.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_test_disabled;

uniform vec3 color : source_color = vec3(1.0);
varying vec3 v_local;

void vertex() { v_local = VERTEX; }

void fragment() {
	vec3 a = abs(v_local) * 2.0;
	float n = step(0.9, a.x) + step(0.9, a.y) + step(0.9, a.z);
	float edge = step(1.5, n);
	float pulse = 0.75 + 0.25 * sin(TIME * 6.0);
	ALBEDO = color * (edge * 0.9 + 0.08) * pulse;
	ALPHA = 1.0;
}
