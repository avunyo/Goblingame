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
            // the upgrade request (if any) runs first and then calls StartIfRequested itself
            if (File.Exists(Request) && !File.Exists(Path.Combine(Root, "Temp/GoblinUpgrade.request")))
                EditorApplication.delayCall += StartIfRequested;
        }

        public static void StartIfRequested()
        {
            if (!File.Exists(Request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += StartIfRequested; return; }
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
