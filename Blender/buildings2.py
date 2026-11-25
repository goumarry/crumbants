# Machines de Crumbants, deuxième version (Models/buildings.glb).
# 1 unité = 1 case du jeu. Repère Blender : X à droite, +Y = SORTIE de la machine
# (−Z dans Godot), Z vers le haut. Chaque objet est exporté avec une transformation
# identité (ModelLib ignore les transformations des nœuds).
#
# Les fourmis « opératrices » ne sont PAS dans le modèle : chaque machine réserve leur
# place (plate-forme, poste de pilotage…) et la marque par un repère « op_<type>_spot » (jamais « …_wheel » : Godot en ferait une roue de véhicule) :
# un petit triangle isocèle dont la pointe donne la direction du regard (lu par
# Scripts/Render/BuildingsView.cs, ModelLib.Marker).
#
# Le casque de chantier (« hardhat ») est modélisé dans le repère de la TÊTE de la
# fourmi (Models/ant.glb) : le jeu l'accroche à la tête de chaque opératrice.
#
# Lancer dans Blender (via le MCP) : exec(open(r"...buildings2.py").read(), {"OUT_PATH": r"...glb"})
# ONLY=["comp", …] dans les globals : ne construire que ces groupes (aperçu).
import bpy, bmesh, math
from mathutils import Vector, Matrix

OUT = globals().get("OUT_PATH")
ONLY = globals().get("ONLY")
SCN_NAME = "Bld2"
SCENE = bpy.data.scenes.get(SCN_NAME) or bpy.data.scenes.new(SCN_NAME)
try:
    bpy.context.window.scene = SCENE
except Exception:
    pass
for o in list(SCENE.objects):
    bpy.data.objects.remove(o, do_unlink=True)

def srgb(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

_M = {}
def mat(name, hexcol, rough=0.5, metal=0.0, emit=None, strength=2.0, alpha=1.0):
    if name in _M: return _M[name]
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value = (*srgb(hexcol), 1)
    p.inputs['Roughness'].default_value = rough
    p.inputs['Metallic'].default_value = metal
    p.inputs['Alpha'].default_value = alpha
    if emit:
        p.inputs['Emission Color'].default_value = (*srgb(emit), 1)
        p.inputs['Emission Strength'].default_value = strength
    else:
        p.inputs['Emission Strength'].default_value = 0.0
    _M[name] = m
    return m

# ───── palette (les noms pilotent les surfaces du jeu : Scripts/Render/MatLib.cs) ─────
STEEL = mat("Steel", "#b9c0c8", 0.35, 0.6)       # métal brossé clair
DARK = mat("Steel2", "#8a929b", 0.45, 0.5)       # métal gris moyen (socles, carters)
IRON = mat("Iron", "#6c737c", 0.5, 0.6)
CHROME = mat("Chrome", "#e6ebf0", 0.12, 0.9)
BRASS = mat("Brass", "#d9a441", 0.3, 0.8)
COPPER = mat("Copper", "#c8703f", 0.3, 0.8)
RUBBER = mat("Rubber", "#7a7f86", 0.75, 0.0)
YELLOW = mat("Yellow", "#f4b81c", 0.35)          # jaune chantier
ORANGE = mat("Orange", "#f07a1f", 0.35)
RED = mat("Red", "#e0463a", 0.35)
TEAL = mat("Teal", "#2b9c94", 0.35)
CREAM = mat("Cream", "#f4ead6", 0.3)
PINK = mat("Pink", "#f39bb5", 0.35)
BLACK = mat("Black", "#3a3634", 0.5)
WOOD = mat("Wood", "#c08a52", 0.6)
WOODD = mat("WoodDark", "#7a4c28", 0.65)
CONC = mat("Concrete", "#b3aca2", 0.85)
GLASS = mat("Glass", "#cfe8f2", 0.05, 0.0, alpha=0.3)
SCREEN = mat("Screen", "#5ff0d0", 0.3, emit="#5ff0d0", strength=1.6)
LAMP_R = mat("LampRed", "#ff4a3a", 0.3, emit="#ff3a2a", strength=2.5)
LAMP_G = mat("LampGreen", "#5aff7a", 0.3, emit="#3aff6a", strength=2.5)
BULB = mat("Bulb", "#ffe9a8", 0.2, emit="#ffd36a", strength=4.0)
FIRE = mat("Fire", "#ff8a2a", 0.4, emit="#ff7a1a", strength=5.0)
CHERRY = mat("Cherry", "#d81e34", 0.2)
LEAF = mat("Green", "#4aa83a", 0.45)
CRUMB = mat("CrumbPile", "#e2b878", 0.9)
CHALK = mat("Chalk", "#2a4a3e", 0.85)            # ardoise des panneaux
CHALKLINE = mat("ChalkLine", "#efe6cf", 0.8)
RIBBON = mat("Ribbon", "#c9343a", 0.5)
GOLD = mat("CardGold", "#e6b54a", 0.3, 0.8)

# ───── construction en bmesh ─────
class Obj:
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.mats = []
    def mi(self, m):
        if m not in self.mats: self.mats.append(m)
        return self.mats.index(m)
    def merge(self, bm2, m, M=None):
        if M is not None: bmesh.ops.transform(bm2, matrix=M, verts=bm2.verts)
        # normales vers l'extérieur, quel que soit le sens de construction (Godot ne dessine
        # pas l'envers des faces : une pièce retournée paraît trouée ou noire)
        bmesh.ops.recalc_face_normals(bm2, faces=list(bm2.faces))
        idx = self.mi(m)
        for f in bm2.faces: f.material_index = idx
        me = bpy.data.meshes.new("_tmp"); bm2.to_mesh(me); bm2.free()
        self.bm.from_mesh(me); bpy.data.meshes.remove(me)
        return self
    # primitives ------------------------------------------------------------------
    def box(self, size, at, m, bevel=0.0, segs=2, rot=None):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size) * 0.49), segments=segs, profile=0.5, affect='EDGES', clamp_overlap=True)
        return self.merge(bm, m, _place(at, rot))
    def cyl(self, r, h, at, m, r2=None, seg=24, bevel=0.0, segs=2, rot=None, caps=True):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
        if bevel > 0 and caps:
            rim = [e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) < 1e-6 and abs(abs(e.verts[0].co.z) - h / 2) < 1e-6]
            bmesh.ops.bevel(bm, geom=rim, offset=min(bevel, h * 0.45, r * 0.45), segments=segs, profile=0.5, affect='EDGES', clamp_overlap=True)
        return self.merge(bm, m, _place(at, rot))
    def sphere(self, r, at, m, seg=16, rings=10, scale=(1, 1, 1), rot=None):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=r)
        bmesh.ops.scale(bm, vec=Vector(scale), verts=bm.verts)
        return self.merge(bm, m, _place(at, rot))
    def torus(self, R, r, at, m, seg=32, rseg=8, rot=None, arc=2 * math.pi):
        bm = bmesh.new()
        full = arc >= 2 * math.pi - 1e-4
        n = seg if full else seg + 1
        rings = []
        for i in range(n):
            a = arc * i / seg
            c, s = math.cos(a), math.sin(a)
            ring = []
            for j in range(rseg):
                b = 2 * math.pi * j / rseg
                rr = R + r * math.cos(b)
                ring.append(bm.verts.new((rr * c, rr * s, r * math.sin(b))))
            rings.append(ring)
        for i in range(n if full else n - 1):
            a, b = rings[i], rings[(i + 1) % n]
            for j in range(rseg):
                bm.faces.new((a[j], b[j], b[(j + 1) % rseg], a[(j + 1) % rseg]))
        return self.merge(bm, m, _place(at, rot))
    def lathe(self, prof, at, m, seg=32, cap_top=False, cap_bot=False, rot=None):
        bm = bmesh.new()
        rings = []
        for r, z in prof:
            rings.append([bm.verts.new((math.cos(2 * math.pi * i / seg) * r, math.sin(2 * math.pi * i / seg) * r, z)) for i in range(seg)])
        for a, b in zip(rings, rings[1:]):
            for i in range(seg):
                j = (i + 1) % seg
                bm.faces.new((a[i], a[j], b[j], b[i]))
        if cap_bot: bm.faces.new(list(reversed(rings[0])))
        if cap_top: bm.faces.new(rings[-1])
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        return self.merge(bm, m, _place(at, rot))
    def tube(self, pts, r, m, seg=8, caps=True):
        pts = [Vector(p) for p in pts]
        bm = bmesh.new()
        rings = []
        prev = None
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            if prev is None:
                prev = t.cross(Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))).normalized()
            n = (prev - t * prev.dot(t)).normalized(); prev = n
            b = t.cross(n)
            rings.append([bm.verts.new(p + (n * math.cos(2 * math.pi * k / seg) + b * math.sin(2 * math.pi * k / seg)) * r) for k in range(seg)])
        for a, c in zip(rings, rings[1:]):
            for k in range(seg):
                bm.faces.new((a[k], a[(k + 1) % seg], c[(k + 1) % seg], c[k]))
        if caps:
            bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
        return self.merge(bm, m)
    def bar(self, a, b, w, m, h=None, bevel=0.0):
        """poutre de section w × h de a à b"""
        a, b = Vector(a), Vector(b)
        d = b - a; L = d.length
        if L < 1e-6: return self
        z = d.normalized()
        x = z.cross(Vector((0, 0, 1)))
        if x.length < 1e-4: x = Vector((1, 0, 0))
        x.normalize(); y = z.cross(x)
        R = Matrix((x, y, z)).transposed().to_4x4()
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=Vector((w, h or w, L)), verts=bm.verts)
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, w * 0.45), segments=1, affect='EDGES', clamp_overlap=True)
        return self.merge(bm, m, Matrix.Translation((a + b) / 2) @ R)
    def rod(self, a, b, r, m, seg=8):
        a, b = Vector(a), Vector(b)
        d = b - a; L = d.length
        if L < 1e-6: return self
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=L)
        q = Vector((0, 0, 1)).rotation_difference(d.normalized())
        return self.merge(bm, m, Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4())
    def prism(self, outline, z0, z1, m, bevel=0.0, axis='Z', at=(0, 0, 0), rot=None):
        """polygone 2D (x, y) extrudé de z0 à z1"""
        bm = bmesh.new()
        lo = [bm.verts.new((x, y, z0)) for x, y in outline]
        hi = [bm.verts.new((x, y, z1)) for x, y in outline]
        n = len(outline)
        bm.faces.new(list(reversed(lo))); bm.faces.new(hi)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        if bevel > 0:
            ring = [e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) < 1e-6]
            bmesh.ops.bevel(bm, geom=ring, offset=bevel, segments=2, affect='EDGES', clamp_overlap=True)
        M = _place(at, rot)
        if axis == 'Y': M = M @ Matrix.Rotation(math.pi / 2, 4, 'X')  # contour dans le plan XZ, épaisseur en −Y..
        return self.merge(bm, m, M)
    def marker(self, pos, fwd):
        """repère d'opératrice : triangle (pointe = regard)"""
        p = Vector(pos); f = Vector(fwd).normalized()
        s = f.cross(Vector((0, 0, 1)))
        if s.length < 1e-3: s = Vector((1, 0, 0))
        s.normalize()
        bm = bmesh.new()
        v = [bm.verts.new(p + f * 0.12), bm.verts.new(p + s * 0.03), bm.verts.new(p - s * 0.03)]
        bm.faces.new(v)
        return self.merge(bm, BLACK)
    def finish(self, sharp_deg=50, smooth=True):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        if smooth:
            lim = math.radians(sharp_deg)
            for f in bm.faces: f.smooth = True
            for e in bm.edges:
                if len(e.link_faces) == 2:
                    try:
                        if e.calc_face_angle() > lim: e.smooth = False
                    except ValueError:
                        e.smooth = False
                else:
                    e.smooth = False
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me); bm.free()
        for m in self.mats: me.materials.append(m)
        o = bpy.data.objects.new(self.name, me)
        SCENE.collection.objects.link(o)
        return o

