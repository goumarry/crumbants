# Décor du pique-nique, deuxième version (Models/props.glb).
# 1 unité = 2 mm. Chaque objet est centré sur son origine, posé au sol (z = 0),
# avec le nom attendu par Scripts/Render/PropsView.cs. Les noms de matériaux
# pilotent les surfaces procédurales du jeu (Scripts/Render/MatLib.cs).
# Lancer dans Blender (scène dédiée) : exec(open(r"...props2.py").read())
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, noise

random.seed(11)
SCENE = bpy.context.scene
OUT = globals().get("OUT_PATH")

def srgb(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

_M = {}
def mat(name, hexcol, rough=0.5, metal=0.0, alpha=1.0, emit=None):
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
        p.inputs['Emission Strength'].default_value = 2.0
    _M[name] = m
    return m

# ───── outils ─────
def link(me, name):
    o = bpy.data.objects.new(name, me); SCENE.collection.objects.link(o); return o

def lathe(name, prof, m, seg=64, cap_top=False, cap_bot=False, smooth=True, bands=None):
    """prof : liste (r, z) de bas en haut ; surface de révolution autour de Z.
    bands : [(z0, z1, rmin, rmax, matériau)] : zones peintes DANS la surface (filets,
    étiquettes) — affleurantes, sans bande rapportée qui flotte ou fait rainure."""
    if bands:
        # on coupe le profil exactement aux bords des zones
        cuts = sorted({z for b in bands for z in (b[0], b[1])})
        out = [prof[0]]
        for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
            lo, hi = min(z0, z1), max(z0, z1)
            mids = [z for z in cuts if lo < z < hi]
            mids.sort(reverse=z1 < z0)
            for z in mids:
                t = (z - z0) / (z1 - z0); out.append((r0 + (r1 - r0) * t, z))
            out.append((r1, z1))
        prof = out
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        ring = [bm.verts.new((math.cos(2 * math.pi * i / seg) * r, math.sin(2 * math.pi * i / seg) * r, z)) for i in range(seg)]
        rings.append(ring)
    mats = [m] + [b[4] for b in (bands or [])]
    for k, (a, b) in enumerate(zip(rings, rings[1:])):
        (ra, za), (rb, zb) = prof[k], prof[k + 1]
        zc, rc = (za + zb) / 2, (ra + rb) / 2
        mi = 0
        for j, bd in enumerate(bands or []):
            if bd[0] <= zc <= bd[1] and bd[2] <= rc <= bd[3]: mi = j + 1
        for i in range(seg):
            j2 = (i + 1) % seg
            f = bm.faces.new((a[i], a[j2], b[j2], b[i])); f.material_index = mi
    if cap_bot: bm.faces.new(list(reversed(rings[0])))
    if cap_top: bm.faces.new(rings[-1])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = link(me, name)
    for mm in mats: o.data.materials.append(mm)
    if smooth:
        for p in me.polygons: p.use_smooth = True
    return o

def prim(kind, m, **kw):
    getattr(bpy.ops.mesh, 'primitive_' + kind + '_add')(**kw)
    o = bpy.context.object
    o.data.materials.clear(); o.data.materials.append(m)
    return o

def smooth(o, on=True):
    for p in o.data.polygons: p.use_smooth = on
    return o

def mod(o, kind, **kw):
    if not o.modifiers and (o.scale - Vector((1, 1, 1))).length > 1e-6:
        # on fige l'échelle avant tout modificateur (sinon biseaux et épaisseurs sont faux)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    md = o.modifiers.new(kind.lower(), kind)
    for k, v in kw.items(): setattr(md, k, v)
    return md

def apply(o):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
    bpy.context.view_layer.objects.active = o
    for md in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return o

def join(name, objs):
    objs = [o for o in objs if o]
    for o in objs: apply(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.object; o.name = name; o.data.name = name
    return o

def ground(o):
    z = min((o.matrix_world @ v.co).z for v in o.data.vertices)
    o.location.z -= z; apply(o); return o

def jitter(o, amp, freq):
    for v in o.data.vertices:
        v.co += Vector((noise.noise(v.co * freq), noise.noise(v.co * freq + Vector((7, 3, 1))), noise.noise(v.co * freq + Vector((2, 9, 5))))) * amp

# ───── matériaux ─────
WICKER = mat("Wicker", "#c38846", 0.75); WICKERD = mat("WickerDark", "#986533", 0.8)
TOWR = mat("TowelRed", "#c62c2e", 0.85); TOWW = mat("TowelWhite", "#f6eee1", 0.85)
BREAD = mat("Bread", "#d19446", 0.6); BREADL = mat("BreadLight", "#f0d198", 0.7)
APPLE = mat("Apple", "#d52a2e", 0.25); STEM = mat("Stem", "#6a4527", 0.6); LEAF = mat("Leaf", "#4e8d32", 0.5)
BGLASS = mat("BottleGlass", "#316a3e", 0.05, alpha=0.75); CIDER = mat("Cider", "#e7a63c", 0.1, alpha=0.85)
LABEL = mat("Label", "#f3e5c0", 0.7); CORK = mat("Cork", "#b78958", 0.9); WINE = mat("WineStain", "#6e1a2a", 0.8)
STEEL = mat("Steel2", "#d7dce1", 0.12, 0.95); BRASS = mat("Brass2", "#e1b24d", 0.25, 0.9)
FAI = mat("Faience", "#faf6ed", 0.15); PBLUE = mat("PaintBlue", "#325fac", 0.2); PYEL = mat("PaintYellow", "#eeb731", 0.25)
CLEM = mat("Clementine", "#f17919", 0.45); CHERRY = mat("CherryRed", "#b61023", 0.1)
CARD = mat("CardGold", "#f2b32a", 0.25, 0.4); JB = mat("JewelBlue", "#2e5ace", 0.05); JR = mat("JewelRed", "#ce1e31", 0.05)
BOX = mat("MatchBox", "#305ea6", 0.6); MYEL = mat("MatchYellow", "#f5bf3c", 0.5); STRK = mat("Striker", "#5a3a25", 0.95)
PAPER = mat("Paper2", "#f5eedc", 0.85); MWOOD = mat("MatchWood", "#e7ce9e", 0.7); MHEAD = mat("MatchHead", "#d22e2e", 0.6)
WOODH = mat("WoodHandle", "#885533", 0.55); CUSH = mat("Cushion", "#c62c2e", 0.8)
CER = mat("CiderCeramic", "#c78a4b", 0.3); JGLASS = mat("JarGlass", "#e7f3f1", 0.03, alpha=0.35)
JAM = mat("Jam", "#9c1626", 0.15); GING = mat("Gingham", "#d43c3c", 0.8); STRING = mat("String", "#e8dbbe", 0.9)
LINEN = mat("Linen", "#eee6d4", 0.9); STITCH = mat("Stitch", "#b62c2e", 0.8)
GCRUST = mat("GaletteCrust", "#d79032", 0.4); GFILL = mat("GaletteFill", "#efd28a", 0.7)
SUGAR = mat("Sugar", "#faf9f5", 0.7); GRAPHITE = mat("Black", "#2e2a26", 0.5); WOOD = mat("Wood", "#ae7946", 0.7)
STRAW_R = mat("Red", "#d63c31", 0.4); GREEN = mat("Green", "#3e8d4b", 0.5)

made = {}

# ───── contour 2D → plaque épaisse (couverts, feuilles) ─────
def outline_solid(name, pts, thick, m, bevel=0.0, slices=1.0):
    """pts : contour fermé (x, y) ; plaque d'épaisseur `thick` centrée sur z = 0,
    redécoupée tous les `slices` en x pour pouvoir être cambrée ensuite."""
    c = bpy.data.curves.new(name, 'CURVE'); c.dimensions = '2D'; c.fill_mode = 'BOTH'
    sp = c.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for p, (x, y) in zip(sp.points, pts): p.co = (x, y, 0, 1)
    sp.use_cyclic_u = True
    c.extrude = thick / 2; c.bevel_depth = bevel; c.bevel_resolution = 2 if bevel else 0
    c.resolution_u = 1
    o = bpy.data.objects.new(name, c); SCENE.collection.objects.link(o); o.data.materials.append(m)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target='MESH'); o = bpy.context.object
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    xs = [v.co.x for v in bm.verts]
    x = min(xs) + slices
    while x < max(xs):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(x, 0, 0), plane_no=(1, 0, 0))
        x += slices
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.to_mesh(o.data); bm.free()
    return o

def deform(o, fz=None, fthick=None):
    """fz(x, y) : décalage vertical ; fthick(x) : facteur d'épaisseur."""
    for v in o.data.vertices:
        x, y, z = v.co
        if fthick: z *= fthick(x)
        if fz: z += fz(x, y)
        v.co.z = z

def spline(pts):
    """interpolation lisse (Catmull-Rom) d'une liste (x, valeur) triée."""
    def f(x):
        if x <= pts[0][0]: return pts[0][1]
        if x >= pts[-1][0]: return pts[-1][1]
        for i in range(len(pts) - 1):
            if pts[i][0] <= x <= pts[i + 1][0]:
                p0 = pts[max(0, i - 1)][1]; p1 = pts[i][1]; p2 = pts[i + 1][1]; p3 = pts[min(len(pts) - 1, i + 2)][1]
                t = (x - pts[i][0]) / (pts[i + 1][0] - pts[i][0])
                return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
    return f

def slab(name, nx, ny, xy, ztop, zbot, m, mat_of=None):
    """volume FERMÉ : grille (nx × ny) de dessus xy(i, j) → (x, y), hauteur ztop(x, y),
    dessous zbot(x, y) (plat au sol en général), flancs verticaux. Pas de vide dessous :
    on ne peut pas se glisser sous l'objet."""
    bm = bmesh.new()
    T = [[None] * (ny + 1) for _ in range(nx + 1)]; B = [[None] * (ny + 1) for _ in range(nx + 1)]
    for i in range(nx + 1):
        for j in range(ny + 1):
            x, y = xy(i, j)
            T[i][j] = bm.verts.new((x, y, ztop(x, y))); B[i][j] = bm.verts.new((x, y, zbot(x, y)))
    for i in range(nx):
        for j in range(ny):
            f = bm.faces.new((T[i][j], T[i + 1][j], T[i + 1][j + 1], T[i][j + 1]))
            if mat_of: f.material_index = mat_of(i, j)
            bm.faces.new((B[i][j], B[i][j + 1], B[i + 1][j + 1], B[i + 1][j]))
    def side(seq):
        for (a, b) in zip(seq, seq[1:]):
            bm.faces.new((T[a[0]][a[1]], B[a[0]][a[1]], B[b[0]][b[1]], T[b[0]][b[1]]))
    ring = [(i, 0) for i in range(nx + 1)] + [(nx, j) for j in range(1, ny + 1)] + [(i, ny) for i in range(nx - 1, -1, -1)] + [(0, j) for j in range(ny - 1, -1, -1)]
    side(ring)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return link(me, name)

