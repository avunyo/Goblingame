using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    public static class GoblinVisualPolish
    {
        [MenuItem("Goblingame/Fix Color and Walk Loop")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation first.");
                return;
            }
            var scene = SceneManager.GetActiveScene();
            var player = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Player");
            var visual = player != null ? player.transform.Find("GoblinVisual") : null;
            if (visual == null || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Open the GoblinPlayable scene with Player/GoblinVisual first.");
            var animator = visual.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("The goblin has no Animator Controller.");
            var clip = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c =>
                AssetDatabase.GetAssetPath(c).EndsWith("/Goblin.fbx", StringComparison.Ordinal));
            string path = AssetDatabase.GetAssetPath(clip);
            if (!path.StartsWith("Assets/GoblingameTest/Generated", StringComparison.Ordinal) || !path.EndsWith("/Goblin.fbx", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected the generated Goblin.fbx. Original art will not be modified.");
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing model importer.");
            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips.Length == 0) throw new InvalidOperationException("Missing walk clip.");
            // Preserve a copy of the scene before changing its material assignments.
            string backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinBeforePolish.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new InvalidOperationException("Could not save a scene backup.");
            string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string backupSettings = AssetDatabase.GenerateUniqueAssetPath(folder + "/BeforeLoopSettings.txt");
            System.IO.File.WriteAllText(backupSettings, EditorJsonUtility.ToJson(importer, true));
            foreach (var c in clips)
            {
                // Matching full gait cycle measured in the source Blender action.
                c.firstFrame = 9f;
                c.lastFrame = 78f;
                c.loopTime = true;
                c.loopPose = true;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            // Reacquire scene references after the prefab import.
            visual = player.transform.Find("GoblinVisual");
            ApplyColor(visual.GetComponentsInChildren<Renderer>(true), folder);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the scene.");
            Debug.Log("Goblin color connected; walk loops through frames 9–78 with Loop Pose. Press Play and hold W to check several cycles.");
        }

        public static void ApplyColor(Renderer[] renderers, string folder)
        {
            const string texturePath = "Assets/GoblinArt/GoblinBaseColor.png";
            AssetDatabase.ImportAsset(texturePath);
            var textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (textureImporter == null) throw new InvalidOperationException("GoblinBaseColor.png is missing.");
            if (!textureImporter.sRGBTexture || textureImporter.textureType != TextureImporterType.Default)
            {
                textureImporter.sRGBTexture = true;
                textureImporter.textureType = TextureImporterType.Default;
                textureImporter.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || texture == null) throw new InvalidOperationException("Missing URP Lit shader or goblin texture.");
            string materialPath = folder + "/GoblinColor.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            Undo.RecordObject(material, "Set goblin color");
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(material);
            foreach (var renderer in renderers)
            {
                Undo.RecordObject(renderer, "Assign goblin material");
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }
    }
}
