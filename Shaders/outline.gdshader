// Post-traitement plein écran : contour « encre » (sauts de profondeur et
// plis de normales), léger renforcement de netteté, vignette douce.
// Les brins d'herbe (rugosité marquée 156/255) ne prennent qu'un contour léger.
shader_type spatial;
render_mode unshaded, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D normal_tex : hint_normal_roughness_texture, filter_nearest;
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform vec3 ink : source_color = vec3(0.20, 0.11, 0.06);
uniform float thickness = 1.0;
uniform float strength = 0.85;
uniform float far_fade = 110.0;
uniform float sharpen = 0.35;
uniform float vignette = 0.22;

void vertex() {
	POSITION = vec4(VERTEX.xy * 2.0, 1.0, 1.0);
}

float lin_depth(vec2 uv, mat4 inv_proj) {
	float d = textureLod(depth_tex, uv, 0.0).r;
	vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
	return -v.z / v.w;
}

bool is_grass(vec4 nr) { return abs(nr.a - 156.0 / 255.0) < 3.5 / 255.0; }

void fragment() {
	vec2 texel = 1.0 / VIEWPORT_SIZE;
	vec2 px = thickness * texel;
	vec2 uv = SCREEN_UV;
	float d0 = lin_depth(uv, INV_PROJECTION_MATRIX);
	vec4 nr0 = textureLod(normal_tex, uv, 0.0);
	vec3 n0 = nr0.xyz * 2.0 - 1.0;
	bool g0 = is_grass(nr0);
	float edge = 0.0;
	vec2 offs[4] = vec2[](vec2(px.x, 0.0), vec2(-px.x, 0.0), vec2(0.0, px.y), vec2(0.0, -px.y));
	for (int i = 0; i < 4; i++) {
		float d = lin_depth(uv + offs[i], INV_PROJECTION_MATRIX);
		vec4 nr = textureLod(normal_tex, uv + offs[i], 0.0);
		vec3 n = nr.xyz * 2.0 - 1.0;
		// seul le pixel le plus proche se cerne : trait net d'un pixel, sans halo
		// herbe autour (ou pixels de bord moyennés par le MSAA) : pas de trait
		if (d > d0 && !g0 && !is_grass(nr)) {
			float dd = (d - d0) / max(d0, 0.05);
			float e = smoothstep(0.02, 0.06, dd);
			edge = max(edge, e);
		}
		if (!g0 && !is_grass(nr)) edge = max(edge, smoothstep(0.5, 0.85, 1.0 - dot(n, n0)) * 0.4);
	}
	float fade = 1.0 - smoothstep(far_fade * 0.45, far_fade, d0);

	// netteté adaptative (façon CAS) sur l'image HDR
	vec3 c = textureLod(screen_tex, uv, 0.0).rgb;
	vec3 cn = textureLod(screen_tex, uv + vec2(0.0, texel.y), 0.0).rgb;
	vec3 cs = textureLod(screen_tex, uv - vec2(0.0, texel.y), 0.0).rgb;
	vec3 ce = textureLod(screen_tex, uv + vec2(texel.x, 0.0), 0.0).rgb;
	vec3 cw = textureLod(screen_tex, uv - vec2(texel.x, 0.0), 0.0).rgb;
	vec3 mn = min(c, min(min(cn, cs), min(ce, cw)));
	vec3 mx = max(c, max(max(cn, cs), max(ce, cw)));
	vec3 amp = clamp(min(mn, 2.0 - mx) / max(mx, 1e-3), 0.0, 1.0);
	vec3 w = -sqrt(amp) * sharpen * 0.2;
	vec3 col = (c + (cn + cs + ce + cw) * w) / (1.0 + 4.0 * w);
	col = max(col, vec3(0.0));

	col = mix(col, ink * (0.25 + 0.75 * clamp(dot(c, vec3(0.33)), 0.0, 1.0)), edge * strength * fade);
	// vignette
	vec2 q = uv - 0.5;
	col *= 1.0 - vignette * smoothstep(0.25, 0.85, dot(q, q) * 2.2);
	ALBEDO = col;
}
