using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;

namespace VaspHotfix {
 // Cached, type-checked field metadata. No name lookup in any lifecycle/frame callback.
 internal static class H1 {
  static FieldInfo Field(Type t,string n,Type expected) {
   FieldInfo f=AccessTools.Field(t,n);
   if(f==null || f.FieldType!=expected || f.IsStatic) throw new InvalidOperationException("H1 field mismatch: "+t+"."+n);
   return f;
  }
  static readonly FieldInfo Flight=Field(typeof(Aircraft),"flight",typeof(AircraftFlight));
  static readonly FieldInfo Rank=Field(typeof(Aircraft),"flightRank",typeof(int));
  static readonly FieldInfo State=Field(typeof(Aircraft),"currentState",typeof(Aircraft.State));
  static readonly FieldInfo Info=Field(typeof(Aircraft),"unitInfoUI",typeof(UIWorldspaceUnitInfo));
  static readonly FieldInfo Removing=Field(typeof(Aircraft),"removing",typeof(bool));
  static readonly FieldInfo Tmp=Field(typeof(Aircraft),"tmpTransform",typeof(Transform));
  static readonly FieldInfo SquadronRef=Field(typeof(GlobalCombatObject),"squadron",typeof(Squadron));
  static readonly FieldInfo SquadronRank=Field(typeof(GlobalCombatObject),"squadronRank",typeof(int));
  static readonly FieldInfo Independent=Field(typeof(GlobalCombatObject),"independent",typeof(bool));
  static readonly FieldInfo Cache=Field(typeof(Squadron),"_leader",typeof(GlobalCombatObject));
  static readonly FieldInfo PortSquadrons=Field(typeof(Port),"squadrons",typeof(List<Squadron>));
  static readonly FieldInfo Controlled=Field(typeof(InteractionManager),"controlled",typeof(GlobalCombatObject));
  static readonly FieldInfo Headers=Field(typeof(UISquadronList),"fleetHeaders",typeof(List<UIFleetHeader>));
  static readonly FieldInfo Subheaders=Field(typeof(UIFleetHeader),"squadronHeaders",typeof(List<UISquadronHeader>));
  static readonly FieldInfo HeaderSquadron=Field(typeof(UISquadronHeader),"squadron",typeof(Squadron));
  static readonly FieldInfo NavyCache=Field(typeof(AircraftFlight),"_navy",typeof(Navy));
  static readonly MethodInfo Delayed=AccessTools.Method(typeof(Aircraft),"DelayedRemove",new[]{typeof(float)});
  static readonly MethodInfo DoRemoveMethod=AccessTools.Method(typeof(Aircraft),"DoRemove",Type.EmptyTypes);

