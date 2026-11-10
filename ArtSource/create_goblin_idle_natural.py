"""Bake a 12-second relaxed idle using the existing goblin/Mixamo bones."""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix
root=Path(__file__).resolve().parent.parent
source=root/'ArtSource/Goblin_Idle_Curious.blend'
before=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source))
scene=bpy.context.scene; rig=bpy.data.objects['Armature']; mesh=bpy.data.objects['Cube']
scene.frame_set(1)
base={b.name:(b.location.copy(),b.rotation_quaternion.copy(),b.scale.copy()) for b in rig.pose.bones}
rig.animation_data_clear(); rig.animation_data_create().action=bpy.data.actions.new('Goblin_Idle_Natural')
scene.render.fps=30; scene.frame_start=1; scene.frame_end=361
world_up=Vector((0,0,1))
up=(rig.matrix_world.inverted().to_3x3()@world_up).normalized()
right=(rig.pose.bones['mixamorig:RightUpLeg'].head-rig.pose.bones['mixamorig:LeftUpLeg'].head).normalized()
forward=-right.cross(up).normalized()
height=max(v.co.z for v in mesh.data.vertices)-min(v.co.z for v in mesh.data.vertices)
# Use rig-space height, since mesh and armature may have different object scales.
height=abs((rig.pose.bones['mixamorig:Head'].head-rig.pose.bones['mixamorig:LeftFoot'].head).dot(up))*1.2
def env(t,a,b,ramp=.7):
    if t<=a or t>=b:return 0.
    x=min((t-a)/ramp,(b-t)/ramp,1.)
    return x*x*(3-2*x)
def aim(bone,target):
    direction=target-bone.head
    if direction.length<1e-6:return
    delta=(bone.tail-bone.head).normalized().rotation_difference(direction.normalized())
    bone.matrix=Matrix.Translation(bone.head) @ delta.to_matrix().to_4x4() @ bone.matrix.to_3x3().to_4x4()
    bpy.context.view_layer.update()
def solve(upper,lower,end,target,pole):
    a=rig.pose.bones[upper]; b=rig.pose.bones[lower]; c=rig.pose.bones[end]
    origin=a.head.copy(); l1=(b.head-origin).length; l2=(c.head-b.head).length
    direction=target-origin; distance=max(.0001,min(direction.length,(l1+l2)*.995))
    axis=direction.normalized(); bend=pole-axis*pole.dot(axis)
    if bend.length<1e-5: bend=axis.cross(up)
    bend.normalize()
    along=(l1*l1-l2*l2+distance*distance)/(2*distance)
    elbow=origin+axis*along+bend*math.sqrt(max(0,l1*l1-along*along))
    aim(a,elbow); aim(b,origin+axis*distance)
