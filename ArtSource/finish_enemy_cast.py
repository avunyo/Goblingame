import bpy,json,math
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent.parent
src=root/'ArtSource/EnemyCast';out=root/'Assets/EnemyCast'
def linear(v):return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
report=[]
for label in ['RatRogue','MossGourmand','BoneSentinel','IronTusk']:
 bpy.ops.wm.open_mainfile(filepath=str(src/(label+'.blend')))
 rig=bpy.data.objects['Armature'];scene=bpy.context.scene
 for m in bpy.data.materials:
  if m.use_nodes:
   p=m.node_tree.nodes.get('Principled BSDF')
   if p:p.inputs['Base Color'].default_value=(*[linear(c) for c in m.diffuse_color[:3]],1)
 scene.view_settings.view_transform='AgX'
 try:scene.view_settings.look='AgX - Medium High Contrast'
 except:pass
 # Matching start/end positions and bone motion checked directly from Blender actions.
 actions={a.name:a for a in bpy.data.actions};assert 'Idle' in actions and 'Walk' in actions
 rig.animation_data.action=actions['Walk'];scene.frame_set(1)
 first={b.name:b.matrix.copy() for b in rig.pose.bones};scene.frame_set(8)
 motion=max(first[b.name].to_quaternion().rotation_difference(b.matrix.to_quaternion()).angle for b in rig.pose.bones)
 scene.frame_set(31);seam=max((first[b.name].translation-b.matrix.translation).length for b in rig.pose.bones)
 assert motion>.1 and seam<.0001,(motion,seam)
 rig.animation_data.action=actions['Idle'];scene.frame_end=121;scene.frame_set(1)
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
 for o in bpy.data.objects:
  if o.type=='MESH':o.select_set(True)
 bpy.context.view_layer.objects.active=rig
 bpy.ops.wm.save_as_mainfile(filepath=str(src/(label+'.blend')))
 bpy.ops.export_scene.fbx(filepath=str(out/(label+'.fbx')),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
 scene.render.filepath=str(src/(label+'.png'));bpy.ops.render.render(write_still=True)
 # Separate full-body card and head portrait, rendered from the actual 3D model.
 bpy.data.images['Render Result'].save_render(str(out/(label+'_Card.png')),scene=scene)
 cam=scene.camera;head=rig.matrix_world@rig.pose.bones['Head'].tail
 centre=Vector((0,-.1,head.z-.1));cam.location=centre+Vector((1.3,-4,.25));cam.rotation_euler=(centre-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=1.25 if label!='MossGourmand' else 2.0
 scene.render.resolution_x=384;scene.render.resolution_y=384;scene.render.filepath=str(out/(label+'_Portrait.png'));bpy.ops.render.render(write_still=True)
 report.append({'name':label,'walkMotionRadians':motion,'loopPositionSeam':seam,'bones':len(rig.pose.bones),'actions':list(actions)})
(src/'validation.json').write_text(json.dumps(report,indent=2))

