using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace VaspCommunityMods {
 [BepInPlugin("com.vasp.communitymods", "VASP Community Mods", "0.3.0.1")]
 [BepInProcess("VictoryAtSeaPacific.exe")]
 [BepInDependency("com.vasp.hotfix")]
 [BepInDependency("com.vasp.scoutpatrol")]
 [BepInDependency("com.vasp.interfacetools")]
 public sealed class Plugin : BaseUnityPlugin {
  private Harmony harmony;
  private Harmony construction;
  private Harmony industry;
  private ConfigEntry<bool> industryChoice;
  private ConfigEntry<bool> constructionChoice;
  private ConfigEntry<int> constructionPercent;
  public static Plugin Instance { get; private set; }
  public bool ConstructionApplied { get; private set; }
  public string ConstructionError { get; private set; }
  public bool IndustryAvailable { get; private set; }
  public bool IndustryEnabled { get { return IndustryAvailable && industryChoice.Value; } }
  public string IndustryError { get; private set; }
  public int NormalEnemyBuildSpeedPercent { get { return constructionPercent.Value; } }
  private void Awake() {
   Instance=this;
   constructionChoice=Config.Bind("Balance","NormalConstructionEnabled",true,"Medium difficulty only: use the selected enemy ship construction speed. Disabled restores vanilla 150%. Live change; existing queue work and saves are not rewritten.");
   constructionPercent=Config.Bind("Balance","NormalEnemyBuildSpeedPercent",125,new ConfigDescription("Enemy ship construction speed on Medium, as a percentage of the player's rate. Vanilla is 150; default is 125. Other difficulties and incomes are unchanged.",new AcceptableValueRange<int>(100,150)));
   industryChoice=Config.Bind("Industry","Enabled",true,"Paid home-port production upgrades: 3000 bonds for 120%, then 5000 for 144%. Disabling pauses the bonus/purchases but preserves campaign levels. AI does not buy automatically.");
   if(constructionChoice.Value) SetConstructionEnabled(true);
   harmony=new Harmony("com.vasp.communitymods.screen");
   try { harmony.Patch(AccessTools.Method(typeof(UIModOptions),"OnEnable"),postfix:new HarmonyMethod(typeof(Plugin),"Attach")); }
   catch(Exception e) { harmony.UnpatchSelf(); Logger.LogError(e); }
   industry=new Harmony(HomeIndustry.Owner);
   try { HomeIndustry.Install(industry); IndustryAvailable=true; }
   catch(Exception e) { industry.UnpatchSelf(); IndustryError=e.ToString(); Logger.LogError(IndustryError); }
  }
  public void SetIndustryEnabled(bool value) { industryChoice.Value=value; Config.Save(); }
  public bool SetConstructionEnabled(bool value) {
   ConstructionError=null;
   try {
    if(value!=ConstructionApplied) {
     if(value) { construction=new Harmony(NormalConstruction.Owner); NormalConstruction.Install(construction); ConstructionApplied=true; }
     else { construction.UnpatchSelf(); ConstructionApplied=false; }
    }
    constructionChoice.Value=value; Config.Save(); return true;
   } catch(Exception error) {
    if(value && !ConstructionApplied && construction!=null) construction.UnpatchSelf();
    ConstructionError=error.ToString(); Logger.LogError(ConstructionError); return false;
   }
  }
  public void SetNormalEnemyBuildSpeedPercent(int value) {
   if(value<100 || value>150) throw new ArgumentOutOfRangeException("value");
   constructionPercent.Value=value; Config.Save();
  }
  internal void ReportConstructionConflict() {
   if(ConstructionError!=null) return;
   ConstructionError="Множитель строительства изменён другим модом; его значение сохранено.";
   Logger.LogWarning(ConstructionError);
  }
  private static void Attach(UIModOptions __instance) {
   if(!__instance.GetComponent<CommunitySection>()) __instance.gameObject.AddComponent<CommunitySection>();
  }
  private void OnDestroy() {
   try { if(industry!=null) industry.UnpatchSelf(); if(construction!=null) construction.UnpatchSelf(); }
   finally { ConstructionApplied=false; if(harmony!=null) harmony.UnpatchSelf(); if(Instance==this) Instance=null; }
  }
 }
 // Child of the real screen; never registered with ModManager or its single-profile list.
 public sealed class CommunitySection : MonoBehaviour {
  private readonly List<GameObject> rows=new List<GameObject>();
  private Text state;
  private LayoutElement stateSize;
  private UIModOptions screen;
  private float nextRefresh;
  private string actionError;
  private void Start() {
   screen=GetComponent<UIModOptions>();
   var source=screen.activeModLabel;
   if(!source) throw new InvalidOperationException("Native modifications label missing");
   var root=new GameObject("Community code modules",typeof(RectTransform));
   root.transform.SetParent(transform,false); rows.Add(root);
   var rect=(RectTransform)root.transform; rect.anchorMin=new Vector2(.48f,.18f); rect.anchorMax=new Vector2(.73f,.88f); rect.offsetMin=Vector2.zero; rect.offsetMax=Vector2.zero;
   var background=root.AddComponent<Image>(); background.color=new Color(.07f,.1f,.13f,.96f);
   var scroll=root.AddComponent<ScrollRect>(); scroll.horizontal=false; scroll.vertical=true; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=25;
   var viewport=new GameObject("Community viewport",typeof(RectTransform),typeof(RectMask2D)); viewport.transform.SetParent(root.transform,false);
   var viewportRect=(RectTransform)viewport.transform; viewportRect.anchorMin=Vector2.zero; viewportRect.anchorMax=Vector2.one; viewportRect.offsetMin=new Vector2(8,8); viewportRect.offsetMax=new Vector2(-20,-8); scroll.viewport=viewportRect;
   var barObject=new GameObject("Community scrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar)); barObject.transform.SetParent(root.transform,false);
   var barRect=(RectTransform)barObject.transform; barRect.anchorMin=new Vector2(1,0); barRect.anchorMax=Vector2.one; barRect.offsetMin=new Vector2(-12,8); barRect.offsetMax=new Vector2(-4,-8);
   barObject.GetComponent<Image>().color=new Color(.15f,.2f,.25f,1);
   var handle=new GameObject("Community scroll handle",typeof(RectTransform),typeof(Image)); handle.transform.SetParent(barObject.transform,false); handle.GetComponent<Image>().color=new Color(.65f,.75f,.8f,1);
   var handleRect=(RectTransform)handle.transform; handleRect.anchorMin=Vector2.zero; handleRect.anchorMax=Vector2.one; handleRect.offsetMin=Vector2.zero; handleRect.offsetMax=Vector2.zero;
   var bar=barObject.GetComponent<Scrollbar>(); bar.handleRect=(RectTransform)handle.transform; bar.targetGraphic=handle.GetComponent<Image>(); bar.direction=Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar=bar;
   var content=new GameObject("Community controls",typeof(RectTransform)); content.transform.SetParent(viewport.transform,false);
   var contentRect=(RectTransform)content.transform; contentRect.anchorMin=new Vector2(0,1); contentRect.anchorMax=Vector2.one; contentRect.pivot=new Vector2(.5f,1); contentRect.sizeDelta=Vector2.zero; scroll.content=contentRect;
   var layout=content.AddComponent<VerticalLayoutGroup>(); layout.childForceExpandHeight=false; layout.childForceExpandWidth=true; layout.spacing=4;
   var fit=content.AddComponent<ContentSizeFitter>(); fit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
   AddText(content.transform,source,"Community Mods — независимые кодовые модули");
   AddText(content.transform,source,"H1–H5: выбор сохраняется; изменение вступит в силу только после полного перезапуска игры.");
   for(int i=0;i<5;i++) { int n=i; AddButton(content.transform,source,"H"+(i+1)+" — изменить выбор (перезапуск)",delegate { var h=VaspHotfix.VaspHotfixPlugin.Instance; if(h) h.Request(n,!h.Requested(n)); }); }
   AddButton(content.transform,source,"Автопатруль — включить / отключить",delegate { ToggleModule("VaspScoutPatrol.PatrolPlugin", "VaspScoutPatrol", "SetEnabled", "Applied"); });
   AddButton(content.transform,source,"Конвои — включить / отключить",delegate { ToggleModule("VaspInterfaceTools.InterfacePlugin", "VaspInterfaceTools", "SetConvoyEnabled", "ConvoyApplied"); });
   AddButton(content.transform,source,"Дополнение RU — включить / отключить",delegate { ToggleModule("VaspInterfaceTools.InterfacePlugin", "VaspInterfaceTools", "SetRussianEnabled", "RussianApplied"); });
   AddButton(content.transform,source,"Обычный уровень: баланс строительства — вкл. / выкл.",delegate { var p=Plugin.Instance; if(p) p.SetConstructionEnabled(!p.ConstructionApplied); });
   AddButton(content.transform,source,"Промышленность портов — включить / отключить",delegate { var p=Plugin.Instance; if(p) p.SetIndustryEnabled(!p.IndustryEnabled); });
   state=AddText(content.transform,source,""); stateSize=state.GetComponent<LayoutElement>();
  }
  private void ToggleModule(string type,string assembly,string method,string property) {
   actionError=null;
   try {
    var t=Type.GetType(type+", "+assembly,true);
    var instance=t.GetProperty("Instance",BindingFlags.Public|BindingFlags.Static).GetValue(null,null);
    if(instance==null) throw new InvalidOperationException("Модуль недоступен");
    var p=t.GetProperty(property); var m=t.GetMethod(method);
    if(p==null || m==null) throw new InvalidOperationException("API модуля недоступен");
    m.Invoke(instance,new object[] { !(bool)p.GetValue(instance,null) });
   } catch(Exception e) { actionError=e.GetBaseException().Message; Debug.LogError(e); }
   nextRefresh=0;
  }
  private static Text AddText(Transform parent,Text source,string label) {
   var go=new GameObject("Community label",typeof(RectTransform)); go.transform.SetParent(parent,false);
   var text=go.AddComponent<Text>(); text.font=source.font; text.fontSize=14; text.color=Color.white; text.text=label; text.raycastTarget=false; text.horizontalOverflow=HorizontalWrapMode.Wrap; text.verticalOverflow=VerticalWrapMode.Overflow;
   var size=go.AddComponent<LayoutElement>(); size.preferredHeight=38; return text;
  }
  private static void AddButton(Transform parent,Text source,string label,UnityEngine.Events.UnityAction action) {
   var go=new GameObject("Community action",typeof(RectTransform)); go.transform.SetParent(parent,false);
   var image=go.AddComponent<Image>(); image.color=new Color(.12f,.18f,.22f,.95f);
   var button=go.AddComponent<Button>(); button.targetGraphic=image; button.onClick.AddListener(action);
   var text=AddText(go.transform,source,label); text.alignment=TextAnchor.MiddleLeft; var rt=(RectTransform)text.transform; rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=new Vector2(5,0); rt.offsetMax=Vector2.zero;
   var size=go.AddComponent<LayoutElement>(); size.preferredHeight=32;
  }
  private void Update() {
   if(!state || Time.unscaledTime<nextRefresh) return; nextRefresh=Time.unscaledTime+.2f;
   var h=VaspHotfix.VaspHotfixPlugin.Instance; string text="";
   if(h) for(int i=0;i<5;i++) text+="H"+(i+1)+": "+(h.Applied(i)?"действует":"выкл.")+(h.PendingRestart(i)?" → "+(h.Requested(i)?"вкл.":"выкл.")+" после перезапуска":"")+"\n";
   var p=VaspScoutPatrol.PatrolPlugin.Instance; var tools=VaspInterfaceTools.InterfacePlugin.Instance;
   text+="Патруль: "+(p && p.Applied?"действует":"выкл.")+"\nКонвои: "+(tools && tools.ConvoyApplied?"действуют":"выкл.")+"\nRU: "+(tools && tools.RussianApplied?"действует":"выкл.");
   var balance=Plugin.Instance;
   if(balance) text+="\nОбычный уровень — строительство ИИ: "+(balance.ConstructionApplied?balance.NormalEnemyBuildSpeedPercent:150)+"%";
   if(balance) text+="\nПромышленность: "+(balance.IndustryEnabled?"действует":"выкл.");
   if(p && p.LastError!=null) text+="\nПатруль: "+p.LastError;
   if(tools && tools.LastError!=null) text+="\nИнтерфейс: "+tools.LastError;
   if(h && h.LastError!=null) text+="\nХотфикс: "+h.LastError;
   if(balance && balance.ConstructionError!=null) text+="\nСтроительство: "+balance.ConstructionError;
   if(balance && balance.IndustryError!=null) text+="\nПромышленность: "+balance.IndustryError;
   if(IndustryState.Instance && IndustryState.Instance.LoadError!=null) text+="\nПромышленность: "+IndustryState.Instance.LoadError;
   if(actionError!=null) text+="\nДействие: "+actionError;
   state.text=text;
   stateSize.preferredHeight=Math.Max(38,state.preferredHeight+8);
  }
  private void OnDestroy() { foreach(var row in rows) if(row) Destroy(row); }
 }
}
