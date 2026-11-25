import bpy, sys, math
from mathutils import Vector
a = sys.argv[sys.argv.index('--')+1:]
glb, png = a[0], a[1]
cam_loc = Vector(tuple(float(x) for x in a[2].split(','))); tgt = Vector(tuple(float(x) for x in a[3].split(',')))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
sc = bpy.context.scene
for e in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = e; break
    except TypeError: pass
sc.render.resolution_x, sc.render.resolution_y = 900, 600
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.6, 0.75, 0.9, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
bpy.ops.mesh.primitive_plane_add(size=40); g = bpy.context.object
m = bpy.data.materials.new("g"); m.use_nodes = True; next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs[0].default_value = (0.3, 0.5, 0.15, 1); g.data.materials.append(m)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); sc.collection.objects.link(sun); sun.rotation_euler = (0.8, 0.3, 0.6); sun.data.energy = 3
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.location = cam_loc; cam.rotation_euler = (tgt - cam_loc).to_track_quat('-Z', 'Y').to_euler()
sc.render.filepath = png
bpy.ops.render.render(write_still=True)
