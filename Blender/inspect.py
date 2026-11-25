import bpy, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=sys.argv[-1])
for o in bpy.data.objects:
    if o.type=='MESH':
        d=o.dimensions
        print("OBJ", o.name, tuple(round(x,1) for x in o.location), tuple(round(x,1) for x in d), len(o.data.polygons), [m.name for m in o.data.materials], len(o.data.color_attributes))
