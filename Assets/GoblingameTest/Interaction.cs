using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    /// <summary>One line of "press E" help shown by the HUD; world objects offer it while the player is close.</summary>
    public static class Interaction
    {
        private static string prompt; private static int frame = -10;
        public static void Offer(string text) { prompt = text; frame = Time.frameCount; }
        public static string Current => Time.frameCount - frame <= 1 ? prompt : null;
        public static bool Pressed => Application.isFocused && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        public static GoblinCombat Player
        {
            get { if (player == null) player = Object.FindFirstObjectByType<GoblinCombat>(); return player; }
        }
        private static GoblinCombat player;
        public static float Distance(Vector3 point) { var p = Player; if (p == null) return float.MaxValue; Vector3 d = p.transform.position - point; d.y = 0; return d.magnitude; }
    }
}
