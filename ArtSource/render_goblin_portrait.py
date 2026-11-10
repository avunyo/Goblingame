import bpy
from pathlib import Path
from mathutils import Vector
r=Path(__file__).resolve().parent.parent
bpy.ops.wm.open_mainfile(filepath=str(r/'ArtSource/Goblin_Idle_Natural.blend'))
s=bpy.context.scene;s.frame_set(1)
body=bpy.data.objects['Cube'];rig=bpy.data.objects['Armature'];head=rig.matrix_world@rig.pose.bones['mixamorig:Head'].head
for o in list(bpy.data.objects):
 if o.type in {'CAMERA','LIGHT'}:bpy.data.objects.remove(o,do_unlink=True)
# The goblin's existing UV texture is reused without changing it.
m=bpy.data.materials.new('Portrait Goblin');m.use_nodes=True
tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(r/'Assets/GoblinArt/GoblinBaseColor.png'))
m.node_tree.links.new(tex.outputs['Color'],m.node_tree.nodes['Principled BSDF'].inputs['Base Color']);m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.85
body.data.materials.clear();body.data.materials.append(m)
uv=body.data.uv_layers.active.data
sets=[]
for cx,cy in ((.442,.780),(.566,.774)):
 ids={body.data.loops[i].vertex_index for i in range(len(uv)) if (uv[i].uv.x-cx)**2+(uv[i].uv.y-cy)**2<.009**2}
 ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get())
 sets.append(sum((ev.matrix_world@ev.data.vertices[i].co for i in ids),Vector())/len(ids))
size=(sets[0]-sets[1]).length*3.5
centre=(sets[0]+sets[1])/2+Vector((0,0,-size*.08))
bpy.ops.object.camera_add(location=centre+Vector((.1,-3,.08))*size);cam=bpy.context.object;cam.rotation_euler=(centre-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=size;s.camera=cam
for pos,power in [((-3,-4,6),850),((3,-2,5),350)]:
 bpy.ops.object.light_add(type='AREA',location=pos);l=bpy.context.object;l.data.energy=power;l.data.size=4;l.rotation_euler=(centre-l.location).to_track_quat('-Z','Y').to_euler()
s.render.engine='BLENDER_EEVEE';s.render.resolution_x=384;s.render.resolution_y=384;s.render.resolution_percentage=100;s.render.film_transparent=True;s.render.image_settings.file_format='PNG';s.render.filepath=str(r/'Assets/EnemyCast/Goblin_Portrait.png');bpy.ops.render.render(write_still=True)
