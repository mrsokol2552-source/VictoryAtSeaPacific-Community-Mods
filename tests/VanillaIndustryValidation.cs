using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Path = System.IO.Path;

// Deliberately has no dependency or reference to any Community Mods assembly.
[BepInPlugin("com.vasp.validation.vanilla-industry", "Vanilla Industry Save Validation", "0.3.0.1")]
public sealed class VanillaIndustryValidation : BaseUnityPlugin
{
    private static string root;
    private static int checks, writes;
    private float deadline;
    private bool finished;
    private static void Log(string text) { File.AppendAllText(Path.Combine(root,"runtime.txt"),text+Environment.NewLine); }
    private static void Check(bool condition,string name) { if(!condition)throw new Exception(name);checks++;Log("PASS "+name); }
    private static bool Block() { writes++;return false; }
    private static bool Load(SaveManager __instance) { __instance.LoadCampaignData(File.ReadAllBytes(Path.Combine(root,"input.data")));return false; }
    private void Awake()
    {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-vanillaIndustryReport");if(i<0)return;
        root=args[i+1];File.WriteAllText(Path.Combine(root,"runtime.txt"),"Native save loading with all four Community Mods DLLs disabled.\n");
        AudioListener.pause=true;AudioListener.volume=0;Application.runInBackground=true;deadline=Time.realtimeSinceStartup+150;
        var h=new Harmony("com.vasp.validation.vanilla-industry");
        foreach(string name in new[]{"AutoSave","SaveCampaign","DeleteCampaign"})h.Patch(AccessTools.Method(typeof(SaveManager),name),prefix:new HarmonyMethod(typeof(VanillaIndustryValidation),"Block"));
        foreach(string name in new[]{"SteamSaveManager","DebugSaveManager"})
        {
            Type type=AccessTools.TypeByName(name);if(type==null)continue;
            foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                if(method.Name=="SaveGameData"||method.Name=="DeleteGameData")h.Patch(method,prefix:new HarmonyMethod(typeof(VanillaIndustryValidation),"Block"));
        }
        h.Patch(AccessTools.Method(typeof(SaveManager),"LoadCampaign"),prefix:new HarmonyMethod(typeof(VanillaIndustryValidation),"Load"));
        StartCoroutine(Guard(Run()));
    }
    private void Update() { if(root!=null&&!finished&&Time.realtimeSinceStartup>deadline)Finish("FAIL timeout"); }
    private void Finish(string result) { Log(result+"; checks="+checks+"; blockedWrites="+writes);finished=true;Application.Quit(); }
    private IEnumerator Guard(IEnumerator test)
    {
        while(true){bool more;try{more=test.MoveNext();}catch(Exception e){Finish("FAIL "+e);yield break;}if(!more)yield break;yield return test.Current;}
    }
    private IEnumerator Run()
    {
        while(!GameManager.instance||!SaveManager.instance)yield return null;
        yield return new WaitForSecondsRealtime(2);GameManager.instance.LoadCampaignGame(0);
        while((bool)AccessTools.Field(typeof(GameManager),"isLoadingGame").GetValue(GameManager.instance))yield return null;
        GameTime.SetPlayerPause(true);GameTime.timeScale=0;yield return new WaitForSecondsRealtime(1);
        Check(AccessTools.TypeByName("VaspCommunityMods.Plugin")==null,"Community Mods assembly is absent");
        Check(PortManager.instance.allPorts.Count>50 && FactionManager.instance.GetNavy(Player.instance.faction).ports.Count>0,"native game restores ports and faction ownership");
        Check(SaveManager.instance.managerReferenceData.ContainsKey("VaspCommunityMods.IndustryState"),"native loader safely retains the unknown optional manager blob");
        GameManager.instance.currentDifficulty=GameManager.GameDifficulty.Medium;
        Check(Math.Abs(GameManager.instance.GetEnemyShipBuildTimeMultiplier()-1.5f)<.0001f,"vanilla Medium construction remains 150% without mods");
        SaveManager.instance.AutoSave(ESaveType.Autosave);Check(writes>0,"native save writes remain blocked in the private test");
        byte[] vanilla=SaveManager.instance.SaveCampaignData(ESaveType.Manual,"Private vanilla validation");
        Check(!SaveManager.instance.managerReferenceData.ContainsKey("VaspCommunityMods.IndustryState") && vanilla.Length>1000000,"vanilla resave omits the absent module data as documented");
        Finish("ALL PASS vanilla-industry");
    }
}
