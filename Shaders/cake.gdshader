// Pâtisserie procédurale, un style par gâteau (uniform « style ») :
//   0 galette frangipane · 1 forêt-noire · 2 fraisier · 3 matcha
//   4 citron meringué   · 5 myrtille cosmique
// Trois aspects : le DESSUS d'origine (dorure, glaçage, décor), le FLANC
// d'origine (feuilletage, copeaux, fraises coupées…) et l'INTÉRIEUR
// croqué (strates selon la hauteur). Relief par dérivées écran d'une
// hauteur procédurale ; occlusion et « galerie » viennent des sommets.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform int style = 0;
uniform vec3 crust : source_color = vec3(0.85, 0.63, 0.24);
uniform vec3 glaze : source_color = vec3(0.88, 0.65, 0.25);
uniform vec3 fill : source_color = vec3(0.94, 0.83, 0.55);
uniform vec3 accent : source_color = vec3(0.96, 0.79, 0.24);
uniform vec3 chunk : source_color = vec3(0.73, 0.47, 0.24);
uniform vec2 center = vec2(0.0);
uniform float highlight = 0.0;

const float R = 38.0;

varying vec3 v_wpos;
varying vec3 v_wn;
varying vec3 v_shade; // ao, cave, top


float h31(vec3 p) { p = fract(p * vec3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return fract((p.x + p.y) * p.z); }
float h21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
vec2 h22(vec2 p) { return vec2(h21(p), h21(p + 17.7)); }
float vnoise(vec3 p) {
	vec3 i = floor(p); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(h31(i), h31(i + vec3(1,0,0)), f.x), mix(h31(i + vec3(0,1,0)), h31(i + vec3(1,1,0)), f.x), f.y),
			   mix(mix(h31(i + vec3(0,0,1)), h31(i + vec3(1,0,1)), f.x), mix(h31(i + vec3(0,1,1)), h31(i + vec3(1,1,1)), f.x), f.y), f.z);
}
// taille d'un pixel en unités monde : les motifs plus fins que ~2 pixels sont lissés (pas de scintillement)
float lodk(float freq) { return 1.0 - smoothstep(0.25, 0.6, length(fwidth(v_wpos)) * freq); }
float bnoise(vec3 p, float freq) { return mix(0.5, vnoise(p * freq), lodk(freq)); }
float fbm(vec3 p) { return 0.55 * vnoise(p) + 0.3 * vnoise(p * 2.07 + 11.0) + 0.15 * vnoise(p * 4.13 + 37.0); }
float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1, 0)), f.x), mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), f.x), f.y);
}
// cellules : distance au centre le plus proche (x), identifiant (y)
vec2 cells(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	float best = 9.0, id = 0.0;
	for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec2 o = vec2(float(x), float(y));
		vec2 c = o + 0.15 + 0.7 * h22(i + o);
		float d = length(f - c);
		if (d < best) { best = d; id = h21(i + o + 3.1); }
	}
	return vec2(best, id);
}
vec2 cells3(vec3 p) {
	vec3 i = floor(p), f = fract(p);
	float best = 9.0, id = 0.0;
	for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec3 o = vec3(float(x), float(y), float(z));
		vec3 c = o + 0.2 + 0.6 * vec3(h31(i + o), h31(i + o + 7.3), h31(i + o + 13.1));
		float d = length(f - c);
		if (d < best) { best = d; id = h31(i + o + 3.7); }
	}
	return vec2(best, id);
}


// décor en couronne : position locale par rapport à l'élément le plus proche
// d'un anneau de n éléments au rayon rr0 (x = radial, y = tangent), et son numéro
vec3 ring_local(vec2 rel, float rr0, float n) {
	float th = atan(rel.y, rel.x);
	float k = floor(th / TAU * n + 0.5);
	float a = k * TAU / n;
	vec2 c = vec2(cos(a), sin(a)) * rr0;
	vec2 d = rel - c;
	vec2 rad = vec2(cos(a), sin(a));
	return vec3(dot(d, rad), dot(d, vec2(-rad.y, rad.x)), k);
}

void vertex() {
	v_wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_shade = COLOR.rgb;
}

