using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace VaspInterfaceTools
{
    internal static class RussianCompletion
    {
        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Raw = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Recorded = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Inspected = new HashSet<string>(StringComparer.Ordinal);
        // Track the original text and best fit attributes modified by raw overrides (e.g. ETA)
        private struct TextState
        {
            public string OriginalText;
            public bool BestFit;
            public int MinSize;
            public int MaxSize;
            public HorizontalWrapMode HorizontalWrap;
            public VerticalWrapMode VerticalWrap;
        }
        private static readonly Dictionary<Text, TextState> TrackedLabels = new Dictionary<Text, TextState>();
        private static string auditPath, lastLanguage;
        private static bool refreshing, restoring;
        private static readonly Dictionary<string, string> OriginalByTranslated = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static bool Russian { get { return LocalisationManager.instance && (LocalisationManager.instance.GetCurrentLanguageName() ?? "").StartsWith("ru", StringComparison.OrdinalIgnoreCase); } }

        internal static void Install(Harmony h, string configPath)
        {
            if (Keys.Count != 117) Keys.Clear();
            if (Keys.Count == 0)
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VaspInterfaceTools.RuOverrides.tsv"))
                {
                    if (stream == null) throw new FileNotFoundException("Embedded Russian overrides missing.");
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int tab = line.IndexOf('\t');
                            if (tab <= 0 || tab == line.Length - 1) throw new FormatException("Invalid translation row.");
                            Keys[line.Substring(0, tab)] = line.Substring(tab + 1);
                        }
                    }
                }
            }
            if (Raw.Count == 0)
            {
                Raw["ETA"] = "Прибытие";
                Raw["Create convoy waypoint"] = "Создать точку маршрута конвоя";
                Raw["Connect"] = "Соединить";
                Raw["Click another convoy waypoint to create a connection."] = "Выберите другую точку маршрута. Esc или ПКМ — отмена.";
            }
            auditPath = Path.Combine(configPath, "VaspUntranslated.csv");
            h.Patch(AccessTools.Method(typeof(LocalisationManager), "GetLocalised", new[] { typeof(string), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(RussianCompletion), "Lookup"));
            h.Patch(AccessTools.PropertySetter(typeof(Text), "text"), prefix: new HarmonyMethod(typeof(RussianCompletion), "SetText"));
            h.Patch(AccessTools.Method(typeof(Text), "OnEnable"), postfix: new HarmonyMethod(typeof(RussianCompletion), "EnableText"));
            InterfacePlugin.Log("Russian overrides loaded: " + Keys.Count + ". Residual English audit: " + auditPath);
        }

        private static void Lookup(string id, bool replaceNewLines, ref string __result)
        {
            string value;
            if (restoring || !Russian || id == null) return;
            if (!Keys.TryGetValue(id, out value)) { AuditLookup(id, __result); return; }
            string replacement = replaceNewLines ? value.Replace("\\n", "\n") : value;
            if (__result != null) OriginalByTranslated[replacement] = __result;
            __result = replacement;
        }

        private static void AuditLookup(string id, string __result)
        {
            if (!Russian || id == null || __result == null || Keys.ContainsKey(id)) return;
            if (id.StartsWith("UI_") || id.StartsWith("TOOLTIP_") || id.StartsWith("LOG_") || id.StartsWith("FEEDBACK_") || id.StartsWith("CHOICE_"))
                Record(id, __result);
        }

        private static void SetText(Text __instance, ref string value)
        {
            string translated;
            if (restoring) return;
            TextState previous;
            if (TrackedLabels.TryGetValue(__instance, out previous))
            {
                __instance.resizeTextForBestFit = previous.BestFit;
                __instance.resizeTextMinSize = previous.MinSize; __instance.resizeTextMaxSize = previous.MaxSize;
                __instance.horizontalOverflow = previous.HorizontalWrap; __instance.verticalOverflow = previous.VerticalWrap;
                TrackedLabels.Remove(__instance);
            }
            if (Russian && value != null && (Raw.TryGetValue(value, out translated) || OriginalByTranslated.ContainsKey(value)))
            {
                string original;
                if (Raw.TryGetValue(value, out translated)) original = value;
                else { original = OriginalByTranslated[value]; translated = value; }
                TrackOriginal(__instance, original);
                if (value == "ETA") FitETA(__instance);
                value = translated;
            }
        }

        private static void TrackOriginal(Text text, string original)
        {
            if (!text || TrackedLabels.ContainsKey(text)) return;
            TrackedLabels[text] = new TextState
            {
                OriginalText = original,
                BestFit = text.resizeTextForBestFit,
                MinSize = text.resizeTextMinSize,
                MaxSize = text.resizeTextMaxSize,
                HorizontalWrap = text.horizontalOverflow,
                VerticalWrap = text.verticalOverflow
            };
        }

        private static void FitETA(Text text)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Math.Min(10, text.fontSize);
            text.resizeTextMaxSize = Math.Max(10, text.fontSize);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void EnableText(Text __instance)
        {
            string translated;
            if (Russian && __instance && __instance.text != null && Raw.TryGetValue(__instance.text, out translated))
            {
                TrackOriginal(__instance, __instance.text);
                if (__instance.text == "ETA") FitETA(__instance);
                __instance.text = translated;
            }
        }

        internal static void RefreshLanguage()
        {
            if (!LocalisationManager.instance) return;
            string language = LocalisationManager.instance.GetCurrentLanguageName();
            if (language == lastLanguage || refreshing) return;
            lastLanguage = language;
            if (!Russian)
            {
                RestoreOriginals();
                return;
            }
            refreshing = true;
            try { foreach (Text text in Resources.FindObjectsOfTypeAll<Text>()) EnableText(text); }
            finally { refreshing = false; }
        }

        private static void RestoreOriginals()
        {
            var dead = new List<Text>();
            foreach (KeyValuePair<Text, TextState> kv in TrackedLabels)
            {
                Text t = kv.Key;
                if (!t) { dead.Add(t); continue; }
                TextState state = kv.Value;
                string translated;
                if ((Raw.TryGetValue(state.OriginalText, out translated) && t.text == translated) || OriginalByTranslated.ContainsKey(t.text))
                    t.text = state.OriginalText;
                t.resizeTextForBestFit = state.BestFit;
                t.resizeTextMinSize = state.MinSize;
                t.resizeTextMaxSize = state.MaxSize;
                t.horizontalOverflow = state.HorizontalWrap;
                t.verticalOverflow = state.VerticalWrap;
            }
            foreach (Text t in dead) TrackedLabels.Remove(t);
        }

        internal static void Cleanup()
        {
            restoring = true;
            try { RestoreOriginals(); }
            finally { restoring = false; }
            TrackedLabels.Clear();
            lastLanguage = null;

        }

        private static void Record(string key, string text)
        {
            // Localized labels are often read every frame. Inspect each key once,
            // including Russian results, instead of running regexes on every read.
            if (Recorded.Count >= 2000 || Inspected.Count >= 5000 || !Inspected.Add(key)) return;
            if (!Regex.IsMatch(text, "[A-Za-z]{3}") || Regex.IsMatch(text, "[А-Яа-яЁё]")) return;
            string stripped = Regex.Replace(text, "\\[[^]]*\\]|<[^>]*>", "");
            if (!Regex.IsMatch(stripped, "[A-Za-z]{3}")) return;
            Recorded.Add(key);
            try
            {
                if (!File.Exists(auditPath)) File.WriteAllText(auditPath, "key,english\r\n", new UTF8Encoding(true));
                File.AppendAllText(auditPath, "\"" + key.Replace("\"", "\"\"") + "\",\"" + text.Replace("\"", "\"\"").Replace("\r", "").Replace("\n", "\\n") + "\"\r\n", Encoding.UTF8);
            }
            catch (IOException e) { InterfacePlugin.Log("Could not append untranslated UI audit: " + e.Message); }
        }
    }
}
