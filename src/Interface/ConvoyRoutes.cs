using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Pathfinding;
using UnityEngine;
using UnityEngine.UI;

namespace VaspInterfaceTools
{
    internal static class ConvoyRoutes
    {
        private static readonly FieldInfo Connecting = AccessTools.Field(typeof(DebugMapInfo), "connectingWaypointMarker");
        private static readonly FieldInfo Markers = AccessTools.Field(typeof(DebugMapInfo), "convoyWaypointMarkers");
        private static readonly FieldInfo Links = AccessTools.Field(typeof(ConvoyWaypointMarker), "connectedTo");
        private static readonly FieldInfo Lines = AccessTools.Field(typeof(ConvoyWaypointMarker), "routeLines");
        private static readonly FieldInfo Data = AccessTools.Field(typeof(ConvoyWaypointMarker), "waypoint");
        private static readonly FieldInfo Preview = AccessTools.Field(typeof(ConvoyWaypointMarker), "mouseoverRouteLine");
        private static ConvoyWaypointMarker selected;
        private static bool disconnecting;
        private static GameObject toolbar;
        private static Text status;
        private static Button disconnect, cancel;

        internal static void Install(Harmony h)
        {
            foreach (FieldInfo field in new[] { Connecting, Markers, Links, Lines, Data, Preview })
                if (field == null) throw new MissingFieldException("Convoy editor schema mismatch.");
            h.Patch(AccessTools.Method(typeof(ConvoyWaypointMarker), "CompleteConnection"), prefix: new HarmonyMethod(typeof(ConvoyRoutes), "Complete"));
            h.Patch(AccessTools.Method(typeof(ConvoyWaypointMarker), "OnClick"), postfix: new HarmonyMethod(typeof(ConvoyRoutes), "Select"));
            h.Patch(AccessTools.Method(typeof(ConvoyWaypointMarker), "Connect"), prefix: new HarmonyMethod(typeof(ConvoyRoutes), "BeginConnect"));
            h.Patch(AccessTools.Method(typeof(ConvoyWaypointMarker), "OnPointerEnter"), prefix: new HarmonyMethod(typeof(ConvoyRoutes), "PreviewGuard"));
            ConvoyNavigation.Install(h);
        }

        public static IEnumerable<CodeInstruction> ArrivalIL(IEnumerable<CodeInstruction> source)
        {
            var result = new List<CodeInstruction>(source);
            int found = 0;
            foreach (CodeInstruction instruction in result)
                if (instruction.opcode == OpCodes.Ldc_R4 && Equals(instruction.operand, 0.25f))
                { instruction.operand = 0.01f; found++; }
            if (found != 1) throw new InvalidOperationException("Convoy arrival threshold pattern count=" + found);
            return result;
        }
        public static void WeightedConnection(GraphNode from, GraphNode to, uint ignored)
        {
            // Int3 uses the A* graph's own cost units. Native code assigns 1 to every
            // edge, making a long sea crossing cost the same as a short local leg.
            from.AddConnection(to, (uint)Math.Max(1, (from.position - to.position).costMagnitude));
        }

