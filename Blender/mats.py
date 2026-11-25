import bpy, sys
bpy.ops.wm.read_factory_settings(use_empty=True)
for f in sys.argv[sys.argv.index('--')+1:]:
    bpy.ops.import_scene.gltf(filepath=f)
seen=set()
for m in bpy.data.materials:
    if not m.node_tree: continue
    p=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
    if not p: continue
    c=p.inputs['Base Color'].default_value
    tex = any(n.type=='TEX_IMAGE' for n in m.node_tree.nodes)
    print("MAT", m.name, '#'+''.join('%02x'%int(255*min(1,x**(1/2.2))) for x in c[:3]), 'a=%.2f'%p.inputs['Alpha'].default_value, 'r=%.2f'%p.inputs['Roughness'].default_value,'m=%.2f'%p.inputs['Metallic'].default_value, 'tr=%.2f'%p.inputs['Transmission Weight'].default_value, 'em='+str(tuple(round(x,2) for x in p.inputs['Emission Color'].default_value[:3])), 'tex' if tex else '')