// ───────────────────────── DESSUS ─────────────────────────
void top_look(vec3 w, float rr, float th, out vec3 col, out float rough, out float h, out float sss) {
	float r = rr / R;
	float big = fbm(w * 0.18 + 3.0);
	vec2 rel = w.xz - center;
	sss = 0.08;
	if (style == 0) {
		// galette : rayons courbes depuis un médaillon, frise de chevrons au bord
		float ang = th + rr * 0.042;
		float nR = 22.0;
		float rc = fract(ang / TAU * nR);
		float gd = min(rc, 1.0 - rc) * TAU * max(rr, 1.0) / nR; // distance (unités) au sillon
		float rays = smoothstep(5.2, 6.2, rr) * (1.0 - smoothstep(29.5, 30.5, rr));
		float s = th * 32.5; // abscisse curviligne sur la frise
		float t = rr - 30.8;
		float chev = fract(s / 2.4 + abs(t - 2.4) * 0.55);
		float cd = min(chev, 1.0 - chev) * 2.4;
		float band = smoothstep(30.8, 31.3, rr) * (1.0 - smoothstep(35.6, 36.2, rr));
		float ring1 = abs(rr - 30.6), ring2 = abs(rr - 5.6), ring3 = abs(rr - 3.2);
		float groove = rays * (1.0 - smoothstep(0.1, 0.32, gd));
		groove = max(groove, band * (1.0 - smoothstep(0.1, 0.3, cd)));
		groove = max(groove, 1.0 - smoothstep(0.08, 0.22, min(ring1, min(ring2, ring3))));
		// petits losanges du médaillon
		vec2 q = mat2(vec2(0.7071, -0.7071), vec2(0.7071, 0.7071)) * rel / 1.15;
		vec2 fq = abs(fract(q) - 0.5);
		float lat = (1.0 - smoothstep(3.0, 3.2, rr)) * (1.0 - smoothstep(0.0, 0.07, min(0.5 - fq.x, 0.5 - fq.y)));
		groove = max(groove, lat * 0.9);
		// bourrelets entre les sillons, bord plus doré
		float pillow = rays * smoothstep(0.0, 1.2, gd) + band * smoothstep(0.0, 0.7, cd) + (1.0 - rays - band) * 0.6;
		h = pillow * 0.6 - groove * 0.5 + (big - 0.5) * 0.4;
		vec3 dark = vec3(0.34, 0.11, 0.015);
		vec3 gold = vec3(0.74, 0.36, 0.06);
		vec3 pale = vec3(0.86, 0.56, 0.22);
		col = mix(gold, dark, clamp(big * 1.3 - 0.25 + r * r * 0.35, 0.0, 1.0));
		// la dorure s'accumule au bord des sillons (plus foncée), le sillon s'ouvre sur la pâte claire
		float edgeDark = rays * (1.0 - smoothstep(0.25, 0.7, gd)) * (1.0 - groove);
		col = mix(col, dark, edgeDark * 0.55 + (1.0 - pillow) * 0.2 * (1.0 - groove));
		col = mix(col, pale, groove * 0.7);
		rough = mix(0.16, 0.6, groove);
		sss = 0.12;
	} else if (style == 1) {
		// forêt-noire : glaçage chocolat brillant, copeaux au centre,
		// couronne de rosaces de chantilly surmontées d'une cerise
		vec2 c = cells(rel * 0.9);
		float flake = (1.0 - smoothstep(13.0, 14.0, rr)) * smoothstep(0.1, 0.45, c.x);
		col = glaze * (0.85 + 0.3 * big);
		float swirl = sin(th * 6.0 + rr * 0.25 + big * 4.0);
		col *= 1.0 + 0.1 * swirl;
		col = mix(col, chunk * 0.3 + vec3(0.16, 0.08, 0.04), flake * (0.5 + 0.5 * c.y));
		h = flake * 0.6 + (big - 0.5) * 0.15;
		rough = mix(0.22, 0.6, flake);
		sss = 0.08;
	} else if (style == 2) {
		// fraisier : pâte d'amande rose satinée, couronne de fraises coupées, perles de crème au bord
		vec3 pink = mix(glaze, accent, 0.42);
		float sugar = smoothstep(0.82, 0.9, bnoise(w, 6.0)) * 0.6;
		col = pink * (0.92 + 0.14 * big);
		col = mix(col, vec3(1.0), sugar * 0.35);
		h = (big - 0.5) * 0.25 + sugar * 0.1;
		rough = 0.4;
		sss = 0.2;
		float pip = fract(th * 70.0 / TAU);
		float beadZone = smoothstep(34.5, 35.0, rr) * (1.0 - smoothstep(36.6, 37.1, rr));
		float bead = (1.0 - smoothstep(0.25, 0.5, abs(pip - 0.5) * 2.0)) * beadZone;
		col = mix(col, vec3(0.97, 0.94, 0.9), beadZone);
		h += bead * 0.7;
	} else if (style == 3) {
		// matcha : glaçage vert velouté poudré, décor en treillis crème
		vec2 q = rel / 3.0;
		vec2 f = abs(fract(q) - 0.5);
		float lat = (1.0 - smoothstep(0.03, 0.08, min(f.x, f.y))) * (1.0 - smoothstep(25.0, 26.0, rr)) * smoothstep(8.0, 9.0, rr);
		float dust = bnoise(w, 9.0);
		h = lat * 0.35 + (dust - 0.5) * 0.08;
		col = glaze * (0.82 + 0.3 * dust) * (0.95 + 0.1 * big);
		col = mix(col, accent, lat * 0.9);
		rough = mix(0.85, 0.4, lat);
		sss = 0.1;
	} else if (style == 4) {
		// citron meringué : meringue italienne pochée en flammes, pointes toastées au chalumeau
		vec3 rl = ring_local(rel, max(4.0, floor(rr / 3.2) * 3.2 + 1.6), max(6.0, floor(rr / 3.2 * 2.0 + 0.5) * 3.0));
		float d = length(rl.xy);
		float ang = atan(rl.y, rl.x);
		float flame = 1.0 - smoothstep(0.0, 1.7, d + 0.35 * sin(ang * 3.0 + d * 3.0));
		h = flame * 1.6;
		vec3 cream = vec3(0.98, 0.93, 0.80);
		vec3 toast = vec3(0.55, 0.25, 0.07);
		float burn = smoothstep(0.45, 0.9, flame + (big - 0.5) * 0.4);
		col = mix(cream, mix(vec3(0.85, 0.6, 0.3), toast, smoothstep(0.75, 1.0, flame)), burn * 0.9);
		col *= mix(0.85, 1.0, flame);
		rough = 0.5;
		sss = 0.3;
	} else {
		// myrtille cosmique : glaçage miroir violet, galaxie et étoiles
		float neb = fbm(vec3(rel * 0.08, 1.0) + vec3(big));
		col = mix(glaze * 0.55, accent * 0.9, smoothstep(0.45, 0.8, neb));
		col = mix(col, vec3(0.95, 0.45, 0.75), smoothstep(0.62, 0.85, fbm(vec3(rel * 0.12, 4.0))) * 0.35);
		vec2 c = cells(rel * 1.4);
		float star = (1.0 - smoothstep(0.0, 0.06 + 0.05 * c.y, c.x)) * step(0.72, c.y);
		col += vec3(1.0, 0.95, 0.85) * star * 2.0;
		h = (big - 0.5) * 0.1;
		rough = 0.06;
	}
}

