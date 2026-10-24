using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace VaspCommunityMods
{
    // Changes only the multiplier returned for Medium. Queues and saves retain
    // their native remaining-work values; other difficulties keep their rules.
    internal static class NormalConstruction
    {
        internal const string Owner = "com.vasp.communitymods.normal-construction";
        private const string AssemblyHash = "7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23";
        private const string MethodHash = "37bb62c53a46565ac3d02df5a37e140e9766759e8aa73742729c767233128da6";

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static void Install(Harmony harmony)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(typeof(GameManager).Assembly.Location))
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != AssemblyHash)
                    throw new InvalidOperationException("Normal construction refused: unsupported game assembly.");

            MethodInfo method = AccessTools.Method(typeof(GameManager), "GetEnemyShipBuildTimeMultiplier", Type.EmptyTypes);
            if (method == null || method.IsStatic || method.ReturnType != typeof(float)
                || method.GetMethodBody() == null || Hash(method.GetMethodBody().GetILAsByteArray()) != MethodHash)
                throw new InvalidOperationException("Normal construction refused: multiplier signature/IL mismatch.");
            var postfix = new HarmonyMethod(typeof(NormalConstruction), "Apply");
            postfix.priority = Priority.Last;
            harmony.Patch(method, postfix: postfix);
        }

        private static void Apply(GameManager __instance, ref float __result)
        {
            Plugin plugin = Plugin.Instance;
            if (!plugin || !plugin.ConstructionApplied || __instance.currentDifficulty != GameManager.GameDifficulty.Medium)
                return;
            // Do not replace a different result supplied by another mod.
            if (Math.Abs(__result - 1.5f) > 0.00001f)
            {
                plugin.ReportConstructionConflict();
                return;
            }
            __result = plugin.NormalEnemyBuildSpeedPercent / 100f;
        }
    }
}
