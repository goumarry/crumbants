// Objets transportés (miette, boulette, petit gâteau, gâteau royal).
// Les parties « teintables » (alpha de la couleur de sommet = 1) prennent la couleur des miettes
// du gâteau d'origine, passée par instance (INSTANCE_CUSTOM) ; les autres (crème, cerise,
// caissette) gardent leur couleur. La couleur de sommet module la teinte (cuisson, ombre).
shader_type spatial;
render_mode diffuse_burley;

uniform vec4 fallback_tint : source_color = vec4(0.88, 0.6, 0.2, 1.0);
uniform bool use_instance = true;
varying vec3 v_tint;

void vertex() {
	v_tint = use_instance ? INSTANCE_CUSTOM.rgb : fallback_tint.rgb;
}

void fragment() {
	vec3 base = COLOR.rgb;
	ALBEDO = mix(base, base * v_tint, COLOR.a);
	ROUGHNESS = mix(0.45, 0.8, COLOR.a);
	SPECULAR = 0.4;
	RIM = 0.2;
}
