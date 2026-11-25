// Radiographie : silhouette rouge des ouvrières, vue à travers les murs du gâteau.
// Surcouche (material_overlay) sur le rendu instancié des fourmis : contour lumineux
// (bords rasants) et intérieur à peine teinté. Aucune géométrie en plus.
shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

uniform vec3 col : source_color = vec3(1.0, 0.12, 0.08);

void fragment() {
	float f = 1.0 - abs(dot(NORMAL, VIEW));
	float rim = smoothstep(0.3, 0.75, f);
	ALBEDO = col * (0.9 + 0.8 * rim);
	ALPHA = 0.45 + 0.55 * rim;
}