def arc(cx, cy, r, a0, a1, n):
    return [(cx + r * math.cos(a0 + (a1 - a0) * i / n), cy + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]

# ═════════ FOURCHETTE (80 de long, couchée, dents vers +X) ═════════
def fork():
    W = 5.2                       # demi-largeur de la tête
    wf = spline([(-40, 3.0), (-36, 3.7), (-28, 3.6), (-14, 2.6), (0, 1.5), (6, 1.35), (12, 2.4), (18, 4.4), (22, W)])
    # bord supérieur du manche jusqu'à la tête, bout du manche arrondi
    top = [(-40 + 52 * i / 104, wf(-40 + 52 * i / 104)) for i in range(1, 105)]   # x : -39.5 → 12
    top += [(12 + 10 * i / 20, wf(12 + 10 * i / 20)) for i in range(1, 21)]          # → 22
    # quatre dents arrondies, fentes à fond rond
    g = 0.9; tw = (2 * W - 3 * g) / 4; tip = 40.0; slot = 27.0
    tines = []
    for k in range(4):
        ya = W - k * (tw + g); yb = ya - tw          # bords de la dent k (du haut vers le bas)
        tines += [(slot + 1 if k else 22.5, ya), (tip - tw * 0.6, ya)]
        tines += arc(tip - tw * 0.6, (ya + yb) / 2, tw / 2, math.pi / 2, -math.pi / 2, 8)[1:-1]
        tines += [(tip - tw * 0.6, yb)]
        if k < 3:
            # fond de fente arrondi
            tines += arc(slot, yb - g / 2, g / 2, math.pi / 2, 3 * math.pi / 2, 6)[0:]
    tines += [(22.5, -W)]
    bot = [(x, -y) for (x, y) in reversed(top)]
    endcap = arc(-39.6, 0, 3.0, -math.pi / 2, -3 * math.pi / 2, 16)[1:-1]
    pts = top + tines + bot + endcap
    o = outline_solid("fork", pts, 1.0, STEEL, bevel=0.32, slices=1.0)
    # épaisseur : manche plein, col fin, dents minces ; cambrure : manche et tête relevés
    thick = spline([(-40, 1.5), (-20, 1.35), (0, 1.0), (14, 0.9), (26, 0.8), (40, 0.6)])
    zc = spline([(-40, 0.4), (-30, 0.6), (-15, 1.4), (0, 2.6), (10, 2.8), (20, 1.6), (27, 1.0), (34, 1.1), (40, 2.0)])
    # tête légèrement creusée en travers
    deform(o, fz=lambda x, y: zc(x) + (0.035 * y * y if x > 14 else 0), fthick=thick)
    smooth(o)
    ground(o)
    return o

# ═════════ COUTEAU (125, lame +X, manche bois à rivets) ═════════
def knife():
    parts = []
    # manche : profil arrondi
    # manche galbé (plus large vers le talon), section ovale
    wf = spline([(-62, 3.6), (-58, 4.4), (-40, 4.2), (-22, 3.9), (-14, 4.1)])
    top = [(-62 + 48 * i / 96, wf(-62 + 48 * i / 96)) for i in range(97)]
    bot = [(x, -y) for (x, y) in reversed(top)]
    h = outline_solid("handle", top + bot, 4.8, WOODH, bevel=1.6, slices=1.5)
    for v in h.data.vertices: v.co.z += 2.6
    smooth(h); parts.append(h)
    for x in (-54, -38, -24):
        r = prim('cylinder', BRASS, radius=0.9, depth=5.2 + 3.2, location=(x, 0, 2.6), vertices=20); r.scale.z = 1.0; smooth(r); parts.append(r)
    # mitre : collerette d'acier ovale, à peine plus épaisse que le manche
    mit = prim('cylinder', STEEL, radius=1.0, depth=3.0, location=(-12.5, 0, 2.6), rotation=(0, math.pi / 2, 0), vertices=48)
    mit.scale = (1.0, 4.7, 2.7)
    mod(mit, 'BEVEL', width=0.5, segments=3); smooth(mit); parts.append(mit)
    # lame de couteau de table : dos droit, tranchant qui remonte vers une pointe arrondie
    n = 70
    def edge_y(t): return -4.3 + 3.2 * t ** 2.4          # tranchant
    def back_y(t): return 3.9 - 0.9 * t ** 3              # dos
    top = [(-10 + 70 * i / n, back_y(i / n)) for i in range(n + 1)]
    tipc = arc(60, (back_y(1) + edge_y(1)) / 2, (back_y(1) - edge_y(1)) / 2, math.pi / 2, -math.pi / 2, 10)[1:-1]
    bot = [(-10 + 70 * i / n, edge_y(i / n)) for i in range(n, -1, -1)]
    b = outline_solid("blade", top + tipc + bot, 1.0, STEEL, bevel=0.15, slices=1.5)
    # biseau : épais au dos, fin au tranchant
    for v in b.data.vertices:
        t = min(1, max(0, (v.co.x + 10) / 70))
        k = (v.co.y - edge_y(t)) / max(0.1, back_y(t) - edge_y(t))
        v.co.z = v.co.z * (0.25 + 0.75 * max(0, min(1, k))) + 2.6
    smooth(b); parts.append(b)
    o = join("knife", parts)
    # posé à plat : le talon du manche et la pointe de la lame touchent la nappe
    o.rotation_euler = (0, math.radians(1.6), 0); apply(o)
    ground(o); return o

# ═════════ BOUCHON DE LIÈGE (couché, 9,2 de long, Ø 8,2) ═════════
def cork():
    prof = [(0, 0), (3.6, 0), (3.95, 0.25), (4.1, 0.7), (4.0, 8.4), (3.8, 9.0), (3.5, 9.2), (0, 9.2)]
    o = lathe("cork", prof, CORK, seg=48)
    jitter(o, 0.12, 0.6)
    for f in o.data.polygons:      # bouts plats : ombrage plat (sinon « moulinet »)
        if abs(f.normal.z) > 0.9: f.use_smooth = False
    # tache de vin sur le bout (un disque légèrement en saillie)
    bm = bmesh.new(); ctr = bm.verts.new((0, 0, 9.4)); ring = []
    for i in range(40):
        a = 2 * math.pi * i / 40; k = 2.7 * (1 + 0.08 * math.sin(a * 5) + 0.05 * math.sin(a * 11 + 1))
        ring.append(bm.verts.new((math.cos(a) * k, math.sin(a) * k, 9.32)))
    for i in range(40): bm.faces.new((ctr, ring[i], ring[(i + 1) % 40]))
    me = bpy.data.meshes.new("stain"); bm.to_mesh(me); bm.free()
    s = link(me, "stain"); s.data.materials.append(WINE)
    o = join("cork", [o, s])
    o.rotation_euler = (0, math.pi / 2, 0.0); o.location = (-4.6, 0, 4.05)
    apply(o); ground(o)
    return o

def corkscrew_into(cork_o):
    """tire-bouchon d'un seul tenant : une vrille pointue qui, en haut, se resserre
    vers l'axe et devient la tige ; la tige entre dans une virole en laiton sertie
    dans la poignée en bois (pommeaux en laiton aux deux bouts)."""
    # repère local : axe +Z, pointe en z = 0
    pts = []
    turns, Hh, Rh = 3.5, 7.5, 1.25
    n = 140
    for i in range(n + 1):
        t = i / n; a = t * turns * 2 * math.pi; z = t * Hh
        rr = Rh * min(1.0, t * 6)                    # la pointe part de l'axe
        pts.append((math.cos(a) * rr, math.sin(a) * rr, z, 0.35 + 0.65 * min(1.0, t * 5)))
    # raccord : la spire rejoint l'axe en un quart de tour, puis la tige monte droit
    a_end = turns * 2 * math.pi
    for i in range(1, 13):
        u = i / 12; a = a_end + u * math.pi / 2; rr = Rh * (1 - u) ** 1.5
        pts.append((math.cos(a) * rr, math.sin(a) * rr, Hh + u * 1.2, 1.0))
    for i in range(1, 9):
        pts.append((0, 0, Hh + 1.2 + i * 0.5, 1.0))     # tige jusqu'à z = Hh + 5.2
    c = bpy.data.curves.new("screw", 'CURVE'); c.dimensions = '3D'
    sp = c.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for p_, (x, y, z, w) in zip(sp.points, pts): p_.co = (x, y, z, 1); p_.radius = w
    c.bevel_depth = 0.34; c.bevel_resolution = 3; c.use_fill_caps = True
    h = bpy.data.objects.new("screw", c); SCENE.collection.objects.link(h); h.data.materials.append(STEEL)
    bpy.ops.object.select_all(action='DESELECT'); h.select_set(True); bpy.context.view_layer.objects.active = h
    bpy.ops.object.convert(target='MESH'); h = bpy.context.object; smooth(h)
    top = Hh + 5.2
    # virole en laiton qui enserre la tige et s'enfonce dans la poignée
    fer = lathe("ferrule", [(0.36, top - 1.6), (0.75, top - 1.2), (0.95, top - 0.2), (0.95, top + 0.9)], BRASS, seg=32, cap_bot=True)
    # poignée : bois tourné, renflé au milieu, percé par la virole
    bar = lathe("handle", [(0.0, -7.0), (0.9, -6.9), (1.15, -5.5), (1.25, -2.5), (1.45, 0.0), (1.25, 2.5), (1.15, 5.5), (0.9, 6.9), (0.0, 7.0)], WOODH, seg=32)
    bar.rotation_euler = (0, math.pi / 2, 0); bar.location = (0, 0, top + 1.2); apply(bar)
    parts = [h, fer, bar]
    for sd in (-1, 1):
        cap = prim('uv_sphere', BRASS, radius=1.15, location=(sd * 7.2, 0, top + 1.2), segments=20, ring_count=10); smooth(cap); parts.append(cap)
    cs = join("corkscrew", parts)
    # planté dans le bout du bouchon couché (axe +X) : la vrille est à moitié vissée dedans
    cs.matrix_world = Matrix.Translation((0.2, 0, 4.05)) @ Matrix.Rotation(math.pi / 2, 4, 'Y') @ Matrix.Rotation(math.pi / 2, 4, 'Z')
    apply(cs)
    return join("cork", [cork_o, cs])

# ═════════ BOUTEILLE DE CIDRE (Ø 35, h 150) ═════════
def bottle():
    R = 17.6
    prof = [(0, 1.2), (R - 3, 0), (R - 0.6, 0.6), (R, 3), (R, 84), (R - 0.5, 90), (14.5, 100), (9, 112), (6.6, 122), (6.2, 136), (7.4, 137.5), (7.4, 141), (6.1, 142), (0, 142)]
    glass = lathe("bottle_g", prof, BGLASS, seg=64, bands=[(26, 62, R - 1, R + 1, LABEL), (109, 118, 6.8, 11, LABEL)])
    # fond creusé (piqûre)
    cider_prof = [(0, 2.2), (R - 1.6, 2.2), (R - 1.4, 2.6), (R - 1.4, 86), (R - 2.2, 91), (13.0, 99), (0, 99)]   # pleine jusqu'à l'épaule
    cid = lathe("bottle_c", cider_prof, CIDER, seg=48)
    # étiquette (bande de papier un peu en saillie) + collerette
    # bouchon champignon + muselet
    cork_p = [(0, 136), (5.8, 136), (6.3, 142), (8.0, 143), (8.3, 148), (7.4, 150.5), (0, 150.5)]
    ck = lathe("bcork", cork_p, CORK, seg=40, cap_top=False)
    plate = prim('cylinder', BRASS, radius=4.6, depth=0.5, location=(0, 0, 150.6), vertices=32)
    wires = []
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        c = bpy.data.curves.new("w", 'CURVE'); c.dimensions = '3D'; sp = c.splines.new('POLY'); sp.points.add(3)
        for i, (r, z) in enumerate([(4.4, 150.5), (8.3, 146.5), (7.6, 140.5), (7.5, 139.0)]):
            sp.points[i].co = (math.cos(a) * r, math.sin(a) * r, z, 1)
        c.bevel_depth = 0.25
        w = bpy.data.objects.new("w", c); SCENE.collection.objects.link(w); w.data.materials.append(STEEL)
        bpy.ops.object.select_all(action='DESELECT'); w.select_set(True); bpy.context.view_layer.objects.active = w
        bpy.ops.object.convert(target='MESH'); wires.append(bpy.context.object)
    ring = prim('torus', STEEL, major_radius=7.55, minor_radius=0.3, location=(0, 0, 139.3)); wires.append(ring)
    o = join("bottle", [glass, cid, ck, plate] + wires)
    ground(o); return o

# ═════════ COUPE À FRUITS + CLÉMENTINES (Ø 83, h 60) ═════════
def bowl():
    prof = [(0, 0.8), (12, 0), (13, 1.2), (12, 3), (6, 5), (5, 14), (8, 17), (22, 21), (34, 28), (40, 36), (41.5, 38.5), (40.4, 39), (33, 31.5), (21, 24.5), (0, 23)]
    b = lathe("bowl_f", prof, FAI, seg=96, bands=[(37.0, 39.1, 39.6, 42, PBLUE), (31.6, 33.6, 36.0, 38.5, PBLUE), (0.4, 2.2, 12.3, 13.5, PBLUE)])
    rims = []
    fruits = []
    spots = [(0, 0, 33), (-14, 6, 31), (13, 9, 31), (4, -15, 31), (-9, -13, 32), (16, -7, 32), (-18, -4, 33), (2, 15, 31), (-2, 1, 47), (9, -4, 45), (-10, 5, 45)]
    for (x, y, z) in spots:
        r = 9.0 + random.uniform(-0.6, 0.6)
        c = prim('uv_sphere', CLEM, radius=r, location=(x, y, z), segments=32, ring_count=16); c.scale.z = 0.86
        smooth(c); jitter(c, 0.18, 0.7); fruits.append(c)
        # pédoncule + feuille sur quelques-uns
        st = prim('cylinder', STEM, radius=0.6, depth=1.6, location=(x, y, z + r * 0.86 + 0.4), vertices=8); fruits.append(st)
        if random.random() < 0.5:
            lf = leaf_mesh(7, 3.2); lf.location = (x + 2.5, y, z + r * 0.86 + 0.4); lf.rotation_euler = (0.3, -0.25, random.uniform(0, 6.28)); fruits.append(lf)
    o = join("bowl", [b] + rims + fruits); ground(o); return o

def leaf_mesh(length, width, m=None):
    bm = bmesh.new(); N = 10
    L, Rr = [], []
    for i in range(N + 1):
        t = i / N; w = width * math.sin(math.pi * t) ** 0.8 * (1 - 0.25 * t)
        z = 0.6 * math.sin(math.pi * t)
        L.append(bm.verts.new((t * length, -w / 2, z))); Rr.append(bm.verts.new((t * length, w / 2, z)))
    mid = [bm.verts.new((t * length / N * N / N * 0 + i * length / N, 0, 0.6 * math.sin(math.pi * i / N) + 0.25)) for i, t in enumerate([0] * (N + 1))]
    for i in range(N):
        bm.faces.new((L[i], L[i + 1], mid[i + 1], mid[i])); bm.faces.new((mid[i], mid[i + 1], Rr[i + 1], Rr[i]))
    me = bpy.data.meshes.new("leaf"); bm.to_mesh(me); bm.free()
    o = link(me, "leaf"); o.data.materials.append(m or LEAF)
    mod(o, 'SOLIDIFY', thickness=0.15); smooth(o)
    return o

# ═════════ CERISES ═════════
def cherry_fruit(loc, r=5.3):
    c = prim('uv_sphere', CHERRY, radius=r, location=loc, segments=32, ring_count=16); smooth(c)
    # forme de cœur : creux au pédoncule, sillon léger
    for v in c.data.vertices:
        p = v.co; d = math.hypot(p.x, p.y)
        if p.z > 0: v.co.z -= 1.6 * math.exp(-(d / (r * 0.35)) ** 2)
        v.co.x *= 1.04
    return c

def stem_curve(pts, r=0.45):
    c = bpy.data.curves.new("stem", 'CURVE'); c.dimensions = '3D'
    sp = c.splines.new('BEZIER'); sp.bezier_points.add(len(pts) - 1)
    for bp, p in zip(sp.bezier_points, pts):
        bp.co = p; bp.handle_left_type = bp.handle_right_type = 'AUTO'
    c.bevel_depth = r; c.bevel_resolution = 3
    o = bpy.data.objects.new("stem", c); SCENE.collection.objects.link(o); o.data.materials.append(STEM)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target='MESH'); return bpy.context.object

