// Brins d'herbe instanciés : dégradé base → pointe, translucidité, vent.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;

uniform vec3 base_col : source_color = vec3(0.16, 0.40, 0.10);
uniform vec3 mid_col : source_color = vec3(0.36, 0.66, 0.17);
uniform vec3 tip_col : source_color = vec3(0.72, 0.88, 0.36);
uniform float wind_strength = 1.0;
uniform vec3 player_pos = vec3(0.0, -100.0, 0.0);
// cases occupées (bâtiments) dans la clairière : l'herbe n'y pousse pas
uniform sampler2D occupancy : filter_linear;
uniform vec4 occ_rect = vec4(-64.0, 36.0, 128.0, 64.0); // x0, z0, largeur, profondeur

varying float v_h;
varying vec3 v_tint;

void vertex() {
	v_h = UV.y;
	v_tint = COLOR.rgb;
	vec3 wpos = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec2 ouv = (wpos.xz - occ_rect.xy) / occ_rect.zw;
	if (ouv.x > 0.0 && ouv.y > 0.0 && ouv.x < 1.0 && ouv.y < 1.0) {
		// près d'un bâtiment, les brins raccourcissent (texture filtrée) ; dessous, ils disparaissent
		float occ = max(textureLod(occupancy, ouv, 0.0).r, textureLod(occupancy, ouv + vec2(0.6 / 128.0, 0.0), 0.0).r);
		occ = max(occ, max(textureLod(occupancy, ouv - vec2(0.6 / 128.0, 0.0), 0.0).r, max(textureLod(occupancy, ouv + vec2(0.0, 0.6 / 64.0), 0.0).r, textureLod(occupancy, ouv - vec2(0.0, 0.6 / 64.0), 0.0).r)));
		VERTEX *= 1.0 - smoothstep(0.05, 0.5, occ);
	}
	float height = length(MODEL_MATRIX[1].xyz);
	float t = TIME;
	float gust = sin(wpos.x * 0.05 + t * 0.6) * 0.5 + sin(wpos.z * 0.043 - t * 0.45) * 0.5;
	float sway = (sin(t * 1.3 + wpos.x * 0.35 + wpos.z * 0.27) * 0.5 + gust * 0.3 + 0.25) * wind_strength;
	float bend = UV.y * UV.y;
	// le vent souffle dans le repère monde : on le ramène dans le repère du brin
	vec3 wdir = normalize(vec3(1.0, 0.0, 0.35)) * sway * 0.045;
	// les brins s'écartent autour de la fourmi
	vec3 away = wpos - player_pos; away.y = 0.0;
	float d = length(away);
	vec3 push = d < 3.0 && d > 0.001 ? normalize(away) * (1.0 - d / 3.0) * 0.6 : vec3(0.0);
	vec3 local_off = (inverse(mat3(MODEL_MATRIX)) * ((wdir + push) * height)) * bend;
	VERTEX += local_off;
	VERTEX.y -= length(local_off) * 0.3 * UV.y;
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.55));
}

void fragment() {
	// couleur unie par brin (pas de dégradé le long du brin) ; juste un léger voile d'ombre au pied
	vec3 col = mid_col * v_tint;
	float rib = 1.0 - smoothstep(0.0, 0.12, abs(UV.x - 0.5));
	col *= 1.0 + rib * 0.06;
	ALBEDO = col;
	ROUGHNESS = 156.0 / 255.0; // marqueur lu par le contour (herbe = trait léger)
	SPECULAR = 0.3;
	AO = mix(0.8, 1.0, smoothstep(0.0, 0.25, v_h));
	AO_LIGHT_AFFECT = 0.1;
	BACKLIGHT = col * vec3(0.3, 0.42, 0.18);
}
