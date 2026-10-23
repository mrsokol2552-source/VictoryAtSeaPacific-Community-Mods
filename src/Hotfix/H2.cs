using System;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace VaspHotfix {
 // Bounded H2 subset. No exception suppression and no per-object strong-reference cache.
 internal static class H2 {
  static FieldInfo Field(string n,Type t) {
   var f=AccessTools.Field(typeof(Aircraft),n);
   if(f==null || f.IsStatic || f.FieldType!=t) throw new InvalidOperationException("H2 field mismatch: "+n);
   return f;
  }
  static readonly FieldInfo Flight=Field("flight",typeof(AircraftFlight));
  static readonly FieldInfo Removing=Field("removing",typeof(bool));
  static readonly FieldInfo State=Field("currentState",typeof(Aircraft.State));
  static readonly FieldInfo OrderedHeight=Field("orderedFlightHeight",typeof(float));
  static readonly FieldInfo Fuel=AccessTools.Field(typeof(GlobalCombatObject),"currentFuel");
  static readonly int[] Counts=new int[8];
  internal static Action<string> Log;
  static void Note(int reason,Aircraft a,string detail) {
   int n=Counts[reason]; if(n<int.MaxValue) Counts[reason]=++n;
   // One complete managed diagnostic stack per reason, then logarithmic counters, capped.
   if(Log!=null && (n==1 || n==2 || n==4 || n==8 || n==16 || n==32 || n==64))
    Log("H2 reason="+reason+" count="+n+" state="+State.GetValue(a)+" fuel="+Fuel.GetValue(a)+" "+detail+(n==1?"\n"+Environment.StackTrace:""));
  }
  static MethodInfo Verify(Type t,string name,Type result,Type[] args,string expected) {
   var m=t.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly,null,args,null);
   string hash="";
   if(m!=null && m.GetMethodBody()!=null) using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(m.GetMethodBody().GetILAsByteArray())).Replace("-","").ToLowerInvariant();
   if(m==null || m.IsStatic || m.ReturnType!=result || hash!=expected) throw new InvalidOperationException("H2 refused signature/IL: "+t.Name+"."+name);
   return m;
  }
  public static void Install(Harmony h) { InstallCore(h,true); }
  public static void InstallHost(Harmony h) { InstallCore(h,false); }
  static void InstallCore(Harmony h,bool native) {
   if(Fuel==null || Fuel.FieldType!=typeof(float) || Fuel.IsStatic) throw new InvalidOperationException("H2 fuel field mismatch");
   using(var stream=System.IO.File.OpenRead(typeof(Aircraft).Assembly.Location)) using(var sha=SHA256.Create())
    if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!="7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23") throw new InvalidOperationException("H2 refused assembly hash");
   var fuel=Verify(typeof(Aircraft),"OutOfFuel",typeof(bool),Type.EmptyTypes,"850ec5add1fe273543bbb6e42ccca9e6ba4346199f8b84a3c0072160367d35ce");
   var height=Verify(typeof(Aircraft),"GetGlobalMovementHeight",typeof(float),Type.EmptyTypes,"10903ff6f58d55022072c2d6777d36ec2b15de1b5e57a9cdc1e73432f7f82c0c");
   var move=Verify(typeof(Aircraft),"MoveUpdate",typeof(void),new[]{typeof(float)},"7c9df82696673422bff947c42f0a5966489f07a8bd407b08dd5633d8998b6ba4");
   var update=Verify(typeof(Aircraft),"AircraftUpdate",typeof(void),new[]{typeof(float)},"fae7de6254cef1a520349c85837e475b3d14e743a6c9797e1765d1d420337e80");
   Verify(typeof(ShipManager),"Update",typeof(void),Type.EmptyTypes,"d6b6ce6cc576cf88de3f1f547f62173c07f13e28d441b26261b24fd771290556");
   try {
    h.Patch(fuel,prefix:new HarmonyMethod(typeof(H2),"OutOfFuel"));
    if(native) {
     h.Patch(height,prefix:new HarmonyMethod(typeof(H2),"Height"));
     h.Patch(move,prefix:new HarmonyMethod(typeof(H2),"BeforeUpdate"));
     h.Patch(update,prefix:new HarmonyMethod(typeof(H2),"BeforeUpdate"));
    }
   } catch { throw; }
  }
  static AircraftFlight Repair(Aircraft a) {
   var f=(AircraftFlight)Flight.GetValue(a);
   // Repair only established ownership; never transfer aircraft between flights.
   if(f!=null && f.aircraft!=null && f.aircraft.Contains(a) && (!f.leader || !ReferenceEquals(Flight.GetValue(f.leader),f) || !f.aircraft.Contains(f.leader) || (bool)Removing.GetValue(f.leader))) H1.UpdateLeader(f);
   return f;
  }
  public static bool BeforeUpdate(Aircraft __instance) {
   if((bool)Removing.GetValue(__instance)) { Note(0,__instance,"scheduled removal: no further update"); return false; }
   Repair(__instance);
   RecoverAI(__instance);
   return true;
  }
  // Same component/prefab/Initialise sequence as ShipManager.InstantiateAircraft.
  // Reuse a live component when only the serialized field was lost.
  internal static Func<Aircraft,AircraftAIAgent> AgentFactory { get; set; }
  static AircraftAIAgent CreateAgent(Aircraft a) {
   if(AgentFactory!=null) return AgentFactory(a);
   return NativeCreateAgent(a);
  }
  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  static AircraftAIAgent NativeCreateAgent(Aircraft a) {
   var agent=a.GetComponent<AircraftAIAgent>();
   if(agent) return agent; // Preserve a detached component's plans, orders and timers.
   if(!ShipManager.instance) throw new InvalidOperationException("H2 AI recovery requires live ShipManager");
   var prefab=AgentPrefab.GetValue(ShipManager.instance) as AircraftAIAgent;
   if(!prefab) throw new InvalidOperationException("H2 AI recovery requires game AI prefab");
   agent=a.gameObject.AddComponent<AircraftAIAgent>();
   Prefab.SetValue(agent,prefab);
   a.aiAgent=agent; // Initialise/plan setup can refer back to the owning aircraft.
   agent.Initialise(a);
   return agent;
  }
  static void RecoverAI(Aircraft a) {
   if(!a.aiAgent) {
    a.aiAgent=CreateAgent(a);
    Note(5,a,"rebound/recreated AI using game initialization path");
   }
   var agent=a.aiAgent;
   if(agent.data==null) {
    // Do not reset live plans, behaviour, timers or recovered orders on healthy AI.
    var data=new AircraftAIData();
    data.SetAircraft(a);
    agent.data=data;
    Note(6,a,"recreated missing AI data using constructor and SetAircraft");
   }
   // An override is a one-frame movement supplied by formation behaviour. If its
   // source is gone, consume no stale override: let ordinary MoveUpdate run now.
   // formationLeader is a squadron relationship, not necessarily this flight's leader.
   if(!agent.data.formationLeader || (bool)Removing.GetValue(agent.data.formationLeader)) {
    agent.data.formationLeader=null;
    if((bool)Override.GetValue(a)) {
     Override.SetValue(a,false);
     Note(7,a,"discarded stale one-frame formation override; normal movement continues");
    }
   }
  }
  static readonly FieldInfo Override=Field("overridePosition",typeof(bool));
  static readonly FieldInfo Prefab=AccessTools.Field(typeof(AircraftAIAgent),"prefab");
  static readonly FieldInfo AgentPrefab=AccessTools.Field(typeof(ShipManager),"aircraftAIPrefab");
  public static bool OutOfFuel(Aircraft __instance,ref bool __result) {
   var a=__instance;
   // Exactly preserve short circuit and normal valid-state predicate.
   if((bool)Removing.GetValue(a) || !((float)Fuel.GetValue(a)<=0f) || (Aircraft.State)State.GetValue(a)!=Aircraft.State.FLYING) { __result=false; return false; }
   var f=Repair(a);
   if(f!=null && f.leader && !ReferenceEquals(f.leader,a)) { __result=false; return false; }
   if(f==null) Note(1,a,"flight absent; exhausted airborne aircraft uses original fuel descent/sea landing path");
   else if(!f.leader) Note(2,a,"leader absent after ownership-aware repair");
   if(!a.aiAgent) { Note(3,a,"aiAgent absent; exhausted airborne aircraft uses fuel descent"); __result=true; return false; }
   if(a.aiAgent.data==null) { Note(4,a,"AI data absent; exhausted airborne aircraft uses fuel descent"); __result=true; return false; }
   __result=a.aiAgent.data.currentTactic!=AircraftAIData.Tactic.LAND && a.aiAgent.data.currentTactic!=AircraftAIData.Tactic.LAND_ON_SEA;
   return false;
  }
  public static bool Height(Aircraft __instance,ref float __result) {
   var a=__instance; var f=Repair(a);
   if(f!=null && f.ai!=null && f.leader && ReferenceEquals(Flight.GetValue(f.leader),f) && f.aircraft.Contains(f.leader)) return true;
   // Loading/detachment fallback: same ordered/default height formula, no recursive stale leader.
   float ordered=(float)OrderedHeight.GetValue(a);
   __result=ordered>0f?ordered:a.serviceCeiling*0.3048f*0.1f*(a.type==Aircraft.AircraftType.LEVEL_BOMBER?0.8f:0.2f);
   return false;
  }
 }
}
