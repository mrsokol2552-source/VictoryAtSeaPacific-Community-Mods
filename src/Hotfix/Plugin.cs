using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
namespace VaspHotfix {
 // Shared by Awake and desktop contract tests; no Unity construction is needed to test configuration/transaction semantics.
 public static class ModuleInstaller {
  public const string Owner = "com.vasp.hotfix";
  public static bool[] ReadSettings(ConfigFile config) {
   bool enabled=config.Bind<bool>("General","Enabled",true,"Master switch; restart game after changes.").Value;
   var result=new bool[5];
   for(int i=0;i<5;i++) result[i]=config.Bind<bool>("Fixes","EnableH"+(i+1),i==0,"Enable H"+(i+1)+" (restart required; gameplay unverified).").Value;
   if(!enabled) Array.Clear(result,0,result.Length);
   return result;
  }
  public static string Apply(Harmony harmony,bool[] enabled,Action<Harmony>[] stages) {
   return Apply(harmony,enabled,stages,delegate { harmony.UnpatchSelf(); });
  }
  // Injectable rollback is solely for preserving/testing rollback errors, not an alternate production installer.
  public static string Apply(Harmony harmony,bool[] enabled,Action<Harmony>[] stages,Action rollback) {
   if(harmony.Id!=Owner || enabled.Length!=5 || stages.Length!=5) throw new ArgumentException("Invalid hotfix transaction");
   var installed=new List<string>();
   try {
    for(int i=0;i<5;i++) if(enabled[i]) { stages[i](harmony); installed.Add("H"+(i+1)); }
   } catch(Exception original) {
    try { rollback(); }
    catch(Exception recovery) { throw new InstallationRollbackException(original,recovery); }
    throw;
   }
   return installed.Count==0?"none":string.Join(", ",installed.ToArray());
  }
 }
 public sealed class InstallationRollbackException : Exception {
  public readonly Exception RollbackError;
  public InstallationRollbackException(Exception original,Exception rollback):base("Hotfix installation failed; rollback also failed: "+rollback,original) { RollbackError=rollback; }
 }
 [BepInPlugin("com.vasp.hotfix", "VictoryAtSeaPacificHotfix", "1.14.2.1")]
 public class VaspHotfixPlugin : BaseUnityPlugin {
  private Harmony harmony;
  public static VaspHotfixPlugin Instance;
  private ConfigFile settings;
  private bool[] applied = new bool[5];
  public string LastError { get; private set; }
  public bool Applied(int index) { return applied[index]; }
  public bool Requested(int index) { return ModuleInstaller.ReadSettings(settings)[index]; }
  public bool PendingRestart(int index) { return Requested(index)!=Applied(index); }
  // Runtime object repairs cannot be undone by unpatching. Only persist intent here.
  public void Request(int index,bool value) {
   if(index<0 || index>=5) throw new ArgumentOutOfRangeException("index");
   if(value && !settings.Bind<bool>("General","Enabled",true).Value) settings.Bind<bool>("General","Enabled",true).Value=true;
   settings.Bind<bool>("Fixes","EnableH"+(index+1),index==0).Value=value;
   settings.Save();
  }
  private void Awake() {
   Instance=this;
   // Explicit filename matches the optional installed template (not BepInEx GUID-default Config).
   settings=new ConfigFile(Path.Combine(Paths.ConfigPath,"VaspHotfix.cfg"),true);
   var enabled=ModuleInstaller.ReadSettings(settings);
   harmony=new Harmony(ModuleInstaller.Owner);
   H2.Log=message=>Logger.LogWarning(message); H3.Log=message=>Logger.LogWarning(message);
   try {
    string applied=ModuleInstaller.Apply(harmony,enabled,new Action<Harmony>[] { H1.Install,H2.Install,H3.Install,H4.Install,H5.Install });
    Array.Copy(enabled,this.applied,5);
    Logger.LogInfo("VictoryAtSeaPacificHotfix 1.14.2.1 installed modules: "+applied+". Harmony installation is not gameplay validation.");
   } catch(Exception error) { LastError=error.ToString(); Logger.LogError(error); }
  }
  private void OnDestroy() { if(harmony!=null) harmony.UnpatchSelf(); if(Instance==this) Instance=null; }
 }
}
