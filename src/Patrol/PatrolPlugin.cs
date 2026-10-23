using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace VaspScoutPatrol
{
    [BepInPlugin(Id, "VASP Scout Patrol", "0.1.0")]
    [BepInProcess("VictoryAtSeaPacific.exe")]
    public sealed class PatrolPlugin : BaseUnityPlugin
    {
        public const string Id = "com.vasp.scoutpatrol";
        public const string GameHash = "7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23";
        public static PatrolPlugin Instance { get; private set; }
        public bool Applied { get; private set; }
        public string LastError { get; private set; }
        private ConfigEntry<bool> enabled;
        private readonly List<PatrolAnchor> anchors = new List<PatrolAnchor>();
        internal PatrolController Controller;
        internal ConfigEntry<float> Reserve, RefuelThreshold, Offset;
        private Harmony harmony;
        private float nextRefresh;
        private bool uiFailed;
        private readonly List<PatrolStrip> strips = new List<PatrolStrip>();

        private void Awake()
        {
            Instance = this;
            enabled = Config.Bind("General", "Enabled", true, "Enable scout patrol. May be changed live from Modifications.");
            Reserve = Config.Bind("Patrol", "FuelReserve", 0.10f, "Fuel kept in reserve (0.05-0.35). Return distance also includes 10% allowance.");
            RefuelThreshold = Config.Bind("Patrol", "RelaunchFuel", 0.95f, "Minimum fuel fraction for all aircraft before the next automatic sortie (0.75-1.0).");
            Offset = Config.Bind("UI", "VerticalOffset", 110f, "Patrol strip position above the aircraft icons and launch queue, in canvas units.");
            Reserve.Value = PatrolGeometry.Finite(Reserve.Value) ? Mathf.Clamp(Reserve.Value, 0.05f, 0.35f) : 0.10f;
            RefuelThreshold.Value = PatrolGeometry.Finite(RefuelThreshold.Value) ? Mathf.Clamp(RefuelThreshold.Value, 0.75f, 1f) : 0.95f;
            Offset.Value = PatrolGeometry.Finite(Offset.Value) ? Mathf.Clamp(Offset.Value, 40f, 300f) : 110f;
            if (enabled.Value) SetEnabled(true);
        }

        // Called on the Unity main thread. The return value describes applied state, not just the saved checkbox.
        public bool SetEnabled(bool value)
        {
            LastError = null;
            if (value == Applied) { enabled.Value = value; Config.Save(); return true; }
            try
            {
                if (!value)
                {
                    if (Controller != null) Controller.Shutdown();
                    if (harmony != null) harmony.UnpatchSelf();
                    RemoveUI();
                    Controller = null; Applied = false;
                    enabled.Value = false; Config.Save();
                    return true;
                }
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(typeof(AircraftFlight).Assembly.Location))
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (hash != GameHash) throw new InvalidOperationException("Unsupported Assembly-CSharp.dll: " + hash);
                }
                NativeAccess.Validate();
                Controller = new PatrolController(this);
                Instance = this;
                harmony = new Harmony(Id);
                InstallHooks();
                Applied = true; uiFailed = false; nextRefresh = 0;
                enabled.Value = true; Config.Save();
                Logger.LogInfo("Scout Patrol ready: medium/MAX native scouting, fuel-aware return and automatic repeat. Game hash verified.");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                if (value)
                {
                    try { if (harmony != null) harmony.UnpatchSelf(); Controller = null; Applied = false; RemoveUI(); }
                    catch (Exception recovery) { LastError += " Rollback failed: " + recovery; }
                }
                Logger.LogError("Scout Patrol transition failed: " + LastError);
                return false;
            }
        }

        private void InstallHooks()
        {
            Patch(typeof(FlightAIAgent), "AIUpdate", "BeforeFlightUpdate", new[] { typeof(float) });
            Patch(typeof(AircraftFlight), "GiveExplicitOrders", "BeforeExplicitOrder", new[] { typeof(List<CampaignOrder>) });
            Patch(typeof(AircraftFlight), "ClearOrders", "BeforeExplicitOrder", Type.EmptyTypes);
            // These are the native manual/tactical command entry points. They keep their original implementations.
            foreach (MethodInfo method in typeof(AircraftAIData).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (method.Name == "ReceiveOrder" || method.Name == "ReceiveDirectAttackOrder" || method.Name == "ReceiveDefendOrder")
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(PatrolPlugin), "BeforeAircraftOrder"));
        }

        private void Patch(Type type, string name, string prefix, Type[] args)
        {
            MethodInfo target = AccessTools.Method(type, name, args);
            if (target == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PatrolPlugin), prefix));
        }

        private static void BeforeFlightUpdate(AircraftFlight ___flight, float delta)
        {
            PatrolPlugin p = Instance;
            if (p != null && p.Controller != null && delta > 0) p.Controller.SafeTick(___flight, delta);
        }

        private static void BeforeExplicitOrder(AircraftFlight __instance)
        {
            PatrolPlugin p = Instance;
            if (p != null && p.Controller != null && !p.Controller.IssuingOrders)
                p.Controller.Cancel(__instance, "native/manual order");
        }

        private static void BeforeAircraftOrder(Aircraft ___aircraft)
        {
            PatrolPlugin p = Instance;
            if (p != null && p.Controller != null && !p.Controller.IssuingOrders && ___aircraft)
                p.Controller.Cancel(NativeAccess.Flight(___aircraft), "native/manual aircraft order");
        }

        private void Update()
        {
            if (Controller == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.35f;
            if (GameManager.instance && Player.instance && !GameTime.IsPaused()) Controller.TickAll();
            if (uiFailed) return;
            try
            {
                if (UIShipCard.instance && UIShipCard.instance.ui)
                    EnsureStrip(UIShipCard.instance.ui, UIShipCard.instance.weaponControlContainer, PanelKind.Ship);
                if (UIAirfieldCard.instance && UIAirfieldCard.instance.ui)
                    EnsureStrip(UIAirfieldCard.instance.ui, UIAirfieldCard.instance.flightControlContainer, PanelKind.Airfield);
                if (UIFlightCard.instance && UIFlightCard.instance.ui)
                    EnsureStrip(UIFlightCard.instance.ui, UIFlightCard.instance.ui.transform, PanelKind.Flight);
                for (int i = strips.Count - 1; i >= 0; i--)
                    if (!strips[i]) strips.RemoveAt(i); else strips[i].Refresh();
            }
            catch (Exception ex)
            {
                uiFailed = true;
                Logger.LogError("Patrol UI disabled after error: " + ex);
                foreach (PatrolStrip strip in strips) if (strip) strip.gameObject.SetActive(false);
            }
        }

        private void EnsureStrip(GameObject owner, Transform anchor, PanelKind kind)
        {
            if (!anchor || owner.GetComponent<PatrolAnchor>()) return;
            var marker = owner.AddComponent<PatrolAnchor>();
            anchors.Add(marker);
            var go = new GameObject("VASP Scout Patrol", typeof(RectTransform));
            go.transform.SetParent(anchor, false);
            marker.Strip = go.AddComponent<PatrolStrip>();
            strips.Add(marker.Strip);
            marker.Strip.Initialize(this, kind);
            Logger.LogInfo("Patrol buttons attached to " + kind + " aircraft panel.");
        }

        internal void Log(string message) { Logger.LogInfo(message); }
        internal void Error(string message) { Logger.LogError(message); }
        private void RemoveUI()
        {
            foreach (PatrolStrip strip in strips) if (strip) { strip.gameObject.SetActive(false); DestroyImmediate(strip.gameObject); }
            strips.Clear();
            foreach (PatrolAnchor anchor in anchors) if (anchor) DestroyImmediate(anchor);
            anchors.Clear();
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            SetEnabled(false);
            Instance = null;
        }
    }
}
