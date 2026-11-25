# Entrées de livraison de la fourmilière (Models/docks.glb).
# Origine = centre de la case du guichet, au sol ; +Y (Blender) pointe vers le nid.
import bpy, bmesh, math, random, sys
from mathutils import Vector, noise
bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(7)
out = sys.argv[sys.argv.index('--')+1]

def mat(name, col, rough=0.8, emit=None, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value = (*col, 1)
    p.inputs['Roughness'].default_value = rough
    p.inputs['Metallic'].default_value = metal
    if emit:
        p.inputs['Emission Color'].default_value = (*emit, 1)
        p.inputs['Emission Strength'].default_value = 3.0
    return m

def srgb(h):
    h = h.lstrip('#'); c = [int(h[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

M_SOIL = mat("Soil", srgb("#7a4e2c"), 0.95)
M_DARK = mat("DarkHole", srgb("#140b05"), 1.0)
M_WOOD = mat("Wood", srgb("#ae7946"), 0.7)
M_WOODD = mat("WoodDark", srgb("#7c5233"), 0.75)
M_STONE = mat("Stone", srgb("#9a938a"), 0.8)
M_IRON = mat("Iron", srgb("#3c3a38"), 0.5, metal=0.7)
M_BULB = mat("Bulb", srgb("#fff1ae"), 0.2, emit=(1.0, 0.65, 0.15))
M_ROPE = mat("Rope", srgb("#d6c298"), 0.9)

def obj(name, me):
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o

def join(name, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    objs[0].name = name
    return objs[0]

def apply_all(o):
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
    for m in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)

# ── Tunnel de terre : arche extrudée vers le nid, bouche évasée ──
def arch(w, h, n=20):
    pts = []
    for i in range(n + 1):
        a = math.pi * i / n
        pts.append((math.cos(a) * w, math.sin(a) ** 0.85 * h))
    return pts
bm = bmesh.new()
NY = 14
rings = []
for j in range(NY + 1):
    t = j / NY
    y = -0.05 + t * 3.4
    flare = 1 + 0.35 * max(0, 1 - t * 4) ** 2           # bouche évasée
    wo, ho = 1.05 * flare, 1.08 * (0.85 + 0.15 * min(1, t * 3))
    wi, hi = 0.6, 0.7
    outer = [bm.verts.new((x, y, z)) for x, z in arch(wo, ho)]
    inner = [bm.verts.new((x, y, z)) for x, z in arch(wi, hi)]
    rings.append((outer, inner))
for j in range(NY):
    (o0, i0), (o1, i1) = rings[j], rings[j + 1]
    for k in range(len(o0) - 1):
        f = bm.faces.new((o0[k], o0[k + 1], o1[k + 1], o1[k])); f.material_index = 0
        f = bm.faces.new((i0[k + 1], i0[k], i1[k], i1[k + 1])); f.material_index = 1
# face avant (entre l'arche intérieure et extérieure)
o0, i0 = rings[0]
for k in range(len(o0) - 1):
    f = bm.faces.new((o0[k + 1], o0[k], i0[k], i0[k + 1])); f.material_index = 0
# fond noir dans le tunnel
oN, iN = rings[2]
f = bm.faces.new(list(reversed(iN))); f.material_index = 1
bm.normal_update()
me = bpy.data.meshes.new("dock_mound"); bm.to_mesh(me); bm.free()
mound = obj("dock_mound", me)
me.materials.append(M_SOIL); me.materials.append(M_DARK)
# relief organique
sub = mound.modifiers.new("s", 'SUBSURF'); sub.levels = 2
apply_all(mound)
for v in mound.data.vertices:
    p = v.co
    d = noise.noise(Vector((p.x * 2.1, p.y * 2.1, p.z * 2.1))) * 0.09 + noise.noise(Vector((p.x * 6, p.y * 6, p.z * 6))) * 0.025
    r = Vector((p.x, 0, p.z))
    if r.length > 0.62 and p.z > 0.02: v.co += r.normalized() * d
    if p.z < 0.0: v.co.z = 0.0
for poly in mound.data.polygons: poly.use_smooth = True

# grumeaux de terre autour de la bouche et cailloux
lumps = []
for i in range(14):
    side = -1 if i % 2 else 1
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=random.uniform(0.12, 0.22), location=(side * random.uniform(1.05, 1.5), random.uniform(-0.1, 0.8), random.uniform(-0.04, 0.25)))
    l = bpy.context.object; l.scale.z = 0.6; l.data.materials.append(M_SOIL); lumps.append(l)
for l in lumps:
    for p in l.data.polygons: p.use_smooth = True
stones = []
for i in range(9):
    side = random.choice([-1, 1])
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=random.uniform(0.06, 0.13), location=(side * random.uniform(0.85, 1.5), random.uniform(-0.6, 0.3), 0.02))
    s = bpy.context.object; s.scale = (1, random.uniform(0.7, 1.2), 0.55); s.data.materials.append(M_STONE); stones.append(s)
    for p in s.data.polygons: p.use_smooth = True
apply_all(mound)
mound = join("dock_mound", [mound] + lumps)
stones_o = join("dock_stones", stones)

# ── Portique en bois de la bouche : deux poteaux, linteau, lanterne ──
parts = []
for sx in (-0.72, 0.72):
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.07, depth=1.25, location=(sx, -0.08, 0.62))
    p = bpy.context.object; p.rotation_euler = (random.uniform(-0.04, 0.04), sx * 0.05, 0); p.data.materials.append(M_WOODD); parts.append(p)
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, -0.08, 1.24))
lin = bpy.context.object; lin.scale = (1.8, 0.16, 0.13); lin.rotation_euler = (0, 0.03, 0); lin.data.materials.append(M_WOOD); parts.append(lin)
bev = lin.modifiers.new("b", 'BEVEL'); bev.width = 0.03; bev.segments = 2
frame = join("dock_frame", parts); apply_all(frame)

lamp = []
bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.008, depth=0.2, location=(0.55, -0.18, 1.08)); c = bpy.context.object; c.data.materials.append(M_ROPE); lamp.append(c)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=0.1, location=(0.55, -0.18, 0.92)); b = bpy.context.object; b.scale.z = 1.2; b.data.materials.append(M_BULB); lamp.append(b)
for p in b.data.polygons: p.use_smooth = True
bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.07, depth=0.04, location=(0.55, -0.18, 1.04)); cap = bpy.context.object; cap.data.materials.append(M_IRON); lamp.append(cap)
lantern = join("dock_lantern", lamp)

# ── Sol sombre du tunnel (le tapis s'y enfonce) ──
bpy.ops.mesh.primitive_plane_add(size=1, location=(0, 1.9, 0.004))
fl = bpy.context.object; fl.scale = (1.25, 3.0, 1); fl.name = "dock_floor"; fl.data.materials.append(M_DARK)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in bpy.data.objects: print('OBJ', o.name, tuple(round(v,2) for v in o.dimensions))
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_active_scene=True, export_apply=True)
print("EXPORTED", out)