def cherries():
    a = cherry_fruit((-4.5, -2, 5.2)); b = cherry_fruit((4.8, 2.5, 5.2), 5.0)
    s1 = stem_curve([(-4.5, -2, 9.4), (-3, -1, 15), (0.5, 0.5, 20.5)])
    s2 = stem_curve([(4.8, 2.5, 9.2), (3.5, 1.8, 15), (0.5, 0.5, 20.5)])
    lf = leaf_mesh(9, 4.2); lf.location = (0.5, 0.5, 20.3); lf.rotation_euler = (0.2, -0.5, 0.6)
    o = join("cherries", [a, b, s1, s2, lf]); ground(o); return o

def cherry():
    a = cherry_fruit((0, 0, 5.4), 5.5)
    s = stem_curve([(0, 0, 9.4), (1.2, 0.3, 14), (3.2, 0.8, 18.5)])
    o = join("cherry", [a, s]); ground(o); return o

# ═════════ COURONNE DES ROIS EN CARTON DORÉ (Ø 34, h 24) ═════════
def crown():
    """couronne de galette : bandeau de carton doré découpé en 8 fleurons arrondis
    (pointe en ogive terminée par une perle), carton épais au chant visible, frise
    embossée en bas, filet en relief qui suit la découpe, cabochons sertis
    (rubis, saphirs, émeraudes) au pied de chaque fleuron, intérieur crème."""
    R, N, P = 16.0, 384, 8
    EMER = mat("JewelGreen", "#1f9a5a", 0.05)
    CREAM_IN = mat("Paper2", "#f5eedc", 0.85)
    def top_h(t):
        # t ∈ [0, 1) autour de la couronne ; fleuron centré sur chaque 1/P
        u = (t * P) % 1.0               # 0 … 1 à travers un fleuron (pointe en 0,5)
        d = abs(u - 0.5) * 2            # 0 à la pointe, 1 au creux
        base = 9.0
        # ogive : flancs creusés (courbe concave), creux arrondi entre deux fleurons
        peak = 13.0 * (1 - d) ** 1.7
        valley = 1.2 * (1 - math.cos(math.pi * max(0.0, d - 0.82) / 0.18)) * 0.5 if d > 0.82 else 0.0
        return base + peak - valley
    def rad(t, z):
        return R * (1 + 0.015 * math.sin(t * 2 * math.pi * 2)) + 0.014 * z * R / 16  # carton un peu évasé
    # bandeau : paroi extérieure (or), paroi intérieure (crème), chant (or)
    bm = bmesh.new(); TH = 0.7
    NZ = 10
    rows_o, rows_i = [], []
    for k in range(NZ + 1):
        ro, ri = [], []
        for i in range(N):
            t = i / N; a = 2 * math.pi * t
            z = top_h(t) * k / NZ
            r = rad(t, z)
            ro.append(bm.verts.new((math.cos(a) * r, math.sin(a) * r, z)))
            ri.append(bm.verts.new((math.cos(a) * (r - TH), math.sin(a) * (r - TH), z)))
        rows_o.append(ro); rows_i.append(ri)
    for k in range(NZ):
        for i in range(N):
            j = (i + 1) % N
            bm.faces.new((rows_o[k][i], rows_o[k][j], rows_o[k + 1][j], rows_o[k + 1][i]))
            f = bm.faces.new((rows_i[k][j], rows_i[k][i], rows_i[k + 1][i], rows_i[k + 1][j])); f.material_index = 1
    for i in range(N):
        j = (i + 1) % N
        bm.faces.new((rows_o[NZ][i], rows_o[NZ][j], rows_i[NZ][j], rows_i[NZ][i]))   # chant du haut
        bm.faces.new((rows_o[0][j], rows_o[0][i], rows_i[0][i], rows_i[0][j]))       # chant du bas
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("crown_band"); bm.to_mesh(me); bm.free()
    band = link(me, "crown_band"); band.data.materials.append(CARD); band.data.materials.append(CREAM_IN)
    smooth(band); parts = [band]
    def tube(name, pts, r, m, cyclic=True):
        c = bpy.data.curves.new(name, 'CURVE'); c.dimensions = '3D'
        sp = c.splines.new('POLY'); sp.points.add(len(pts) - 1)
        for p, q in zip(sp.points, pts): p.co = (*q, 1)
        sp.use_cyclic_u = cyclic
        c.bevel_depth = r; c.bevel_resolution = 3
        o = bpy.data.objects.new(name, c); SCENE.collection.objects.link(o); o.data.materials.append(m)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.convert(target='MESH'); o = bpy.context.object; smooth(o); return o
    # frise du bas : deux joncs et une rangée de perles embossées
    for z in (1.0, 4.6):
        parts.append(tube("crown_bead", [(math.cos(2 * math.pi * i / 192) * (rad(i / 192, z) + 0.25), math.sin(2 * math.pi * i / 192) * (rad(i / 192, z) + 0.25), z) for i in range(192)], 0.42, CARD))
    for i in range(64):
        t = i / 64; a = 2 * math.pi * t; z = 2.8; r = rad(t, z) + 0.2
        b_ = prim('uv_sphere', CARD, radius=0.55, location=(math.cos(a) * r, math.sin(a) * r, z), segments=12, ring_count=6); smooth(b_); parts.append(b_)
    # filet en relief qui suit la découpe des fleurons (1,2 sous le bord)
    pts = []
    for i in range(N):
        t = i / N; a = 2 * math.pi * t; z = max(5.6, top_h(t) - 1.3); r = rad(t, z) + 0.2
        pts.append((math.cos(a) * r, math.sin(a) * r, z))
    parts.append(tube("crown_filet", pts, 0.3, CARD))
    # chaque fleuron : perle au sommet, cabochon serti au pied, petit losange gravé
    gems = [JR, JB, EMER, JB]
    for i in range(P):
        t = (i + 0.5) / P; a = 2 * math.pi * t
        zt = top_h(t); r = rad(t, zt)
        pearl = prim('uv_sphere', FAI, radius=1.25, location=(math.cos(a) * r, math.sin(a) * r, zt + 0.9), segments=20, ring_count=10); smooth(pearl); parts.append(pearl)
        zg = 8.6; rg = rad(t, zg)
        bez = prim('torus', CARD, major_radius=1.75, minor_radius=0.38, location=(math.cos(a) * (rg + 0.3), math.sin(a) * (rg + 0.3), zg),
                   rotation=(math.pi / 2, 0, a + math.pi / 2), major_segments=28, minor_segments=8); smooth(bez); parts.append(bez)
        gem = prim('uv_sphere', gems[i % 4], radius=1.45, location=(math.cos(a) * (rg + 0.35), math.sin(a) * (rg + 0.35), zg), segments=24, ring_count=12)
        gem.scale = (1, 1, 1); gem.rotation_euler = (0, 0, a); smooth(gem)
        for v in gem.data.vertices:              # cabochon : aplati côté carton
            if v.co.x < 0: v.co.x *= 0.25
        parts.append(gem)
        # petite perle entre deux fleurons (au creux)
        t2 = i / P; a2 = 2 * math.pi * t2; z2 = top_h(t2) - 2.2; r2 = rad(t2, z2) + 0.35
        sp_ = prim('uv_sphere', FAI, radius=0.7, location=(math.cos(a2) * r2, math.sin(a2) * r2, z2), segments=14, ring_count=7); smooth(sp_); parts.append(sp_)
    o = join("crown", parts)
    o.rotation_euler = (math.radians(6), 0, 0); apply(o); ground(o); return o