def _place(at, rot):
    M = Matrix.Translation(Vector(at))
    if rot is not None:
        if isinstance(rot, (tuple, list)):
            M = M @ Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X')
        else:
            M = M @ rot
    return M

def want(g):
    return ONLY is None or g in ONLY

# ───── pièces communes ─────
def plate(o, w=0.96, accent=YELLOW):
    """socle commun : dalle métal sombre chanfreinée, liseré de couleur, boulons laiton"""
    o.box((w, w, 0.07), (0, 0, 0.035), STEEL, bevel=0.03, segs=3)
    o.box((w - 0.1, w - 0.1, 0.012), (0, 0, 0.074), RUBBER, bevel=0.004, segs=1)
    # liseré peint sur le chant
    o.box((w + 0.004, w + 0.004, 0.018), (0, 0, 0.045), accent, bevel=0.009, segs=2)
    for sx in (-1, 1):
        for sy in (-1, 1):
            o.cyl(0.022, 0.02, (sx * (w / 2 - 0.06), sy * (w / 2 - 0.06), 0.085), BRASS, seg=10, bevel=0.006, segs=1)

def out_chute(o, z=0.3, w=0.3, y0=0.3):
    """goulotte de sortie (face +Y) : on voit d'où sortent les objets"""
    o.box((w + 0.06, 0.5 - y0, 0.05), (0, (y0 + 0.5) / 2, z - 0.02), STEEL, bevel=0.015)
    for sx in (-1, 1):
        o.box((0.03, 0.5 - y0, 0.08), (sx * (w / 2 + 0.03), (y0 + 0.5) / 2, z + 0.03), YELLOW, bevel=0.012)
    o.box((w + 0.12, 0.03, 0.03), (0, 0.49, z + 0.0), YELLOW, bevel=0.01)

def railing(o, pts, z, h=0.2, m=YELLOW, posts=True):
    """garde-corps tubulaire le long d'une polyligne (x, y) au niveau z"""
    top = [(x, y, z + h) for x, y in pts]
    o.tube(top, 0.024, m, seg=10)
    if posts:
        for x, y in pts:
            o.rod((x, y, z), (x, y, z + h), 0.022, m, seg=10)

def console(o, at, yaw, w=0.22, color=TEAL):
    """pupitre incliné : écran lumineux, voyants, deux leviers"""
    R = Matrix.Rotation(yaw, 4, 'Z')
    T = Matrix.Translation(Vector(at)) @ R
    def P(x, y, z): return (T @ Vector((x, y, z)))
    o.box((w, 0.08, 0.16), tuple(P(0, 0, 0.08)), color, bevel=0.02, rot=R)
    tilt = R @ Matrix.Rotation(-0.6, 4, 'X')
    o.box((w + 0.02, 0.1, 0.03), tuple(P(0, -0.01, 0.17)), DARK, bevel=0.01, rot=tilt)
    o.box((w * 0.55, 0.055, 0.008), tuple(P(-w * 0.12, -0.012, 0.188)), SCREEN, bevel=0.002, segs=1, rot=tilt)
    o.sphere(0.012, tuple(P(w * 0.3, -0.02, 0.19)), LAMP_G, seg=8, rings=5)
    o.sphere(0.012, tuple(P(w * 0.3, 0.012, 0.21)), LAMP_R, seg=8, rings=5)
    for k, sx in enumerate((-0.35, 0.05)):
        base = P(sx * w, 0.03, 0.2)
        tip = P(sx * w + 0.01, 0.0, 0.29 + 0.02 * k)
        o.rod(tuple(base), tuple(tip), 0.007, CHROME, seg=6)
        o.sphere(0.018, tuple(tip), RED if k == 0 else BLACK, seg=10, rings=6)

def ladder(o, x0, x1, y, z0, z1, m=STEEL, step=0.09):
    for x in (x0, x1):
        o.box((0.035, 0.035, z1 - z0), (x, y, (z0 + z1) / 2), m, bevel=0.01, segs=1)
    z = z0 + step
    while z < z1 - 0.02:
        o.rod((x0, y, z), (x1, y, z), 0.015, m, seg=8)
        z += step

def stripes(o, at, w, h, n, axis='X', depth=0.006):
    """bandes de danger jaunes et noires (plaquette)"""
    x0 = -w / 2
    for i in range(n):
        m = YELLOW if i % 2 == 0 else BLACK
        cx = x0 + (i + 0.5) * w / n
        if axis == 'X':
            o.box((w / n, depth, h), (at[0] + cx, at[1], at[2]), m)
        else:
            o.box((depth, w / n, h), (at[0], at[1] + cx, at[2]), m)

# ════════════════════════════ MACHINES 3 × 3 ════════════════════════════
# Emprise : 3 × 3 cases, centre (0, 0). Les cases du milieu des côtés portent les tapis
# intégrés (posés par le jeu : modèles de Models/belts.glb) : la sortie à l'avant (+Y),
# les entrées à gauche, à droite et à l'arrière. Le modèle laisse ces cases libres ; au
# bout intérieur de chaque tapis, un capot à lanières (l'objet « entre » dans la machine).
# Les coins (±1, ±1) portent tours, réservoirs, plates-formes des opératrices…
# Pièces jamais trop fines : le contour encre du jeu noircirait tout ce qui fait < 3 px.
BRICK = mat("Brick", "#c46a45", 0.85)
LINEN = mat("Linen", "#efe3c8", 0.9)
LAMP_A = mat("LampAmber", "#ffb03a", 0.3, emit="#ff9a1a", strength=2.5)
Z0 = 0.111            # dessus du socle
OP_Z = {}             # repères des opératrices (pour info)

def side_rot(sx, sy):
    """rotation autour de Z qui amène +Y sur (sx, sy)"""
    return Matrix.Rotation(math.atan2(-sx, sy), 4, 'Z')

def RP(R, x, y, z):
    return tuple(R @ Vector((x, y, z)))

def big_base(o, accent):
    """socle : dalle centrale + 4 dalles d'angle ; les cases des tapis restent libres"""
    for cx, cy in ((0, 0), (-1, -1), (1, -1), (-1, 1), (1, 1)):
        o.box((0.97, 0.97, 0.1), (cx, cy, 0.05), STEEL, bevel=0.035, segs=3)
        o.box((0.985, 0.985, 0.028), (cx, cy, 0.056), accent, bevel=0.013, segs=2)
        o.box((0.85, 0.85, 0.016), (cx, cy, 0.104), RUBBER, bevel=0.006, segs=1)
    for cx, cy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        for sx in (-1, 1):
            for sy in (-1, 1):
                o.cyl(0.032, 0.026, (cx + sx * 0.4, cy + sy * 0.4, 0.112), BRASS, seg=12, bevel=0.009, segs=1)

def stripes_r(o, R, at, w, h, n, depth=0.012):
    """bandes de danger (jaune / noir) le long de l'axe X local"""
    for i in range(n):
        cx = -w / 2 + (i + 0.5) * w / n
        o.box((w / n, depth, h), RP(R, at[0] + cx, at[1], at[2]), YELLOW if i % 2 == 0 else BLACK, rot=R)

def hood(o, side, color, out=False):
    """capot bas au bout intérieur d'un tapis intégré (case du milieu du côté `side`) :
    juste assez haut pour laisser passer les objets, pour ne pas masquer la machine"""
    R = side_rot(*side)
    for k in (-1, 1):
        o.box((0.12, 0.42, 0.8), RP(R, k * 0.57, 0.68, 0.4), color, bevel=0.04, segs=2, rot=R)
        o.box((0.16, 0.46, 0.08), RP(R, k * 0.57, 0.68, 0.04 + Z0 - 0.07), DARK, bevel=0.02, rot=R)
    o.box((1.3, 0.44, 0.2), RP(R, 0, 0.68, 0.82), color, bevel=0.07, segs=3, rot=R)
    o.box((1.32, 0.46, 0.04), RP(R, 0, 0.68, 0.935), STEEL, bevel=0.015, rot=R)
    if out:
        stripes_r(o, R, (0, 0.905, 0.82), 1.0, 0.1, 9)
        o.sphere(0.05, RP(R, 0.44, 0.8, 1.0), LAMP_G, seg=12, rings=8)
    else:
        o.box((0.9, 0.02, 0.06), RP(R, 0, 0.905, 0.82), CREAM, bevel=0.01, rot=R)
        o.sphere(0.045, RP(R, 0.44, 0.8, 1.0), LAMP_A, seg=12, rings=8)
    # rideau à lanières (les objets passent dessous et disparaissent dans la machine)
    for i in range(6):
        x = -0.42 + i * 0.168
        o.box((0.145, 0.03, 0.3), RP(R, x, 0.86, 0.57), RUBBER, bevel=0.008, segs=1, rot=R)

