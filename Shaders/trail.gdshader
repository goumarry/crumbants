// Chemin de fourmis : bande à peine tassée, traces de pas en quinconce, quelques grains.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

float h21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }

void fragment() {
	float u = UV.x, v = UV.y;
	float edge = smoothstep(0.0, 0.25, u) * smoothstep(1.0, 0.75, u);
	// traces de pas : petites ellipses en quinconce
	float row = floor(v / 0.55);
	float side = mod(row, 2.0) < 1.0 ? 0.35 : 0.65;
	vec2 c = vec2(side, (row + 0.5) * 0.55);
	vec2 d = (vec2(u, v) - c) * vec2(9.0, 7.0);
	float step_ = 1.0 - smoothstep(0.6, 1.0, length(d));
	float grain = step(0.93, h21(floor(vec2(u * 30.0, v * 12.0))));
	ALBEDO = mix(vec3(0.82, 0.68, 0.48), vec3(0.55, 0.42, 0.28), step_ * 0.8);
	ALPHA = edge * (0.32 + step_ * 0.45 + grain * 0.3);
	ROUGHNESS = 1.0;
}