# ═════════ BOLÉE DE CIDRE (anse) ═════════
def glass():
    prof = [(0, 0.6), (9, 0), (10, 1), (10.5, 2.2), (14.5, 6), (16.6, 14), (17.2, 21.2), (16.4, 22), (15.8, 21), (15.3, 14), (13.3, 6.5), (0, 4.5)]
    b = lathe("bolee", prof, CER, seg=72, bands=[(16.5, 18.5, 16.5, 17.5, PYEL)])
    liq = lathe("bolee_cider", [(0, 4.6), (13.4, 6.8), (15.4, 14), (15.8, 17.8), (0, 17.8)], CIDER, seg=60)
    # anse : tore coupé
    h = prim('torus', CER, major_radius=5.0, minor_radius=1.3, location=(19.5, 0, 12.5), rotation=(math.pi / 2, 0, 0), major_segments=40, minor_segments=14)
    smooth(h)
    o = join("glass", [b, liq, h]); ground(o); return o

# ═════════ POT DE CONFITURE (tissu vichy noué) ═════════
def jar():
    R = 15.0
    gl = lathe("jar_g", [(0, 0.8), (R - 2, 0), (R, 1.8), (R + 0.4, 6), (R + 0.4, 22), (R - 1.4, 25.5), (R - 2.0, 26), (R - 2.0, 27.8), (R - 1.0, 28.2), (R - 1.0, 29.5), (0, 29.5)], JGLASS, seg=72, bands=[(9, 16, R, R + 1, LABEL)])
    jm = lathe("jam", [(0, 1.4), (R - 1.2, 1.4), (R - 0.6, 3), (R - 0.6, 20.5), (0, 21.5)], JAM, seg=60)

    # tissu : disque ondulé qui retombe sur le col
    bm = bmesh.new(); N, M = 96, 10
    rings = []
    for j in range(M + 1):
        t = j / M; rr = t * 19.0
        ring = []
        for i in range(N):
            a = 2 * math.pi * i / N
            drop = max(0, rr - 12.5) * 1.15
            wave = math.sin(a * 9) * 1.2 * max(0, (rr - 13) / 6)
            r2 = min(rr, 13.6 + 0.4 * math.sin(a * 9)) if rr > 13.6 else rr
            ring.append(bm.verts.new((math.cos(a) * (r2 + wave * 0.3), math.sin(a) * (r2 + wave * 0.3), 30.4 - drop + 0.6 * (1 - t))))
        rings.append(ring)
    for a_, b_ in zip(rings, rings[1:]):
        for i in range(N):
            k = (i + 1) % N; bm.faces.new((a_[i], a_[k], b_[k], b_[i]))
    me = bpy.data.meshes.new("cloth"); bm.to_mesh(me); bm.free()
    cl = link(me, "cloth"); cl.data.materials.append(GING); mod(cl, 'SOLIDIFY', thickness=0.35); smooth(cl)
    st = prim('torus', STRING, major_radius=13.9, minor_radius=0.55, location=(0, 0, 26.8), major_segments=64, minor_segments=8); smooth(st)
    bow = []
    for s in (-1, 1):
        lp = prim('torus', STRING, major_radius=2.0, minor_radius=0.45, location=(14.6, s * 2.0, 26.8), rotation=(0, math.pi / 2, 0)); bow.append(lp)
    o = join("jar", [gl, jm, cl, st] + bow); ground(o); return o

# ═════════ SERVIETTE PLIÉE (50 × 50) ═════════
def napkin():
    """serviette en lin pliée en quatre, posée À PLAT : trois épaisseurs qui
    s'arrondissent sur les bords (pli bombé côté +x/+y, bords libres arrondis),
    chacune pleine jusqu'à la nappe (aucun vide dessous). Liseré brodé rouge
    dans la couche du dessus."""
    parts = []
    # (taille, dessus, décalage x, y) : chaque pli dépasse nettement du suivant (marches de ~1,5),
    # toutes les couches sont pleines jusqu'à la nappe : aucun espace entre elles
    layers = [(49.0, 1.4, 0.0, 0.0), (45.0, 3.0, 2.6, -2.1), (41.0, 4.6, 5.2, -4.2)]
    N = 56
    for k, (sz, top, dx, dy) in enumerate(layers):
        h = sz / 2
        def xy(i, j, h=h, sz=sz, dx=dx, dy=dy): return (-h + sz * i / N + dx, -h + sz * j / N + dy)
        def zt(x, y, h=h, sz=sz, dx=dx, dy=dy, top=top, k=k):
            ex = (x - dx + h) / sz; ey = (y - dy + h) / sz
            # bords arrondis vers la nappe (épaisseur qui diminue), pli bombé
            e = min(ex, ey, 1 - ex, 1 - ey)
            edge = 1 - math.exp(-e * 22)
            bulge = 0.22 * (math.exp(-(1 - ex) * 10) + math.exp(-(1 - ey) * 10))
            ripple = 0.08 * noise.noise(Vector((x * 0.09, y * 0.09, k * 3.1))) + 0.04 * math.sin(x * 0.35 + y * 0.12)
            return max(0.25, top * (0.72 + 0.28 * edge) + bulge + ripple)   # marche nette au bord de chaque pli
        def mat_of(i, j, h=h, sz=sz, k=k):
            if k != 2: return 0
            cx = -h + sz * (i + 0.5) / N; cy = -h + sz * (j + 0.5) / N
            d = min(h - abs(cx), h - abs(cy))
            return 1 if 2.0 < d < 3.8 else 0
        p = slab("napkin_l", N, N, xy, zt, lambda x, y: 0.0, LINEN, mat_of)
        p.data.materials.append(LINEN); p.data.materials.append(STITCH)
        smooth(p); parts.append(p)
    o = join("napkin", parts); ground(o); return o

# ═════════ ASSIETTE + PART DE GALETTE (Ø 75) ═════════
def plate():
    prof = [(0, 0.8), (22, 0.3), (24, 0), (26, 0.6), (26, 1.6), (25, 2.0), (27, 2.2), (35, 5.8), (37.5, 7.2), (37.2, 7.8), (34.5, 6.8), (26.5, 3.0), (0, 2.6)]
    p = lathe("plate_f", prof, FAI, seg=128, bands=[(6.9, 7.8, 36.5, 38, PBLUE), (4.6, 5.4, 30.4, 31.8, PYEL)])
    deco = []
    for i in range(16):
        a = 2 * math.pi * i / 16
        d = prim('uv_sphere', PBLUE, radius=0.9, location=(math.cos(a) * 33.4, math.sin(a) * 33.4, 6.0), segments=12, ring_count=6); d.scale.z = 0.25; deco.append(d)
    # part de galette : prisme feuilleté (angle 50°)
    bm = bmesh.new(); A = math.radians(50); RR = 22.0; N = 20; H = 7.0
    def P(a, r, z): return (math.cos(a) * r, math.sin(a) * r, z)
    bot = [bm.verts.new((0, 0, 2.6))] + [bm.verts.new(P(-A / 2 + A * i / N, RR, 2.6)) for i in range(N + 1)]
    top = [bm.verts.new((0, 0, 2.6 + H))] + [bm.verts.new(P(-A / 2 + A * i / N, RR - 0.6 * math.sin(math.pi * i / N), 2.6 + H - 1.6 * (i % 2 == 0) * 0)) for i in range(N + 1)]
    for i in range(N + 1):
        top[i + 1].co.z = 2.6 + H - 1.2 * (1 - math.sin(math.pi * i / N)) - 0.6
    for i in range(N):
        bm.faces.new((bot[0], bot[i + 2], bot[i + 1])); bm.faces.new((top[0], top[i + 1], top[i + 2]))
        bm.faces.new((bot[i + 1], bot[i + 2], top[i + 2], top[i + 1]))
    bm.faces.new((bot[0], bot[1], top[1], top[0])); bm.faces.new((bot[0], top[0], top[N + 1], bot[N + 1]))
    me = bpy.data.meshes.new("slice"); bm.to_mesh(me); bm.free()
    sl = link(me, "slice"); sl.data.materials.append(GFILL); sl.data.materials.append(GCRUST)
    for f in sl.data.polygons:
        if f.normal.z > 0.5 or (f.center.x ** 2 + f.center.y ** 2) > (RR - 1.5) ** 2: f.material_index = 1
    mod(sl, 'BEVEL', width=0.5, segments=2); smooth(sl, False)
    sl.location = (-6, 2, 0); sl.rotation_euler = (0, 0, 0.5)
    # miettes sur l'assiette
    crumbs = []
    for i in range(14):
        a = random.uniform(0, 6.28); r = random.uniform(8, 27)
        c = prim('ico_sphere', GCRUST if i % 3 else GFILL, radius=random.uniform(0.6, 1.4), location=(math.cos(a) * r, math.sin(a) * r, 3.0), subdivisions=1); c.scale.z = 0.6; crumbs.append(c)
    o = join("plate", [p, sl] + deco + crumbs); ground(o); return o

