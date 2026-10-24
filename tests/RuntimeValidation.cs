using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Pathfinding;
using UnityEngine;
using UnityEngine.UI;
using VaspCommunityMods;
using Path = System.IO.Path;

// Test helper, compiled into a separate private client only. Never packaged.
[BepInPlugin("com.vasp.communitymods.validation", "Community Runtime Validation", "0.2.0.2")]
[BepInDependency("com.vasp.communitymods")]
public sealed class RuntimeValidation : BaseUnityPlugin
{
    private static string root;
    private static int blockedWrites, checks;
    private bool finished;
    private float deadline;
    private const string Canary = "com.vasp.validation.foreign";
    private static void Record(string message) { File.AppendAllText(Path.Combine(root, "runtime.txt"), message + Environment.NewLine); }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; Record("PASS " + name); }
    private static bool Near(float a, float b) { return Math.Abs(a-b) < .0001f; }
    private static T Get<T>(object obj, string name) { return (T)AccessTools.Field(obj.GetType(), name).GetValue(obj); }
    private static void Set(object obj, string name, object value) { AccessTools.Field(obj.GetType(), name).SetValue(obj,value); }
    private static string Arg(string name) { var a=Environment.GetCommandLineArgs(); int i=Array.IndexOf(a,name); return i>=0 && i+1<a.Length ? a[i+1] : null; }

    private void Awake()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-vasp-community-validation")<0) return;
        root=Arg("-validationRoot");
        if (String.IsNullOrEmpty(root) || !Directory.Exists(root)) throw new InvalidOperationException("Private validation directory required.");
        AudioListener.pause=true; AudioListener.volume=0; Application.runInBackground=true;
        File.WriteAllText(Path.Combine(root,"runtime.txt"),"Private Unity/Mono validation; no production probes or save writes.\n");
        deadline=Time.realtimeSinceStartup+180;
        try
        {
            var guard=new Harmony("com.vasp.communitymods.validation");
            foreach(string name in new[]{"AutoSave","SaveCampaign","DeleteCampaign"})
                guard.Patch(AccessTools.Method(typeof(SaveManager),name),prefix:new HarmonyMethod(typeof(RuntimeValidation),"BlockWrite"));
            foreach(string typeName in new[]{"SteamSaveManager","DebugSaveManager"})
            {
                Type type=AccessTools.TypeByName(typeName); if(type==null) continue;
                foreach(MethodInfo method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                    if(method.Name=="SaveGameData" || method.Name=="DeleteGameData")
                        guard.Patch(method,prefix:new HarmonyMethod(typeof(RuntimeValidation),"BlockWrite"));
            }
            guard.Patch(AccessTools.Method(typeof(SaveManager),"LoadCampaign"),prefix:new HarmonyMethod(typeof(RuntimeValidation),"LoadCopy"));
            StartCoroutine(Guard(Run()));
        }
        catch(Exception error) { Finish("FAIL startup "+error); }
    }
    private static bool BlockWrite() { blockedWrites++; return false; }
    private static bool LoadCopy(SaveManager __instance) { __instance.LoadCampaignData(File.ReadAllBytes(Path.Combine(root,"input.data"))); return false; }
    private void Update() { if(root!=null && !finished && Time.realtimeSinceStartup>deadline) Finish("FAIL timeout"); }
    private void Finish(string result) { Record(result+"; checks="+checks+"; blockedWrites="+blockedWrites); finished=true; Application.Quit(); }
    private IEnumerator Guard(IEnumerator routine)
    {
        while(true) { bool more; try { more=routine.MoveNext(); } catch(Exception e) { Finish("FAIL "+e); yield break; } if(!more) yield break; yield return routine.Current; }
    }
    private static void ForeignMultiplier(ref float __result) { __result=2f; }

    private static void TestMultipliers()
    {
        var plugin=Plugin.Instance; var gm=GameManager.instance; var old=gm.currentDifficulty;
        Check(plugin && plugin.ConstructionApplied,"construction module loaded");
        plugin.SetNormalEnemyBuildSpeedPercent(125); plugin.SetConstructionEnabled(true);
        var modes=new[]{GameManager.GameDifficulty.Easy,GameManager.GameDifficulty.Medium,GameManager.GameDifficulty.Hard,GameManager.GameDifficulty.Very_Hard};
        var expected=new[]{1f,1.25f,2f,8f};
        for(int i=0;i<modes.Length;i++) { gm.currentDifficulty=modes[i]; Check(Near(gm.GetEnemyShipBuildTimeMultiplier(),expected[i]),"native getter "+modes[i]+"="+expected[i]); }
        gm.currentDifficulty=GameManager.GameDifficulty.Medium;
        for(int i=0;i<3;i++)
        {
            Check(plugin.SetConstructionEnabled(false) && Near(gm.GetEnemyShipBuildTimeMultiplier(),1.5f),"disable restores native 150%, cycle "+i);
            Check(plugin.SetConstructionEnabled(true) && Near(gm.GetEnemyShipBuildTimeMultiplier(),1.25f),"enable restores selected 125%, cycle "+i);
        }
        foreach(int percent in new[]{100,150,125}) { plugin.SetNormalEnemyBuildSpeedPercent(percent); Check(Near(gm.GetEnemyShipBuildTimeMultiplier(),percent/100f),"config rate "+percent); }
        var foreign=new Harmony(Canary);
        foreign.Patch(AccessTools.Method(typeof(GameManager),"GetEnemyShipBuildTimeMultiplier"),postfix:new HarmonyMethod(typeof(RuntimeValidation),"ForeignMultiplier"));
        Check(Near(gm.GetEnemyShipBuildTimeMultiplier(),2f),"foreign multiplier not replaced");
        plugin.SetConstructionEnabled(false);
        Check(Near(gm.GetEnemyShipBuildTimeMultiplier(),2f),"disabling own module preserves foreign patch");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(GameManager),"GetEnemyShipBuildTimeMultiplier")).Owners.Contains(Canary),"foreign Harmony owner retained");
        foreign.UnpatchSelf(); plugin.SetConstructionEnabled(true); gm.currentDifficulty=old;
    }

    private static void TestConstruction()
    {
        var gm=GameManager.instance; var old=gm.currentDifficulty;
        var port=new Port(); Set(port,"currentFaction",FactionManager.instance.GetFaction("UK")); Set(port,"homePort",true);
        Check(FactionManager.instance.IsHostile(port.GetFaction(),Player.instance.faction,true),"construction fixture uses actual enemy faction");
        var agent=new PortAIAgent(port);
        var update=AccessTools.Method(typeof(PortAIAgent),"UpdateShipConstruction",new[]{typeof(Fleet),typeof(float)});
        foreach(var mode in new[]{GameManager.GameDifficulty.Easy,GameManager.GameDifficulty.Medium,GameManager.GameDifficulty.Hard,GameManager.GameDifficulty.Very_Hard})
        {
            gm.currentDifficulty=mode;
            var queue=new List<Fleet.ShipConstruction>(); for(int i=0;i<7;i++) queue.Add(new Fleet.ShipConstruction{remainingBuildTime=100,totalBuildTime=100});
            var fleet=new Fleet(); Set(fleet,"buildQueue",queue);
            update.Invoke(agent,new object[]{fleet,4f});
            float rate=mode==GameManager.GameDifficulty.Easy?1:mode==GameManager.GameDifficulty.Medium?1.25f:mode==GameManager.GameDifficulty.Hard?2:8;
            Check(Near(queue[0].remainingBuildTime,100-4*rate) && Near(queue[5].remainingBuildTime,100-4*rate),"native queue progress and six parallel slots: "+mode);
            Check(Near(queue[6].remainingBuildTime,100),"waiting construction slot unchanged: "+mode);
        }
        gm.currentDifficulty=GameManager.GameDifficulty.Medium; Set(port,"currentFaction",Player.instance.faction);
        var ownQueue=new List<Fleet.ShipConstruction>{new Fleet.ShipConstruction{remainingBuildTime=100,totalBuildTime=100}};
        var ownFleet=new Fleet(); Set(ownFleet,"buildQueue",ownQueue); update.Invoke(agent,new object[]{ownFleet,4f});
        Check(Near(ownQueue[0].remainingBuildTime,96),"player queue remains 100%"); gm.currentDifficulty=old;
    }

    private static Button Button(CommunitySection section,string prefix)
    {
        foreach(var button in section.GetComponentsInChildren<Button>(true)) { var text=button.GetComponentInChildren<Text>(); if(text && text.text.StartsWith(prefix)) return button; }
        throw new Exception("Community button missing: "+prefix);
    }

    private IEnumerator Run()
    {
        while(!GameManager.instance || !SaveManager.instance) yield return null;
        yield return new WaitForSecondsRealtime(2);
        if(Arg("-validationPhase")=="restart")
        {
            Check(Plugin.Instance.ConstructionApplied && Plugin.Instance.NormalEnemyBuildSpeedPercent==125,"125% and enabled choice survive a full restart");
            GameManager.instance.currentDifficulty=GameManager.GameDifficulty.Medium;
            Check(Near(GameManager.instance.GetEnemyShipBuildTimeMultiplier(),1.25f),"native multiplier after full restart");
            Finish("ALL PASS restart"); yield break;
        }
        GameManager.instance.LoadCampaignGame(0);
        while(Get<bool>(GameManager.instance,"isLoadingGame")) yield return null;
        GameTime.SetPlayerPause(true); GameTime.timeScale=0;
        yield return new WaitForSecondsRealtime(2);
        TestMultipliers(); TestConstruction();
        SaveManager.instance.AutoSave(ESaveType.Autosave); Check(blockedWrites>0,"native autosave write blocked");
        var tools=VaspInterfaceTools.InterfacePlugin.Instance; Check(tools.ConvoyApplied && tools.RussianApplied,"interface modules initially loaded");
        var graphs=(NavGraph[])AstarPath.active.astarData.graphs.Clone();
        for(int i=0;i<100;i++) { GlobalNavigationManager.instance.SetConvoyWaypoints(); yield return null; }
        yield return null;
        Check(AstarPath.active.astarData.graphs.Length==graphs.Length,"100 graph rebuilds retain slot count");
        for(int i=0;i<graphs.Length;i++) Check(object.ReferenceEquals(graphs[i],AstarPath.active.astarData.graphs[i]),"graph identity "+i);
        Check(GlobalNavigationManager.instance.pointGraphMask!=GlobalNavigationManager.instance.navmeshGraphMask,"convoy and sea masks separate");
        UIRoot.ShowScreen("ModScreen",false,true); yield return new WaitForSecondsRealtime(1);
        UIModOptions options=null; foreach(var value in Resources.FindObjectsOfTypeAll<UIModOptions>()) if(value.gameObject.activeInHierarchy) { options=value; break; }
        Check(options,"native Modifications screen active"); var section=options.GetComponent<CommunitySection>(); Check(section,"community section attached");
        var stock=ModManager.instance.activeModName;
        var h=VaspHotfix.VaspHotfixPlugin.Instance; bool requested=h.Requested(0),applied=h.Applied(0);
        Button(section,"H1 ").onClick.Invoke(); Check(h.Requested(0)!=requested && h.Applied(0)==applied,"H1 choice deferred until restart");
        Button(section,"H1 ").onClick.Invoke(); Check(h.Requested(0)==requested,"H1 choice restored");
        for(int cycle=0;cycle<2;cycle++)
        {
            bool patrol=VaspScoutPatrol.PatrolPlugin.Instance.Applied;
            Button(section,"Автопатруль").onClick.Invoke(); Check(VaspScoutPatrol.PatrolPlugin.Instance.Applied!=patrol,"patrol button transition "+cycle);
            Button(section,"Автопатруль").onClick.Invoke(); Check(VaspScoutPatrol.PatrolPlugin.Instance.Applied==patrol,"patrol button restore "+cycle);
            Button(section,"Конвои").onClick.Invoke(); Check(!tools.ConvoyApplied,"convoy button disabled "+cycle);
            Button(section,"Конвои").onClick.Invoke(); Check(tools.ConvoyApplied,"convoy button enabled "+cycle);
            Button(section,"Дополнение RU").onClick.Invoke(); Check(!tools.RussianApplied,"RU button disabled "+cycle);
            Button(section,"Дополнение RU").onClick.Invoke(); Check(tools.RussianApplied,"RU button enabled "+cycle);
        }
        Button(section,"Обычный уровень").onClick.Invoke(); Check(!Plugin.Instance.ConstructionApplied,"construction menu button disabled");
        Button(section,"Обычный уровень").onClick.Invoke(); Check(Plugin.Instance.ConstructionApplied,"construction menu button enabled");
        Check(ModManager.instance.activeModName==stock,"stock data-mod selection preserved");
        string ratio=LocalisationManager.instance.GetLocalised("UI_SUPPLY_FLEETS_PER_PORT");
        Check(ratio.Contains("[number]") && ratio.Contains("в среднем"),"supply-fleet ratio label explains average, preserves placeholder");
        var communityRoot=section.transform.Find("Community code modules"); Check(communityRoot,"community controls root");
        var scroll=communityRoot.GetComponent<ScrollRect>(); Check(scroll && scroll.viewport && scroll.content,"scrollable community controls");
        Check(scroll.verticalScrollbar && scroll.verticalScrollbar.handleRect,"visible scrollbar and draggable handle");
        yield return new WaitForSecondsRealtime(.5f); Canvas.ForceUpdateCanvases();
        Check(scroll.verticalScrollbar.handleRect.rect.width<=16,"scrollbar handle does not cover controls");
        Record("LAYOUT viewport="+scroll.viewport.rect+" content="+scroll.content.rect+" preferred="+LayoutUtility.GetPreferredHeight(scroll.content)+" children="+scroll.content.childCount);
        Check(scroll.content.childCount==12 && scroll.content.rect.height>=LayoutUtility.GetPreferredHeight(scroll.content)-1,"content height includes every control and status line");
        Check(scroll.content.rect.height>scroll.viewport.rect.height,"overflow reachable by scrolling at 1280x720");
        scroll.verticalNormalizedPosition=1; Application.CaptureScreenshot(Path.Combine(root,"modifications-top.png"));
        yield return new WaitForSecondsRealtime(.5f); scroll.verticalNormalizedPosition=0;
        yield return new WaitForSecondsRealtime(.5f); Canvas.ForceUpdateCanvases();
        Application.CaptureScreenshot(Path.Combine(root,"modifications-bottom.png"));
        yield return new WaitForSecondsRealtime(1);
        Check(File.Exists(Path.Combine(root,"modifications-top.png")) && File.Exists(Path.Combine(root,"modifications-bottom.png")),"menu screenshots rendered");
        int sections=options.GetComponents<CommunitySection>().Length;
        options.gameObject.SetActive(false); yield return null; options.gameObject.SetActive(true); yield return new WaitForSecondsRealtime(.5f);
        Check(options.GetComponents<CommunitySection>().Length==sections && sections==1,"screen reopening does not duplicate controls");
        Plugin.Instance.SetNormalEnemyBuildSpeedPercent(125); Plugin.Instance.SetConstructionEnabled(true);
        yield return new WaitForSecondsRealtime(.5f); Finish("ALL PASS main");
    }
}