        private static bool PreviewGuard(ConvoyWaypointMarker __instance)
        {
            return !DebugMapInfo.instance || Connecting.GetValue(DebugMapInfo.instance) as ConvoyWaypointMarker != __instance;
        }
        private static void BeginConnect() { disconnecting = false; }
        private static void Select(ConvoyWaypointMarker __instance) { selected = __instance; RefreshUI(); }
        private static bool Complete(ConvoyWaypointMarker __instance, ConvoyWaypointMarker connectTo, bool existingConnection)
        {
            if (!connectTo || __instance == connectTo) { disconnecting = false; return false; }
            if (!disconnecting || existingConnection) return true;
            disconnecting = false;
            var a = Links.GetValue(__instance) as List<ConvoyWaypointMarker>;
            var b = Links.GetValue(connectTo) as List<ConvoyWaypointMarker>;
            if (a == null || b == null || !a.Contains(connectTo)) return false;
            var dataA = Data.GetValue(__instance) as Navy.ConvoyWaypoint;
            var dataB = Data.GetValue(connectTo) as Navy.ConvoyWaypoint;
            a.Remove(connectTo); b.Remove(__instance);
            if (dataA != null && dataB != null) { dataA.connectedTo.Remove(dataB); dataB.connectedTo.Remove(dataA); }
            var linesA = Lines.GetValue(__instance) as List<MapRouteLine>;
            var linesB = Lines.GetValue(connectTo) as List<MapRouteLine>;
            if (linesA != null) foreach (MapRouteLine line in new List<MapRouteLine>(linesA))
                if (line && ((line.startTransform == __instance.rectTransform && line.endTransform == connectTo.rectTransform)
                    || (line.endTransform == __instance.rectTransform && line.startTransform == connectTo.rectTransform)))
                { linesA.Remove(line); if (linesB != null) linesB.Remove(line); UnityEngine.Object.Destroy(line.gameObject); }
            GlobalNavigationManager.instance.SetConvoyWaypoints();
            InterfacePlugin.Log("Convoy link removed through native waypoint data.");
            return false;
        }
        private static void Cancel()
        {
            if (!DebugMapInfo.instance) return;
            Connecting.SetValue(DebugMapInfo.instance, null);
            disconnecting = false;
            var markers = Markers.GetValue(DebugMapInfo.instance) as List<ConvoyWaypointMarker>;
            if (markers != null) foreach (ConvoyWaypointMarker marker in markers)
            {
                if (!marker) continue;
                var line = Preview.GetValue(marker) as MapRouteLine;
                if (line) UnityEngine.Object.Destroy(line.gameObject);
                Preview.SetValue(marker, null);
            }
            if (UIMapScreen.instance) UIMapScreen.instance.convoyWaypointMenu.Hide();
            selected = null;
            if (toolbar) toolbar.SetActive(false);
        }
        internal static void InputUpdate()
        {
            if (!DebugMapInfo.instance) return;
            bool connecting = Connecting.GetValue(DebugMapInfo.instance) as ConvoyWaypointMarker;
            bool menuOpen = UIMapScreen.instance && UIMapScreen.instance.convoyWaypointMenu && UIMapScreen.instance.convoyWaypointMenu.isActiveAndEnabled;
            if ((connecting || menuOpen) && (!UIMapScreen.instance || !UIMapScreen.instance.isActiveAndEnabled || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))) Cancel();
        }
        internal static void RefreshUI()
        {
            if (!UIMapScreen.instance || !DebugMapInfo.instance) { if (toolbar) toolbar.SetActive(false); return; }
            bool connecting = Connecting.GetValue(DebugMapInfo.instance) as ConvoyWaypointMarker;
            bool menuOpen = UIMapScreen.instance.convoyWaypointMenu && UIMapScreen.instance.convoyWaypointMenu.isActiveAndEnabled;
            bool editing = UIMapScreen.instance.isActiveAndEnabled && (connecting || menuOpen);
            if (!editing) { if (toolbar) toolbar.SetActive(false); return; }
            var markers = Markers.GetValue(DebugMapInfo.instance) as List<ConvoyWaypointMarker>;
            if (markers == null) return;
            if (!toolbar) MakeToolbar();
            int points = 0, edges = 0;
            foreach (ConvoyWaypointMarker marker in markers)
                if (marker) { points++; var connected = Links.GetValue(marker) as List<ConvoyWaypointMarker>; if (connected != null) edges += connected.Count; }
            toolbar.SetActive(true);
            bool ru = RussianCompletion.Russian;
            status.text = connecting ? (disconnecting ? (ru ? "Выберите второй конец связи. Esc / ПКМ — отмена." : "Select the other endpoint. Esc / RMB cancels.")
                : (ru ? "Выберите другую точку маршрута. Esc / ПКМ — отмена." : "Select another waypoint. Esc / RMB cancels."))
                : (ru ? "Маршруты конвоев: точек " + points + ", связей " + edges / 2 + ". Выберите точку для изменения."
                      : "Convoy routes: " + points + " points, " + edges / 2 + " links. Select a point to edit.");
            disconnect.interactable = selected && ((List<ConvoyWaypointMarker>)Links.GetValue(selected)).Count > 0 && !connecting;
            cancel.interactable = connecting;
        }
        private static void MakeToolbar()
        {
            toolbar = new GameObject("Convoy route controls", typeof(RectTransform), typeof(Image));
            toolbar.transform.SetParent(UIMapScreen.instance.transform, false);
            var rect = (RectTransform)toolbar.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -90); rect.sizeDelta = new Vector2(650, 65);
            toolbar.GetComponent<Image>().color = new Color(.08f, .13f, .17f, .94f);
            status = Label(toolbar.transform, "Route status", 0, 12, 635, 20, 14);
            disconnect = Button(toolbar.transform, "Разорвать связь", -180, -13, 170, delegate
            {
                if (!selected || !DebugMapInfo.instance) return;
                ConvoyWaypointMarker marker = selected;
                Cancel(); selected = marker; disconnecting = true;
                Connecting.SetValue(DebugMapInfo.instance, selected);
                RefreshUI();
            });
            cancel = Button(toolbar.transform, "Отмена соединения", 30, -13, 190, delegate { Cancel(); RefreshUI(); });
            Button(toolbar.transform, "Готово", 245, -13, 90, delegate { Cancel(); RefreshUI(); });
            var tooltip = toolbar.AddComponent<UITooltip>(); tooltip.localise = false; tooltip.tooltipAnchor = rect;
            tooltip.tooltip = "Соединяйте точки в безопасный маршрут для своих конвоев снабжения. От портов до сети суда идут штатным морским путём. Военные соединения эту сеть не используют.";
        }
        private static Text Label(Transform parent, string name, float x, float y, float width, float height, int size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(width, height);
            Text text = go.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            return text;
        }
        private static Button Button(Transform parent, string title, float x, float y, float width, UnityEngine.Events.UnityAction action)
        {
            Text text = Label(parent, title, x, y, width, 25, 14); text.text = title;
            var imageObject = new GameObject(title + " button", typeof(RectTransform), typeof(Image)); imageObject.transform.SetParent(parent, false);
            var rect = (RectTransform)imageObject.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, 25);
            imageObject.GetComponent<Image>().color = new Color(.22f, .33f, .4f, 1);
            text.transform.SetParent(imageObject.transform, false); ((RectTransform)text.transform).anchoredPosition = Vector2.zero;
            Button button = imageObject.AddComponent<Button>(); button.targetGraphic = imageObject.GetComponent<Image>(); button.onClick.AddListener(action);
            return button;
        }
        internal static void Cleanup() { Cancel(); ConvoyNavigation.HandoverToNative(); if (toolbar) UnityEngine.Object.Destroy(toolbar); }
    }
}