# ═════════ SALADIER DE SALADE DE FRUITS (Ø 74, h 25) ═════════
def fruit_salad():
    """bol en calotte de sphère (façon bol de « coupe au bol »), bas, en faïence à liserés
    bleus, PLEIN : le sirop affleure sous le bord (un disque, sans autre surface confondue :
    rien ne clignote). Les fruits flottent en UNE couche, posés à plat, sans se toucher."""
    import random as _r
    _r.seed(21)
    parts = []
    RS, ZB = 40.0, 2.2                       # rayon de la sphère, bas du bol (sur son pied)
    ZC = ZB + RS                             # centre de la sphère
    RIM = 24.0; TH = 2.0; ZS = 20.0          # bord, épaisseur, niveau du sirop
    out = [(0, 0.5), (10.5, 0), (11.8, 0.9), (11.4, 2.0)]
    a_rim = math.acos((ZC - RIM) / RS)
    for k in range(1, 25):
        a_ = a_rim * k / 24
        r = RS * math.sin(a_)
        if r < 11.6: continue
        out.append((r, ZC - RS * math.cos(a_)))
    rr_ = RS * math.sin(a_rim)
    out += [(rr_ + 0.7, RIM + 0.5), (rr_ + 0.3, RIM + 1.1), (rr_ - 1.3, RIM + 0.9)]
    ri = RS - TH
    inner = []
    for k in range(13):
        z = RIM + 0.4 - (RIM + 0.4 - ZS) * k / 12
        inner.append((math.sqrt(max(0.0, ri * ri - (ZC - z) ** 2)), z))
    prof = out + inner
    b = lathe("salad_bowl", prof, FAI, seg=96, bands=[(RIM - 1.6, RIM + 1.2, rr_ - 0.5, rr_ + 1.0, PBLUE), (12.0, 14.0, 20.0, 40.0, PBLUE), (0.2, 1.8, 11.0, 12.5, PBLUE)])
    parts.append(b)
    r_s = inner[-1][0]
    SYRUP = mat("Cider", "#f0b545", 0.1)
    sy = prim('cylinder', SYRUP, radius=r_s + 0.3, depth=1.0, location=(0, 0, ZS - 0.5), vertices=64); parts.append(sy)
    MELON = mat("Clementine", "#f39a3a", 0.5); KIWI = mat("Green", "#7cbf3a", 0.4); KIWIC = mat("Cream", "#f4f0d0", 0.5)
    GRAPEG = mat("Grape", "#6b2f7a", 0.2); BLUEB = mat("Blue", "#3b3f8f", 0.3); MINT = mat("Leaf", "#4e9d3a", 0.5)
    placed = []
    def spot(rf, rmax):
        for _ in range(400):
            a_ = _r.random() * 6.283; r = (rmax - rf) * math.sqrt(_r.random())
            x, y = math.cos(a_) * r, math.sin(a_) * r
            if all((x - px) ** 2 + (y - py) ** 2 > (rf + pr + 0.4) ** 2 for px, py, pr in placed):
                placed.append((x, y, rf)); return x, y
        return None
    R_IN = r_s - 0.8
    def add(kind, n, rf):
        for _ in range(n):
            p_ = spot(rf, R_IN)
            if p_ is None: return
            x, y = p_; rz = _r.random() * 6.28
            if kind == "melon":
                c = prim('cube', MELON, size=4.0, location=(x, y, ZS + 1.4), rotation=(0, 0, rz)); mod(c, 'BEVEL', width=0.6, segments=2); parts.append(c)
            elif kind == "kiwi":
                kv = prim('cylinder', KIWI, radius=3.5, depth=1.2, location=(x, y, ZS + 0.3), vertices=24); smooth(kv); parts.append(kv)
                kc = prim('cylinder', KIWIC, radius=1.1, depth=1.9, location=(x, y, ZS + 0.3), vertices=12); smooth(kc); parts.append(kc)
            elif kind == "banana":
                bn = prim('cylinder', BANANA, radius=2.7, depth=1.4, location=(x, y, ZS + 0.4), vertices=20); mod(bn, 'BEVEL', width=0.4, segments=2); smooth(bn); parts.append(bn)
            elif kind == "straw":
                fr = prim('uv_sphere', STRAW_R, radius=2.6, location=(x, y, ZS + 0.6), segments=16, ring_count=8); fr.scale = (1, 1.25, 0.75); fr.rotation_euler = (0, 0, rz); smooth(fr); parts.append(fr)
            elif kind == "grape":
                g = prim('uv_sphere', GRAPEG, radius=2.0, location=(x, y, ZS + 1.2), segments=14, ring_count=8); smooth(g); parts.append(g)
            elif kind == "blue":
                g = prim('uv_sphere', BLUEB, radius=1.4, location=(x, y, ZS + 0.9), segments=12, ring_count=7); smooth(g); parts.append(g)
            elif kind == "orange":
                q = prim('uv_sphere', CLEM, radius=1, location=(x, y, ZS + 0.9), segments=16, ring_count=8); q.scale = (4.2, 1.9, 2.0); q.rotation_euler = (0, 0, rz); smooth(q); parts.append(q)
            elif kind == "mint":
                lf = prim('uv_sphere', MINT, radius=1, location=(x, y, ZS + 0.25), segments=12, ring_count=6); lf.scale = (3.0, 1.5, 0.35); lf.rotation_euler = (0, 0, rz); smooth(lf); parts.append(lf)
    BANANA = mat("Cream", "#f4e7a8", 0.5)
    add("orange", 5, 4.4); add("kiwi", 7, 3.6); add("straw", 9, 3.3); add("melon", 11, 3.0)
    add("banana", 7, 2.8); add("mint", 3, 3.1); add("grape", 10, 2.1); add("blue", 12, 1.5)
    o = join("fruit_salad", parts); ground(o); return o

# ═════════ MORCEAUX DE SUCRE ═════════
def sugar():
    parts = []
    for (x, y, z, rz, ry) in [(-3, 0, 0, 0.2, 0), (3.2, 1.2, 0, -0.3, 0), (0.6, -5.4, 0, 0.6, 0), (0.2, -1.0, 6.4, 0.9, 0.25)]:
        c = prim('cube', SUGAR, size=6.4, location=(x, y, z + 3.2)); c.rotation_euler = (0, ry, rz)
        mod(c, 'BEVEL', width=0.5, segments=2); jitter(c, 0.0, 1); parts.append(c)
    o = join("sugar", parts); ground(o); return o

