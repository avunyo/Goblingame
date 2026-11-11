using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Goblingame.TestScene.Editor
{
    /// <summary>
    /// Makes seamless loops out of the goblin's walk / run clips.
    /// The source clips do not end in the pose they start with (the right shin jumps ~100° at the
    /// loop point of the run), which reads as a hitch every cycle. We cut the cleanest full stride
    /// out of each clip and spread the small remaining difference over its last frames.
    /// </summary>
    public static class GoblinAnimTools
    {
        private const string Folder = "Assets/RecoveredGoblinGraphics/Generated/";

        private struct LoopSpec { public string state, source, output; public float start, end; }
        // windows found by scanning every start/end pair for the smallest pose difference
        private static readonly LoopSpec[] Loops =
        {
            new LoopSpec { state = "Run",  source = "goblin_running_bone_Retargeted.anim", output = "GoblinRun_Loop.anim",  start = 0.140f, end = 0.782f },
            new LoopSpec { state = "Walk", source = "goblin_walking_bone_Retargeted.anim", output = "GoblinWalk_Loop.anim", start = 0.578f, end = 1.735f },
        };

        public static void BuildLoops(AnimatorController controller)
        {
            foreach (var spec in Loops)
            {
                var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + spec.source);
                if (src == null) { Debug.LogWarning("[GoblinAnim] missing " + spec.source); continue; }
                var clip = MakeLoop(src, spec.start, spec.end, spec.output.Replace(".anim", ""));
                string path = Folder + spec.output;
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(clip, path);
                foreach (var child in controller.layers[0].stateMachine.states)
                    if (child.state.name == spec.state) { child.state.motion = clip; EditorUtility.SetDirty(child.state); }
                Debug.Log("[GoblinAnim] " + spec.state + " now uses a seamless " + (spec.end - spec.start).ToString("0.00") + " s loop (" + spec.output + ")");
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static AnimationClip MakeLoop(AnimationClip src, float start, float end, string name)
        {
            const float fps = 60f;
            float len = end - start, blend = Mathf.Min(0.18f, len * 0.3f);
            var clip = new AnimationClip { name = name, frameRate = fps };
            foreach (var binding in AnimationUtility.GetCurveBindings(src))
            {
                var curve = AnimationUtility.GetEditorCurve(src, binding);
                if (curve == null) continue;
                float v0 = curve.Evaluate(start), v1 = curve.Evaluate(end);
                var keys = new List<Keyframe>();
                int frames = Mathf.RoundToInt(len * fps);
                for (int i = 0; i <= frames; i++)
                {
                    float t = len * i / frames;
                    float v = curve.Evaluate(start + t);
                    // fade the loop residual in over the tail so the last frame equals the first
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(len - blend, len, t));
                    v += (v0 - v1) * w;
                    keys.Add(new Keyframe(t, v));
                }
                var c = new AnimationCurve(keys.ToArray());
                for (int i = 0; i < c.length; i++) AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
                for (int i = 0; i < c.length; i++) AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(clip, binding, c);
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true; settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }
    }
}