def run(o, a, b, z, h=0.36, m=YELLOW, r=0.032):
    """garde-corps droit de a à b (lisse, sous-lisse, poteaux tous les ~0,45)"""
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    o.rod((a.x, a.y, z + h), (b.x, b.y, z + h), r, m, seg=10)
    o.rod((a.x, a.y, z + h * 0.52), (b.x, b.y, z + h * 0.52), r * 0.75, m, seg=8)
    n = max(1, math.ceil((b - a).length / 0.45))
    for i in range(n + 1):
        p = a.lerp(b, i / n)
        o.rod((p.x, p.y, z), (p.x, p.y, z + h), r * 1.1, m, seg=10)
    for p in (a, b):
        o.sphere(r * 1.45, (p.x, p.y, z + h), m, seg=12, rings=7)

def platform(o, cx, cy, z, s=0.9, legs=True):
    """plate-forme carrée (caillebotis) sur 4 pieds entretoisés"""
    o.box((s, s, 0.08), (cx, cy, z - 0.04), DARK, bevel=0.025, segs=2)
    for k in range(5):
        o.box((s - 0.1, 0.035, 0.012), (cx, cy - s / 2 + 0.1 + k * (s - 0.2) / 4, z + 0.004), STEEL)
    if legs and z > 0.3:
        for sx in (-1, 1):
            for sy in (-1, 1):
                x, y = cx + sx * (s / 2 - 0.07), cy + sy * (s / 2 - 0.07)
                o.box((0.09, 0.09, z - 0.08 - Z0), (x, y, Z0 + (z - 0.08 - Z0) / 2), STEEL, bevel=0.02, segs=1)
        hz = Z0 + (z - Z0) * 0.45
        for sx in (-1, 1):
            o.bar((cx + sx * (s / 2 - 0.07), cy - s / 2 + 0.07, hz), (cx + sx * (s / 2 - 0.07), cy + s / 2 - 0.07, hz), 0.05, STEEL, bevel=0.01)
        for sy in (-1, 1):
            o.bar((cx - s / 2 + 0.07, cy + sy * (s / 2 - 0.07), hz), (cx + s / 2 - 0.07, cy + sy * (s / 2 - 0.07), hz), 0.05, STEEL, bevel=0.01)

def ladder_y(o, x, y0, y1, z0, z1, m=STEEL, step=0.14):
    """échelle contre une face d'abscisse x, montants en y0 et y1, crosse au-dessus du palier"""
    for y in (y0, y1):
        o.box((0.045, 0.045, z1 - z0 + 0.36), (x, y, (z0 + z1 + 0.36) / 2), m, bevel=0.012, segs=1)
    z = z0 + step
    while z < z1 - 0.03:
        o.rod((x, y0, z), (x, y1, z), 0.02, m, seg=8)
        z += step

def operator_deck(o, cx, cy, z, gap_side_x=None, rail=YELLOW):
    """poste d'opératrice dans un coin : plate-forme, échelle sur la face extérieure en x,
    garde-corps ouvert EXACTEMENT au droit de l'échelle"""
    platform(o, cx, cy, z)
    sx = 1 if cx > 0 else -1
    sy = 1 if cy > 0 else -1
    xo = cx + sx * 0.43           # face extérieure (x)
    yo = cy + sy * 0.43           # face extérieure (y)
    xi = cx - sx * 0.43           # côté intérieur (vers le centre)
    yi = cy - sy * 0.43
    # échelle sur la face x extérieure, entre y = cy ± 0.2
    ya, yb = cy - 0.2, cy + 0.2
    ladder_y(o, xo + sx * 0.05, ya, yb, Z0, z)
    # garde-corps : face x extérieure en deux morceaux (ouverture face à l'échelle)
    run(o, (xo, yo), (xo, cy + sy * 0.2), z, m=rail)
    run(o, (xo, cy - sy * 0.2), (xo, yi), z, m=rail)
    # face y extérieure, et les deux côtés intérieurs
    run(o, (xo, yo), (xi, yo), z, m=rail)
    run(o, (xi, yo), (xi, yi), z, m=rail)
    run(o, (xi, yi), (xo, yi), z, m=rail)

def diag_rot(cx, cy):
    """rotation qui tourne +Y vers le centre de la machine depuis le coin (cx, cy)"""
    return side_rot(-cx / math.hypot(cx, cy), -cy / math.hypot(cx, cy))

def panel(o, R, at, w=0.34, color=TEAL):
    """pupitre incliné vu depuis −Y local : écran, voyants, leviers (pièces épaisses)"""
    T = Matrix.Translation(Vector(at)) @ R
    def P(x, y, z): return tuple(T @ Vector((x, y, z)))
    o.box((w, 0.12, 0.3), P(0, 0, 0.15), color, bevel=0.035, segs=2, rot=R)
    tilt = R @ Matrix.Rotation(0.55, 4, 'X')
    o.box((w + 0.04, 0.16, 0.05), P(0, -0.02, 0.32), DARK, bevel=0.018, rot=tilt)
    o.box((w * 0.55, 0.09, 0.012), P(-w * 0.13, -0.04, 0.35), SCREEN, bevel=0.004, segs=1, rot=tilt)
    o.sphere(0.022, P(w * 0.3, -0.05, 0.35), LAMP_G, seg=10, rings=6)
    o.sphere(0.022, P(w * 0.3, -0.0, 0.37), LAMP_R, seg=10, rings=6)
    for k, x in enumerate((-0.3, 0.1)):
        o.rod(P(x * w, 0.02, 0.34), P(x * w, -0.02, 0.46 + 0.02 * k), 0.014, CHROME, seg=8)
        o.sphere(0.032, P(x * w, -0.02, 0.46 + 0.02 * k), RED if k == 0 else ORANGE, seg=12, rings=8)

def op_marker(name, cx, cy, z):
    f = Vector((-cx, -cy, 0)).normalized()
    mk = Obj(name); mk.marker((cx + 0.06 * (1 if cx > 0 else -1), cy + 0.06 * (1 if cy > 0 else -1), z), tuple(f)); mk.finish(smooth=False)

# ════════════════════════════ COMPACTEUR ════════════════════════════
# grande presse hydraulique : enclume au centre, 4 colonnes chromées, couronne sarcelle
# et son vérin ; poste de pilotage surélevé dans le coin arrière gauche ; pompe et réservoir
# (arrière droit), armoire électrique (avant gauche), caisse de boulettes (avant droit).
COMP_CROWN = 2.15
def build_comp():
    o = Obj("comp_base")
    big_base(o, TEAL)
    for side, out in (((0, 1), True), ((-1, 0), False), ((1, 0), False), ((0, -1), False)):
        hood(o, side, TEAL, out)
    # enclume
    o.cyl(0.42, 0.22, (0, 0, Z0 + 0.11), IRON, seg=40, bevel=0.045)
    o.cyl(0.36, 0.1, (0, 0, Z0 + 0.27), STEEL, seg=40, bevel=0.03)
    o.torus(0.36, 0.024, (0, 0, Z0 + 0.3), YELLOW, seg=40, rseg=8)
    o.cyl(0.29, 0.02, (0, 0, Z0 + 0.325), DARK, seg=40)
    # colonnes
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * 0.42, sy * 0.42
            o.cyl(0.11, 0.1, (x, y, Z0 + 0.05), DARK, seg=20, bevel=0.025)
            o.cyl(0.075, COMP_CROWN - Z0, (x, y, (COMP_CROWN + Z0) / 2), CHROME, seg=24)
            for z in (0.85, 1.55):
                o.cyl(0.095, 0.08, (x, y, z), TEAL, seg=24, bevel=0.02)
    # couronne
    o.box((1.32, 1.32, 0.4), (0, 0, COMP_CROWN + 0.2), TEAL, bevel=0.09, segs=3)
    o.box((1.36, 1.36, 0.05), (0, 0, COMP_CROWN + 0.42), DARK, bevel=0.02)
    for s in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        stripes_r(o, side_rot(*s), (0, 0.665, COMP_CROWN + 0.2), 1.0, 0.1, 11)
    # vérin
    o.cyl(0.22, 0.45, (0, 0, COMP_CROWN - 0.225), STEEL, seg=36, bevel=0.04)
    o.cyl(0.25, 0.07, (0, 0, COMP_CROWN - 0.04), DARK, seg=36, bevel=0.02)
    o.cyl(0.24, 0.06, (0, 0, COMP_CROWN - 0.43), DARK, seg=36, bevel=0.02)
    # dessus : ventilateurs, gyrophare
    for x in (-0.3, 0.25):
        o.cyl(0.17, 0.08, (x, -0.28, COMP_CROWN + 0.48), DARK, seg=28, bevel=0.02)
        o.cyl(0.13, 0.02, (x, -0.28, COMP_CROWN + 0.53), IRON, seg=28)
        o.box((0.24, 0.04, 0.02), (x, -0.28, COMP_CROWN + 0.545), STEEL, rot=(0, 0, 0.6))
        o.box((0.24, 0.04, 0.02), (x, -0.28, COMP_CROWN + 0.545), STEEL, rot=(0, 0, -0.9))
    o.cyl(0.07, 0.1, (0.42, 0.42, COMP_CROWN + 0.5), DARK, seg=16, bevel=0.02)
    o.sphere(0.08, (0.42, 0.42, COMP_CROWN + 0.6), ORANGE, seg=16, rings=10, scale=(1, 1, 1.2))
    # ── arrière gauche : poste de pilotage
    zd = 1.0
    operator_deck(o, -1, -1, zd)
    Rd = diag_rot(-1, -1)
    panel(o, Rd, (-0.74, -0.74, zd), w=0.36, color=TEAL)
    op_marker("op_comp_spot", -1.0, -1.0, zd)
    # ── arrière droit : réservoir d'huile et pompe
    o.cyl(0.31, 0.85, (1.04, -1.04, Z0 + 0.43), TEAL, seg=36, bevel=0.06)
    o.lathe([(0.0, 0.0), (0.31, 0.0), (0.27, 0.12), (0.16, 0.2), (0.0, 0.22)], (1.04, -1.04, Z0 + 0.85), TEAL, seg=36)
    for z in (0.3, 0.7):
        o.torus(0.315, 0.025, (1.04, -1.04, Z0 + z), DARK, seg=36, rseg=6)
    o.cyl(0.07, 0.03, (1.04, -0.725, Z0 + 0.55), CREAM, seg=20, rot=(math.pi / 2, 0, 0))
    o.torus(0.07, 0.014, (1.04, -0.715, Z0 + 0.55), BRASS, seg=20, rseg=5, rot=(math.pi / 2, 0, 0))
    o.box((0.3, 0.26, 0.26), (0.76, -1.3, Z0 + 0.13), DARK, bevel=0.04)
    o.cyl(0.1, 0.12, (0.76, -1.3, Z0 + 0.3), STEEL, seg=20, bevel=0.02)
    o.tube([(1.0, -1.0, Z0 + 1.02), (1.0, -1.0, 1.55), (0.85, -0.78, 1.95), (0.66, -0.45, COMP_CROWN + 0.15)], 0.05, YELLOW, seg=10)
    o.tube([(1.14, -0.95, Z0 + 1.0), (1.14, -0.92, 1.6), (0.92, -0.62, 2.0), (0.66, -0.25, COMP_CROWN + 0.12)], 0.045, RED, seg=10)
    # ── avant gauche : armoire électrique
    o.box((0.62, 0.48, 1.2), (-1.04, 1.06, Z0 + 0.6), CREAM, bevel=0.06, segs=3)
    o.box((0.02, 0.4, 1.0), (-0.725, 1.06, Z0 + 0.6), DARK, bevel=0.008)
    for k, z in enumerate((0.85, 0.55)):
        o.cyl(0.08, 0.03, (-0.72, 1.0 + k * 0.0, Z0 + z), CREAM, seg=20, rot=(0, math.pi / 2, 0))
        o.torus(0.08, 0.016, (-0.708, 1.0, Z0 + z), BRASS, seg=20, rseg=5, rot=(0, math.pi / 2, 0))
    o.box((0.66, 0.52, 0.05), (-1.04, 1.06, Z0 + 1.23), DARK, bevel=0.02)
    o.cyl(0.06, 0.08, (-1.04, 1.06, Z0 + 1.3), DARK, seg=14)
    o.sphere(0.075, (-1.04, 1.06, Z0 + 1.38), LAMP_A, seg=14, rings=8, scale=(1, 1, 1.2))
    o.tube([(-0.9, 0.82, Z0 + 1.1), (-0.7, 0.62, 1.6), (-0.5, 0.42, COMP_CROWN - 0.1)], 0.035, BLACK, seg=8)
    # ── avant droit : caisse de boulettes
    for sx in (-1, 1):
        o.box((0.06, 0.74, 0.42), (1.04 + sx * 0.34, 1.04, Z0 + 0.21), WOOD, bevel=0.015)
        o.box((0.74, 0.06, 0.42), (1.04, 1.04 + sx * 0.34, Z0 + 0.21), WOODD, bevel=0.015)
    import random
    random.seed(7)
    for k in range(9):
        o.sphere(0.11, (1.04 + random.uniform(-0.2, 0.2), 1.04 + random.uniform(-0.2, 0.2), Z0 + 0.3 + random.uniform(0, 0.12)), CRUMB, seg=14, rings=9)
    o.finish(sharp_deg=50)
    p = Obj("comp_press")
    # tête du piston (origine : dessous du tampon)
    p.box((0.88, 0.88, 0.24), (0, 0, 0.17), IRON, bevel=0.06, segs=3)
    p.cyl(0.34, 0.05, (0, 0, 0.025), CHROME, seg=36, bevel=0.015)
    for s in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        stripes_r(p, side_rot(*s), (0, 0.442, 0.17), 0.7, 0.09, 7)
    p.cyl(0.1, 1.0, (0, 0, 0.29 + 0.5), CHROME, seg=24)
    p.finish()

