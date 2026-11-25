// Rayon de lumière doré au-dessus du nid ou de la fève (additif, s'efface en hauteur).
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 color : source_color = vec3(1.0, 0.8, 0.3);
uniform float intensity = 0.6;
uniform float height = 30.0;
varying float v_h;

void vertex() { v_h = clamp(VERTEX.y / height + 0.5, 0.0, 1.0); }

void fragment() {
	float side = pow(clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 1.5);
	float fade = (1.0 - v_h) * (1.0 - v_h) * smoothstep(0.0, 0.04, v_h);
	float fl = 0.85 + 0.15 * sin(TIME * 2.0 - v_h * 14.0);
	float near = smoothstep(10.0, 28.0, distance(CAMERA_POSITION_WORLD, NODE_POSITION_WORLD)); // s'efface tout près
	ALBEDO = color * intensity * side * fade * fl * near;
}
