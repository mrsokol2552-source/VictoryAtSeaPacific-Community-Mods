using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace VaspCommunityMods {
 [BepInPlugin("com.vasp.communitymods", "VASP Community Mods", "0.2.0")]
 [BepInDependency("com.vasp.hotfix")]
 [BepInDependency("com.vasp.scoutpatrol")]
 [BepInDependency("com.vasp.interfacetools")]
 public sealed class Plugin : BaseUnityPlugin {
  private Harmony harmony;
  private void Awake() {
   harmony=new Harmony("com.vasp.communitymods.screen");
   try { harmony.Patch(AccessTools.Method(typeof(UIModOptions),"OnEnable"),postfix:new HarmonyMethod(typeof(Plugin),"Attach")); }
   catch(Exception e) { harmony.UnpatchSelf(); Logger.LogError(e); }
  }
  private static void Attach(UIModOptions __instance) {
   if(!__instance.GetComponent<CommunitySection>()) __instance.gameObject.AddComponent<CommunitySection>();
  }
  private void OnDestroy() { if(harmony!=null) harmony.UnpatchSelf(); }
 }
 // Child of the real screen; never registered with ModManager or its single-profile list.
 public sealed class CommunitySection : MonoBehaviour {
  private readonly List<GameObject> rows=new List<GameObject>();
  private Text state;
  private UIModOptions screen;
  private void Start() {
   screen=GetComponent<UIModOptions>();
   var source=screen.activeModLabel;
   if(!source) throw new InvalidOperationException("Native modifications label missing");
   var root=new GameObject("Community code modules",typeof(RectTransform));
   root.transform.SetParent(transform,false); rows.Add(root);
   var rect=(RectTransform)root.transform; rect.anchorMin=new Vector2(.52f,.22f); rect.anchorMax=new Vector2(.96f,.76f); rect.offsetMin=Vector2.zero; rect.offsetMax=Vector2.zero;
   var layout=root.AddComponent<VerticalLayoutGroup>(); layout.childForceExpandHeight=false; layout.spacing=4;
   AddText(root.transform,source,"Community Mods — независимые кодовые модули");
   AddText(root.transform,source,"H1–H5: выбор сохраняется; изменение вступит в силу только после полного перезапуска игры.");
   for(int i=0;i<5;i++) { int n=i; AddButton(root.transform,source,"H"+(i+1)+" — изменить выбор (перезапуск)",delegate { var h=VaspHotfix.VaspHotfixPlugin.Instance; if(h) h.Request(n,!h.Requested(n)); }); }
   AddButton(root.transform,source,"Автопатруль — включить / отключить",delegate { ToggleModule("VaspScoutPatrol.PatrolPlugin", "VaspScoutPatrol", "SetEnabled", "Applied"); });
   AddButton(root.transform,source,"Конвои — включить / отключить",delegate { ToggleModule("VaspInterfaceTools.InterfacePlugin", "VaspInterfaceTools", "SetConvoyEnabled", "ConvoyApplied"); });
   AddButton(root.transform,source,"Дополнение RU — включить / отключить",delegate { ToggleModule("VaspInterfaceTools.InterfacePlugin", "VaspInterfaceTools", "SetRussianEnabled", "RussianApplied"); });
   state=AddText(root.transform,source,"");
  }
  private void ToggleModule(string type,string assembly,string method,string property) {
   var t=Type.GetType(type+", "+assembly,true);
   var instance=t.GetProperty("Instance",BindingFlags.Public|BindingFlags.Static).GetValue(null,null);
   if(instance==null) { state.text="Модуль недоступен"; return; }
   var p=t.GetProperty(property); var m=t.GetMethod(method);
   if(p==null || m==null) { state.text="API модуля недоступен"; return; }
   try { m.Invoke(instance,new object[] { !(bool)p.GetValue(instance,null) }); }
   catch(Exception e) { state.text=e.GetBaseException().Message; Debug.LogError(e); }
  }
  private static Text AddText(Transform parent,Text source,string label) {
   var go=new GameObject("Community label",typeof(RectTransform)); go.transform.SetParent(parent,false);
   var text=go.AddComponent<Text>(); text.font=source.font; text.fontSize=14; text.color=source.color; text.text=label; text.raycastTarget=false;
   var size=go.AddComponent<LayoutElement>(); size.preferredHeight=38; return text;
  }
  private static void AddButton(Transform parent,Text source,string label,UnityEngine.Events.UnityAction action) {
   var go=new GameObject("Community action",typeof(RectTransform)); go.transform.SetParent(parent,false);
   var image=go.AddComponent<Image>(); image.color=new Color(.12f,.18f,.22f,.95f);
   var button=go.AddComponent<Button>(); button.targetGraphic=image; button.onClick.AddListener(action);
   var text=AddText(go.transform,source,label); var rt=(RectTransform)text.transform; rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=new Vector2(5,0); rt.offsetMax=Vector2.zero;
   var size=go.AddComponent<LayoutElement>(); size.preferredHeight=32;
  }
  private void Update() {
   if(!state) return;
   var h=VaspHotfix.VaspHotfixPlugin.Instance; string text="";
   if(h) for(int i=0;i<5;i++) text+="H"+(i+1)+": "+(h.Applied(i)?"действует":"выкл.")+(h.PendingRestart(i)?" → "+(h.Requested(i)?"вкл.":"выкл.")+" после перезапуска":"")+"\n";
   var p=VaspScoutPatrol.PatrolPlugin.Instance; var tools=VaspInterfaceTools.InterfacePlugin.Instance;
   text+="Патруль: "+(p && p.Applied?"действует":"выкл.")+"\nКонвои: "+(tools && tools.ConvoyApplied?"действуют":"выкл.")+"\nRU: "+(tools && tools.RussianApplied?"действует":"выкл.");
   if(p && p.LastError!=null) text+="\nПатруль: "+p.LastError;
   if(tools && tools.LastError!=null) text+="\nИнтерфейс: "+tools.LastError;
   if(h && h.LastError!=null) text+="\nХотфикс: "+h.LastError;
   state.text=text;
  }
  private void OnDestroy() { foreach(var row in rows) if(row) Destroy(row); }
 }
}
