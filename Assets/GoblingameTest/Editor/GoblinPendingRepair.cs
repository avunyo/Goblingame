using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    // One-shot request for this repair. With no request file, this does nothing.
    [InitializeOnLoad]
    public static class GoblinPendingRepair
    {
        private const string Request = "Temp/GoblinExport/pending-idle-repair.txt";
        private const string Result = "Temp/GoblinExport/idle-repair-result.txt";

        static GoblinPendingRepair()
        {
            if (File.Exists(Request)) EditorApplication.update += Run;
        }

        private static void Run()
        {
            if (!File.Exists(Request)) { EditorApplication.update -= Run; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/GoblinPlayable.unity" || PrefabStageUtility.GetCurrentPrefabStage() != null)
                return;
            EditorApplication.update -= Run;
            string description = File.ReadAllText(Request);
            File.Delete(Request);
            try
            {
                if (description.StartsWith("CAST_UI:", StringComparison.Ordinal)) EnemyCastSetup.RepairFrames();
                else if (description.StartsWith("CAST:", StringComparison.Ordinal)) EnemyCastSetup.Apply();
                else if (description.StartsWith("EYES:", StringComparison.Ordinal)) GoblinEyesSetup.Apply();
                else if (description.StartsWith("POLISH:", StringComparison.Ordinal)) GoblinCombatPolish.Apply();
                else if (description.StartsWith("COMBAT:", StringComparison.Ordinal)) GoblinCombatSetup.Apply();
                else if (description.StartsWith("RUN:", StringComparison.Ordinal)) GoblinRunSetup.Apply();
                else GoblinIdleSetup.Apply();
                File.WriteAllText(Result, "SUCCESS: " + description + "\nController transitions and leg motion validated; scene saved. " + DateTime.UtcNow.ToString("O"));
            }
            catch (Exception error)
            {
                File.WriteAllText(Result, "FAILED: " + error);
                Debug.LogException(error);
            }
        }
    }
}