# ═════════ PANIER EN OSIER (110 × 115 × 84) ═════════
def basket():
    """panier de pique-nique en osier : corps arrondi évasé, montants tressés,
    bourrelets torsadés, couvercle à deux rabats (l'arrière ouvert, appuyé sur
    les anses), sangles de cuir à boucle, torchon vichy, baguette et pommes."""
    W, D, H = 100.0, 68.0, 46.0
    parts = []
    def rr(t, a, b, n=5.0):
        # rectangle arrondi (super-ellipse) paramétré par t ∈ [0, 1)
        ang = 2 * math.pi * t
        c, s_ = math.cos(ang), math.sin(ang)
        return (a * math.copysign(abs(c) ** (2 / n), c), b * math.copysign(abs(s_) ** (2 / n), s_))
    NA = 128
    def shell(name, prof, m):
        """prof : liste (échelle, z) ; anneaux de super-ellipse empilés."""
        bm = bmesh.new(); rings = []
        for sc_, z in prof:
            rings.append([bm.verts.new((*rr(i / NA, W / 2 * sc_, D / 2 * sc_), z)) for i in range(NA)])
        for r0, r1 in zip(rings, rings[1:]):
            for i in range(NA):
                k = (i + 1) % NA; bm.faces.new((r0[i], r0[k], r1[k], r1[i]))
        me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
        o = link(me, name); o.data.materials.append(m); smooth(o); return o
    # paroi extérieure (évasée), lèvre, paroi intérieure, fond
    # corps PLEIN : posé à plat sur la nappe, parois droites (rien à passer dessous), dessus fermé
    # sous le contenu (on ne rentre plus dans le creux)
    body = shell("bk_body", [(0.0, 0.0), (0.975, 0.0), (0.995, 0.5), (1.0, 1.6), (1.0, H - 1.0), (0.99, H), (0.0, H)], WICKER)
    parts.append(body)
    # bas lisse : bandeau d'osier sombre affleurant (au lieu du bourrelet torsadé)
    parts.append(shell("bk_band", [(1.0, 0.4), (1.012, 1.0), (1.012, 7.0), (1.0, 7.6)], WICKERD))
    # montants verticaux (osier plus sombre) : ils rythment le tressage
    for i in range(44):
        t = i / 44
        x1, y1 = rr(t, W / 2 * 0.998, D / 2 * 0.998)
        c = bpy.data.curves.new("stake", 'CURVE'); c.dimensions = '3D'
        sp = c.splines.new('POLY'); sp.points.add(1)
        # montants seulement sur le haut (le bas reste lisse), collés à la paroi
        xi, yi = rr(t, W / 2 * 0.975, D / 2 * 0.975)  # le bas plonge dans la paroi : aucun creux
        sp.points.add(1)
        sp.points[0].co = (xi, yi, 7.0, 1); sp.points[1].co = (x1, y1, 9.5, 1); sp.points[2].co = (x1, y1, H - 2.5, 1)
        c.bevel_depth = 0.9; c.bevel_resolution = 2; c.use_fill_caps = True
        o = bpy.data.objects.new("stake", c); SCENE.collection.objects.link(o); o.data.materials.append(WICKERD)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.convert(target='MESH'); parts.append(bpy.context.object)
    # bourrelets torsadés (deux brins enroulés) en haut et en bas
    def braid(z, sc_, r, turns, m):
        for ph in (0, math.pi):
            c = bpy.data.curves.new("braid", 'CURVE'); c.dimensions = '3D'
            sp = c.splines.new('POLY'); n = 600; sp.points.add(n - 1)
            for i in range(n):
                t = i / n; x, y = rr(t, W / 2 * sc_, D / 2 * sc_)
                a = t * turns * 2 * math.pi + ph
                L = math.hypot(x, y) or 1
                sp.points[i].co = (x + x / L * r * 0.8 * math.cos(a), y + y / L * r * 0.8 * math.cos(a), z + r * 0.8 * math.sin(a), 1)
            sp.use_cyclic_u = True
            c.bevel_depth = r; c.bevel_resolution = 2
            o = bpy.data.objects.new("braid", c); SCENE.collection.objects.link(o); o.data.materials.append(m)
            bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
            bpy.ops.object.convert(target='MESH'); parts.append(bpy.context.object)
    braid(H - 0.2, 0.985, 1.9, 70, WICKERD)
    # couvercle : rabat avant fermé (légèrement bombé), rabat arrière ouvert
    def flap(name, ysign):
        bm = bmesh.new(); NX, NY = 40, 16
        vs = []
        for i in range(NX + 1):
            row = []
            for j in range(NY + 1):
                x = -W / 2 * 0.99 + W * 0.99 * i / NX
                ex = abs(x) / (W / 2 * 0.99)
                ymax = D / 2 * 0.99 * max(0.0, 1 - ex ** 5) ** 0.2
                y = ysign * ymax * j / NY
                ey = j / NY
                z = 2.2 * (1 - ex ** 4) * (1 - ey ** 2)
                row.append(bm.verts.new((x, y, z)))
            vs.append(row)
        for i in range(NX):
            for j in range(NY):
                f = (vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1])
                bm.faces.new(f if ysign < 0 else tuple(reversed(f)))
        me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
        o = link(me, name); o.data.materials.append(WICKER)
        mod(o, 'SOLIDIFY', thickness=2.4, offset=-1); smooth(o)
        return o
    front = flap("bk_lid_front", -1); front.location.z = H + 0.6; apply(front); parts.append(front)
    back = flap("bk_lid_back", 1)
    # charnière en y = 0 : le rabat arrière est relevé (72°), on voit le contenu
    back.rotation_euler = (math.radians(72), 0, 0); back.location = (0, 0.5, H + 1.2); apply(back); parts.append(back)
    # traverse centrale (charnière) en bois
    hinge = prim('cylinder', WOODH, radius=1.6, depth=W * 0.96, location=(0, 0, H + 1.4), rotation=(0, math.pi / 2, 0), vertices=16); smooth(hinge); parts.append(hinge)
    # anses en arceau (gainées d'osier), rabattues vers l'avant sur le couvercle fermé
    LEATHER = mat("Leather", "#7a4523", 0.6)
    tilt = math.radians(55)
    for s_ in (-1, 1):
        c = bpy.data.curves.new("handle", 'CURVE'); c.dimensions = '3D'
        sp = c.splines.new('POLY'); n = 60; sp.points.add(n - 1)
        for i in range(n):
            a = math.pi * i / (n - 1)
            hx = s_ * 26 - math.cos(a) * 13; hz = math.sin(a) * 17
            sp.points[i].co = (hx, -hz * math.sin(tilt), H + 5.0 + hz * math.cos(tilt), 1)
        c.bevel_depth = 1.7; c.bevel_resolution = 3; c.use_fill_caps = True
        o = bpy.data.objects.new("handle", c); SCENE.collection.objects.link(o); o.data.materials.append(WICKERD)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.convert(target='MESH'); parts.append(bpy.context.object)
        for dx in (-13, 13):   # attaches de cuir sur la charnière
            lp = prim('cube', LEATHER, size=1, location=(s_ * 26 + dx, 0, H + 3.4)); lp.scale = (3.4, 4.6, 4.2)
            mod(lp, 'BEVEL', width=0.6, segments=2); parts.append(lp)
    # sangles de cuir sur la face avant avec boucle en laiton
    for x in (-30, 30):
        st = prim('cube', LEATHER, size=1, location=(x, -D / 2 - 0.4, H - 9)); st.scale = (5, 0.7, 22); mod(st, 'BEVEL', width=0.25, segments=2); parts.append(st)
        top_ = prim('cube', LEATHER, size=1, location=(x, -D / 2 + 9, H + 3.2)); top_.scale = (5, 19, 0.6); parts.append(top_)
        bk = prim('torus', BRASS, major_radius=3.1, minor_radius=0.55, location=(x, -D / 2 - 1.0, H - 6), rotation=(math.pi / 2, 0, 0), major_segments=24, minor_segments=8)
        bk.scale = (1, 1.3, 1); smooth(bk); parts.append(bk)
    # contenu visible par l'ouverture arrière (moitié y > 0, posé sur le fond fermé z = H).
    # Agencement calculé pour que RIEN ne se traverse : torchon découpé à la forme du panier,
    # chaque objet posé sur le torchon dans sa propre zone, les baguettes empilées au fond.
    YI = D / 2 * 0.93                       # bord intérieur (y) ; demi-largeur selon y : xin(y)
    def xin(y): return W / 2 * 0.93 * max(0.0, 1 - (abs(y) / YI) ** 5) ** 0.2
    NX_, NY_ = 48, 16
    bm = bmesh.new(); grid = []
    for j in range(NY_ + 1):
        y = 0.6 + (YI - 0.6) * j / NY_
        row = []
        for i in range(NX_ + 1):
            x = xin(y) * (-1 + 2 * i / NX_)
            row.append(bm.verts.new((x, y, H + 0.9 + 0.35 * math.sin(x * 0.2) * math.cos(y * 0.25))))
        grid.append(row)
    for j in range(NY_):
        for i in range(NX_):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    me = bpy.data.meshes.new("bk_towel"); bm.to_mesh(me); bm.free()
    tw = link(me, "bk_towel"); tw.data.materials.append(TOWR)
    mod(tw, 'SOLIDIFY', thickness=0.7, offset=-1); smooth(tw); parts.append(tw)
    T = H + 1.3                             # dessus du torchon (au plus haut)
    # pommes (une rouge, une verte) : côte à côte, à gauche
    AGREEN = mat("AppleGreen", "#8cc23a", 0.3)
    AR = 6.6
    for k, (x, y) in enumerate([(-37, 13), (-23, 12.5)]):
        z = T + AR * 0.92
        ap = prim('uv_sphere', APPLE if k != 1 else AGREEN, radius=AR, location=(x, y, z), segments=32, ring_count=16)
        ap.scale = (1.0, 1.0, 0.92); smooth(ap)
        for v in ap.data.vertices:
            d = math.hypot(v.co.x, v.co.y)
            if v.co.z > 0: v.co.z -= 2.0 * math.exp(-(d / 2.4) ** 2)
        st = prim('cylinder', STEM, radius=0.55, depth=3.0, location=(x, y, z + AR * 0.92 - 0.6), rotation=(0.2, 0.1, 0), vertices=8)
        lf = prim('uv_sphere', LEAF, radius=1, location=(x + 1.9, y, z + AR * 0.92 + 0.6), segments=12, ring_count=6); lf.scale = (2.4, 1.0, 0.25); lf.rotation_euler = (0, -0.35, 0.3); smooth(lf)
        parts += [ap, st, lf]
    # bocal de confiture (au milieu, devant)
    jx, jy = -6.0, 13.5
    jr = lathe("bk_jar", [(0, 0), (5.5, 0), (6, 1), (6, 9), (5, 10.5), (5, 12), (0, 12)], JGLASS, seg=32, cap_bot=True); jr.location = (jx, jy, T); parts.append(jr)
    jm = lathe("bk_jam", [(0, 0.6), (5.3, 0.6), (5.3, 8.5), (0, 8.5)], JAM, seg=32, cap_bot=True, cap_top=True); jm.location = (jx, jy, T); parts.append(jm)
    jl = prim('cylinder', GING, radius=6.3, depth=1.0, location=(jx, jy, T + 12.5), vertices=32); smooth(jl); parts.append(jl)
    # quart de meule de fromage (à plat)
    CHEESE = mat("Cheese", "#f3c74a", 0.6)
    wedge = [(0, 0), (13, 0), (12.4, 4.0), (10.6, 7.6), (7.6, 10.6), (4.0, 12.4), (0, 13)]
    ch = outline_solid("cheese", wedge, 7.0, CHEESE, bevel=0.5, slices=20.0)
    ch.location = (4, 6.5, T + 3.5); smooth(ch); parts.append(ch)
    # grappe de raisin en pyramide (grains jointifs, sans se traverser)
    GRAPE = mat("Grape", "#6b2f7a", 0.2)
    gr_, gx, gy = 2.3, 31.0, 14.0
    sp_ = 2 * gr_ + 0.05
    hx = [(0, 0)] + [(sp_ * math.cos(k * math.pi / 3), sp_ * math.sin(k * math.pi / 3)) for k in range(6)]
    lay1 = [(sp_ / math.sqrt(3) * math.cos(k * 2 * math.pi / 3 + math.pi / 6), sp_ / math.sqrt(3) * math.sin(k * 2 * math.pi / 3 + math.pi / 6)) for k in range(3)]
    dz1 = math.sqrt(sp_ ** 2 - (sp_ / math.sqrt(3)) ** 2)
    for (dx, dy, z) in [(x, y, T + gr_) for x, y in hx] + [(x, y, T + gr_ + dz1) for x, y in lay1] + [(0, 0, T + gr_ + 2 * dz1 + 0.6)]:
        g = prim('uv_sphere', GRAPE, radius=gr_, location=(gx + dx, gy + dy, z), segments=14, ring_count=8); smooth(g); parts.append(g)
    gst = prim('cylinder', STEM, radius=0.5, depth=4, location=(gx, gy, T + gr_ + 2 * dz1 + 3.4), rotation=(0.5, 0, 0), vertices=8); parts.append(gst)
    # baguettes : couchées l'une sur l'autre le long du fond (y ≈ 27), sur le torchon
    def loaf(at, length, r=5.0):
        bg = prim('cylinder', BREAD, radius=r, depth=length, location=at, rotation=(0, math.pi / 2, 0), vertices=24)
        mod(bg, 'SUBSURF', levels=2); smooth(bg); apply(bg); parts.append(bg)
        for k in range(5):
            t = -0.32 + k * 0.16
            g_ = prim('uv_sphere', BREADL, radius=1, location=(at[0] + t * length, at[1], at[2] + r - 0.95), segments=12, ring_count=6)
            g_.scale = (3.6, 0.9, 0.9); g_.rotation_euler = (0, 0, 0.5); smooth(g_); parts.append(g_)
    LB, RB = 66.0, 5.0
    yb = 26.5
    loaf((0, yb, T + RB), LB)
    loaf((6, yb, T + 3 * RB + 0.1), LB - 10)
    o = join("basket", parts); ground(o); return o