diagnostics=[]
for frame in range(1,362):
    t=(frame-1)/30
    for bone in rig.pose.bones:
        bone.rotation_mode='QUATERNION'
        bone.location, bone.rotation_quaternion, bone.scale=base[bone.name]
    breath=math.sin(2*math.pi*t/4)
    toe=env(t,2,4.8); scratch=env(t,7,10.8,1.1)
    nod=-math.radians(14)*toe-math.radians(5)*scratch
    glance=math.radians(9)*env(t,5.3,6.7,.5)
    for name,angle,axis in [('mixamorig:Spine1',math.radians(.6)*breath,(1,0,0)),
                             ('mixamorig:Spine2',math.radians(.8)*breath+nod*.12,(1,0,0)),
                             ('mixamorig:Neck',nod*.3,(1,0,0)),('mixamorig:Head',nod*.7,(1,0,0))]:
        bone=rig.pose.bones[name]; bone.rotation_quaternion=base[name][1]@Quaternion(axis,angle)
    rig.pose.bones['mixamorig:Head'].rotation_quaternion @= Quaternion((0,1,0),glance)
    bpy.context.view_layer.update()
    # The left leg stays planted. A small right-foot scuff returns to the same spot.
    foot=rig.pose.bones['mixamorig:RightFoot']; foot_matrix=foot.matrix.copy()
    if toe>0:
        foot_target=foot.head+forward*height*.045*toe+up*height*.006*toe
        solve('mixamorig:RightUpLeg','mixamorig:RightLeg','mixamorig:RightFoot',foot_target,forward)
        foot.matrix=Matrix.Translation(foot.head)@foot_matrix.to_3x3().to_4x4()
        bpy.context.view_layer.update()
    # Right hand scratches the side of the belly, then returns to rest.
    if scratch>0:
        hand=rig.pose.bones['mixamorig:RightHand']
        target=rig.pose.bones['mixamorig:Hips'].head+up*height*.13+forward*height*.15+right*height*.08
        target+=up*height*.008*math.sin(2*math.pi*1.6*(t-7))
        # Blend the whole arm pose, not just the hand target: an IK solver can
        # choose a different elbow/roll even when the hand reaches its rest spot.
        arm_names=('mixamorig:RightArm','mixamorig:RightForeArm','mixamorig:RightHand')
        resting={n:(rig.pose.bones[n].location.copy(),rig.pose.bones[n].rotation_quaternion.copy(),rig.pose.bones[n].scale.copy()) for n in arm_names}
        solve('mixamorig:RightArm','mixamorig:RightForeArm','mixamorig:RightHand',target,forward+right-up*.3)
        solved={n:(rig.pose.bones[n].location.copy(),rig.pose.bones[n].rotation_quaternion.copy(),rig.pose.bones[n].scale.copy()) for n in arm_names}
        for n in arm_names:
            bone=rig.pose.bones[n]; start_loc,start_rot,start_scale=resting[n]; end_loc,end_rot,end_scale=solved[n]
            bone.location=start_loc.lerp(end_loc,scratch)
            bone.rotation_quaternion=start_rot.slerp(end_rot,scratch)
            bone.scale=start_scale.lerp(end_scale,scratch)
        bpy.context.view_layer.update()
        diagnostics.append({'frame':frame,'reach_error':(hand.head-target).length,'weight':scratch})
    for bone in rig.pose.bones:
        bone.keyframe_insert(data_path='location',frame=frame,group=bone.name)
        bone.keyframe_insert(data_path='rotation_quaternion',frame=frame,group=bone.name)
        bone.keyframe_insert(data_path='scale',frame=frame,group=bone.name)

def vertices(frame):
    scene.frame_set(frame); dg=bpy.context.evaluated_depsgraph_get(); obj=mesh.evaluated_get(dg); data=obj.to_mesh()
    result=[mesh.matrix_world@v.co for v in data.vertices]; obj.to_mesh_clear(); return result
first=vertices(1); last=vertices(361)
seam=max((a-b).length for a,b in zip(first,last)); assert seam<1e-4
blend=root/'ArtSource/Goblin_Idle_Natural.blend'; fbx=root/'Assets/GoblinArt/Goblin_Idle_Natural.fbx'
scene.frame_set(1); bpy.ops.wm.save_as_mainfile(filepath=str(blend))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,
    bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True,bake_anim_step=1.,bake_anim_simplify_factor=0.,axis_forward='-Z',axis_up='Y')
report={'seconds':12,'seam':seam,'source_unchanged':before==hashlib.sha256(source.read_bytes()).hexdigest(),
    'scratch_reach_error_max':max(d['reach_error'] for d in diagnostics if d['weight']>.99)}
(root/'Temp/GoblinExport/natural_idle_report.json').write_text(json.dumps(report,indent=2))
# Preview the two gestures without adding preview objects to the saved .blend.
camdata=bpy.data.cameras.new('Preview');cam=bpy.data.objects.new('Preview',camdata);scene.collection.objects.link(cam);scene.camera=cam
points=vertices(1);low=Vector(tuple(min(v[i] for v in points) for i in range(3)));high=Vector(tuple(max(v[i] for v in points) for i in range(3)))
center=(low+high)/2;h=high.z-low.z
fwd=rig.matrix_world.to_3x3()@forward;rt=rig.matrix_world.to_3x3()@right
cam.location=center+fwd.normalized()*h*2.5+rt.normalized()*h*.7+world_up*h*.35
cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();camdata.type='ORTHO';camdata.ortho_scale=h*1.4
scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='TEXTURE'
scene.render.resolution_x=640;scene.render.resolution_y=640;scene.render.resolution_percentage=100
for frame in (105,260):
    scene.frame_set(frame);scene.render.filepath=str(root/f'Temp/GoblinExport/natural_idle_{frame}.png');bpy.ops.render.render(write_still=True)
