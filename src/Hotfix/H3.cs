using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
namespace VaspHotfix {
 internal static class H3 {
  internal static Action<string> Log;
  static readonly int[] notes=new int[16];
  static void Note(int i,string text) {int n=++notes[i]; if(Log!=null && (n==1||n==2||n==4||n==8||n==16||n==32||n==64)) Log("H3 "+text+" count="+n+(n==1?"\n"+Environment.StackTrace:""));}
  static FieldInfo F(Type t,string n) {var f=AccessTools.Field(t,n);if(f==null||f.IsStatic)throw new InvalidOperationException("H3 field "+t+"."+n);return f;}
  static readonly FieldInfo SQ=F(typeof(UISquadronHeader),"squadron"), HadBullets=F(typeof(UISquadronHeader),"hadBullets"),HadBomb=F(typeof(UISquadronHeader),"hadBomb");
  static readonly FieldInfo Flight=F(typeof(Aircraft),"flight"),State=F(typeof(Aircraft),"currentState"),Damage=F(typeof(GlobalCombatObject),"lastDamageTime");
  static readonly FieldInfo Headers=F(typeof(UISquadronList),"fleetHeaders"),Subheaders=F(typeof(UIFleetHeader),"squadronHeaders"),Elements=F(typeof(UISquadronHeader),"squadronListElements"),Target=F(typeof(UISquadronListElement),"target");
  static readonly FieldInfo Fleet=F(typeof(Ship),"currentFleet"),Home=F(typeof(Aircraft),"home"),PortRef=F(typeof(CoastalStructure),"port");
  static readonly FieldInfo Collapsed=F(typeof(UIFleetHeader),"collapsed");
  static readonly FieldInfo Bullets=F(typeof(Aircraft),"gunAmmo"),Weapons=F(typeof(GlobalCombatObject),"weapons");
  static string Hash(byte[] bytes){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static MethodInfo Verify(Type t,string n,Type r,Type[] a,string hash){var m=t.GetMethod(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly,null,a,null);if(m==null||m.ReturnType!=r||m.GetMethodBody()==null||Hash(m.GetMethodBody().GetILAsByteArray())!=hash)throw new InvalidOperationException("H3 refused exact signature/IL "+t+"."+n);return m;}
  public static void Install(Harmony h){InstallCore(h,true);}
  public static void InstallHost(Harmony h){InstallCore(h,false);}
  static void InstallCore(Harmony h,bool native) {
   if(Hash(System.IO.File.ReadAllBytes(typeof(Aircraft).Assembly.Location))!="7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23")throw new InvalidOperationException("H3 refused assembly SHA256");
   var methods=new[]{
    Verify(typeof(UISquadronHeader),"Update",typeof(void),Type.EmptyTypes,"d99e8259e21b187b0e086aa529507bbb7e335ed2855ad89654bdb39fb35d1f61"),
    Verify(typeof(UISquadronHeader),"SetAircraftAmmoIcons",typeof(void),new[]{typeof(bool)},"9b18d653762d80a9a23034af8a24f1f1f70b3cb708c8a8cc4175237b38d9b0a7"),
    Verify(typeof(UISquadronHeader),"ShowTargetStatus",typeof(void),Type.EmptyTypes,"139608d65faae27c93900e97f9d89c614f1fc44faad9bc48fd324b1bfe87ad54"),
    Verify(typeof(UISquadronList),"GetFleetHeader",typeof(UIFleetHeader),new[]{typeof(Squadron)},"a281489c560b2e18ff8d843dd1061098b27776474c292181b0a76e8c3bdf77bb"),
    Verify(typeof(UISquadronList),"AddSquadron",typeof(UISquadronHeader),new[]{typeof(Squadron)},"78419424f9b6817ddfaee48a880467348066c18fab6b22390a2966c3bd1cad8f"),
    Verify(typeof(InteractionManager),"ClearHighlightedElements",typeof(void),Type.EmptyTypes,"073928115f6379371828d6f462bb1de9dd3828e0019933b1e0e26b47576aa209"),
    Verify(typeof(Squadron),"GetEffectiveMoveRange",typeof(float),Type.EmptyTypes,"a3ca78b334aaee249699d3acfd7cac46c09a8e1a5dde0bb0c65c29f864721246"),
    Verify(typeof(InteractionManager),"CommandInput",typeof(void),new[]{typeof(Vector3),typeof(bool),typeof(GlobalCombatObject),typeof(Port)},"3fd0b4e183bd93d3a761117e10cd34ffac5fc2f7134560422e915628a8b81a86"),
    Verify(typeof(UIFleetShipIcon),"Initialise",typeof(void),new[]{typeof(Ship)},"15e049c3c65e5d65bb457a4c2fe4ff019a71bc1e2245e19782c6c8e4ce62d3cb")};
   // Verify the additional caller before patching it, too.
   var addHeader=Verify(typeof(UISquadronList),"AddHeader",typeof(void),new[]{typeof(UISquadronHeader),typeof(int)},"37600461e0848d457795ee1ee5aa98db4bb990f70c032833fa31a9d38acd7d45");
   string[] prefixes={"Update","Ammo","StatusPrefix","GetHeader","Add","Clear","Range",null,"Icon"};
   try {for(int i=0;i<methods.Length;i++){if(!native&&(i==0||i==7))continue; if(prefixes[i]!=null)h.Patch(methods[i],prefix:new HarmonyMethod(typeof(H3),prefixes[i])); if(i==2)h.Patch(methods[i],transpiler:new HarmonyMethod(typeof(H3),"StatusIL")); if(i==7)h.Patch(methods[i],transpiler:new HarmonyMethod(typeof(H3),"CommandIL"));}
    if(native)h.Patch(addHeader,prefix:new HarmonyMethod(typeof(H3),"AddHeader"));
   }catch{throw;}
  }
  static Squadron Group(UISquadronHeader h){return (Squadron)SQ.GetValue(h);}
  public static bool Update(UISquadronHeader __instance){if(!__instance)return false;var s=Group(__instance);if(s==null||s.members==null||(s.type!=Squadron.Type.None&&s.members.Count==0)){UnityEngine.Object.Destroy(__instance.gameObject);return false;}return true;}
  public static bool Ammo(UISquadronHeader __instance,bool init){if(!__instance)return false;var s=Group(__instance);if(s==null||s.members==null)return false;bool b=false,p=false;foreach(var m in s.members){var a=m as Aircraft;if(!a)continue;b|=(int)Bullets.GetValue(a)>0;var w=(List<Weapon>)Weapons.GetValue(a);if(w!=null&&w.Count>0&&w[0]!=null&&w[0].CanFire())p=true;}
   if(__instance.aircraftBulletImage&&(init||b!=(bool)HadBullets.GetValue(__instance)))__instance.aircraftBulletImage.sprite=b?__instance.fighterSprite:__instance.depletedBulletsSprite;
   if(__instance.aircraftPayloadImage&&(init||p!=(bool)HadBomb.GetValue(__instance))){__instance.aircraftPayloadImage.sprite=s.aircraftType==Aircraft.AircraftType.TORPEDO_BOMBER?(p?__instance.torpedoBomberSprite:__instance.depletedTorpedoSprite):s.aircraftType==Aircraft.AircraftType.ASW_BOMBER?(p?__instance.aswSprite:__instance.depletedASWSprite):(p?__instance.bomberSprite:__instance.depletedBombSprite);__instance.aircraftPayloadImage.SetNativeSize();}HadBullets.SetValue(__instance,b);HadBomb.SetValue(__instance,p);return false;}
  public static bool StatusPrefix(UISquadronHeader __instance){return __instance&&Group(__instance)!=null;}
  public static float DamageTime(GlobalCombatObject m){return m?(float)Damage.GetValue(m):0f;}
  public static IEnumerable<CodeInstruction> StatusIL(IEnumerable<CodeInstruction> instructions){int n=0;foreach(var c in instructions){if(c.opcode==OpCodes.Ldfld&&Equals(c.operand,Damage)){c.opcode=OpCodes.Call;c.operand=AccessTools.Method(typeof(H3),"DamageTime");n++;}yield return c;}if(n!=3)throw new InvalidOperationException("H3 ShowTargetStatus damage read pattern "+n);}
  static GlobalCombatObject Candidate(Squadron s){var l=s.leader;if(ValidKind(l,s))return l;if(s.members!=null)foreach(var m in s.members)if(ValidKind(m,s))return m;return null;}
  static bool ValidKind(GlobalCombatObject m,Squadron s){return m&&(s.type==Squadron.Type.Ship?m is Ship:s.type==Squadron.Type.Aircraft?m is Aircraft:true);}
  public static bool GetHeader(UISquadronList __instance,Squadron squadron,ref UIFleetHeader __result){__result=null;if(!__instance||squadron==null)return false;var m=Candidate(squadron);if(!m)return false;Fleet f=null;if(squadron.type==Squadron.Type.Ship)f=(Fleet)Fleet.GetValue(m);else if(squadron.type==Squadron.Type.Aircraft){var home=(GlobalCombatObject)Home.GetValue(m);var ship=home as Ship;var coast=home as CoastalStructure;if(ship)f=(Fleet)Fleet.GetValue(ship);else if(coast){var port=(Port)PortRef.GetValue(coast);if(port!=null)__result=__instance.GetFleetHeader(port);return false;}}__result=__instance.GetFleetHeader(f);return false;}
  public static bool Add(UISquadronList __instance,Squadron squadron,ref UISquadronHeader __result){__result=null;if(!__instance||squadron==null)return false;var h=__instance.GetFleetHeader(squadron);if(h&&!(bool)Collapsed.GetValue(h))__result=h.AddSquadron(squadron);return false;}
  public static bool AddHeader(UISquadronList __instance,UISquadronHeader header,int index){if(!header||Group(header)==null)return false;var h=__instance.GetFleetHeader(Group(header));if(h){h.AddHeader(header,index);__instance.RefreshLayout();}return false;}
  public static bool Clear(){if(!UISquadronList.instance)return false;var hs=(List<UIFleetHeader>)Headers.GetValue(UISquadronList.instance);if(hs==null)return false;foreach(var f in hs.ToArray()){if(!f)continue;var ss=(List<UISquadronHeader>)Subheaders.GetValue(f);if(ss==null)continue;foreach(var s in ss.ToArray()){if(!s)continue;var es=(List<UISquadronListElement>)Elements.GetValue(s);if(es==null)continue;foreach(var e in es.ToArray())if(e)e.Highlight(false);}}return false;}
  public static bool Range(Squadron __instance,ref float __result){__result=-1f;if(__instance==null||__instance.members==null)return false;float speed=0f;if(__instance.type==Squadron.Type.Ship)foreach(var m in __instance.members){var s=m as Ship;if(!s)continue;float v=s.GetSpeedInMetresPerSecond();speed=speed>0f?Mathf.Min(speed,v):v;}
   foreach(var m in __instance.members){float r; if(__instance.type==Squadron.Type.Aircraft){var a=m as Aircraft;if(!a)continue;var f=(AircraftFlight)Flight.GetValue(a);if(f==null)continue;r=f.GetEffectiveMoveRange();}else if(__instance.type==Squadron.Type.Ship){var s=m as Ship;if(!s)continue;r=s.GetEffectiveRange(speed);}else continue;__result=__result==-1f?r:Mathf.Min(__result,r);}return false;}
  public static bool Icon(UIFleetShipIcon __instance,Ship s){if(!__instance)return false;if(!s){Note(0,"Initialise invalid input Ship (CLR-null="+ReferenceEquals(s,null)+")");return false;}if(!__instance.shipName||!__instance.xpGainLabel||!__instance.aircraftIcon||!__instance.shipButton||!__instance.healthBar||!__instance.levelUpIcon){Note(1,"Initialise incomplete/destroyed UI references");return false;}if(!ShipManager.instance||!UIShipFleetScreen.instance){Note(2,"Initialise manager/screen absent");return false;}if(s.aircraftFlights==null){Note(3,"Initialise Ship.aircraftFlights absent");return false;}if(!__instance.shipButton.GetComponent<Button>()){Note(4,"Initialise shipButton Button missing/destroyed");return false;}return true;}
  // Read-only fallbacks: never manufacture flights or prune gameplay collections.
  static readonly FieldInfo FlightAircraft=F(typeof(AircraftFlight),"aircraft"), ShipAgent=F(typeof(Ship),"aiAgent"), AirAgent=F(typeof(Aircraft),"aiAgent"), ShipData=F(typeof(ShipAIAgent),"data"), AirData=F(typeof(AircraftAIAgent),"data");
  public static GlobalCombatObject CommandTarget(UISquadronListElement e){return e?(GlobalCombatObject)Target.GetValue(e):null;}
  public static AircraftFlight CommandFlight(Aircraft a){return a?(AircraftFlight)Flight.GetValue(a):null;}
  public static List<Aircraft> CommandAircraft(AircraftFlight f){return f==null?new List<Aircraft>():((List<Aircraft>)FlightAircraft.GetValue(f)??new List<Aircraft>());}
  public static ShipAIData CommandShipData(Ship s){if(!s)return null;var a=(ShipAIAgent)ShipAgent.GetValue(s);return a?(ShipAIData)ShipData.GetValue(a):null;}
  public static AircraftAIData CommandAirData(Aircraft s){if(!s)return null;var a=(AircraftAIAgent)AirAgent.GetValue(s);return a?(AircraftAIData)AirData.GetValue(a):null;}
  public static void CommandCopyShip(Squadron s,ShipAIData data,bool queue){if(s!=null&&data!=null)s.CopyOrders(data,queue);}
  public static void CommandCopyAir(Squadron s,AircraftAIData data,bool queue){if(s!=null&&data!=null)s.CopyOrders(data,queue);}
  static void Call(CodeInstruction c,string name){c.opcode=OpCodes.Call;c.operand=AccessTools.Method(typeof(H3),name);}
  public static IEnumerable<CodeInstruction> CommandIL(IEnumerable<CodeInstruction> instructions){
   var code=new List<CodeInstruction>(instructions);int targets=0,flights=0,lists=0,leaders=0,copies=0;
   // All edits are stack-equivalent and in-place, retaining labels and EH blocks.
   for(int i=0;i<code.Count;i++){
    var c=code[i];
    if(c.opcode==OpCodes.Ldfld&&Equals(c.operand,Target)){Call(c,"CommandTarget");targets++;}
    else if(c.opcode==OpCodes.Ldfld&&Equals(c.operand,Flight)){Call(c,"CommandFlight");flights++;}
    else if(c.opcode==OpCodes.Ldfld&&Equals(c.operand,FlightAircraft)){Call(c,"CommandAircraft");lists++;}
    else if(c.opcode==OpCodes.Ldfld&&(Equals(c.operand,ShipAgent)||Equals(c.operand,AirAgent))&&i>0&&code[i-1].opcode==OpCodes.Isinst&&i+1<code.Count&&code[i+1].opcode==OpCodes.Ldfld&&Equals(code[i+1].operand,Equals(c.operand,ShipAgent)?ShipData:AirData)){
     Call(c,Equals(c.operand,ShipAgent)?"CommandShipData":"CommandAirData");code[i+1].opcode=OpCodes.Nop;code[i+1].operand=null;leaders++;
    }
    else if(c.opcode==OpCodes.Callvirt&&c.operand is MethodInfo){var m=(MethodInfo)c.operand;if(m.DeclaringType==typeof(Squadron)&&m.Name=="CopyOrders"){Call(c,m.GetParameters()[0].ParameterType==typeof(ShipAIData)?"CommandCopyShip":"CommandCopyAir");copies++;}}
   }
   if(targets!=2||flights!=2||lists!=6||leaders!=2||copies!=2)throw new InvalidOperationException("H3 CommandInput patterns "+targets+","+flights+","+lists+","+leaders+","+copies);
   return code;
  }
 }
}