// ───────────────────────── FLANC D'ORIGINE ─────────────────────────
void side_look(vec3 w, float rr, float th, out vec3 col, out float rough, out float h, out float sss) {
	float s = th * R; // abscisse le long du flanc
	vec3 cp = vec3(s, w.y, rr);
	float nn = fbm(vec3(s * 0.35, w.y * 0.6, 1.0));
	sss = 0.1;
	if (style == 0) {
		// feuilletage : feuilles empilées presque droites ; le bord de chaque
		// feuille accroche la lumière, l'ombre se loge dessous
		float jag = (n2(vec2(s * 1.7, w.y * 3.0)) - 0.5) * 0.12 + (n2(vec2(s * 0.12, 1.0)) - 0.5) * 0.5;
		float L = (w.y + jag) * 3.0;
		float layerAA = lodk(3.0 * 4.0); // les feuilles s'estompent quand elles deviennent trop fines à l'écran
		float id = floor(L), f = fract(L);
		float lr = h21(vec2(id, 7.0));
		float lip = 1.0 - smoothstep(0.0, 0.16, 1.0 - f);       // arête claire en haut de la feuille
		float under = 1.0 - smoothstep(0.0, 0.3, f);            // ombre sous la feuille du dessus
		float split = smoothstep(0.55, 0.8, n2(vec2(s * 0.4 + id * 7.0, id)));  // feuilles décollées
		vec3 golden = vec3(0.86, 0.44, 0.09), deep = vec3(0.36, 0.13, 0.02), light = vec3(0.98, 0.72, 0.34);
		col = golden * (0.9 + 0.2 * lr);
		col = mix(col, light, lip * 0.6 * layerAA);
		col = mix(col, deep, under * (0.25 + 0.6 * split) * layerAA);
		float blot = fbm(vec3(s * 0.12, w.y * 0.4, 9.0));
		col *= 0.86 + 0.26 * blot;
		col = mix(col, vec3(0.55, 0.25, 0.05), smoothstep(5.5, 7.5, w.y + (nn - 0.5) * 1.5) * 0.4);
		col *= mix(0.72, 1.0, smoothstep(0.0, 1.4, w.y)); // base plus cuite
		h = (0.5 - 0.5 * cos(6.28318 * f)) * 0.6 * layerAA;
		rough = mix(0.55, 0.3, lip);
		sss = 0.15;
	} else if (style == 1) {
		// forêt-noire : copeaux de chocolat collés sur la crème
		vec2 c = cells(vec2(s * 0.9, w.y * 0.45));
		float flake = smoothstep(0.08, 0.4, c.x);
		h = flake * 0.6;
		col = mix(chunk * 0.25 + vec3(0.2, 0.11, 0.06), glaze * 1.8 + vec3(0.06, 0.03, 0.01), flake * (0.3 + 0.7 * c.y));
		col = mix(vec3(0.9, 0.86, 0.8), col, smoothstep(0.03, 0.12, c.x));
		rough = 0.6;
	} else if (style == 2) {
		// fraisier : fraises coupées en deux contre la crème, génoise en bas
		float sc = s / 3.2;
		vec2 q = vec2(fract(sc) - 0.5, (w.y - 5.6) / 3.0);
		float tri = length(q * vec2(1.0, 0.85) + vec2(0.0, q.y * q.y * 0.35));
		float straw = 1.0 - smoothstep(0.36, 0.4, tri + q.y * 0.12);
		float heart = 1.0 - smoothstep(0.08, 0.26, length(vec2(q.x, q.y + 0.05) * vec2(2.4, 0.8)));
		vec3 cream = fill * 1.05;
		col = cream * (0.95 + 0.08 * nn);
		col = mix(col, mix(chunk, accent * 1.1, 0.4), straw);
		col = mix(col, vec3(1.0, 0.7, 0.72), straw * heart * 0.8);
		float seeds = (1.0 - smoothstep(0.0, 0.05, cells(vec2(s, w.y) * 2.5).x)) * straw * (1.0 - heart);
		col = mix(col, vec3(1.0, 0.86, 0.5), seeds);
		float sponge = 1.0 - smoothstep(1.4, 1.7, w.y);
		col = mix(col, crust * (0.9 + 0.2 * bnoise(cp, 3.0)), sponge);
		h = straw * 0.5 + sponge * (bnoise(cp, 4.0) - 0.5) * 0.3;
		rough = mix(0.5, 0.25, straw);
		sss = 0.25;
	} else if (style == 3) {
		// matcha : strates fines visibles (biscuit, crème, ganache)
		float L = w.y * 0.9;
		float f = fract(L);
		col = mix(glaze * 0.75, accent, step(0.55, f));
		col = mix(col, crust * 0.8, (1.0 - step(0.12, f)) * 0.8);
		col *= 0.92 + 0.12 * nn;
		h = step(0.55, f) * 0.15;
		rough = 0.6;
	} else if (style == 4) {
		// citron : fond de tarte sablé cannelé, doré
		float flute = sin(s * 2.0);
		h = flute * 0.35;
		col = crust * (0.9 + 0.12 * flute + 0.15 * nn);
		col = mix(col, crust * 0.6, smoothstep(0.6, 0.9, bnoise(cp, 2.0)) * 0.3);
		rough = 0.7;
	} else {
		// myrtille : coulures de glaçage violet sur la mousse
		float drip = 6.6 - (2.0 + 2.6 * n2(vec2(s * 0.7, 3.0)) + 1.2 * step(0.7, n2(vec2(s * 2.1, 1.0))));
		float glz = smoothstep(drip - 0.08, drip + 0.08, w.y);
		col = mix(fill * (0.9 + 0.1 * nn), glaze * 0.6, glz);
		float berry = (1.0 - smoothstep(0.18, 0.24, cells(vec2(s, w.y) * 0.8).x)) * (1.0 - glz);
		col = mix(col, chunk * 1.6, berry);
		h = glz * 0.25 + berry * 0.4;
		rough = mix(0.6, 0.08, glz);
	}
}

