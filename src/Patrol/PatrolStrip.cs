using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace VaspScoutPatrol
{
    internal enum PanelKind { Ship, Airfield, Flight }
    public sealed class PatrolAnchor : MonoBehaviour { public PatrolStrip Strip; }
    public sealed class PatrolStrip : MonoBehaviour
    {
        private PatrolPlugin plugin;
        private PanelKind kind;
        private Text status;
        private Button medium, maximum, stop;
        private Font font;
        private static readonly FieldInfo ShipField = AccessToolsField(typeof(UIShipCard), "ship");
        private static readonly FieldInfo SquadronField = AccessToolsField(typeof(UIShipCard), "squadron");
        private static readonly FieldInfo AirfieldField = AccessToolsField(typeof(UIAirfieldCard), "airfield");
        private static readonly FieldInfo FlightField = AccessToolsField(typeof(UIFlightCard), "flight");
        private static readonly FieldInfo SelectionField = AccessToolsField(typeof(InteractionManager), "selectedFlights");
        private static readonly FieldInfo ShipQueueField = AccessToolsField(typeof(UIShipCard), "flightQueue");
        private static readonly FieldInfo AirfieldQueueField = AccessToolsField(typeof(UIAirfieldCard), "flightQueue");
        private static readonly FieldInfo ButtonFlightField = AccessToolsField(typeof(UIFlightButton), "flight");
        private static FieldInfo AccessToolsField(Type t, string name)
        {
            return t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        internal void Initialize(PatrolPlugin p, PanelKind k)
        {
            plugin = p; kind = k;
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, p.Offset.Value + (k == PanelKind.Flight ? 36 : 0));
            rect.sizeDelta = new Vector2(434, 60);
            LayoutElement layout = gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            Image background = gameObject.AddComponent<Image>();
            background.color = new Color(0.09f, 0.14f, 0.18f, 0.94f);
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            status = Label("Status", "Разведка: автопатруль", 0, 40, 424, 18, 13);
            medium = MakeButton("Средний", -139, 6, 138, delegate { plugin.Controller.Start(Targets(), false); Refresh(); });
            maximum = MakeButton("Макс. дальность", 8, 6, 148, delegate { plugin.Controller.Start(Targets(), true); Refresh(); });
            stop = MakeButton("Стоп / домой", 153, 6, 128, delegate { plugin.Controller.StopAndRecall(Targets()); Refresh(); });
            Tooltip(medium.gameObject, "Средняя дальность: половина безопасного радиуса. Разведчики обходят сектора вокруг корабля или аэродрома, возвращаются на заправку и вылетают снова.");
            Tooltip(maximum.gameObject, "Максимальный безопасный радиус с учётом текущего топлива, обратного пути и резерва. Новый вылет — в следующий сектор. Ручной приказ отменяет автопатруль.");
            Tooltip(stop.gameObject, "Отключить повторные вылеты и вернуть патрульных разведчиков на базу.");
            Refresh();
        }

        private void Tooltip(GameObject go, string text)
        {
            var tooltip = go.AddComponent<UITooltip>();
            tooltip.localise = false; tooltip.tooltip = text; tooltip.tooltipAnchor = (RectTransform)go.transform;
        }

        private Text Label(string name, string text, float x, float y, float w, float h, int size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            RectTransform r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0);
            r.pivot = new Vector2(0.5f, 0); r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            Text t = go.AddComponent<Text>();
            t.text = text; t.font = font; t.fontSize = size; t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        private Button MakeButton(string text, float x, float y, float width, UnityEngine.Events.UnityAction click)
        {
            var go = new GameObject(text, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            RectTransform r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0); r.pivot = new Vector2(0.5f, 0);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(width, 29);
            Image image = go.AddComponent<Image>(); image.color = new Color(0.24f, 0.34f, 0.4f, 1);
            Button b = go.AddComponent<Button>(); b.targetGraphic = image;
            ColorBlock colors = b.colors;
            colors.highlightedColor = new Color(0.72f, 0.9f, 1f); colors.pressedColor = new Color(0.4f, 0.8f, 0.7f);
            b.colors = colors; b.onClick.AddListener(click);
            Text label = Label(text + " label", text, 0, 0, width - 6, 29, 14);
            label.transform.SetParent(go.transform, false);
            return b;
        }

        private static void Add(List<AircraftFlight> result, AircraftFlight f)
        {
            if (PatrolController.Scout(f) && !result.Contains(f)) result.Add(f);
        }
        private static void AddHome(List<AircraftFlight> result, GlobalCombatObject home)
        {
            if (!home || home.aircraftFlights == null) return;
            foreach (AircraftFlight f in home.aircraftFlights) Add(result, f);
        }

        internal static void UpdateQueueUI(AircraftFlight f, bool add)
        {
            GlobalCombatObject home = NativeAccess.Home(f.leader);
            UIShipCard shipCard = UIShipCard.instance;
            UIAirfieldCard airfieldCard = UIAirfieldCard.instance;
            if (shipCard && shipCard.ui && shipCard.ui.activeInHierarchy)
            {
                var queue = ShipQueueField.GetValue(shipCard) as List<UIFlightButton>;
                bool found = false;
                if (queue != null) foreach (UIFlightButton button in new List<UIFlightButton>(queue))
                    if (button && ButtonFlightField.GetValue(button) == f)
                    { found = true; if (!add) shipCard.RemoveQueuedFlight(button); }
                var squadron = SquadronField.GetValue(shipCard) as Squadron;
                bool owns = ShipField.GetValue(shipCard) as GlobalCombatObject == home || (squadron != null && squadron.members.Contains(home));
                if (add && !found && owns) shipCard.AddQueuedFlight(f);
            }
            if (airfieldCard && airfieldCard.ui && airfieldCard.ui.activeInHierarchy)
            {
                var queue = AirfieldQueueField.GetValue(airfieldCard) as List<UIFlightButton>;
                bool found = false;
                if (queue != null) foreach (UIFlightButton button in new List<UIFlightButton>(queue))
                    if (button && ButtonFlightField.GetValue(button) == f)
                    { found = true; if (!add) airfieldCard.RemoveQueuedFlight(button); }
                if (add && !found && AirfieldField.GetValue(airfieldCard) as GlobalCombatObject == home) airfieldCard.AddQueuedFlight(f);
            }
        }

        internal List<AircraftFlight> Targets()
        {
            var result = new List<AircraftFlight>();
            var manager = InteractionManager.instance;
            var selected = manager && SelectionField != null ? SelectionField.GetValue(manager) as List<AircraftFlight> : null;
            // Explicit aircraft selection wins over the home panel, including a mixed selection.
            if (selected != null && selected.Count > 0)
            {
                foreach (AircraftFlight f in selected) Add(result, f);
                return result;
            }
            if (kind == PanelKind.Ship && UIShipCard.instance)
            {
                AddHome(result, ShipField.GetValue(UIShipCard.instance) as Ship);
                Squadron squadron = SquadronField.GetValue(UIShipCard.instance) as Squadron;
                if (squadron != null) foreach (GlobalCombatObject ship in squadron.members) AddHome(result, ship);
            }
            else if (kind == PanelKind.Airfield && UIAirfieldCard.instance)
                AddHome(result, AirfieldField.GetValue(UIAirfieldCard.instance) as CoastalStructure);
            else if (kind == PanelKind.Flight && UIFlightCard.instance)
                Add(result, FlightField.GetValue(UIFlightCard.instance) as AircraftFlight);
            return result;
        }

        internal void Refresh()
        {
            List<AircraftFlight> flights = Targets();
            bool visible = flights.Count > 0 && Player.instance;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;
            int active = 0, ready = 0, max = 0;
            foreach (AircraftFlight f in flights)
            {
                if (PatrolController.Ready(f)) ready++;
                if (plugin.Controller.Active(f)) { active++; if (plugin.Controller.IsMaximum(f)) max++; }
            }
            medium.interactable = maximum.interactable = ready > 0;
            stop.interactable = active > 0;
            medium.image.color = active > 0 && max < active ? new Color(0.15f, 0.55f, 0.4f) : new Color(0.24f, 0.34f, 0.4f);
            maximum.image.color = max > 0 ? new Color(0.15f, 0.55f, 0.4f) : new Color(0.24f, 0.34f, 0.4f);
            status.text = "Разведчики: " + flights.Count + "  |  Автопатруль: " + active + "  |  Средний / MAX";
        }
    }
}
