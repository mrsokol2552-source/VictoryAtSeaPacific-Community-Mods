using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;
namespace VaspHotfix {
 internal static class H5 {
  public static void Install(Harmony h) {
   var m=typeof(InteractionManager).GetMethod("CommandInput",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Vector3),typeof(bool),typeof(GlobalCombatObject),typeof(Port)},null);
   string hash="";if(m!=null&&m.GetMethodBody()!=null)using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(m.GetMethodBody().GetILAsByteArray())).Replace("-","").ToLowerInvariant();
   if(m==null||m.ReturnType!=typeof(void)||hash!="3fd0b4e183bd93d3a761117e10cd34ffac5fc2f7134560422e915628a8b81a86")throw new InvalidOperationException("H5 CommandInput exact signature/IL mismatch");
   h.Patch(m,transpiler:new HarmonyMethod(typeof(H5),"Transpile"));
  }
  public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> input) {
   var code=new List<CodeInstruction>(input);var controlled=AccessTools.Field(typeof(InteractionManager),"controlled");
   // Match only the late grouped manual-fire branch. Preserve instruction identity,
   // labels and exception metadata by mutating, not removing, the two-load pairs.
   int count=0;
   for(int i=0;i+1<code.Count;i++) {
    if(code[i].opcode!=OpCodes.Ldarg_0||code[i+1].opcode!=OpCodes.Ldfld||!Equals(code[i+1].operand,controlled))continue;
    bool weapons=i+2<code.Count&&code[i+2].opcode==OpCodes.Ldfld&&Equals(code[i+2].operand,AccessTools.Field(typeof(GlobalCombatObject),"weapons"));
    bool fire=i+4<code.Count&&(code[i+4].opcode==OpCodes.Callvirt||code[i+4].opcode==OpCodes.Call)&&code[i+4].operand is MethodInfo&&((MethodInfo)code[i+4].operand).Name=="FireAt";
    if(!weapons&&!fire)continue;
    // Original receiver is selected local V_48. Only accept the grouped region
    // after that local has been assigned and validity checked.
    bool assigned=false;for(int j=0;j<i;j++)if(code[j].opcode==OpCodes.Stloc_S&&code[j].operand is LocalBuilder&&((LocalBuilder)code[j].operand).LocalIndex==48)assigned=true;
    if(!assigned)continue;
    code[i].opcode=OpCodes.Ldloc_S;code[i].operand=(byte)48;
    code[i+1].opcode=OpCodes.Nop;code[i+1].operand=null;count++;
   }
   if(count!=2)throw new InvalidOperationException("H5 grouped receiver pair count mismatch: "+count);
   return code;
  }
 }
}
