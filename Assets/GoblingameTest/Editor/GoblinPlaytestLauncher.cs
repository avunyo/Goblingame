using System.IO;
using UnityEditor;
using UnityEngine;

namespace Goblingame.TestScene.Editor
{
    /// <summary>Starts the automated play-mode check (see Dev/GoblinPlaytest.cs).</summary>
    [InitializeOnLoad]
    public static class GoblinPlaytestLauncher
    {
        private static string Root => Directory.GetParent(Application.dataPath).FullName;
        private static string Request => Path.Combine(Root, "Temp/GoblinPlaytest.request");

        static GoblinPlaytestLauncher()
        {
            // Poll the queue once a second, so requests also run when no script recompiles
            // (the static constructors of the tools only fire on a domain reload).
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private static double nextPoll;
        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating) return;
            string upgrade = Path.Combine(Root, "Temp/GoblinUpgrade.request"), mesh = Path.Combine(Root, "Temp/GoblinMesh.request");
            bool any = File.Exists(upgrade) || File.Exists(mesh) || File.Exists(Request) || File.Exists(Path.Combine(Root, "Temp/GoblinSwamp.request"));
            if (!any) return;
            // never run a queued job with stale code: pick up edited scripts first, come back after the reload
            AssetDatabase.Refresh();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(upgrade)) { GoblinGameUpgrade.RunQueued(upgrade); return; }
            string swamp = Path.Combine(Root, "Temp/GoblinSwamp.request");
            if (File.Exists(swamp))
            {
                File.Delete(swamp);
                var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (active.isDirty)
                {
                    string copy = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinUnsavedBeforeRetry.unity");
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active, copy, true);
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(active.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    Debug.Log("[Swamp] unsaved scene state copied to " + copy + " before rebuilding");
                }
                try { SwampBuilder.Build(); } catch (System.Exception e) { Debug.LogException(e); }
                return;
            }
            if (File.Exists(mesh)) { GoblinMeshTools.RunQueued(); return; }
            if (File.Exists(Request)) StartIfRequested();
        }

        public static void StartIfRequested()
        {
            if (!File.Exists(Request)) return;
            if (File.Exists(Path.Combine(Root, "Temp/GoblinSwamp.request"))) return;   // build the world first; the poll comes back
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += StartIfRequested; return; }
            // pick up edited scripts first; never start Play with stale code
            AssetDatabase.Refresh();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;   // the poll retries after the reload
            File.Delete(Request);
            Run();
        }

        [MenuItem("Goblingame/Run Playtest")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) return;
            File.WriteAllText(Path.Combine(Root, "Temp/GoblinPlaytest.run"), "run");
            Debug.Log("[Playtest] starting play mode");
            EditorApplication.EnterPlaymode();
        }
    }
}
