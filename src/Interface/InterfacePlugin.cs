using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace VaspInterfaceTools
{
    [BepInPlugin("com.vasp.interfacetools", "VASP Interface Tools", "0.1.2")]
    [BepInProcess("VictoryAtSeaPacific.exe")]
    public sealed class InterfacePlugin : BaseUnityPlugin
    {
        public static InterfacePlugin Instance { get; private set; }
        public bool ConvoyApplied { get { return navigationReady; } }
        public bool RussianApplied { get { return localeReady; } }
        public string LastError { get; private set; }
        private Harmony locale, navigation;
        private ConfigEntry<bool> enabled, russian, convoy;
        private float nextTick;
        private bool localeReady, navigationReady;
        private void Awake()
        {
            Instance = this;
            enabled = Config.Bind("General", "Enabled", true, "Legacy master switch. Independent choices are available in Modifications.");
            russian = Config.Bind("Modules", "RussianCompletion", true, "Translate the remaining English interface keys and ETA.");
            convoy = Config.Bind("Modules", "ConvoyRoutes", true, "Improve convoy waypoint controls and correct route point/edge calculations.");
            // Preserve the effective legacy selection, including a disabled master.
            if (enabled.Value) { if (russian.Value) SetRussianEnabled(true); if (convoy.Value) SetConvoyEnabled(true); }
        }
        private static void VerifyGame()
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(typeof(AircraftFlight).Assembly.Location))
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant()
                    != "7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23")
                    throw new InvalidOperationException("Unsupported game assembly.");
        }
        private void SaveChoice(ConfigEntry<bool> choice, bool value)
        {
            // A legacy disabled master must not silently reactivate the other choice next launch.
            if (value && !enabled.Value) { russian.Value = localeReady; convoy.Value = navigationReady; enabled.Value = true; }
            choice.Value = value; Config.Save();
        }
        public bool SetRussianEnabled(bool value)
        {
            LastError = null;
            try
            {
                if (value != localeReady)
                {
                    if (value) { VerifyGame(); locale = new Harmony("com.vasp.interfacetools.locale"); RussianCompletion.Install(locale, Paths.ConfigPath); localeReady = true; RussianCompletion.RefreshLanguage(); }
                    else { RussianCompletion.Cleanup(); locale.UnpatchSelf(); localeReady = false; }
                }
                SaveChoice(russian, value); return true;
            }
            catch (Exception e)
            {
                LastError = e.ToString();
                if (value) try { RussianCompletion.Cleanup(); if (locale != null) locale.UnpatchSelf(); localeReady = false; }
                    catch (Exception recovery) { LastError += " Rollback failed: " + recovery; }
                Logger.LogError(LastError); return false;
            }
        }
        public bool SetConvoyEnabled(bool value)
        {
            LastError = null;
            try
            {
                if (value != navigationReady)
                {
                    if (value) { VerifyGame(); navigation = new Harmony("com.vasp.interfacetools.navigation"); ConvoyRoutes.Install(navigation); navigationReady = true; }
                    else { ConvoyRoutes.Cleanup(); navigation.UnpatchSelf(); navigationReady = false; }
                }
                SaveChoice(convoy, value); return true;
            }
            catch (Exception e)
            {
                LastError = e.ToString();
                if (value) try { ConvoyRoutes.Cleanup(); if (navigation != null) navigation.UnpatchSelf(); navigationReady = false; }
                    catch (Exception recovery) { LastError += " Rollback failed: " + recovery; }
                Logger.LogError(LastError); return false;
            }
        }
        private void Update()
        {
            if (navigationReady) ConvoyRoutes.InputUpdate();
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + .5f;
            if (localeReady) RussianCompletion.RefreshLanguage();
            if (navigationReady) ConvoyRoutes.RefreshUI();
        }
        internal static void Log(string text) { if (Instance) Instance.Logger.LogInfo(text); }
        private void OnDestroy()
        {
            // Teardown must not change saved user preferences.
            try { if (localeReady) RussianCompletion.Cleanup(); if (navigationReady) ConvoyRoutes.Cleanup(); }
            finally { if (locale != null) locale.UnpatchSelf(); if (navigation != null) navigation.UnpatchSelf(); if (Instance == this) Instance = null; }
        }
    }
}
