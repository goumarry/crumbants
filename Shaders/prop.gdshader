// Matériau « objet » procédural pour les modèles Blender (décor, bâtiments) :
// une famille de surfaces choisie par « kind », dessinée en triplanaire dans
// l'espace de l'objet (le motif suit l'objet, à l'échelle de la fourmi).
//   0 lisse · 1 bois · 2 osier · 3 faïence · 4 tissu · 5 métal · 6 liège
//   7 croûte de pain · 8 peau de fruit · 9 terre/pierre · 10 papier/carton
//   11 plastique peint · 12 feuille · 13 sucre
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform int kind = 0;
uniform vec3 albedo : source_color = vec3(0.8);
uniform float roughness = 0.5;
uniform float metallic = 0.0;
uniform float scale = 1.0;      // taille du motif (unités monde par motif)
uniform float bump = 1.0;
uniform vec3 emission : source_color = vec3(0.0);
uniform float emission_energy = 0.0;

varying vec3 v_obj;
varying vec3 v_on;

float h31(vec3 p) { p = fract(p * vec3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return fract((p.x + p.y) * p.z); }
float vn(vec3 p) {
	vec3 i = floor(p); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(h31(i), h31(i + vec3(1,0,0)), f.x), mix(h31(i + vec3(0,1,0)), h31(i + vec3(1,1,0)), f.x), f.y),
			   mix(mix(h31(i + vec3(0,0,1)), h31(i + vec3(1,0,1)), f.x), mix(h31(i + vec3(0,1,1)), h31(i + vec3(1,1,1)), f.x), f.y), f.z);
}
float fbm(vec3 p) { return 0.5 * vn(p) + 0.28 * vn(p * 2.03 + 7.1) + 0.14 * vn(p * 4.1 + 3.3) + 0.08 * vn(p * 8.3 + 1.7); }
float cellv(vec3 p) {
	vec3 i = floor(p), f = fract(p); float best = 9.0;
	for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec3 o = vec3(float(x), float(y), float(z));
		vec3 c = o + 0.2 + 0.6 * vec3(h31(i + o), h31(i + o + 7.3), h31(i + o + 13.1));
		best = min(best, length(f - c));
	}
	return best;
}

void vertex() {
	v_obj = VERTEX;
	v_on = NORMAL;
}

// motif 2D projeté selon l'axe dominant de la normale (triplanaire « dur » + fondu)
vec2 proj(vec3 p, vec3 n, out float axis) {
	vec3 a = abs(n);
	if (a.x > a.y && a.x > a.z) { axis = 0.0; return p.zy; }
	if (a.y > a.z) { axis = 1.0; return p.xz; }
	axis = 2.0; return p.xy;
}