// ───────────────────────── INTÉRIEUR ─────────────────────────
void inner_look(vec3 w, out vec3 col, out float rough, out float h, out float sss) {
	float nn = fbm(w * 1.3);
	float yy = w.y + (fbm(w * 0.4 + 5.0) - 0.5) * 0.8;
	float grain = bnoise(w, 7.0) * 0.6 + mix(0.5, vnoise(w * 15.0 + 3.0), lodk(15.0)) * 0.4;
	sss = 0.1;
	rough = 0.8;
	if (style == 0) {
		bool pastry = yy < 1.6 || yy > 10.4;
		if (pastry) {
			float f = fract(yy * 7.0 + nn * 2.0);
			float sheet = smoothstep(0.0, 0.2, f) * (1.0 - smoothstep(0.7, 1.0, f));
			// feuilletage vu en coupe : feuilles fines et dorées, les creux à peine plus sombres
			col = mix(crust * 0.9, mix(crust, vec3(1.0, 0.93, 0.75), 0.35), sheet) * (0.96 + 0.08 * grain);
			h = sheet * 0.18 + (grain - 0.5) * 0.15;
		} else {
			// frangipane : crème d'amande, grain sablé, éclats d'amande
			col = fill * (0.95 + 0.08 * grain) * vec3(1.03, 1.0, 0.94);
			float almond = smoothstep(0.78, 0.82, fbm(w * 0.9 + 17.0));
			col = mix(col, vec3(0.97, 0.9, 0.74), almond);
			col = mix(col, accent, (1.0 - smoothstep(0.4, 0.75, abs(yy - 6.0))) * 0.35);
			h = (grain - 0.5) * 0.25 + almond * 0.3;
		}
	} else if (style == 1) {
		// génoise chocolat / chantilly / cerises
		float L = mod(yy, 4.0);
		if (L > 2.7) { col = accent * (0.95 + 0.06 * grain); h = (grain - 0.5) * 0.15; rough = 0.55; sss = 0.3; }
		else { col = crust * (0.8 + 0.4 * grain); h = (grain - 0.5) * 0.6; }
		vec2 ch = cells3(w * 0.45);
		float cherry = (1.0 - smoothstep(0.22, 0.26, ch.x)) * step(0.55, ch.y);
		col = mix(col, chunk * (0.85 + 0.3 * grain), cherry);
		rough = mix(rough, 0.25, cherry);
	} else if (style == 2) {
		// génoise / mousseline / fraises
		if (yy < 1.6 || (yy > 10.0 && yy < 11.2)) { col = crust * (0.92 + 0.2 * grain); h = (grain - 0.5) * 0.6; }
		else if (yy >= 11.2) { col = glaze; h = 0.0; rough = 0.45; }
		else {
			col = fill * (0.96 + 0.06 * grain); h = (grain - 0.5) * 0.12; rough = 0.5; sss = 0.3;
			vec2 st = cells3(w * 0.3);
			float straw = 1.0 - smoothstep(0.3, 0.34, st.x);
			col = mix(col, mix(accent, vec3(1.0, 0.75, 0.75), 1.0 - smoothstep(0.0, 0.25, st.x)), straw);
			h += straw * 0.2;
		}
	} else if (style == 3) {
		float L = fract(yy * 0.75);
		col = L < 0.45 ? glaze * (0.75 + 0.3 * grain) : (L < 0.6 ? crust * (0.8 + 0.3 * grain) : accent * (0.95 + 0.06 * grain));
		h = (grain - 0.5) * 0.3;
	} else if (style == 4) {
		if (yy < 1.8) { col = crust * (0.85 + 0.3 * grain); h = (grain - 0.5) * 0.6; }
		else if (yy < 7.8) { col = fill * (0.97 + 0.05 * grain); h = 0.0; rough = 0.3; sss = 0.35; }
		else { col = mix(glaze, vec3(1.0), 0.5) * (0.95 + 0.06 * grain); h = (nn - 0.5) * 0.3; rough = 0.6; sss = 0.3; }
	} else {
		col = mix(fill, glaze * 0.8, smoothstep(0.4, 0.7, nn)) * (0.9 + 0.15 * grain);
		vec2 b = cells3(w * 0.7);
		float berry = (1.0 - smoothstep(0.17, 0.21, b.x)) * step(0.4, b.y);
		col = mix(col, chunk * 1.3, berry);
		if (yy < 1.6) col = crust * (0.85 + 0.25 * grain);
		h = (grain - 0.5) * 0.3 + berry * 0.3;
	}
}