# ═════════ BUREAU-BOÎTE D'ALLUMETTES + CHAISE (échelle fourmi) ═════════
def desk():
    """bureau de la gestionnaire : une grosse boîte d'allumettes (à l'échelle fourmi).
    Étui imprimé (bleu, cadre jaune, étoile), grattoirs bruns sur les grands côtés,
    tiroir tiré vers +X plein d'allumettes à tête rouge, et sur le dessus un
    carnet de comptes ouvert, un crayon et un encrier taillé dans une capsule."""
    W, D, H = 5.4, 2.8, 1.0
    parts = []
    # étui (carton) : boîte creuse ouverte aux deux bouts en X
    def slab(cx, cy, cz, sx, sy, sz, m, bev=0.02):
        o = prim('cube', m, size=1, location=(cx, cy, cz)); o.scale = (sx, sy, sz)
        if bev: mod(o, 'BEVEL', width=bev, segments=2)
        parts.append(o); return o
    SX = 3.6; sx0 = -W / 2 + SX / 2   # étui côté -X, le tiroir dépasse vers +X
    t = 0.06
    slab(sx0, 0, H - t / 2, SX, D, t, BOX)          # dessus
    slab(sx0, 0, t / 2, SX, D, t, BOX)              # dessous
    for sy in (-1, 1): slab(sx0, sy * (D / 2 - t / 2), H / 2, SX, t, H, BOX)   # grands côtés
    # grattoirs : bandes rugueuses brunes sur les grands côtés (légèrement en relief)
    for sy in (-1, 1): slab(sx0, sy * (D / 2 + 0.004), H / 2, SX - 0.3, 0.012, H - 0.22, STRK, bev=0)
    # impression du dessus : cadre jaune, bandeau rouge, étoile jaune
    slab(sx0, 0, H + 0.003, SX - 0.18, D - 0.18, 0.008, MYEL, bev=0)
    slab(sx0, 0, H + 0.006, SX - 0.34, D - 0.34, 0.008, BOX, bev=0)
    slab(sx0 - 0.9, 0, H + 0.009, 0.42, D - 0.34, 0.008, STRAW_R, bev=0)
    bm = bmesh.new(); pts = []
    for i in range(10):
        a = math.pi / 2 + i * math.pi / 5; r = 0.42 if i % 2 == 0 else 0.18
        pts.append(bm.verts.new((sx0 + 0.5 + math.cos(a) * r, math.sin(a) * r, H + 0.012)))
    c = bm.verts.new((sx0 + 0.5, 0, H + 0.012))
    for i in range(10): bm.faces.new((c, pts[i], pts[(i + 1) % 10]))
    me = bpy.data.meshes.new("star"); bm.to_mesh(me); bm.free()
    st = link(me, "star"); st.data.materials.append(MYEL); mod(st, 'SOLIDIFY', thickness=0.01); parts.append(st)
    # tiroir (carton jaune), tiré : de x = -W/2 + 0.6 à x = +W/2
    TX0, TX1 = -W / 2 + 0.6, W / 2
    tl = TX1 - TX0; tcx = (TX0 + TX1) / 2; ti = 0.05
    TD, TH = D - 2 * t - 0.03, H - 2 * t - 0.03
    tz = t + 0.015
    slab(tcx, 0, tz + ti / 2, tl, TD, ti, MYEL)
    for sy in (-1, 1): slab(tcx, sy * (TD / 2 - ti / 2), tz + TH / 2, tl, ti, TH, MYEL)
    for x in (TX0 + ti / 2, TX1 - ti / 2): slab(x, 0, tz + TH / 2, ti, TD, TH, MYEL)
    # allumettes rangées dans le tiroir : bois clair, têtes rouges arrondies, têtes alternées
    n = 11
    for i in range(n):
        y = -TD / 2 + 0.18 + i * (TD - 0.36) / (n - 1)
        z = tz + ti + 0.09 + (0.13 if i % 3 == 1 else 0)
        L = tl - 0.75
        sd = 1 if i % 2 == 0 else -1
        m = prim('cube', MWOOD, size=1, location=(tcx, y, z)); m.scale = (L, 0.16, 0.16); mod(m, 'BEVEL', width=0.03, segments=1); parts.append(m)
        hd = prim('uv_sphere', MHEAD, radius=0.15, location=(tcx + sd * (L / 2 + 0.06), y, z), segments=16, ring_count=8)
        hd.scale = (1.5, 1.05, 1.05); smooth(hd); parts.append(hd)
    # carnet de comptes ouvert (pages bombées) + lignes au crayon
    for side in (-1, 1):
        bm = bmesh.new(); NX = 12
        vs = []
        for i in range(NX + 1):
            u = i / NX; x = side * u * 0.9
            z = H + 0.02 + 0.07 * math.sin(u * math.pi) * (1 - u * 0.3)
            vs.append((bm.verts.new((sx0 - 0.15 + x, -0.55, z)), bm.verts.new((sx0 - 0.15 + x, 0.55, z))))
        for i in range(NX):
            f = (vs[i][0], vs[i + 1][0], vs[i + 1][1], vs[i][1])
            bm.faces.new(f if side > 0 else tuple(reversed(f)))
        me = bpy.data.meshes.new("page"); bm.to_mesh(me); bm.free()
        pg = link(me, "page"); pg.data.materials.append(PAPER); mod(pg, 'SOLIDIFY', thickness=0.03); smooth(pg)
        pg.rotation_euler = (0, 0, 0.12); parts.append(pg)
        cover = slab(sx0 - 0.15 + side * 0.47, 0, H + 0.012, 0.98, 1.18, 0.02, STRAW_R, bev=0); cover.rotation_euler = (0, 0, 0.12)
    # crayon jaune taillé, posé en travers
    pen = prim('cylinder', PYEL, radius=0.055, depth=1.3, location=(sx0 + 0.95, -0.75, H + 0.07), rotation=(0, math.pi / 2, 0.5), vertices=6); parts.append(pen)
    tip = prim('cone', MWOOD, radius1=0.055, radius2=0.0, depth=0.16, location=(sx0 + 0.95 + math.cos(0.5) * 0.73, -0.75 - math.sin(0.5) * 0.73 * -1 * -1, H + 0.07), rotation=(0, math.pi / 2, 0.5), vertices=6)
    tip.location = (sx0 + 0.95 + math.cos(0.5) * 0.73, -0.75 + math.sin(0.5) * 0.73, H + 0.07); parts.append(tip)
    era = prim('cylinder', mat("Eraser", "#e98aa0", 0.7), radius=0.058, depth=0.12, location=(sx0 + 0.95 - math.cos(0.5) * 0.7, -0.75 - math.sin(0.5) * 0.7, H + 0.07), rotation=(0, math.pi / 2, 0.5), vertices=12); parts.append(era)
    # encrier : capsule de bouteille dorée crantée, pleine d'encre
    ink = lathe("inkpot", [(0.0, H), (0.3, H), (0.33, H + 0.03), (0.3, H + 0.16), (0.27, H + 0.16), (0.26, H + 0.04), (0.0, H + 0.04)], BRASS, seg=24)
    for v in ink.data.vertices:
        a = math.atan2(v.co.y, v.co.x); r = math.hypot(v.co.x, v.co.y)
        if r > 0.29: k = 1 + 0.05 * math.cos(a * 21); v.co.x *= k; v.co.y *= k
    ink.location = (sx0 + 1.25, 0.7, 0); apply(ink); parts.append(ink)
    dark = prim('cylinder', mat("Ink", "#1a1840", 0.1), radius=0.26, depth=0.02, location=(sx0 + 1.25, 0.7, H + 0.13), vertices=24); parts.append(dark)
    o = join("desk", parts); ground(o); return o

def chair():
    """chaise de bureau en bois tourné : pieds galbés reliés par des barreaux,
    dossier à fuseaux surmonté d'une traverse cintrée, coussin vichy."""
    parts = []
    SH = 0.62
    for (x, y) in [(0.42, 0.42), (-0.42, 0.42), (0.42, -0.42), (-0.42, -0.42)]:
        leg = lathe("leg", [(0.0, 0.0), (0.035, 0.0), (0.05, 0.08), (0.04, 0.2), (0.055, 0.32), (0.04, 0.45), (0.05, SH - 0.04), (0.0, SH)], WOODH, seg=12)
        leg.location = (x, y, 0); apply(leg); parts.append(leg)
    for (a, b) in [((0.42, 0.42), (0.42, -0.42)), ((-0.42, 0.42), (-0.42, -0.42)), ((0.42, -0.42), (-0.42, -0.42))]:
        mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        L = math.hypot(a[0] - b[0], a[1] - b[1])
        r = prim('cylinder', WOODH, radius=0.022, depth=L, location=(mx, my, 0.2), rotation=(math.pi / 2 if a[0] == b[0] else 0, 0 if a[0] == b[0] else math.pi / 2, 0), vertices=10); parts.append(r)
    seat = prim('cube', WOODH, size=1, location=(0, 0, SH + 0.03)); seat.scale = (0.98, 0.98, 0.07); mod(seat, 'BEVEL', width=0.03, segments=2); parts.append(seat)
    cu = prim('cube', mat("CushionGingham", "#c62c2e", 0.85), size=1, location=(0, 0, SH + 0.12)); cu.scale = (0.86, 0.86, 0.12)
    mod(cu, 'BEVEL', width=0.05, segments=4); mod(cu, 'SUBSURF', levels=1); smooth(cu); parts.append(cu)
    # dossier : deux montants, fuseaux, traverse cintrée
    for x in (-0.42, 0.42):
        p = lathe("post", [(0.0, 0.0), (0.04, 0.0), (0.045, 0.3), (0.035, 0.6), (0.05, 0.75), (0.0, 0.8)], WOODH, seg=12)
        p.location = (x, 0.42, SH + 0.06); apply(p); parts.append(p)
    for i in range(4):
        x = -0.27 + i * 0.18
        sp = lathe("spindle", [(0.0, 0.0), (0.018, 0.0), (0.03, 0.25), (0.018, 0.5), (0.0, 0.5)], WOODH, seg=8)
        sp.location = (x, 0.43, SH + 0.1); apply(sp); parts.append(sp)
    bm = bmesh.new(); N = 16; vs = []
    for i in range(N + 1):
        u = i / N; x = -0.46 + 0.92 * u; y = 0.43 + 0.05 * math.sin(u * math.pi)
        vs.append((bm.verts.new((x, y, SH + 0.6)), bm.verts.new((x, y, SH + 0.78))))
    for i in range(N): bm.faces.new((vs[i][0], vs[i + 1][0], vs[i + 1][1], vs[i][1]))
    me = bpy.data.meshes.new("rail"); bm.to_mesh(me); bm.free()
    rl = link(me, "rail"); rl.data.materials.append(WOODH); mod(rl, 'SOLIDIFY', thickness=0.06, offset=0); mod(rl, 'BEVEL', width=0.015, segments=2); smooth(rl); parts.append(rl)
    o = join("chair", parts); ground(o); return o

