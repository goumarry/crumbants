// Grille des cases constructibles autour du curseur (texture : 1 = libre).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D cells : filter_nearest;
uniform float n = 25.0;

void fragment() {
	vec2 p = UV * n;
	vec2 f = fract(p);
	float line = 1.0 - smoothstep(0.0, 0.06, min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y)));
	float ok = texture(cells, (floor(p) + 0.5) / n).r;
	float fade = 1.0 - smoothstep(0.25, 0.5, length(UV - 0.5));
	ALBEDO = mix(vec3(1.0, 1.0, 1.0), vec3(0.6, 1.0, 0.7), 0.5);
	ALPHA = ok * fade * (line * 0.7 + 0.06);
}
