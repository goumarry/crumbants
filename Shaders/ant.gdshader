// Carapace de fourmi : chitine laquée (vernis), couleur par instance,
// ventre plus sombre, liseré de lumière, micro-relief et poils fins.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform float darken = 1.0;  // 0.55 pour les pattes et antennes
uniform float rough = 0.32;

varying vec3 v_wn;
varying vec3 v_obj;
varying vec3 v_col;

float h31(vec3 p) { p = fract(p * vec3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return fract((p.x + p.y) * p.z); }
float vn(vec3 p) {
	vec3 i = floor(p); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(h31(i), h31(i + vec3(1,0,0)), f.x), mix(h31(i + vec3(0,1,0)), h31(i + vec3(1,1,0)), f.x), f.y),
			   mix(mix(h31(i + vec3(0,0,1)), h31(i + vec3(1,0,1)), f.x), mix(h31(i + vec3(0,1,1)), h31(i + vec3(1,1,1)), f.x), f.y), f.z);
}

void vertex() {
	v_wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_obj = VERTEX;
	v_col = COLOR.rgb;
}

void fragment() {
	vec3 base = v_col * darken * 0.72;
	float up = v_wn.y * 0.5 + 0.5;
	vec3 col = base * mix(0.4, 1.15, smoothstep(0.15, 0.95, up));   // dos clair, ventre foncé
	float speck = vn(v_obj * 60.0);
	col *= 0.93 + 0.1 * speck;
	ALBEDO = col;
	ROUGHNESS = rough + (speck - 0.5) * 0.1;
	SPECULAR = 0.6;
	CLEARCOAT = 0.9;
	CLEARCOAT_ROUGHNESS = 0.1;
	RIM = 0.45;
	RIM_TINT = 0.5;
	BACKLIGHT = base * 0.25;
}
