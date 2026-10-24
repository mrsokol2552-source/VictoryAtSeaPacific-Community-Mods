using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;

namespace VaspCommunityMods
{
    public static class HomeIndustry
    {
        internal const string Owner = "com.vasp.communitymods.home-industry";
        private static readonly MethodInfo NativeAdd = AccessTools.Method(typeof(Supplies), "Add", new[] { typeof(Supplies) });
        private static readonly FieldInfo AgentPort = AccessTools.Field(typeof(PortAIAgent), "port");
        private static readonly FieldInfo InCampaign = AccessTools.Field(typeof(GameManager), "inCampaign");
        private static readonly FieldInfo SingleFleet = AccessTools.Field(typeof(GameManager), "singleFleetCampaign");
        private static readonly FieldInfo Selected = AccessTools.Field(typeof(UIBridgeScreen), "selectedPort");
        public static bool CampaignActive { get { return GameManager.instance && (bool)InCampaign.GetValue(GameManager.instance) && !(bool)SingleFleet.GetValue(GameManager.instance); } }
        public static Port SelectedPort { get { return UIBridgeScreen.instance ? (Port)Selected.GetValue(UIBridgeScreen.instance) : null; } }

        private static MethodInfo Guard(Type type, string name, string expected)
        {
            MethodInfo method = AccessTools.Method(type, name);
            if (method == null || method.GetMethodBody() == null) throw new InvalidOperationException("Industry method missing: " + name);
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(method.GetMethodBody().GetILAsByteArray())).Replace("-", "").ToLowerInvariant() != expected)
                    throw new InvalidOperationException("Industry method IL mismatch: " + name);
            return method;
        }
        internal static void Install(Harmony harmony)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(typeof(Port).Assembly.Location))
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != "7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23")
                    throw new InvalidOperationException("Industry unsupported game assembly");
            // All guards precede mutation; failure rolls back only this owner.
            MethodInfo production = Guard(typeof(PortAIAgent), "UpdateManufacturing", "2de8c205fcfa28b86d944fba14f23017a20a2e9e0567af130822ea765bddbfe7");
            Guard(typeof(SaveManager), "SaveCampaignData", "fe57095b4d0832a59f4693593479ea427860d919dea1a0f86466de1e3beec18f");
            Guard(typeof(SaveManager), "LoadCampaignData", "ac30968b83711541ee5177f767e2c284a9b9d5e6209b3fa2e624531dda202a53");
            MethodInfo managers = Guard(typeof(ManagerContainer), "Awake", "4597a45ad1373a969e8f3b0807971da5735d192eed890c727ab1b586fa955f17");
            MethodInfo panel = Guard(typeof(UIPortInformation), "Awake", "ecc4dcc69070f39a9741633fae5e70e59722bc4c1cbc4114dae289d0a25600b9");
            if (AgentPort == null || AgentPort.FieldType != typeof(Port) || NativeAdd == null) throw new InvalidOperationException("Industry native signature mismatch");
            if (InCampaign == null || InCampaign.FieldType != typeof(bool) || SingleFleet == null || SingleFleet.FieldType != typeof(bool) || Selected == null || Selected.FieldType != typeof(Port)) throw new InvalidOperationException("Industry native campaign/UI field mismatch");
            harmony.Patch(production, transpiler: new HarmonyMethod(typeof(HomeIndustry), "ProductionIL"));
            // Keep both save methods native: their caller-sensitive reflection
            // (Type.GetType/GetExecutingAssembly) must run in Assembly-CSharp.
            harmony.Patch(managers, postfix: new HarmonyMethod(typeof(HomeIndustry), "EnsureState"));
            harmony.Patch(panel, postfix: new HarmonyMethod(typeof(HomeIndustry), "AttachPanel"));
            EnsureState(); if (UIPortInformation.instance) AttachPanel(UIPortInformation.instance);
        }
        private static void EnsureState()
        {
            if (ManagerContainer.instance && !ManagerContainer.instance.GetComponent<IndustryState>())
                ManagerContainer.instance.gameObject.AddComponent<IndustryState>();
        }
        private static void AttachPanel(UIPortInformation __instance)
        {
            if (!__instance.GetComponent<IndustryPanel>()) __instance.gameObject.AddComponent<IndustryPanel>();
        }
        private static IEnumerable<CodeInstruction> ProductionIL(IEnumerable<CodeInstruction> instructions)
        {
            int matches = 0;
            foreach (CodeInstruction original in instructions)
            {
                if (!original.Calls(NativeAdd)) { yield return original; continue; }
                if (original.blocks.Count != 0) throw new InvalidOperationException("Unexpected industry exception boundary");
                matches++;
                var port = new CodeInstruction(OpCodes.Ldarg_0); port.labels.AddRange(original.labels);
                yield return port;
                yield return new CodeInstruction(OpCodes.Ldfld, AgentPort);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HomeIndustry), "AddProduction"));
            }
            if (matches != 1) throw new InvalidOperationException("Industry production call count=" + matches);
        }
        public static int GetLevel(Port port)
        {
            if (port == null || !IndustryState.Instance) return 0;
            IndustryEntry entry = IndustryState.Instance.Entry(port.name); return entry == null ? 0 : entry.Level;
        }
        public static int NextCost(Port port) { int level = GetLevel(port); return level == 0 ? 3000 : level == 1 ? 5000 : 0; }
        public static string CostLabel(int cost) { return cost.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " "); }
        public static int ProductionPercent(Port port) { int level = GetLevel(port); return level == 1 ? 120 : level == 2 ? 144 : 100; }
        public static bool CanPurchase(Port port, out string reason)
        {
            if (!Plugin.Instance || !Plugin.Instance.IndustryEnabled) { reason = "Мод производства отключён"; return false; }
            if (!IndustryState.Instance || IndustryState.Instance.LoadError != null) { reason = "Данные производства недоступны"; return false; }
            if (!CampaignActive) { reason = "Нужна обычная кампания"; return false; }
            if (port == null || !port.homePort) { reason = "Улучшение доступно только домашним портам"; return false; }
            if (!Player.instance || port.GetFaction() != Player.instance.faction) { reason = "Порт принадлежит другой стороне"; return false; }
            if (GetLevel(port) >= 2) { reason = "Достигнут максимальный уровень"; return false; }
            if (!port.HasWarBonds(NextCost(port))) { reason = "Недостаточно облигаций: нужно " + CostLabel(NextCost(port)); return false; }
            reason = null; return true;
        }
        public static bool TryPurchase(Port port, int expectedLevel, out string reason)
        {
            if (!CanPurchase(port, out reason)) return false;
            if (GetLevel(port) != expectedLevel) { reason = "Уровень уже изменился; выберите улучшение заново"; return false; }
            int cost = NextCost(port);
            IndustryEntry old = IndustryState.Instance.Entry(port.name);
            IndustryEntry next = old == null ? new IndustryEntry() : old.Copy(); next.Level++;
            if (!port.TrySpendWarBonds(cost)) { reason = "Недостаточно облигаций"; return false; }
            try { IndustryState.Instance.SetEntry(port.name, next); }
            catch { FactionManager.instance.GetNavy(port.GetFaction()).warBonds += cost; throw; }
            reason = null; return true;
        }
        private static int Scale(int original, int numerator, int[] carry, int index)
        {
            long amount = (long)original * numerator + carry[index];
            int result = checked((int)(amount / 25)); carry[index] = (int)(amount % 25); return result;
        }
        // Replaces only the native manufacturing deposit. Transfers, loading,
        // starting stores, capacities and other ports retain their own rules.
        private static void AddProduction(Supplies destination, Supplies produced, Port port)
        {
            IndustryState state = IndustryState.Instance;
            IndustryEntry entry = state && port != null && port.homePort ? state.Entry(port.name) : null;
            if (Plugin.Instance && Plugin.Instance.IndustryEnabled && entry != null && entry.Level > 0 && state.LoadError == null)
            {
                int numerator = entry.Level == 1 ? 30 : 36;
                produced.currentMainAmmo = Scale(produced.currentMainAmmo, numerator, entry.Remainders, 0);
                produced.currentTorpedoes = Scale(produced.currentTorpedoes, numerator, entry.Remainders, 1);
                produced.currentDepthCharges = Scale(produced.currentDepthCharges, numerator, entry.Remainders, 2);
                produced.currentBombs = Scale(produced.currentBombs, numerator, entry.Remainders, 3);
                produced.currentTorpedoBombs = Scale(produced.currentTorpedoBombs, numerator, entry.Remainders, 4);
                produced.currentDepthBombs = Scale(produced.currentDepthBombs, numerator, entry.Remainders, 5);
                produced.currentFuel *= numerator / 25f;
                produced.currentAviationFuel *= numerator / 25f;
                produced.currentRations *= numerator / 25f;
            }
            destination.Add(produced);
        }
    }
}