# ═════════ NOUVEAUTÉS : petite cuillère, fraise, feuilles mortes ═════════
def spoon():
    """petite cuillère en inox posée sur la nappe : un seul volume lissé, construit
    par sections le long de sa longueur — cuilleron ovale creux (bord fin, dos
    galbé qui repose sur la nappe), col étroit qui remonte, manche plat légèrement
    bombé qui s'élargit en spatule arrondie et retombe jusqu'à la nappe."""
    L0, L1 = -42.0, 38.0                    # bout du manche → pointe du cuilleron
    CX, A, B = 24.0, 14.0, 8.4              # cuilleron : centre, demi-longueur, demi-largeur
    D, T = 3.6, 0.75                        # profondeur du cuilleron, épaisseur du métal
    hw = spline([(-42, 0.0), (-41.2, 1.9), (-39.5, 2.9), (-36, 3.4), (-30, 3.2), (-18, 2.2), (-2, 1.25), (6, 1.05), (10, 1.15)])
    def half_w(x):
        u = (x - CX) / A
        wb = B * (1 + 0.1 * u) * math.sqrt(max(0.0, 1 - u * u)) if abs(u) < 1 else 0.0
        wh = hw(x) if x < CX - A * 0.55 else 0.0
        if CX - A < x < CX - A * 0.55:          # raccord doux col → cuilleron
            k = (x - (CX - A)) / (A * 0.45)
            wh = hw(x) * (1 - k)
        return max(wb, wh, 0.02)
    # hauteur du bord (ligne médiane) : cuilleron posé, col qui remonte, manche qui retombe
    hz = spline([(-42, 0.55), (-34, 0.75), (-20, 1.8), (-2, 3.9), (8, 4.6), (12, D + 0.35), (CX, D + 0.2), (L1, D + 0.2)])
    def dish(x):
        u = (x - CX) / A
        return D * max(0.0, 1 - u * u) ** 0.85 if abs(u) < 1 else 0.0
    NX, NY = 200, 28
    bm = bmesh.new(); rings = []
    for i in range(NX + 1):
        x = L0 + (L1 - L0) * i / NX
        w = half_w(x); h = hz(x); d = dish(x)
        top, bot = [], []
        for j in range(NY + 1):
            t = -1 + 2 * j / NY
            y = w * t
            prof = (1 - t * t)
            if d > 0:
                zt = h + T - d * prof ** 0.9         # creux du cuilleron
                zb = zt - T
            else:
                zt = h + T + 0.28 * prof              # manche légèrement bombé
                zb = h - 0.05 * prof
            top.append(bm.verts.new((x, y, zt)))
            bot.append(bm.verts.new((x, y, zb)))
        rings.append(top + list(reversed(bot)))
    M = len(rings[0])
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(M):
            k2 = (k + 1) % M
            bm.faces.new((r0[k], r1[k], r1[k2], r0[k2]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-3)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("spoon"); bm.to_mesh(me); bm.free()
    o = link(me, "spoon"); o.data.materials.append(STEEL); smooth(o)
    apply(o); ground(o); return o

def strawberry():
    """fraise géante couchée : cône arrondi à épaules, akènes dans de petits
    creux, collerette de sépales et queue."""
    Hs, R = 32.0, 12.0
    prof = []
    for i in range(41):
        t = i / 40                       # 0 = pointe, 1 = côté queue
        r = R * (math.sin(min(1.0, t / 0.78) * math.pi / 2) ** 1.15)
        if t > 0.78: r = R * math.cos((t - 0.78) / 0.22 * math.pi / 2 * 0.86)
        prof.append((max(0.05, r), t * Hs))
    prof.append((0.0, Hs * 0.995))
    s = lathe("strawberry_b", prof, STRAW_R, seg=64)
    seeds = []
    rnd = random.Random(4)
    # akènes en quinconce ; chacun dans un léger creux
    rows, per = 11, 16
    for rI in range(rows):
        t = 0.1 + 0.72 * rI / (rows - 1)
        rr_ = R * (math.sin(min(1.0, t / 0.78) * math.pi / 2) ** 1.15)
        for k in range(per):
            a = 2 * math.pi * (k + 0.5 * (rI % 2)) / per + rnd.uniform(-0.05, 0.05)
            pos = Vector((math.cos(a) * rr_, math.sin(a) * rr_, t * Hs))
            for v in s.data.vertices:
                d = (v.co - pos).length
                if d < 1.3: v.co -= Vector((math.cos(a), math.sin(a), 0)) * 0.45 * (1 - d / 1.3) ** 2
            sd = prim('uv_sphere', PYEL, radius=0.42, location=pos - Vector((math.cos(a), math.sin(a), 0)) * 0.15, segments=8, ring_count=5)
            sd.scale = (1, 0.7, 1.5); sd.rotation_euler = (0, 0, a); seeds.append(sd)
    mod(s, 'SUBSURF', levels=1); smooth(s)
    sep = []
    for i in range(7):
        lf = leaf_mesh(10, 3.4, GREEN); lf.location = (0, 0, Hs * 0.985); lf.rotation_euler = (0, math.radians(-12), i * 2 * math.pi / 7)
        for v in lf.data.vertices: v.co.z -= 0.025 * v.co.x ** 2   # sépales qui retombent
        sep.append(lf)
    st = stem_curve([(0, 0, Hs - 0.5), (0.5, 0.3, Hs + 3), (2.0, 0.8, Hs + 6)], r=0.9)
    st.data.materials.clear(); st.data.materials.append(GREEN); sep.append(st)
    o = join("strawberry", [s] + seeds + sep)
    o.rotation_euler = (math.radians(96), 0, 0.4); apply(o); ground(o); return o

def dead_leaf():
    """feuille de chêne morte : lobes arrondis irréguliers, base en cœur, nervures
    gravées dans le limbe (en creux dessus, en relief dessous), bords qui se
    recroquevillent, pétiole qui prolonge la nervure médiane."""
    L = 46.0
    rnd = random.Random(7)
    lob = [(0.16, 0.55), (0.30, 0.85), (0.46, 1.0), (0.62, 0.92), (0.77, 0.72), (0.90, 0.45)]
    def half(t, side):
        # enveloppe + lobes (chaque lobe : bosse arrondie, sinus profond entre deux)
        env = 12.5 * math.sin(math.pi * min(1.0, t)) ** 0.6 * (1 - 0.3 * t)
        best = 0.0
        for k, (c, a) in enumerate(lob):
            c2 = c + (0.015 if side > 0 else -0.012) * (k % 2)
            w = 0.075
            best = max(best, a * math.exp(-((t - c2) / w) ** 2))
        base = 0.42 + 0.58 * best
        return max(0.35, env * base)
    n = 220
    top = [(L * i / n, half(i / n, 1)) for i in range(1, n)]
    bot = [(L * i / n, -half(i / n, -1) * 0.95) for i in range(n - 1, 0, -1)]
    o = outline_solid("leaf_dry", [(0.0, 0.0)] + top + [(L, 0.0)] + bot, 0.45, mat("LeafDry", "#c97b2c", 0.7), slices=0.8)
    def curl(x, y):
        t = x / L
        return 0.02 * y * y * (1 + 0.8 * t) + 2.2 * (t - 0.45) ** 2 * 4 * 0.5 + 0.35 * math.sin(x * 0.3) + 0.6 * math.sin(t * math.pi) * 0.5
    # nervures gravées : on creuse le dessus du limbe le long de la médiane et des secondaires
    sec = []
    for k, (c, a) in enumerate(lob):
        for sd in (1, -1):
            x0 = L * (c - 0.07)
            tip = (L * c + 3.0, sd * half(c, sd) * 0.9)
            sec.append(((x0, 0.0), tip))
    def seg_d(px, py, a, b):
        ax, ay = a; bx, by = b; dx, dy = bx - ax, by - ay
        t = max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
        return math.hypot(px - ax - dx * t, py - ay - dy * t)
    for v in o.data.vertices:
        x, y, z = v.co
        groove = 0.0
        d0 = abs(y)
        groove = max(groove, 0.28 * max(0, 1 - d0 / 0.5))
        for (a_, b_) in sec:
            dd = seg_d(x, y, a_, b_)
            groove = max(groove, 0.16 * max(0, 1 - dd / 0.35))
        if z > 0: z -= groove
        else: z -= groove * 0.6      # relief en dessous
        v.co.z = z + curl(x, y)
    smooth(o)
    # pétiole : prolonge la médiane, légèrement relevé, tranché au bout
    c = bpy.data.curves.new("petiole", 'CURVE'); c.dimensions = '3D'
    sp = c.splines.new('POLY'); pts = [(1.5, 0, curl(1.5, 0)), (-2.0, 0.2, curl(0, 0) + 0.2), (-6.0, 0.6, curl(0, 0) + 0.9), (-8.5, 0.9, curl(0, 0) + 1.6)]
    sp.points.add(len(pts) - 1)
    for p_, q in zip(sp.points, pts): p_.co = (*q, 1)
    c.bevel_depth = 0.55; c.bevel_resolution = 2; c.use_fill_caps = True
    pet = bpy.data.objects.new("petiole", c); SCENE.collection.objects.link(pet); pet.data.materials.append(mat("LeafVein", "#9a5520", 0.75))
    bpy.ops.object.select_all(action='DESELECT'); pet.select_set(True); bpy.context.view_layer.objects.active = pet
    bpy.ops.object.convert(target='MESH'); pet = bpy.context.object
    o = join("leaf_dry", [o, pet]); ground(o); return o

BUILD = [fork, knife, bottle, bowl, cherries, cherry, crown, glass, jar, napkin, fruit_salad, sugar, basket, desk, chair, spoon, dead_leaf]

def build_all():
    for o in list(SCENE.objects): bpy.data.objects.remove(o)
    for o in bpy.data.objects:   # libère les noms pris par d'autres scènes
        if not o.name.startswith("old_") and o.users_scene and SCENE not in o.users_scene: o.name = "old_" + o.name
    for f in BUILD:
        o = f(); made[o.name] = o
    c = cork(); c = corkscrew_into(c); ground(c); made["cork"] = c
    # rangés en ligne pour l'aperçu
    x = 0
    for name in sorted(made):
        o = made[name]
        d = max(o.dimensions.x, o.dimensions.y)
        o.location = (x + d / 2, 0, 0); x += d + 12
    for name, o in made.items():
        print("PROP", name, tuple(round(v, 1) for v in o.dimensions), len(o.data.polygons))

def export(path):
    for o in made.values(): o.location = (0, 0, 0)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_active_scene=True, export_apply=True)
    print("EXPORTED", path)

if not globals().get("NO_BUILD"):
    build_all()
    if OUT: export(OUT)