# ════════════════════════════ FOUR ════════════════════════════
# four à pain rond : tambour de briques cerclé de cuivre, coupole crème, hublot vitré au-dessus
# de la sortie ; chaudière et haute cheminée (arrière droit), poste de la boulangère (arrière
# gauche), échelle à pâtisseries (avant gauche), sacs de farine et pelle (avant droit).
def build_oven():
    o = Obj("oven_base")
    big_base(o, RED)
    H = 1.32
    o.cyl(0.8, H, (0, 0, Z0 + H / 2), BRICK, seg=56, bevel=0.05)
    for z in (0.12, H - 0.1):
        o.torus(0.81, 0.035, (0, 0, Z0 + z), COPPER, seg=56, rseg=8)
    # coupole
    prof = [(0.84, 0.0)] + [(0.84 * math.cos(a), 0.62 * math.sin(a) + 0.0) for a in [i * math.pi / 2 / 10 for i in range(1, 10)]] + [(0.0, 0.62)]
    o.lathe(prof, (0, 0, Z0 + H), CREAM, seg=56, cap_bot=True)
    o.torus(0.845, 0.04, (0, 0, Z0 + H + 0.01), COPPER, seg=56, rseg=8)
    o.cyl(0.15, 0.16, (0, 0, Z0 + H + 0.66), COPPER, seg=24, bevel=0.03)
    o.lathe([(0.0, 0.0), (0.22, 0.0), (0.18, 0.06), (0.0, 0.1)], (0, 0, Z0 + H + 0.74), COPPER, seg=24)
    for side, out in (((0, 1), True), ((-1, 0), False), ((1, 0), False), ((0, -1), False)):
        hood(o, side, RED, out)
    # hublot (au-dessus de la sortie)
    zc = Z0 + 1.2
    o.torus(0.2, 0.045, (0, 0.8, zc), BRASS, seg=40, rseg=10, rot=(math.pi / 2, 0, 0))
    o.cyl(0.2, 0.05, (0, 0.77, zc), IRON, seg=40, rot=(math.pi / 2, 0, 0))
    o.cyl(0.17, 0.012, (0, 0.81, zc), GLASS, seg=40, rot=(math.pi / 2, 0, 0))
    o.rod((-0.12, 0.86, zc - 0.28), (0.12, 0.86, zc - 0.28), 0.02, CHROME, seg=8)
    for x in (-0.12, 0.12): o.rod((x, 0.8, zc - 0.28), (x, 0.86, zc - 0.28), 0.016, CHROME, seg=8)
    # ── arrière droit : chaudière et cheminée
    o.box((0.72, 0.72, 0.78), (1.04, -1.04, Z0 + 0.39), COPPER, bevel=0.1, segs=3)
    for z in (0.18, 0.6): o.box((0.74, 0.74, 0.05), (1.04, -1.04, Z0 + z), BRASS, bevel=0.02)
    o.cyl(0.1, 0.04, (1.04, -0.67, Z0 + 0.4), IRON, seg=20, rot=(math.pi / 2, 0, 0))
    o.cyl(0.07, 0.03, (1.04, -0.645, Z0 + 0.4), FIRE, seg=20, rot=(math.pi / 2, 0, 0))
    o.cyl(0.14, 2.3, (1.1, -1.1, Z0 + 0.78 + 1.15), STEEL, seg=28)
    for z in (1.2, 1.9, 2.6): o.torus(0.145, 0.025, (1.1, -1.1, Z0 + z), DARK, seg=28, rseg=6)
    o.lathe([(0.0, 0.0), (0.26, 0.0), (0.24, 0.05), (0.0, 0.16)], (1.1, -1.1, Z0 + 3.1), DARK, seg=28)
    for k in range(3): o.rod((1.1 + 0.12 * math.cos(k * 2.1), -1.1 + 0.12 * math.sin(k * 2.1), Z0 + 3.06), (1.1 + 0.12 * math.cos(k * 2.1), -1.1 + 0.12 * math.sin(k * 2.1), Z0 + 3.12), 0.018, DARK, seg=6)
    o.tube([(0.82, -0.82, Z0 + 0.5), (0.6, -0.6, Z0 + 0.5)], 0.08, BRASS, seg=14)
    # ── arrière gauche : poste de la boulangère
    zd = 0.55
    operator_deck(o, -1, -1, zd, None, rail=CREAM)
    panel(o, diag_rot(-1, -1), (-0.74, -0.74, zd), w=0.36, color=RED)
    op_marker("op_oven_spot", -1.0, -1.0, zd)
    # ── avant gauche : échelle à pâtisseries
    for sx in (-1, 1):
        for sy in (-1, 1):
            o.box((0.05, 0.05, 1.05), (-1.04 + sx * 0.3, 1.04 + sy * 0.26, Z0 + 0.525), STEEL, bevel=0.012)
    for k, z in enumerate((0.3, 0.62, 0.94)):
        o.box((0.68, 0.6, 0.035), (-1.04, 1.04, Z0 + z), STEEL, bevel=0.012)
        for j, (dx, dy) in enumerate(((-0.16, -0.12), (0.16, 0.1), (-0.12, 0.16))):
            if (j + k) % 3 == 2: continue
            c = (-1.04 + dx, 1.04 + dy, Z0 + z + 0.018)
            o.lathe([(0.0, 0.0), (0.11, 0.0), (0.115, 0.03), (0.11, 0.1), (0.0, 0.1)], c, CREAM, seg=20)
            o.lathe([(0.0, 0.095), (0.11, 0.095), (0.12, 0.11), (0.0, 0.13)], c, PINK, seg=20)
            o.sphere(0.035, (c[0], c[1], c[2] + 0.16), CHERRY, seg=10, rings=6)
    # ── avant droit : sacs de farine et pelle à four
    for k, (dx, dy, dz, a) in enumerate(((-0.12, -0.08, 0.0, 0.2), (0.18, 0.1, 0.0, -0.3), (0.02, 0.0, 0.3, 0.5))):
        c = (1.04 + dx, 1.04 + dy, Z0 + 0.17 + dz)
        o.box((0.42, 0.3, 0.3), c, LINEN, bevel=0.12, segs=4, rot=(0, 0, a))
        o.cyl(0.05, 0.08, (c[0], c[1], c[2] + 0.18), LINEN, seg=12, rot=(0.2, 0, a))
    o.rod((1.38, 0.66, Z0 + 0.05), (1.3, 0.78, Z0 + 1.15), 0.025, WOOD, seg=8)
    o.box((0.3, 0.03, 0.34), (1.39, 0.64, Z0 + 0.2), WOOD, bevel=0.012, rot=(0.0, 0.0, 0.5))
    o.finish(sharp_deg=50)
    g = Obj("oven_glow")
    g.cyl(0.165, 0.01, (0, 0.795, zc), FIRE, seg=40, rot=(math.pi / 2, 0, 0))
    g.finish(smooth=False)

