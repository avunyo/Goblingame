"""Author a separate idle on the existing, corrected Mixamo skeleton."""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Quaternion, Vector

root = Path(__file__).resolve().parent.parent
source = root / 'ArtSource/Goblin_Walk_Fixed.blend'
source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source))
rig = bpy.data.objects['Armature']
mesh = bpy.data.objects['Cube']
scene = bpy.context.scene
rig.data.pose_position = 'POSE'

# Average one complete gait to get relaxed arms and balanced legs, while keeping
# the same body heading as the walk used by the game.
samples = {b.name: [] for b in rig.pose.bones}
for frame in range(9, 78):
    scene.frame_set(frame)
    for bone in rig.pose.bones:
        samples[bone.name].append((bone.location.copy(), bone.matrix_basis.to_quaternion().copy(), bone.scale.copy()))
base = {}
for name, values in samples.items():
    ref = values[0][1]
    quat = [0.0] * 4
    for _, q, _ in values:
        sign = 1 if ref.dot(q) >= 0 else -1
        for i in range(4): quat[i] += q[i] * sign
    base[name] = (sum((v[0] for v in values), Vector()) / len(values),
                  Quaternion(quat).normalized(), sum((v[2] for v in values), Vector()) / len(values))
rig.animation_data_clear()
action = bpy.data.actions.new('Goblin_Idle_Curious')
rig.animation_data_create().action = action
scene.render.fps = 30
scene.frame_start = 1
scene.frame_end = 181
for frame in range(1, 182):
    t = (frame - 1) / 180.0
    breath = math.sin(4 * math.pi * t)
    # Smooth glances, returning to neutral at both ends with zero velocity.
    look = 0.0
    for center, width, amount in [(0.27, 0.19, 14), (0.72, 0.20, -17)]:
        x = abs(t - center) / width
        if x < 1: look += math.radians(amount) * (0.5 + 0.5 * math.cos(math.pi * x))
    for bone in rig.pose.bones:
        location, rotation, scale = base[bone.name]
        bone.rotation_mode = 'QUATERNION'
        bone.location = location
        bone.scale = scale
        delta = Quaternion()
        if bone.name == 'mixamorig:Spine1':
            delta = Quaternion((1, 0, 0), math.radians(0.65) * breath)
        elif bone.name == 'mixamorig:Spine2':
            delta = Quaternion((0, 1, 0), look * 0.12) @ Quaternion((1, 0, 0), math.radians(0.8) * breath)
        elif bone.name == 'mixamorig:Neck':
            delta = Quaternion((0, 1, 0), look * 0.30)
        elif bone.name == 'mixamorig:Head':
            delta = Quaternion((0, 1, 0), look * 0.70) @ Quaternion((1, 0, 0), math.radians(1.0) * breath)
        elif bone.name in ('mixamorig:LeftShoulder', 'mixamorig:RightShoulder'):
            delta = Quaternion((0, 0, 1), math.radians(0.5) * breath)
        bone.rotation_quaternion = rotation @ delta
        bone.keyframe_insert(data_path='location', frame=frame, group=bone.name)
        bone.keyframe_insert(data_path='rotation_quaternion', frame=frame, group=bone.name)
        bone.keyframe_insert(data_path='scale', frame=frame, group=bone.name)

def geometry(frame):
    scene.frame_set(frame)
    dg = bpy.context.evaluated_depsgraph_get()
    evaluated = mesh.evaluated_get(dg)
    data = evaluated.to_mesh()
    points = [v.co.copy() for v in data.vertices]
    evaluated.to_mesh_clear()
    return points
start = geometry(1)
middle = geometry(50)
end = geometry(181)
seam = max((a-b).length for a,b in zip(start,end))
motion = max((a-b).length for a,b in zip(start,middle))
foot_samples = []
for f in (1,50,100,150,181):
    scene.frame_set(f)
    foot_samples.append([rig.pose.bones[n].head.copy() for n in ('mixamorig:LeftFoot','mixamorig:RightFoot')])
foot_drift = max((a-b).length for s in foot_samples[1:] for a,b in zip(foot_samples[0],s))
assert seam < 1e-5 and motion > 0.001 and foot_drift < 1e-5
scene.frame_set(1)
blend_path = root / 'ArtSource/Goblin_Idle_Curious.blend'
fbx_path = root / 'Assets/GoblinArt/Goblin_Idle_Curious.fbx'
assert not blend_path.exists() and not fbx_path.exists(), 'Do not overwrite authored results'
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=str(fbx_path), use_selection=True, object_types={'ARMATURE'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    bake_anim_step=1.0, bake_anim_simplify_factor=0.0, axis_forward='-Z',axis_up='Y')
report = {'seconds':6,'seam_vertex_error':seam,'moving_vertex_distance':motion,'foot_bone_drift':foot_drift,
          'source_unchanged':hashlib.sha256(source.read_bytes()).hexdigest()==source_hash}
(root/'Temp/GoblinExport/idle_report.json').write_text(json.dumps(report,indent=2))

# Render an inspection image from the authored Blender copy, without saving preview objects.
scene.frame_set(50)
dg=bpy.context.evaluated_depsgraph_get()
points=[mesh.matrix_world @ p for p in geometry(50)]
low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
center=(low+high)/2; height=high.z-low.z
right=(rig.matrix_world @ rig.pose.bones['mixamorig:RightUpperLeg'].head) if 'mixamorig:RightUpperLeg' in rig.pose.bones else None
right=(rig.matrix_world.to_3x3() @ (rig.pose.bones['mixamorig:RightUpLeg'].head-rig.pose.bones['mixamorig:LeftUpLeg'].head)).normalized()
up=Vector((0,0,1)); forward=right.cross(up).normalized()
cam_data=bpy.data.cameras.new('PreviewCamera'); cam=bpy.data.objects.new('PreviewCamera',cam_data); scene.collection.objects.link(cam)
cam.location=center+forward*height*2.5+right*height*0.65+up*height*0.3
cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
cam_data.type='ORTHO'; cam_data.ortho_scale=height*1.35; scene.camera=cam
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'; scene.display.shading.color_type='TEXTURE'
scene.render.resolution_x=640; scene.render.resolution_y=640; scene.render.resolution_percentage=100
scene.render.filepath=str(root/'Temp/GoblinExport/idle_preview.png')
bpy.ops.render.render(write_still=True)
