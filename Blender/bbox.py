import bpy, sys, mathutils
bpy.ops.wm.read_factory_settings(use_empty=True)
for f in sys.argv[sys.argv.index('--')+1:]:
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)
    bpy.ops.import_scene.gltf(filepath=f)
    for o in bpy.data.objects:
        if o.type=='MESH':
            bb=[o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
            print("BB", o.name, [round(min(v[i] for v in bb),2) for i in range(3)], [round(max(v[i] for v in bb),2) for i in range(3)], len(o.data.polygons))