# ════════════════════════════ DÉCORATEUR ════════════════════════════
# plateau tournant au centre ; deux tours roses à l'arrière reliées par un pont, d'où
# s'avance le bras et son chariot (la pince descend poser la cerise) ; cuve de crème et
# bocal à cerises sur les tours ; estrade de la décoratrice (avant gauche) ; table des
# sucres décoratifs et cloche à gâteau (avant droit).
def build_deco():
    o = Obj("deco_base")
    big_base(o, PINK)
    for side, out in (((0, 1), True), ((-1, 0), False), ((1, 0), False), ((0, -1), False)):
        hood(o, side, PINK, out)
    # plateau tournant
    o.cyl(0.44, 0.2, (0, 0, Z0 + 0.1), CREAM, seg=48, bevel=0.045)
    o.torus(0.44, 0.03, (0, 0, Z0 + 0.2), PINK, seg=48, rseg=8)
    o.cyl(0.38, 0.05, (0, 0, Z0 + 0.235), STEEL, seg=48, bevel=0.015)
    o.torus(0.3, 0.018, (0, 0, Z0 + 0.262), DARK, seg=40, rseg=5)
    # tours et pont
    TOP = 2.05
    for sx in (-1, 1):
        x = sx * 1.02
        o.box((0.52, 0.52, TOP - Z0), (x, -1.02, (TOP + Z0) / 2), PINK, bevel=0.07, segs=3)
        for z in (0.65, 1.35):
            o.box((0.55, 0.55, 0.07), (x, -1.02, z), CREAM, bevel=0.025)
        o.box((0.58, 0.58, 0.06), (x, -1.02, TOP + 0.03), DARK, bevel=0.02)
        o.cyl(0.08, 0.03, (x, -0.755, 1.0), CREAM, seg=16, rot=(math.pi / 2, 0, 0))
    o.box((2.6, 0.3, 0.28), (0, -1.02, TOP - 0.12), PINK, bevel=0.07, segs=3)
    o.box((2.62, 0.08, 0.06), (0, -0.86, TOP - 0.12), CREAM, bevel=0.02)
    # bras et chariot
    o.box((0.28, 1.2, 0.24), (0, -0.44, TOP - 0.16), CREAM, bevel=0.06, segs=3)
    o.box((0.3, 1.22, 0.04), (0, -0.44, TOP - 0.03), PINK, bevel=0.015)
    o.box((0.4, 0.4, 0.34), (0, 0, TOP - 0.25), PINK, bevel=0.08, segs=3)
    o.cyl(0.09, 0.08, (0, 0, TOP - 0.46), DARK, seg=20, bevel=0.02)
    # cuve de crème (tour gauche) et tuyau jusqu'au chariot
    o.lathe([(0.0, 0.0), (0.22, 0.0), (0.25, 0.05), (0.25, 0.4), (0.2, 0.48), (0.0, 0.5)], (-1.02, -1.02, TOP + 0.06), CREAM, seg=32)
    o.cyl(0.1, 0.06, (-1.02, -1.02, TOP + 0.58), PINK, seg=20, bevel=0.02)
    o.torus(0.252, 0.022, (-1.02, -1.02, TOP + 0.3), PINK, seg=32, rseg=6)
    o.tube([(-0.85, -0.85, TOP + 0.3), (-0.55, -0.6, TOP + 0.35), (-0.25, -0.2, TOP + 0.2), (-0.12, -0.05, TOP - 0.1)], 0.04, CREAM, seg=10)
    # bocal à cerises (tour droite)
    o.lathe([(0.0, 0.0), (0.2, 0.0), (0.24, 0.06), (0.24, 0.44), (0.2, 0.5), (0.0, 0.5)], (1.02, -1.02, TOP + 0.06), GLASS, seg=32)
    import random
    random.seed(5)
    for k in range(10):
        a = random.random() * 6.28; r = random.random() * 0.13
        o.sphere(0.07, (1.02 + math.cos(a) * r, -1.02 + math.sin(a) * r, TOP + 0.14 + k * 0.03), CHERRY, seg=12, rings=8)
    o.cyl(0.24, 0.06, (1.02, -1.02, TOP + 0.59), PINK, seg=32, bevel=0.02)
    # ── avant gauche : estrade de la décoratrice
    zd = 0.55
    operator_deck(o, -1, 1, zd, None, rail=CREAM)
    panel(o, diag_rot(-1, 1), (-0.74, 0.74, zd), w=0.36, color=PINK)
    op_marker("op_deco_spot", -1.0, 1.0, zd)
    # ── avant droit : table des sucres et cloche
    o.box((0.74, 0.66, 0.08), (1.04, 1.04, Z0 + 0.5), CREAM, bevel=0.03)
    for sx in (-1, 1):
        for sy in (-1, 1):
            o.box((0.06, 0.06, 0.46), (1.04 + sx * 0.3, 1.04 + sy * 0.26, Z0 + 0.23), PINK, bevel=0.015)
    for k, (dx, col) in enumerate(((-0.22, PINK), (-0.02, LEAF), (0.18, YELLOW))):
        c = (1.04 + dx, 0.84, Z0 + 0.54)
        o.lathe([(0.0, 0.0), (0.07, 0.0), (0.075, 0.03), (0.075, 0.16), (0.0, 0.16)], c, GLASS, seg=16)
        o.cyl(0.065, 0.1, (c[0], c[1], c[2] + 0.06), col, seg=16)
        o.cyl(0.078, 0.03, (c[0], c[1], c[2] + 0.18), col, seg=16, bevel=0.01)
    c = (1.1, 1.14, Z0 + 0.54)
    o.cyl(0.26, 0.03, c, STEEL, seg=32, bevel=0.01)
    o.lathe([(0.0, 0.02), (0.16, 0.02), (0.165, 0.08), (0.16, 0.16), (0.0, 0.16)], c, CREAM, seg=24)
    o.sphere(0.04, (c[0], c[1], c[2] + 0.2), CHERRY, seg=10, rings=6)
    o.lathe([(0.24, 0.02), (0.235, 0.18), (0.19, 0.3), (0.1, 0.36), (0.0, 0.37)], c, GLASS, seg=32)
    o.sphere(0.035, (c[0], c[1], c[2] + 0.4), BRASS, seg=10, rings=6)
    o.finish(sharp_deg=50)
    ch = Obj("deco_cherry")
    # pince et cerise ; origine : bas de la cerise
    ch.sphere(0.1, (0, 0, 0.1), CHERRY, seg=20, rings=14)
    ch.tube([(0, 0, 0.19), (0.015, 0, 0.27), (0.045, 0, 0.32)], 0.012, LEAF, seg=6)
    for sx in (-1, 1):
        ch.bar((sx * 0.1, 0, 0.4), (sx * 0.06, 0, 0.15), 0.03, CHROME, h=0.05)
    ch.cyl(0.07, 0.08, (0, 0, 0.42), STEEL, seg=20, bevel=0.02)
    ch.cyl(0.035, 1.2, (0, 0, 0.46 + 0.6), CHROME, seg=14)
    ch.finish()