void fragment() {
	vec3 p = v_obj / scale;
	vec3 n = normalize(v_on);
	vec3 col = albedo;
	float rough = roughness;
	float metal = metallic;
	float h = 0.0;
	float spec = 0.5;
	float sss = 0.0;
	float big = fbm(p * 0.35);
	float ax;
	vec2 uv = proj(p, n, ax);

	if (kind == 1) {
		// bois : veines le long de x local, nœuds, pores
		float warp = fbm(p * vec3(0.15, 0.8, 0.8)) * 2.5;
		float ring = fract((length(p.yz * vec2(1.0, 1.6)) + warp) * 2.2);
		float grain = smoothstep(0.0, 0.6, ring) * (1.0 - smoothstep(0.75, 1.0, ring));
		float pores = vn(vec3(p.x * 0.6, p.y * 18.0, p.z * 18.0));
		col *= 0.78 + 0.3 * grain + 0.1 * (big - 0.5);
		col *= 0.94 + 0.08 * pores;
		h = grain * 0.4 + pores * 0.15;
		rough = clamp(roughness + (pores - 0.5) * 0.15, 0.05, 1.0);
	} else if (kind == 2) {
		// osier : tressage en panier (brins croisés, dessus/dessous alternés)
		vec2 q = uv * 1.6;
		vec2 c = floor(q); vec2 f = fract(q);
		bool horiz = mod(c.x + c.y, 2.0) < 1.0;
		float across = horiz ? f.y : f.x;
		float along = horiz ? f.x : f.y;
		float strand = sin(3.14159 * across);
		float sub = fract(across * 3.0);
		float fiber = sin(3.14159 * sub);
		h = strand * 0.8 + fiber * 0.2 + sin(along * 3.14159) * 0.15;
		col *= (0.62 + 0.42 * strand) * (0.9 + 0.12 * fiber) * (0.92 + 0.16 * vn(vec3(c * 3.1, 1.0)));
		col = mix(col, col * vec3(0.7, 0.55, 0.4), (1.0 - strand) * 0.5);
		rough = 0.7;
	} else if (kind == 3) {
		// faïence émaillée : brillante, petits défauts, craquelures très fines
		float crack = 1.0 - smoothstep(0.0, 0.03, abs(cellv(p * 2.5) - cellv(p * 2.5 + 0.37)));
		col *= 0.97 + 0.05 * big;
		col = mix(col, col * 0.85, crack * 0.25);
		// rares petits points ronds (pas de carrés visibles de près)
		vec3 cp = p * 20.0;
		float speck = step(0.994, h31(floor(cp))) * (1.0 - smoothstep(0.12, 0.28, length(fract(cp) - 0.5)));
		col = mix(col, col * 0.72, speck * 0.4);
		h = (big - 0.5) * 0.15;
		rough = roughness * (0.8 + 0.4 * big);
		spec = 0.6;
		sss = 0.05;
	} else if (kind == 4) {
		// tissu : toile fine + fils flammés, duvet doux
		vec2 q = uv * 7.0;
		float wx = sin(q.x * 3.14159) * 0.5 + 0.5, wy = sin(q.y * 3.14159) * 0.5 + 0.5;
		float over = step(0.5, fract((floor(q.x) + floor(q.y)) * 0.5));
		float weave = mix(wx, wy, over);
		float far = smoothstep(0.3, 0.8, length(fwidth(q)));
		weave = mix(weave, 0.5, far);
		col *= 0.86 + 0.18 * weave + 0.06 * (big - 0.5);
		h = weave * 0.5 * (1.0 - far);
		rough = 0.92;
		spec = 0.3;
		sss = 0.08;
	} else if (kind == 5) {
		// métal brossé : stries fines, légère usure
		float br = vn(vec3(p.x * 0.4, p.y * 40.0, p.z * 40.0));
		col *= 0.92 + 0.12 * br;
		rough = clamp(roughness + (big - 0.5) * 0.1, 0.22, 1.0);
		// acier « cartoon » : on garde une base claire (le ciel reflété est trop pâle pour du métal pur)
		col = mix(col, vec3(0.78, 0.8, 0.84), 0.3);
		metal = min(metal, 0.55);
		spec = 0.8;
		h = br * 0.08 * (1.0 - smoothstep(0.004, 0.02, length(fwidth(p)) ));
	} else if (kind == 6) {
		// liège : granulés et trous
		float c = cellv(p * 3.0);
		float hole = 1.0 - smoothstep(0.08, 0.18, c);
		col *= (0.82 + 0.3 * vn(p * 9.0)) * (1.0 - hole * 0.5);
		h = -hole * 0.6 + vn(p * 9.0) * 0.3;
		rough = 0.9;
	} else if (kind == 7) {
		// croûte de pain / pâtisserie : marbrures dorées, alvéoles
		float brown = fbm(p * 0.8);
		col = mix(col * 1.08, col * vec3(0.66, 0.46, 0.3), smoothstep(0.35, 0.75, brown));
		float pore = 1.0 - smoothstep(0.05, 0.14, cellv(p * 4.0));
		col *= 1.0 - pore * 0.25;
		h = brown * 0.4 - pore * 0.4;
		rough = clamp(roughness - brown * 0.2, 0.2, 1.0);
		sss = 0.12;
	} else if (kind == 8) {
		// peau de fruit : pores d'agrume / fossettes, reflets
		float d = cellv(p * 5.0);
		float dimple = smoothstep(0.0, 0.5, d);
		col *= 0.92 + 0.12 * big + 0.04 * dimple;
		h = dimple * 0.3;
		rough = roughness * (0.8 + 0.4 * (1.0 - dimple));
		spec = 0.6;
		sss = 0.3;
	} else if (kind == 9) {
		// terre, pierre : grains, cailloux, mouchetures
		float g = vn(p * 6.0), g2 = vn(p * 19.0);
		col *= 0.78 + 0.28 * g + 0.12 * (big - 0.5);
		col = mix(col, col * 1.25, step(0.86, g2) * 0.6);
		h = g * 0.5 + g2 * 0.3;
		rough = 0.92;
	} else if (kind == 10) {
		// papier, carton : fibres
		float fib = vn(vec3(p.x * 30.0, p.y * 3.0, p.z * 30.0)) * 0.5 + vn(p * 12.0) * 0.5;
		col *= 0.94 + 0.08 * fib;
		h = fib * 0.15;
		rough = 0.85;
		sss = 0.1;
	} else if (kind == 11) {
		// plastique ou peinture laquée
		col *= 0.97 + 0.05 * big;
		h = (big - 0.5) * 0.05;
		spec = 0.55;
	} else if (kind == 12) {
		// feuille : nervures, translucide
		float vein = 1.0 - smoothstep(0.0, 0.05, abs(fract(uv.x * 2.0 + abs(uv.y) * 1.2) - 0.5));
		col *= 0.88 + 0.2 * big;
		col = mix(col, col * 1.35, vein * 0.6);
		h = vein * 0.3;
		sss = 0.5;
	} else if (kind == 13) {
		// sucre : cristaux scintillants
		float c = cellv(p * 6.0);
		float facet = h31(floor(p * 6.0 + 0.5));
		col *= 0.92 + 0.1 * facet;
		h = (1.0 - c) * 0.6;
		rough = 0.25 + 0.3 * facet;
		spec = 0.8;
		sss = 0.3;
	} else {
		col *= 0.96 + 0.06 * big;
		h = (big - 0.5) * 0.1;
	}

	// relief par dérivées écran
	// relief estompé quand le motif devient plus fin que le pixel (sinon normales bruitées : scintillement, métal noir)
	float pw = length(fwidth(p));
	float hh = h * bump * 0.03 * scale * (1.0 - smoothstep(0.02, 0.12, pw));
	vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
	float dhx = dFdx(hh), dhy = dFdy(hh);
	vec3 r1 = cross(dpdy, NORMAL), r2 = cross(NORMAL, dpdx);
	float det = dot(dpdx, r1);
	if (abs(det) > 1e-10) NORMAL = normalize(abs(det) * NORMAL - sign(det) * (dhx * r1 + dhy * r2));

	ALBEDO = col;
	ROUGHNESS = rough;
	METALLIC = metal;
	SPECULAR = spec;
	BACKLIGHT = col * sss;
	EMISSION = emission * emission_energy;
}
