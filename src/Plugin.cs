using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace GolfResume
{
    [BepInPlugin("generalkoski.golfresume", "GolfResume", "0.4.0")]
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource L;
        internal static ConfigEntry<KeyCode> RestoreKey;

        public override void Load()
        {
            L = Log;
            RestoreKey = Config.Bind("General", "RestoreKey", KeyCode.F8,
                "Key the host presses at the start of a game to restore the last saved game on this course.");
            AddComponent<ResumeBehaviour>();
            new Harmony("generalkoski.golfresume").PatchAll(typeof(Patches));
            L.LogInfo($"GolfResume loaded. Host: press {RestoreKey.Value} at the start of a game to resume the last game on this course.");
        }
    }

    static class Patches
    {
        static void Enforce()
        {
            try { ResumeBehaviour.Instance?.EnforceWaiting(); }
            catch (System.Exception e) { Plugin.L.LogError(e); }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(global::User), nameof(global::User.InHole), MethodType.Setter)]
        static void AfterSetInHole(bool value) { if (!value) Enforce(); }

        [HarmonyPostfix, HarmonyPatch(typeof(global::User), nameof(global::User.DeserialiseProperties))]
        static void AfterDeserialise() => Enforce();

        [HarmonyPrefix, HarmonyPatch(typeof(global::GameFlowController), nameof(global::GameFlowController.AreAllPlayersInHole))]
        static void BeforeAllInHole() => Enforce();

        [HarmonyPrefix, HarmonyPatch(typeof(global::GameFlowController), nameof(global::GameFlowController.CanMoveToNextHole))]
        static void BeforeCanMove() => Enforce();
    }
}