# ════════════════════════════ ENTREPÔT ════════════════════════════
# grande cuve en douelles de bois cerclée d'acier, ouverte : le tas de miettes y monte avec
# le stock ; vigie de la magasinière (arrière gauche), caisses empilées (arrière droit),
# balance (avant gauche), lampadaire et enseigne (avant droit).
def build_store():
    o = Obj("store_base")
    big_base(o, YELLOW)
    H = 1.15
    prof = [(0.0, 0.0), (0.74, 0.0), (0.8, 0.3), (0.82, 0.6), (0.8, 0.9), (0.76, H), (0.66, H), (0.7, 0.9), (0.72, 0.6), (0.7, 0.3), (0.66, 0.25), (0.0, 0.25)]
    o.lathe(prof, (0, 0, Z0), WOOD, seg=48)
    # douelles (relief) et cercles d'acier
    for k in range(24):
        a = 2 * math.pi * k / 24
        o.bar((0.805 * math.cos(a), 0.805 * math.sin(a), Z0 + 0.32), (0.818 * math.cos(a), 0.818 * math.sin(a), Z0 + 0.88), 0.03, WOODD, h=0.02)
    for z, r in ((0.15, 0.78), (0.6, 0.83), (1.02, 0.795)):
        o.torus(r, 0.03, (0, 0, Z0 + z), STEEL, seg=48, rseg=8)
    o.torus(0.71, 0.04, (0, 0, Z0 + H), STEEL, seg=48, rseg=8)
    # pas d'entrée par tapis (ce sont les ouvrières qui le remplissent) : seulement la sortie
    hood(o, (0, 1), YELLOW, True)
    # les trois autres côtés n'ont pas de tapis : dalles pleines et sacs de miettes
    for cx, cy in ((-1, 0), (1, 0), (0, -1)):
        o.box((0.97, 0.97, 0.1), (cx, cy, 0.05), STEEL, bevel=0.035, segs=3)
        o.box((0.985, 0.985, 0.028), (cx, cy, 0.056), YELLOW, bevel=0.013, segs=2)
        o.box((0.85, 0.85, 0.016), (cx, cy, 0.104), RUBBER, bevel=0.006, segs=1)
    for (x, y, a) in ((-1.15, 0.1, 0.3), (-1.2, -0.3, -0.4), (1.2, 0.25, 0.2), (0.25, -1.2, 1.2)):
        o.box((0.36, 0.26, 0.26), (x, y, Z0 + 0.14), LINEN, bevel=0.1, segs=4, rot=(0, 0, a))
        o.cyl(0.045, 0.07, (x, y, Z0 + 0.3), LINEN, seg=12)
    # ── arrière gauche : vigie
    zd = 1.2
    operator_deck(o, -1, -1, zd)
    o.box((0.3, 0.12, 0.3), (-0.82, -0.82, zd + 0.15), WOODD, bevel=0.03, rot=(0, 0, -math.pi / 4))
    op_marker("op_store_spot", -1.0, -1.0, zd)
    # ── arrière droit : caisses empilées
    for (dx, dy, z, s, a) in ((-0.05, 0.0, 0.0, 0.62, 0.1), (0.05, -0.02, 0.62, 0.46, -0.2)):
        c = (1.04 + dx, -1.04 + dy, Z0 + z + s / 2)
        o.box((s, s, s), c, WOOD, bevel=0.03, segs=2, rot=(0, 0, a))
        for sz in (-1, 1):
            o.box((s + 0.02, s + 0.02, 0.06), (c[0], c[1], c[2] + sz * (s / 2 - 0.06)), WOODD, bevel=0.015, rot=(0, 0, a))
    # ── avant gauche : balance
    c = (-1.04, 1.04)
    o.box((0.5, 0.4, 0.1), (c[0], c[1], Z0 + 0.05), DARK, bevel=0.03)
    o.cyl(0.05, 0.7, (c[0] - 0.12, c[1], Z0 + 0.45), STEEL, seg=14)
    o.cyl(0.16, 0.05, (c[0] - 0.12, c[1] - 0.03, Z0 + 0.86), CREAM, seg=28, rot=(math.pi / 2, 0, 0))
    o.torus(0.16, 0.025, (c[0] - 0.12, c[1] - 0.06, Z0 + 0.86), RED, seg=28, rseg=6, rot=(math.pi / 2, 0, 0))
    o.bar((c[0] - 0.12, c[1] - 0.085, Z0 + 0.86), (c[0] - 0.04, c[1] - 0.085, Z0 + 0.95), 0.02, BLACK, h=0.01)
    o.lathe([(0.0, 0.0), (0.2, 0.0), (0.24, 0.06), (0.0, 0.06)], (c[0] + 0.12, c[1], Z0 + 0.16), STEEL, seg=28)
    o.lathe([(0.0, 0.0), (0.17, 0.0), (0.1, 0.07), (0.0, 0.1)], (c[0] + 0.12, c[1], Z0 + 0.22), CRUMB, seg=20)
    # ── avant droit : lampadaire et enseigne
    c = (1.18, 1.18)
    o.cyl(0.09, 0.08, (c[0], c[1], Z0 + 0.04), DARK, seg=18, bevel=0.02)
    o.cyl(0.04, 1.6, (c[0], c[1], Z0 + 0.8), DARK, seg=14)
    o.rod((c[0], c[1], Z0 + 1.55), (c[0] - 0.35, c[1] - 0.35, Z0 + 1.55), 0.03, DARK, seg=8)
    o.lathe([(0.0, 0.0), (0.12, 0.0), (0.08, 0.1), (0.0, 0.12)], (c[0] - 0.38, c[1] - 0.38, Z0 + 1.42), DARK, seg=18)
    o.sphere(0.07, (c[0] - 0.38, c[1] - 0.38, Z0 + 1.42), BULB, seg=14, rings=8)
    o.box((0.5, 0.06, 0.3), (c[0] - 0.12, c[1] - 0.12, Z0 + 1.05), CREAM, bevel=0.04, segs=2, rot=(0, 0, -math.pi / 4))
    o.box((0.54, 0.07, 0.04), (c[0] - 0.12, c[1] - 0.12, Z0 + 1.22), WOODD, bevel=0.015, rot=(0, 0, -math.pi / 4))
    o.finish(sharp_deg=50)
    p = Obj("store_pile")
    import random
    random.seed(3)
    prof = [(0.0, 0.48), (0.14, 0.46), (0.32, 0.36), (0.5, 0.2), (0.62, 0.08), (0.66, 0.0)]
    p.lathe(prof, (0, 0, 0), CRUMB, seg=36, cap_bot=True)
    for k in range(30):
        a = random.random() * 6.28; r = random.random() * 0.55
        z = 0.48 * max(0.0, 1 - (r / 0.66) ** 1.4)
        p.sphere(0.035 + random.random() * 0.035, (math.cos(a) * r, math.sin(a) * r, z), CRUMB, seg=8, rings=5)
    p.finish()

# ════════════════════════════ ROUE ════════════════════════════
# générateur 2 × 2 : roue de course TRANSPARENTE (bande de verre, jantes néon cyan qui
# s'allument), sur chevalets bleu nuit liserés de jaune électrique ; bobine de cuivre et
# globe à plasma (côté +X, départ des câbles), batteries témoins, panneau « haute tension ».
WHEEL_AXLE = 1.0
WHEEL_R = 0.82
NAVY = mat("Navy", "#2c3e7a", 0.4)
NEON = mat("Neon", "#7ff3ff", 0.2, emit="#4fe6ff", strength=3.0)
VOLT = mat("Volt", "#ffd21f", 0.3)
def build_wheel():
    o = Obj("wheel_base")
    A = WHEEL_AXLE
    # socle bleu nuit, liseré jaune électrique, isolateurs en céramique aux angles
    o.box((1.94, 1.94, 0.12), (0, 0, 0.06), NAVY, bevel=0.045, segs=3)
    o.box((1.955, 1.955, 0.03), (0, 0, 0.07), VOLT, bevel=0.014, segs=2)
    o.box((1.78, 1.78, 0.016), (0, 0, 0.125), RUBBER, bevel=0.006, segs=1)
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * 0.84, sy * 0.84
            for k in range(3):
                o.cyl(0.075 - 0.008 * k, 0.05, (x, y, 0.16 + k * 0.07), mat("Faience", "#f4f1ea", 0.25), seg=18, bevel=0.018)
            o.cyl(0.035, 0.06, (x, y, 0.36), COPPER, seg=12, bevel=0.01)
    # chevalets en A (bleu nuit, liserés jaunes)
    for sx in (-1, 1):
        x = sx * 0.56
        for sy in (-1, 1):
            o.bar((x, sy * 0.62, 0.13), (x, sy * 0.06, A), 0.11, NAVY, h=0.09, bevel=0.025)
            o.bar((x + sx * 0.05, sy * 0.6, 0.15), (x + sx * 0.05, sy * 0.08, A - 0.05), 0.03, VOLT, h=0.02)
            o.box((0.18, 0.22, 0.07), (x, sy * 0.62, 0.16), DARK, bevel=0.02)
        o.bar((x, -0.42, 0.48), (x, 0.42, 0.48), 0.08, NAVY, h=0.07, bevel=0.02)
        o.cyl(0.13, 0.1, (x, 0, A), CHROME, seg=28, bevel=0.03, rot=(0, math.pi / 2, 0))
    # bobine de cuivre (génératrice, côté +X) et carter
    o.cyl(0.2, 0.06, (0.64, 0, A), NAVY, seg=32, bevel=0.02, rot=(0, math.pi / 2, 0))
    for k in range(7):
        o.torus(0.17, 0.025, (0.69 + k * 0.032, 0, A), COPPER, seg=32, rseg=6, rot=(0, math.pi / 2, 0))
    o.cyl(0.2, 0.06, (0.92, 0, A), NAVY, seg=32, bevel=0.02, rot=(0, math.pi / 2, 0))
    o.box((0.34, 0.4, 0.62), (0.79, 0, A - 0.52), NAVY, bevel=0.06, segs=2)
    o.box((0.36, 0.42, 0.04), (0.79, 0, A - 0.2), VOLT, bevel=0.015)
    # panneau « haute tension » (triangle jaune, éclair) sur le carter, face avant
    tri = [(-0.13, -0.09), (0.13, -0.09), (0.0, 0.13)]
    o.prism(tri, -0.012, 0.012, VOLT, axis='Y', at=(0.79, 0.215, A - 0.5))
    bolt = [(-0.02, 0.09), (0.04, 0.09), (0.0, 0.015), (0.045, 0.015), (-0.03, -0.075), (0.0, -0.005), (-0.045, -0.005)]
    o.prism(bolt, -0.006, 0.006, BLACK, axis='Y', at=(0.79, 0.232, A - 0.5))
    # globe à plasma : socle laiton, verre (la sphère lumineuse est « wheel_bulb »)
    o.cyl(0.08, 0.1, (0.79, 0, A + 0.25), BRASS, seg=20, bevel=0.02)
    o.lathe([(0.0, 0.0), (0.12, 0.0), (0.15, 0.08), (0.16, 0.2), (0.12, 0.3), (0.0, 0.33)], (0.79, 0, A + 0.3), GLASS, seg=28)
    # batteries témoins (arrière gauche) reliées à la bobine
    for k in range(3):
        x = -0.55 + k * 0.2
        o.cyl(0.08, 0.32, (x, -0.8, 0.29), NAVY, seg=20, bevel=0.02)
        o.cyl(0.03, 0.04, (x, -0.8, 0.47), COPPER, seg=10)
        for j in range(3):
            o.box((0.06, 0.02, 0.05), (x, -0.715, 0.2 + j * 0.07), LAMP_G, bevel=0.006, segs=1)
    o.tube([(-0.15, -0.8, 0.4), (0.2, -0.8, 0.42), (0.6, -0.55, 0.5), (0.79, -0.2, 0.55)], 0.03, BLACK, seg=8)
    o.finish(sharp_deg=50)
    b = Obj("wheel_bulb")
    # cœur du globe à plasma (clignote quand la roue tourne)
    b.sphere(0.07, (0.79, 0, A + 0.45), NEON, seg=18, rings=12)
    b.finish()
    r = Obj("wheel_rotor")
    # rotor centré sur l'axe (le jeu le place au pivot et le fait tourner autour de X)
    R = WHEEL_R
    for sx in (-1, 1):
        r.torus(R, 0.045, (sx * 0.44, 0, 0), NEON, seg=64, rseg=10, rot=(0, math.pi / 2, 0))
        r.torus(R + 0.06, 0.02, (sx * 0.44, 0, 0), NAVY, seg=64, rseg=6, rot=(0, math.pi / 2, 0))
        for k in range(4):
            a = k * math.pi / 2 + math.pi / 4
            r.rod((sx * 0.44, math.cos(a) * 0.1, math.sin(a) * 0.1), (sx * 0.44, math.cos(a) * (R - 0.04), math.sin(a) * (R - 0.04)), 0.032, NAVY, seg=8)
        r.cyl(0.12, 0.08, (sx * 0.44, 0, 0), VOLT, seg=24, bevel=0.02, rot=(0, math.pi / 2, 0))
    r.cyl(0.05, 1.04, (0, 0, 0), CHROME, seg=16, rot=(0, math.pi / 2, 0))
    # piste de course en verre (on voit la coureuse au travers) et quelques barreaux discrets
    r.cyl(R - 0.01, 0.86, (0, 0, 0), GLASS, seg=64, caps=False, rot=(0, math.pi / 2, 0))
    for k in range(12):
        a = 2 * math.pi * k / 12
        y, z = math.cos(a) * (R - 0.02), math.sin(a) * (R - 0.02)
        r.rod((-0.43, y, z), (0.43, y, z), 0.018, CHROME, seg=8)
    r.finish()
    mk = Obj("op_wheel_spot"); mk.marker((0, 0, A - R + 0.03), (0, -1, 0)); mk.finish(smooth=False)

