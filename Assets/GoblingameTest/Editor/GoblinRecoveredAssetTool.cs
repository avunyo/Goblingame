using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Goblingame.TestScene.Editor
{
    public static class GoblinRecoveredAssetTool
    {
        private static readonly string[] Paths =
        {
            "Assets/RecoveredGoblinGraphics/BlenderSource/goblin.blend",
            "Assets/RecoveredGoblinGraphics/BlenderSource/boss_goblin.blend",
            "Assets/RecoveredGoblinGraphics/BlenderSource/гоблин бегит бегит.blend",
            "Assets/RecoveredGoblinGraphics/BlenderSource/гоблин с высоким лицом.blend",
            "Assets/RecoveredGoblinGraphics/BlenderSource/Лучший гоблин красотка.blend",
            "Assets/RecoveredGoblinGraphics/Animations/goblin_walking_bone.fbx",
            "Assets/RecoveredGoblinGraphics/Animations/goblin_running_bone.fbx",
            "Assets/RecoveredGoblinGraphics/Animations/standing_melle_combat.fbx",
            "Assets/RecoveredGoblinGraphics/Animations/block_combat.fbx",
            "Assets/RecoveredGoblinGraphics/Animations/punching_combat.fbx",
            "Assets/RecoveredGoblinGraphics/Animations/dying_combat.fbx"
        };

        [MenuItem("Goblingame/Recovered Graphics/Inspect Imported Rigs and Clips")]
        public static void Inspect()
        {
            var report = new StringBuilder();
            foreach (string path in Paths)
            {
                report.AppendLine("ASSET " + path);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    report.AppendLine("  ModelImporter: missing");
                    continue;
                }
                report.AppendLine("  Rig: " + importer.animationType + ", importAnimation=" + importer.importAnimation + ", avatarSetup=" + importer.avatarSetup);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root != null)
                {
                    report.AppendLine("  Root: " + root.name);
                    foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                        report.AppendLine("  Animator: " + animator.name + ", avatar=" + (animator.avatar ? animator.avatar.name + " valid=" + animator.avatar.isValid + " human=" + animator.avatar.isHuman : "none"));
                    foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                        report.AppendLine("  SkinnedMesh: " + renderer.name + ", verts=" + (renderer.sharedMesh ? renderer.sharedMesh.vertexCount : 0) + ", bones=" + renderer.bones.Length + ", materials=" + renderer.sharedMaterials.Length);
                    report.AppendLine("    Bone names: " + string.Join(", ", renderer.bones.Where(b => b != null).Select(b => b.name).Take(40)));
                }
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                        report.AppendLine("  Mesh: " + filter.name + ", verts=" + (filter.sharedMesh ? filter.sharedMesh.vertexCount : 0));
                }
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var clip in subAssets.OfType<AnimationClip>().Where(c => c.length > 0f))
                {
                    report.AppendLine("  Clip: " + clip.name + ", seconds=" + clip.length.ToString("F2") + ", human=" + clip.humanMotion + ", curves=" + AnimationUtility.GetCurveBindings(clip).Length);
                    report.AppendLine("    Paths: " + string.Join(" | ", AnimationUtility.GetCurveBindings(clip).Select(b => b.path + ":" + b.propertyName).Take(30)));
                }
                foreach (var avatar in subAssets.OfType<Avatar>())
                    report.AppendLine("  Avatar: " + avatar.name + ", valid=" + avatar.isValid + ", human=" + avatar.isHuman);
                foreach (var material in subAssets.OfType<Material>())
                    report.AppendLine("  Material: " + material.name + ", shader=" + (material.shader ? material.shader.name : "missing") + ", mainTexture=" + (material.mainTexture ? material.mainTexture.name : "none"));
            }
            var project = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.Combine(project, "Library", "GoblinRecoveredAssetInfo.txt");
            File.WriteAllText(output, report.ToString());
            Debug.Log("Recovered asset inspection saved to " + output + "\n" + report);
        }
    }
}

