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
            if (File.Exists(upgrade)) { GoblinGameUpgrade.RunQueued(upgrade); return; }
            if (File.Exists(mesh)) { GoblinMeshTools.RunQueued(); return; }
            if (File.Exists(Request)) StartIfRequested();
        }

        public static void StartIfRequested()
        {
            if (!File.Exists(Request)) return;
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