void fragment() {
	vec3 w = v_wpos;
	vec2 rel = w.xz - center;
	float rr = length(rel), th = atan(rel.y, rel.x);
	vec3 wn = normalize(v_wn);
	float orig = smoothstep(0.5, 0.95, v_shade.b);

	vec3 col; float rough; float h; float sss;
	if (orig > 0.01) {
		vec3 ct, cs; float rt, rs, ht, hs, st, ss;
		float up = smoothstep(0.22, 0.5, wn.y);
		top_look(w, rr, th, ct, rt, ht, st);
		side_look(w, rr, th, cs, rs, hs, ss);
		col = mix(cs, ct, up); rough = mix(rs, rt, up); h = mix(hs, ht, up); sss = mix(ss, st, up);
		if (orig < 0.99) {
			vec3 ci; float ri, hi, si;
			inner_look(w, ci, ri, hi, si);
			col = mix(ci, col, orig); rough = mix(ri, rough, orig); h = mix(hi, h, orig); sss = mix(si, sss, orig);
		}
	} else {
		inner_look(w, col, rough, h, sss);
	}

	// miettes et pores, visibles de près
	float near = lodk(22.0);
	float fine = vnoise(w * 22.0) * 0.7 + mix(0.5, vnoise(w * 47.0 + 5.0), lodk(47.0)) * 0.3;
	float bumpK = mix(0.12, 0.12, orig);
	h += (fine - 0.5) * 0.1 * near * (1.0 - orig * 0.7);
	col *= mix(1.0, 0.97 + 0.05 * fine, near * (1.0 - orig * 0.6));

	// relief : gradient de surface par dérivées écran (Mikkelsen)
	float hh = h * bumpK;
	vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
	float dhx = dFdx(hh), dhy = dFdy(hh);
	vec3 r1 = cross(dpdy, NORMAL), r2 = cross(NORMAL, dpdx);
	float det = dot(dpdx, r1);
	vec3 grad = sign(det) * (dhx * r1 + dhy * r2);
	vec3 nb = normalize(abs(det) * NORMAL - grad);
	// relief plafonné (~25°) : il modèle la matière sans créer de faux plis que le contour encrerait
	float cosA = dot(nb, NORMAL);
	if (cosA < 0.9) nb = normalize(mix(NORMAL, nb, (1.0 - 0.9) / max(1e-3, 1.0 - cosA)));
	NORMAL = nb;

	float ao = v_shade.r, cave = v_shade.g;
	// intérieur croqué : grains de sucre et éclats qui scintillent sous la lampe, mie humide
	float glint = 0.0;
	if (orig < 0.5) {
		vec3 gp = w * 9.0;
		vec3 gi = floor(gp);
		float pick = step(0.93, fract(sin(dot(gi, vec3(12.9898, 78.233, 37.719))) * 43758.5453));
		float dotk = 1.0 - smoothstep(0.08, 0.22, length(fract(gp) - 0.5));
		vec3 fn = normalize(vec3(fract(sin(dot(gi, vec3(1.3, 7.1, 3.7))) * 913.1), fract(sin(dot(gi, vec3(5.1, 2.3, 9.9))) * 517.3), fract(sin(dot(gi, vec3(8.7, 4.4, 1.2))) * 271.9)) - 0.5);
		float face = pow(max(0.0, dot(reflect(-VIEW, normalize(NORMAL + fn * 0.8)), VIEW)), 12.0);
		glint = pick * dotk * face * near * (1.0 - orig);
		rough = mix(rough, rough * 0.75, cave);   // la mie au fond des galeries paraît plus moelleuse/humide
	}
	ALBEDO = col * mix(1.0, ao, 0.25) * mix(1.0, 0.9, cave);
	ROUGHNESS = rough;
	SPECULAR = mix(0.45, 0.6, orig);
	AO = ao * mix(1.0, 0.6, cave);
	AO_LIGHT_AFFECT = 0.35;
	BACKLIGHT = col * sss;
	// au fond des galeries : une lueur chaude très légère (on y voit un peu plus, sans aplatir le relief)
	EMISSION = vec3(highlight) + vec3(1.0, 0.95, 0.8) * glint * 1.6 + col * cave * 0.05;
}