# ════════════════════════════ GRUE ════════════════════════════
# tour en treillis jaune sur lest en béton ; tête pivotante : couronne, cabine vitrée
# (siège + pupitre : la grutière y est assise), contre-flèche lestée, mât en A ;
# flèche en treillis triangulaire (le jeu l'étire selon la portée) avec son tirant.
MAST_TOP = 12.9
JIB_Z = 13.75
JIB_BAY = 0.9          # longueur d'un tronçon de flèche (BuildingsView.JibBay)
JIB_ROOT = 0.3         # le premier tronçon part d'ici (contre le porte-flèche)
def lattice(o, x0, x1, y0, y1, z0, z1, step, m=YELLOW, chord=0.12, diag=0.075):
    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    for x, y in corners:
        o.box((chord, chord, z1 - z0), (x, y, (z0 + z1) / 2), m, bevel=0.01, segs=1)
    z = z0; k = 0
    while z < z1 - 1e-3:
        z2 = min(z + step, z1)
        for i in range(4):
            a = corners[i]; b = corners[(i + 1) % 4]
            if k % 2 == 0:
                o.bar((a[0], a[1], z), (b[0], b[1], z2), diag, m)
            else:
                o.bar((b[0], b[1], z), (a[0], a[1], z2), diag, m)
            o.bar((a[0], a[1], z2), (b[0], b[1], z2), diag, m)
        z = z2; k += 1

def build_crane():
    o = Obj("crane_base")
    # 2 × 2 : mât au centre de la case arrière droite (MX, MY) ; la case avant gauche porte le
    # tapis de sortie (posé par le jeu), avec la trémie où la fourmi suspendue lâche les miettes
    MX, MY = 0.5, -0.5
    for cx, cy in ((0.5, -0.5), (-0.5, -0.5), (0.5, 0.5)):
        o.box((0.97, 0.97, 0.1), (cx, cy, 0.05), CONC, bevel=0.035, segs=2)
        o.box((0.985, 0.985, 0.028), (cx, cy, 0.056), YELLOW, bevel=0.013, segs=2)
    # lest : blocs de béton empilés (arrière gauche, avant droite)
    for cx, cy in ((-0.5, -0.5), (0.5, 0.5)):
        for k in range(2):
            o.box((0.8 - 0.12 * k, 0.8 - 0.12 * k, 0.24), (cx, cy, 0.22 + 0.25 * k), CONC, bevel=0.04, segs=2)
        stripes_r(o, side_rot(0, 1), (cx, cy + 0.402, 0.22), 0.7, 0.08, 7) if cy > 0 else None
    # pied du mât
    o.box((0.8, 0.8, 0.22), (MX, MY, 0.21), CONC, bevel=0.04, segs=2)
    o.box((0.62, 0.62, 0.1), (MX, MY, 0.37), DARK, bevel=0.025)
    for sx in (-1, 1):
        for sy in (-1, 1):
            o.cyl(0.035, 0.05, (MX + sx * 0.25, MY + sy * 0.25, 0.44), BRASS, seg=10)
    lattice(o, MX - 0.22, MX + 0.22, MY - 0.22, MY + 0.22, 0.42, MAST_TOP, 0.88)
    o.box((0.62, 0.62, 0.06), (MX, MY, MAST_TOP + 0.03), DARK, bevel=0.02)
    # trémie au départ du tapis de sortie (case avant gauche : x ∈ [−1, 0], y ∈ [0, 1])
    hx, hy = -0.5, 0.18
    for sx in (-1, 1):
        o.box((0.07, 0.07, 0.95), (hx + sx * 0.535, hy, 0.475), STEEL, bevel=0.015)
    o.bar((hx - 0.535, hy, 0.95), (hx + 0.535, hy, 0.95), 0.07, STEEL, bevel=0.015)
    o.lathe([(0.12, 0.52), (0.16, 0.56), (0.42, 0.95), (0.46, 0.99), (0.42, 0.99), (0.13, 0.6)], (hx, hy, 0), YELLOW, seg=28)
    o.torus(0.44, 0.03, (hx, hy, 0.99), DARK, seg=28, rseg=6)
    stripes_r(o, side_rot(0, 1), (hx, hy + 0.37, 0.8), 0.5, 0.07, 5)
    o.finish(sharp_deg=40)
    t = Obj("crane_top")
    zt = MAST_TOP + 0.06
    # couronne d'orientation
    t.cyl(0.36, 0.1, (0, 0, zt + 0.05), STEEL, seg=36, bevel=0.02)
    t.torus(0.36, 0.02, (0, 0, zt + 0.05), YELLOW, seg=36, rseg=6)
    t.box((0.62, 0.9, 0.2), (0, 0.05, zt + 0.2), YELLOW, bevel=0.04, segs=2)
    # porte-flèche : du plateau pivotant jusqu'aux pieds de la flèche et de la contre-flèche
    zh0, zh1 = zt + 0.28, JIB_Z + 0.14
    t.box((0.5, 0.56, zh1 - zh0), (0, 0.04, (zh0 + zh1) / 2), YELLOW, bevel=0.04, segs=2)
    for sx in (-1, 1):
        t.box((0.012, 0.36, (zh1 - zh0) * 0.6), (sx * 0.252, 0.04, (zh0 + zh1) / 2), DARK, bevel=0.004, segs=1)
    t.box((0.56, 0.62, 0.05), (0, 0.04, zh1 + 0.02), DARK, bevel=0.015)
    # mât en A (porte-tirants)
    apex = Vector((0, 0.1, JIB_Z + 1.5))
    for sx in (-1, 1):
        for sy in (-0.25, 0.35):
            t.bar((sx * 0.22, sy * 0.85, JIB_Z + 0.16), apex, 0.07, YELLOW, bevel=0.012)
    t.sphere(0.06, tuple(apex), DARK, seg=12, rings=8)
    t.sphere(0.04, tuple(apex + Vector((0, 0, 0.08))), LAMP_R, seg=10, rings=6)
    # contre-flèche (−Y) : treillis plat, plancher, lest, treuil
    for sx in (-1, 1):
        t.box((0.06, 2.3, 0.1), (sx * 0.22, -1.3, JIB_Z + 0.05), YELLOW, bevel=0.015)
    for k in range(4):
        y = -0.35 - k * 0.6
        t.bar((-0.22, y, JIB_Z + 0.05), (0.22, y - 0.3, JIB_Z + 0.05), 0.07, YELLOW)
    t.box((0.5, 1.3, 0.025), (0, -1.0, JIB_Z + 0.11), DARK)
    for k in range(3):
        t.box((0.56, 0.22, 0.42), (0, -2.0 - k * 0.0 + 0.24 * (k - 1), JIB_Z - 0.12), CONC, bevel=0.03)
    t.box((0.4, 0.4, 0.3), (0, -0.9, JIB_Z + 0.27), YELLOW, bevel=0.04, segs=2)
    t.cyl(0.12, 0.42, (0, -0.55, JIB_Z + 0.25), STEEL, seg=24, bevel=0.02, rot=(0, math.pi / 2, 0))
    for sx in (-1, 1):
        t.cyl(0.15, 0.03, (sx * 0.2, -0.55, JIB_Z + 0.25), DARK, seg=24, rot=(0, math.pi / 2, 0))
    # tirants vers la contre-flèche
    for sx in (-1, 1):
        t.rod(tuple(apex), (sx * 0.22, -2.4, JIB_Z + 0.1), 0.028, STEEL, seg=8)
    for sx in (-1, 1):
        run(t, (sx * 0.25, -0.35), (sx * 0.25, -1.65), JIB_Z + 0.12, h=0.28)
    # cabine vitrée (à droite, à l'avant de la couronne)
    cx, cy, cz = 0.58, 0.28, zt + 0.12
    W, D, Hh = 0.46, 0.56, 0.62
    t.box((W, D, 0.08), (cx, cy, cz + 0.04), YELLOW, bevel=0.03, segs=2)          # plancher
    t.box((W, D, 0.07), (cx, cy, cz + Hh - 0.035), YELLOW, bevel=0.03, segs=2)    # toit
    t.box((W + 0.04, D + 0.04, 0.025), (cx, cy, cz + Hh + 0.01), DARK, bevel=0.01)
    for sx in (-1, 1):
        for sy in (-1, 1):
            t.box((0.04, 0.04, Hh - 0.1), (cx + sx * (W / 2 - 0.02), cy + sy * (D / 2 - 0.02), cz + Hh / 2), YELLOW, bevel=0.012)
    # paroi arrière pleine, vitres sur 3 côtés + vitre de plancher avant
    t.box((W - 0.06, 0.03, Hh - 0.12), (cx, cy - D / 2 + 0.02, cz + Hh / 2), YELLOW, bevel=0.01)
    t.box((W - 0.06, 0.012, Hh - 0.16), (cx, cy + D / 2 - 0.02, cz + Hh / 2 + 0.02), GLASS)
    for sx in (-1, 1):
        t.box((0.012, D - 0.06, Hh - 0.16), (cx + sx * (W / 2 - 0.02), cy, cz + Hh / 2 + 0.02), GLASS)
    # siège et pupitre de la grutière
    t.box((0.2, 0.18, 0.05), (cx, cy - 0.12, cz + 0.2), RED, bevel=0.02)
    t.box((0.2, 0.05, 0.2), (cx, cy - 0.2, cz + 0.3), RED, bevel=0.02)
    t.cyl(0.03, 0.12, (cx, cy - 0.12, cz + 0.13), DARK, seg=10)
    console(t, (cx, cy + 0.17, cz + 0.08), 0.0, w=0.3)
    # bras de liaison cabine ↔ couronne
    t.box((0.2, 0.3, 0.08), (0.34, cy, cz + 0.06), DARK, bevel=0.02)
    t.finish(sharp_deg=40)
    mk = Obj("op_crane_spot"); mk.marker((cx, cy - 0.12, cz + 0.225), (0, 1, 0)); mk.finish(smooth=False)
    # flèche : tronçons identiques mis bout à bout par le jeu selon la portée (jamais étirés),
    # le premier collé au porte-flèche ; les tirants sont tracés par le jeu
    JB = JIB_BAY
    zb, ztop = JIB_Z, JIB_Z + 0.42
    j = Obj("crane_jib_bay")
    for sx in (-1, 1):
        j.box((0.1, JB + 0.01, 0.1), (sx * 0.18, JB / 2, zb), YELLOW, bevel=0.015, segs=1)
    j.box((0.11, JB + 0.01, 0.11), (0, JB / 2, ztop), YELLOW, bevel=0.018, segs=1)
    for sx in (-1, 1):
        j.bar((sx * 0.18, 0.0, zb), (0, JB / 2, ztop), 0.07, YELLOW)
        j.bar((0, JB / 2, ztop), (sx * 0.18, JB, zb), 0.07, YELLOW)
    j.bar((-0.18, JB, zb), (0.18, JB, zb), 0.065, YELLOW)
    j.box((0.3, JB - 0.12, 0.02), (0, JB / 2, zb + 0.04), DARK)
    j.finish(sharp_deg=40)
    jt = Obj("crane_jib_tip")
    jt.box((0.46, 0.14, 0.56), (0, 0.07, (zb + ztop) / 2), YELLOW, bevel=0.04, segs=2)
    jt.sphere(0.05, (0, 0.1, ztop + 0.3), LAMP_R, seg=12, rings=8)
    jt.cyl(0.03, 0.2, (0, 0.1, ztop + 0.18), DARK, seg=10)
    jt.finish()
    tr = Obj("crane_trolley")
    tr.box((0.3, 0.3, 0.1), (0, 0, JIB_Z - 0.12), DARK, bevel=0.025)
    for sx in (-1, 1):
        for sy in (-1, 1):
            tr.cyl(0.035, 0.025, (sx * 0.17, sy * 0.09, JIB_Z - 0.03), STEEL, seg=12, rot=(0, math.pi / 2, 0))
    tr.cyl(0.07, 0.04, (0, 0, JIB_Z - 0.2), YELLOW, seg=20, rot=(0, math.pi / 2, 0))
    tr.finish()

