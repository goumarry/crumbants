// Cercle de portée au sol (grue, électricité, écart minimal).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 color : source_color = vec3(1.0, 0.8, 0.2);
uniform float radius = 5.0;

void fragment() {
	float r = length(UV - 0.5) * 2.0; // 0 au centre, 1 au bord
	float w = 0.12 / max(radius, 1.0);
	float ring = smoothstep(1.0 - w * 2.0, 1.0 - w, r) * (1.0 - smoothstep(1.0 - w * 0.2, 1.0, r));
	float fill = (1.0 - step(1.0, r)) * 0.08;
	float dash = step(0.5, fract(atan(UV.y - 0.5, UV.x - 0.5) / 6.2832 * radius * 3.0 + TIME * 0.2));
	ALBEDO = color;
	ALPHA = ring * (0.55 + 0.45 * dash) + fill;
}
