// Verre : on redessine l'arrière-plan (copie d'écran) légèrement réfracté et
// teinté dans l'épaisseur, plus des reflets de Fresnel et un spéculaire net.
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_opaque, specular_schlick_ggx;

uniform vec3 tint : source_color = vec3(0.85, 0.95, 1.0);
uniform float alpha_min = 0.06; // opacité de la teinte (verre épais/coloré = plus haut)
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;

void fragment() {
	float ndv = clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0);
	float fres = pow(1.0 - ndv, 4.0);
	// réfraction atténuée au loin (sinon l'arrière-plan décalé « saute » sur les bords fins)
	float dist = length(VERTEX);
	float nearK = 1.0 - smoothstep(40.0, 140.0, dist);
	vec2 off = NORMAL.xy * (0.012 + 0.03 * (1.0 - ndv)) * nearK;
	vec3 behind = textureLod(screen_tex, SCREEN_UV - off, 0.0).rgb;
	vec3 thick = mix(vec3(1.0), tint, clamp(alpha_min + 0.6 * (1.0 - ndv), 0.0, 1.0));
	ALBEDO = vec3(0.0);
	// reflet du ciel seulement en bord (Fresnel) : pas de voile laiteux au centre
	vec3 sky = mix(vec3(0.75, 0.85, 0.95), vec3(1.0), 0.3);
	fres *= mix(0.45, 1.0, nearK); // liseré de Fresnel plus discret au loin (moins de scintillement)
	EMISSION = behind * thick * (1.0 - fres * 0.5) + sky * fres * 0.6;
	ROUGHNESS = 0.03;
	SPECULAR = 0.12;
	ALPHA = 1.0;
}