# ════════════════════════════ CASQUE ════════════════════════════
# dans le repère de la tête (Godot : x, y haut, z avant  →  Blender : x, −z… : avant = −Y)
def build_hat():
    h = Obj("hardhat")
    c = (0, -0.11, 0.115)
    h.lathe([(0.0, 0.17), (0.07, 0.165), (0.13, 0.13), (0.165, 0.07), (0.18, 0.0), (0.175, -0.005), (0.0, -0.005)], c, YELLOW, seg=32, cap_top=False)
    # rebord moulé tout autour, élargi en visière à l'avant (−Y)
    bm = bmesh.new()
    n = 48
    inner, outer = [], []
    for i in range(n):
        a = 2 * math.pi * i / n
        fr = max(0.0, -math.sin(a))            # 1 tout à l'avant (−Y), 0 sur les côtés et l'arrière
        fr = fr ** 1.6
        r1 = 0.172; r2 = 0.205 + 0.09 * fr
        z2 = -0.012 - 0.035 * fr
        inner.append(bm.verts.new((math.cos(a) * r1, math.sin(a) * r1, 0.004)))
        outer.append(bm.verts.new((math.cos(a) * r2, math.sin(a) * r2, z2)))
    lo_i = [bm.verts.new((v.co.x, v.co.y, v.co.z - 0.014)) for v in inner]
    lo_o = [bm.verts.new((v.co.x, v.co.y, v.co.z - 0.014)) for v in outer]
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((inner[i], outer[i], outer[j], inner[j]))
        bm.faces.new((lo_i[j], lo_o[j], lo_o[i], lo_i[i]))
        bm.faces.new((outer[i], lo_o[i], lo_o[j], outer[j]))
        bm.faces.new((inner[j], lo_i[j], lo_i[i], inner[i]))
    h.merge(bm, YELLOW, Matrix.Translation(Vector(c)))
    # nervure centrale et bande noire
    h.box((0.03, 0.3, 0.03), (c[0], c[1] + 0.0, c[2] + 0.16), YELLOW, bevel=0.012, rot=(0, 0, 0))
    h.torus(0.172, 0.01, (c[0], c[1], c[2] + 0.035), DARK, seg=32, rseg=4)
    h.finish(sharp_deg=60)

# ════════════════════════════ PANNEAU DES GÂTEAUX ════════════════════════════
# ardoise de pâtissier dans un cadre en bois cintré, bannière rouge pour le titre,
# plaquette suspendue pour l'état. Face avant = −Y (Godot +Z), le texte est posé par le jeu.
def build_sign():
    s = Obj("cake_sign")
    W, Hb, z0 = 3.0, 1.55, 1.25
    # poteaux tournés + fleurons
    for sx in (-1, 1):
        x = sx * (W / 2 + 0.12)
        s.lathe([(0.0, 0.0), (0.12, 0.0), (0.12, 0.08), (0.09, 0.12), (0.085, 3.0), (0.11, 3.04), (0.11, 3.1), (0.0, 3.1)], (x, 0, 0), WOODD, seg=16)
        s.sphere(0.12, (x, 0, 3.2), GOLD, seg=16, rings=10)
        for z in (0.5, 0.56): s.torus(0.09, 0.012, (x, 0, z), GOLD, seg=16, rseg=4)
    # contour du cadre : rectangle à fronton cintré
    def outline(w, h, arch, n=24):
        pts = [(-w / 2, 0.0), (w / 2, 0.0), (w / 2, h)]
        for i in range(1, n):
            t = i / n
            x = w / 2 - w * t
            pts.append((x, h + arch * math.sin(math.pi * t)))
        pts.append((-w / 2, h))
        return pts
    # cadre (épaisseur 0,16), ardoise en retrait des deux côtés
    s.prism(outline(W, Hb, 0.3), -0.08, 0.08, WOODD, bevel=0.03, axis='Y', at=(0, 0, z0))
    s.prism(outline(W - 0.22, Hb - 0.22, 0.24), -0.095, 0.095, CHALK, bevel=0.01, axis='Y', at=(0, 0, z0 + 0.11))
    # filets à la craie (cadre intérieur décoratif) des deux côtés
    for side in (-1, 1):
        y = side * 0.098
        pts = outline(W - 0.42, Hb - 0.42, 0.2)
        P = [(x, y, z0 + 0.21 + zz) for x, zz in pts] + [(pts[0][0], y, z0 + 0.21 + pts[0][1])]
        s.tube(P, 0.008, CHALKLINE, seg=4, caps=False)
        # petites étoiles aux coins
        for cx in (-1, 1):
            s.sphere(0.03, (cx * (W / 2 - 0.32), y, z0 + 0.32), CHALKLINE, seg=8, rings=4, scale=(1, 0.3, 1))
    # traverse et tasseaux
    s.box((W + 0.4, 0.14, 0.1), (0, 0, z0 - 0.06), WOODD, bevel=0.03)
    # bannière rouge en haut (titre) avec queues fourchues
    zb = z0 + Hb + 0.16
    s.box((1.5, 0.22, 0.34), (0, 0, zb), RIBBON, bevel=0.05, segs=2)
    for sx in (-1, 1):
        tail = [(0.0, 0.17), (0.42, 0.17), (0.3, 0.0), (0.42, -0.17), (0.0, -0.17)]
        tail = [(sx * (0.7 + x), z) for x, z in tail]
        if sx < 0: tail = list(reversed(tail))
        s.prism(tail, -0.08, 0.08, RIBBON, bevel=0.015, axis='Y', at=(0, 0.04, zb - 0.06))
    for sz in (-1, 1):
        s.box((1.52, 0.235, 0.022), (0, 0, zb + sz * 0.13), GOLD, bevel=0.008)
    # gâteau miniature au sommet du fronton
    zt = z0 + Hb + 0.16 + 0.17 - 0.01
    s.lathe([(0.0, 0.0), (0.26, 0.0), (0.27, 0.03), (0.27, 0.16), (0.25, 0.2), (0.0, 0.2)], (0, 0, zt), CREAM, seg=24)
    s.lathe([(0.0, 0.19), (0.24, 0.19), (0.27, 0.21), (0.0, 0.23)], (0, 0, zt), PINK, seg=24)
    s.sphere(0.07, (0, 0, zt + 0.3), CHERRY, seg=14, rings=10)
    s.tube([(0, 0, zt + 0.36), (0.03, 0, zt + 0.44), (0.08, 0, zt + 0.48)], 0.01, LEAF, seg=5)
    # plaquette d'état suspendue sous la traverse (deux chaînettes)
    zp = z0 - 0.62
    s.box((1.9, 0.08, 0.34), (0, 0, zp), CREAM, bevel=0.04, segs=3)
    s.box((1.96, 0.1, 0.04), (0, 0, zp + 0.18), WOODD, bevel=0.015)
    s.box((1.96, 0.1, 0.04), (0, 0, zp - 0.18), WOODD, bevel=0.015)
    for sx in (-1, 1):
        x = sx * 0.8
        for k in range(4):
            z = zp + 0.24 + k * 0.1
            s.torus(0.03, 0.007, (x, 0, z), GOLD, seg=10, rseg=4, rot=((k % 2) * math.pi / 2, 0, math.pi / 2))
    s.finish(sharp_deg=40)

# ───── lancement ─────
if want("comp"): build_comp()
if want("oven"): build_oven()
if want("deco"): build_deco()
if want("store"): build_store()
if want("wheel"): build_wheel()
if want("crane"): build_crane()
if want("hat"): build_hat()
if want("sign"): build_sign()

print("objets :", sorted(o.name for o in SCENE.objects))
print("triangles :", {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in SCENE.objects if o.type == 'MESH'})

if OUT:
    vl = SCENE.view_layers[0]
    win = bpy.context.window
    if win: win.scene = SCENE
    for o in list(vl.objects):
        if o is not None: o.select_set(False, view_layer=vl)
    for o in SCENE.objects: o.select_set(True, view_layer=vl)
    with bpy.context.temp_override(scene=SCENE, view_layer=vl):
        bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', use_selection=True, use_active_scene=True,
                                  export_apply=True, export_yup=True, export_materials='EXPORT')
    print("exporté :", OUT)
