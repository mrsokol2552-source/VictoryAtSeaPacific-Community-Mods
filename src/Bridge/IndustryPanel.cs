using System;
using UnityEngine;
using UnityEngine.UI;

namespace VaspCommunityMods
{
    public sealed class IndustryPanel : MonoBehaviour
    {
        private UIPortInformation information;
        private GameObject root;
        private Text title, description, buyLabel;
        private Button buy, cancel;
        private Port selected, confirmedPort;
        private int confirmedLevel;
        private float confirmUntil, nextRefresh;
        private string notice;

        private void Start()
        {
            information = GetComponent<UIPortInformation>();
            if (!information || !information.ui || !information.header) return;
            root = new GameObject("Home industry upgrades", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(information.ui.transform, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(0, 100); rect.anchoredPosition = new Vector2(0, 22);
            root.GetComponent<Image>().color = new Color(.06f, .1f, .13f, .97f);
            title = Label(root.transform, information.header.font, "Industry level", 14);
            Place(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(8, -28), new Vector2(-8, -6));
            description = Label(root.transform, information.header.font, "Industry description", 12);
            Place(description.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(8, -56), new Vector2(-8, -28));
            buy = MakeButton("Industry purchase", "", PurchaseClick); buyLabel = buy.GetComponentInChildren<Text>();
            cancel = MakeButton("Industry cancel", "Отмена", Cancel);
            Place((RectTransform)cancel.transform, new Vector2(.68f, 0), new Vector2(1, 0), new Vector2(2, 8), new Vector2(-8, 38));
            Cancel(); Refresh();
        }
        private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        {
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = low; rect.offsetMax = high;
        }
        private static Text Label(Transform parent, Font font, string name, int size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<Text>(); text.font = font; text.fontSize = size; text.color = Color.white;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        private Button MakeButton(string name, string text, UnityEngine.Events.UnityAction action)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(root.transform, false);
            var image = obj.GetComponent<Image>(); image.color = new Color(.15f, .23f, .29f, 1);
            var button = obj.GetComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var label = Label(obj.transform, information.header.font, "Industry button text", 13); label.text = text; label.alignment = TextAnchor.MiddleCenter;
            Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(3, 0), new Vector2(-3, 0));
            return button;
        }
        private void PurchaseClick()
        {
            Refresh();
            string reason;
            if (!HomeIndustry.CanPurchase(selected, out reason)) { notice = reason; Refresh(); return; }
            if (confirmedPort != selected || confirmUntil < Time.unscaledTime)
            {
                confirmedPort = selected; confirmedLevel = HomeIndustry.GetLevel(selected); confirmUntil = Time.unscaledTime + 15;
                notice = null; Refresh(); return;
            }
            bool purchased = HomeIndustry.TryPurchase(selected, confirmedLevel, out reason);
            Cancel(); notice = purchased ? "Улучшение куплено. Выпуск: " + HomeIndustry.ProductionPercent(selected) + "%" : reason;
            Refresh();
        }
        private void Cancel()
        {
            confirmedPort = null; confirmUntil = 0; nextRefresh = 0;
            if (cancel) cancel.gameObject.SetActive(false);
        }
        private void Update()
        {
            if (!root) return;
            if (confirmedPort != null && (Time.unscaledTime > confirmUntil || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))) Cancel();
            if (Time.unscaledTime >= nextRefresh) Refresh();
        }
        public void Refresh()
        {
            if (!root) return;
            nextRefresh = Time.unscaledTime + .2f;
            Port port = HomeIndustry.SelectedPort;
            if (port != selected) { selected = port; notice = null; Cancel(); }
            bool visible = Plugin.Instance && Plugin.Instance.IndustryAvailable && HomeIndustry.CampaignActive
                && UIBridgeScreen.instance && UIBridgeScreen.instance.isActiveAndEnabled && information.ui.activeInHierarchy
                && selected != null && selected.homePort && Player.instance && selected.GetFaction() == Player.instance.faction;
            root.SetActive(visible);
            if (!visible) { Cancel(); return; }
            int level = HomeIndustry.GetLevel(selected), percent = HomeIndustry.ProductionPercent(selected);
            int cost = HomeIndustry.NextCost(selected); string reason;
            bool possible = HomeIndustry.CanPurchase(selected, out reason), armed = confirmedPort == selected;
            if (!possible && armed) { Cancel(); armed = false; }
            int actualPercent = Plugin.Instance.IndustryEnabled && IndustryState.Instance && IndustryState.Instance.LoadError == null ? percent : 100;
            title.text = "Промышленность · уровень " + level + "/2 · выпуск " + actualPercent + "%";
            description.text = notice ?? (IndustryState.Instance && IndustryState.Instance.LoadError != null ? IndustryState.Instance.LoadError
                : !Plugin.Instance.IndustryEnabled ? "Мод отключён; купленные уровни сохранены"
                : level >= 2 ? "Все ресурсы: +44% к базовому производству"
                : armed ? "Подтвердить списание " + HomeIndustry.CostLabel(cost) + " облигаций?"
                : possible ? "Все ресурсы: " + percent + "% → " + (level == 0 ? 120 : 144) + "%"
                : reason);
            buy.interactable = possible;
            buyLabel.text = level >= 2 ? "Максимальный уровень" : armed ? "Купить за " + HomeIndustry.CostLabel(cost) : "Расширить производство — " + HomeIndustry.CostLabel(cost);
            cancel.gameObject.SetActive(armed);
            Place((RectTransform)buy.transform, Vector2.zero, new Vector2(armed ? .66f : 1, 0), new Vector2(8, 8), new Vector2(armed ? -2 : -8, 38));
        }
        private void OnDisable() { Cancel(); if (root) root.SetActive(false); }
        private void OnDestroy() { if (root) Destroy(root); }
    }
}