  static string Hash(byte[] b) { using(SHA256 h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
  static MethodInfo Verify(Type t,string n,Type result,Type[] args,string hash) {
   MethodInfo m=t.GetMethod(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,args,null);
   if(m==null || m.IsStatic || m.ReturnType!=result || m.GetMethodBody()==null || Hash(m.GetMethodBody().GetILAsByteArray())!=hash)
    throw new InvalidOperationException("H1 refused: exact signature/IL mismatch: "+t.Name+"."+n);
   return m;
  }
  public static void Install(Harmony h) { InstallCore(h,true); }
  // Explicit harness entry: native destruction/coroutine methods cannot be JITted by desktop CLR.
  public static void InstallHost(Harmony h) { InstallCore(h,false); }
  static void InstallCore(Harmony h,bool nativeLifecycle) {
   // Entire assembly identity plus exact original IL bytes (including operand tokens) fail closed.
   using(var stream=System.IO.File.OpenRead(typeof(Aircraft).Assembly.Location)) {
    using(SHA256 sha=SHA256.Create()) if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!="7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23")
     throw new InvalidOperationException("H1 refused: unreviewed Assembly-CSharp SHA256");
   }
   var originals=new[]{
    Verify(typeof(AircraftFlight),"RemoveAircraft",typeof(void),new[]{typeof(Aircraft)},"5f4393840bdbd821525573bc58abde0f00219988c8604d45e4746ede43c11332"),
    Verify(typeof(AircraftFlight),"UpdateLeader",typeof(void),Type.EmptyTypes,"1d540d26de64004a337708682d5ee7050c2627a13769d814ae82a9ab41c47940"),
    Verify(typeof(Squadron),"RemoveMember",typeof(void),new[]{typeof(GlobalCombatObject)},"835e2dad6655c90d694dbca067634fdb671896496cde8baa76cf24e7c3665a9c"),
    Verify(typeof(Squadron),"SetLeader",typeof(void),Type.EmptyTypes,"3dc80b6939a6e2622d04c2e56f09cc7040661e9f92fd5ba07e92e11928587513"),
    Verify(typeof(Squadron),"get_leader",typeof(GlobalCombatObject),Type.EmptyTypes,"6cfad569b0121d24b9d60f663087053609bad2ab0b01764246c35811d0dd01a7"),
    Verify(typeof(Aircraft),"Remove",typeof(void),Type.EmptyTypes,"004bc578a7cc1af46dfc288e132fc5f541dcced343249addd4cb96b7ac6744d8"),
    Verify(typeof(Aircraft),"DoRemove",typeof(void),Type.EmptyTypes,"1d6ad1ecdace5847749f9ed4b82b67a2846f095132cff6a02203a6915850f9a0"),
    Verify(typeof(Squadron),"GenerateFormationPositions",typeof(void),new[]{typeof(bool)},"4c12fdc5dd13fdebc2a3866fb6dd75e2833535eb9c335a5e46d9df8713b797a7")};
   Verify(typeof(Aircraft),"DelayedRemove",typeof(IEnumerator),new[]{typeof(float)},"5f2de254b760a53c35ee0408cd9392c0e5399544dc4b2b557e09dd00702d1293");
   string[] patches={"RemoveAircraft","UpdateLeader","RemoveMember","SetLeader","GetLeader","Remove","DoRemove","Formation"};
   for(int i=0;i<originals.Length;i++) { if(!nativeLifecycle && (i==5 || i==6 || i==7)) continue; h.Patch(originals[i],prefix:new HarmonyMethod(typeof(H1),patches[i])); }
  }
  static bool Alive(GlobalCombatObject a) { return a; }
  static Squadron Group(GlobalCombatObject a) { return (Squadron)SquadronRef.GetValue(a); }
  static bool Indep(GlobalCombatObject a) { return (bool)Independent.GetValue(a); }
  static bool Owned(Aircraft a,AircraftFlight f) { return a && ReferenceEquals(Flight.GetValue(a),f) && !(bool)Removing.GetValue(a); }
  static void RepairFlight(AircraftFlight f,bool force) {
   // Remove only nonexistent or foreign-owned list entries, never remove a living object from the game.
   for(int i=f.aircraft.Count-1;i>=0;i--) if(!Owned(f.aircraft[i],f)) f.aircraft.RemoveAt(i);
   for(int i=0;i<f.aircraft.Count;i++) Rank.SetValue(f.aircraft[i],i);
   if(!force && Owned(f.leader,f) && f.aircraft.Contains(f.leader)) { LeaderRank(f); return; }
   Aircraft first=null, flying=null;
   foreach(Aircraft a in f.aircraft) { if(!first) first=a; if(!flying && (Aircraft.State)State.GetValue(a)!=Aircraft.State.LANDED) flying=a; }
   f.leader=flying?flying:first;
   LeaderRank(f);
  }
  static void LeaderRank(AircraftFlight f) {
   // IsLeadAircraft tests rank zero as well as leader identity. Preserve list order, swap
   // the promoted leader's rank with slot zero (the original removal also swaps ranks).
   if(!f.leader || f.aircraft.Count==0) return;
   int rank=(int)Rank.GetValue(f.leader);
   Rank.SetValue(f.aircraft[0],rank); Rank.SetValue(f.leader,0);
  }
  sealed class SquadronStamp { public WeakReference Squadron; public WeakReference Members; public int Version; }
  static readonly List<SquadronStamp> SquadronStamps=new List<SquadronStamp>();
  static readonly FieldInfo MembersVersion=AccessTools.Field(typeof(List<GlobalCombatObject>),"_version");
  static SquadronStamp Stamp(Squadron s) {
   for(int i=SquadronStamps.Count-1;i>=0;i--) { var stamp=SquadronStamps[i]; var target=stamp.Squadron.Target;
    if(target==null) SquadronStamps.RemoveAt(i); else if(ReferenceEquals(target,s)) return stamp;
   }
   var result=new SquadronStamp { Squadron=new WeakReference(s),Members=new WeakReference(null),Version=-1 }; SquadronStamps.Add(result); return result;
  }
  static void RepairSquadron(Squadron s,bool force) {
   GlobalCombatObject old=(GlobalCombatObject)Cache.GetValue(s), first=null, preferred=null;
   // List version changes on add/remove/replacement even when count is unchanged.
   // No member traversal on a healthy repeated getter.
   int version=(int)MembersVersion.GetValue(s.members);
   SquadronStamp stamp=Stamp(s);
   if(!force && ReferenceEquals(stamp.Members.Target,s.members) && stamp.Version==version && old && ReferenceEquals(Group(old),s)) return;
   foreach(GlobalCombatObject m in s.members) {
    if(!Alive(m) || !ReferenceEquals(Group(m),s)) continue;
    if(!first) first=m; if(!Indep(m) && !preferred) preferred=m;
   }
   if(force || !old || !ReferenceEquals(Group(old),s) || !s.members.Contains(old)) Cache.SetValue(s,preferred?preferred:first);
   stamp.Members.Target=s.members; stamp.Version=version;
  }
  public static bool UpdateLeader(AircraftFlight __instance) { RepairFlight(__instance,true); return false; }
  public static bool SetLeader(Squadron __instance) { RepairSquadron(__instance,true); return false; }
  public static bool GetLeader(Squadron __instance,ref GlobalCombatObject __result) { RepairSquadron(__instance,false); __result=(GlobalCombatObject)Cache.GetValue(__instance); return false; }
  public static void Formation(Squadron __instance) { RepairSquadron(__instance,false); }
  static Navy NavyFor(GlobalCombatObject a) { return a && FactionManager.instance ? FactionManager.instance.GetNavy(a.GetFaction()) : null; }
  static void Renumber(Squadron s) { for(int i=0;i<s.members.Count;i++) if(s.members[i]) SquadronRank.SetValue(s.members[i],i); RepairSquadron(s,false); }
  static void Detach(Squadron s,GlobalCombatObject m,Navy n) {
   for(int i=s.members.Count-1;i>=0;i--) if(ReferenceEquals(s.members[i],m) || !s.members[i]) s.members.RemoveAt(i);
   if(!ReferenceEquals(m,null) && ReferenceEquals(Group(m),s)) SquadronRef.SetValue(m,null);
   Renumber(s);
   // Core registration before DebugMapInfo/UI work in the game's CleanUpIfEmpty.
   if(s.members.Count==0) {
    if(n!=null) n.squadrons.Remove(s);
    if(s.fleet!=null) s.fleet.squadrons.Remove(s);
    if(s.port!=null) {
     var pList=(List<Squadron>)PortSquadrons.GetValue(s.port);
     if(pList!=null) pList.Remove(s);
    }
   }
  }
  static void Refresh(Squadron s) {
   if(s==null || !UISquadronList.instance) return;
   var headers=(List<UIFleetHeader>)Headers.GetValue(UISquadronList.instance);
   if(headers==null) return;
   foreach(UIFleetHeader f in headers.ToArray()) {
    if(!f) continue; var children=(List<UISquadronHeader>)Subheaders.GetValue(f); if(children==null) continue;
    for(int i=children.Count-1;i>=0;i--) { var child=children[i]; if(!child || !ReferenceEquals(HeaderSquadron.GetValue(child),s)) continue;
     if(s.members.Count==0) f.RemoveHeader(i); else child.DisplaySquadronMembers();
    }
   }
  }
  public static bool RemoveMember(Squadron __instance,GlobalCombatObject m) {
   if(ReferenceEquals(m,null)) { RepairSquadron(__instance,false); return false; }
   bool owned=ReferenceEquals(Group(m),__instance); Navy n=owned?NavyFor(m):null;
   bool independent=m && Indep(m);
   Detach(__instance,m,n);
   // A stale removal can repair the old container, never mutate the new owner or its navy.
   if(__instance.members.Count==0) __instance.CleanUpIfEmpty(n);
   if(owned && __instance.members.Count>0 && (__instance.formationType!=Squadron.FormationType.Wave || independent)) __instance.GenerateFormationPositions();
   Refresh(__instance); return false;
  }
  public static bool RemoveAircraft(AircraftFlight __instance,Aircraft a) {
   AircraftFlight f=__instance;
   if(ReferenceEquals(a,null)) { RepairFlight(f,false); return false; }
   bool owned=ReferenceEquals(Flight.GetValue(a),f), wasLeader=ReferenceEquals(f.leader,a);
   Squadron old=owned?Group(a):null; Navy n=(Navy)NavyCache.GetValue(f); if(n==null && owned) n=NavyFor(a);
   for(int i=f.aircraft.Count-1;i>=0;i--) if(ReferenceEquals(f.aircraft[i],a)) f.aircraft.RemoveAt(i);
   if(owned) Flight.SetValue(a,null);
   RepairFlight(f,false);
   // Authoritative promotion: squadron represents leaders/independent aircraft, not every flight member.
   Squadron previous=null;
   if(owned && old!=null) {
    if(wasLeader && f.leader) {
     previous=Group(f.leader);
     if(previous!=null && !ReferenceEquals(previous,old)) Detach(previous,f.leader,NavyFor(f.leader));
     if(!old.members.Contains(f.leader)) old.members.Add(f.leader);
     SquadronRef.SetValue(f.leader,old);
    }
    Detach(old,a,n);
   }
   if(owned && wasLeader && f.leader && f.leader.aiAgent && f.leader.aiAgent.data!=null && a.aiAgent && a.aiAgent.data!=null)
    f.leader.aiAgent.data.orderQueue=a.aiAgent.data.orderQueue;
   if(owned && n!=null) n.RemoveAircraft(a);
   // Empty flight removal is safe even for a late callback; it does not unregister a transferred aircraft.
   if(f.aircraft.Count==0 && n!=null) { int i=n.flights.IndexOf(f); if(i>=0)n.flights[i]=null; i=n.airborneFlights.IndexOf(f); if(i>=0)n.airborneFlights[i]=null; }
   f.hasASW=false; foreach(Aircraft member in f.aircraft) if(member.HasASW()) f.hasASW=true;
   if(f.aircraft.Count>0) f.UpdateFlightStatus();
   // All owner/list/navy/cache mutations above complete BEFORE anything dependent on UI/models.
   if(owned && wasLeader) {
    var ui=(UIWorldspaceUnitInfo)Info.GetValue(a); Info.SetValue(a,null);
    if(ui) { if(f.leader) { Info.SetValue(f.leader,ui); ui.Initialise(f.leader); } else ui.Remove(); }
    if(InteractionManager.instance && ReferenceEquals(Controlled.GetValue(InteractionManager.instance),a)) InteractionManager.instance.SetControlled(f.leader);
   }
   if(old!=null) { old.CleanUpIfEmpty(n); if(old.members.Count>0) old.GenerateFormationPositions(); Refresh(old); }
   if(previous!=null && !ReferenceEquals(previous,old)) { previous.CleanUpIfEmpty(NavyFor(f.leader)); Refresh(previous); }
   if(f.aircraft.Count==0 && DebugMapInfo.instance) DebugMapInfo.instance.RemoveFlight(f);
   return false;
  }
  public static bool Remove(Aircraft __instance) {
   Aircraft a=__instance;
   if((bool)Removing.GetValue(a)) return false;
   Removing.SetValue(a,true);
   // A presentation failure must not prevent destruction scheduling/authoritative removal.
   try { var f=(AircraftFlight)Flight.GetValue(a); if(f!=null) f.RemoveAircraft(a); }
   finally { if(a.currentNode) DoRemoveMethod.Invoke(a,null); else if(a) a.StartCoroutine((IEnumerator)Delayed.Invoke(a,new object[]{1f})); }
   return false;
  }
  public static bool DoRemove(Aircraft __instance) {
   Aircraft a=__instance;
   try { Squadron s=Group(a); if(s!=null) s.RemoveMember(a); }
   finally { if(a) { var t=(Transform)Tmp.GetValue(a); if(t) UnityEngine.Object.Destroy(t.gameObject); UnityEngine.Object.Destroy(a.gameObject); } }
   return false;
  }
 }
}
