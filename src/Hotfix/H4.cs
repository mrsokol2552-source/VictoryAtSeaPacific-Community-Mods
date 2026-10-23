using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;
using Pathfinding;

namespace VaspHotfix {
 public static class H4 {
  public const string PortalHash = "2ea9ef5d76c9516c09819d1fc3d02a98bd0f68b9340a9195f691b2fd7c5488fc";
  public const string LineHash   = "f28f45883364f0ed221f4e4b58c7d93278f8832eb3e6bf13b98b3d77fb1bd103";

  static string Hash(byte[] b) {
   using (var s = SHA256.Create()) {
    return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
   }
  }

  public static MethodInfo Portal() {
   return typeof(TriangleMeshNode).GetMethod("GetPortal",
    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
    null,
    new Type[] {
     typeof(GraphNode),
     typeof(List<Vector3>),
     typeof(List<Vector3>),
     typeof(bool),
     typeof(int).MakeByRefType(),
     typeof(int).MakeByRefType()
    },
    null);
  }

  public static MethodInfo Line() {
   return typeof(NavMeshGraph).GetMethod("Linecast",
    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
    null,
    new Type[] {
     typeof(INavmesh),
     typeof(Vector3),
     typeof(Vector3),
     typeof(GraphNode),
     typeof(GraphHitInfo).MakeByRefType(),
     typeof(List<GraphNode>)
    },
    null);
  }

  static void Verify(MethodInfo m, bool stat, string hash) {
   if (m == null || m.IsStatic != stat || m.ReturnType != typeof(bool) || m.GetMethodBody() == null || Hash(m.GetMethodBody().GetILAsByteArray()) != hash)
    throw new InvalidOperationException("H4 refused exact signature/IL hash: " + (m != null ? m.Name : "null"));
  }

  public static void Install(Harmony h) {
   using (var stream = System.IO.File.OpenRead(typeof(GraphNode).Assembly.Location)) {
    if (Hash(new System.IO.BinaryReader(stream).ReadBytes((int)stream.Length)) != "7acb920771bb47779d29c9a4534c78a601837c6af8bce096ea46cb63d212aa23")
     throw new InvalidOperationException("H4 refused assembly SHA256");
   }

   Verify(Portal(), false, PortalHash);
   Verify(Line(), true, LineHash);

   // Pre-flight dry-run of transpilers on original instructions
   var dummyMethod = new DynamicMethod("H4_Preflight", typeof(void), Type.EmptyTypes);
   var ilGen = dummyMethod.GetILGenerator();
   var origPortal = PatchProcessor.GetOriginalInstructions(Portal(), ilGen);
   PortalIL(origPortal);
   var lineGen = new DynamicMethod("H4_LinePreflight", typeof(void), Type.EmptyTypes).GetILGenerator();
   var origLine = PatchProcessor.GetOriginalInstructions(Line(), lineGen);
   LineIL(origLine, lineGen);

   // Plugin entry point owns the complete transaction, including preflight errors.
   h.Patch(Portal(), transpiler: new HarmonyMethod(typeof(H4), "PortalIL"));
   h.Patch(Line(), transpiler: new HarmonyMethod(typeof(H4), "LineIL"));
  }

  static int Local(object o) {
   if (o is LocalBuilder) return ((LocalBuilder)o).LocalIndex;
   if (o is byte) return (int)((byte)o);
   if (o is sbyte) return (int)((sbyte)o);
   if (o is int) return (int)o;
   if (o is short) return (short)o;
   if (o is ushort) return (ushort)o;
   return -1;
  }

  public static IEnumerable<CodeInstruction> PortalIL(IEnumerable<CodeInstruction> input) {
   var c = new List<CodeInstruction>(input);
   int found = -1;

   // Search for:
   // ldloc.s V_13 (13)
   // ldc.i4.m1
   // beq (target = 0x0383 / trampoline `br IL_04d5`)
   for (int i = 2; i < c.Count; i++) {
    if (c[i].opcode == OpCodes.Beq &&
        c[i-1].opcode == OpCodes.Ldc_I4_M1 &&
        c[i-2].opcode == OpCodes.Ldloc_S && Local(c[i-2].operand) == 13) {
     if (found >= 0) throw new InvalidOperationException("H4 ambiguous missing-edge branch in GetPortal");
     found = i;
    }
   }

   if (found < 0) throw new InvalidOperationException("H4 missing-edge beq pattern not found in GetPortal");

   // Verify the method end contains:
   // IL_04D3: ldc.i4.0
   // IL_04D4: ret
   // IL_04D5: ldc.i4.1
   // IL_04D6: ret
   if (c.Count < 4 ||
       c[c.Count - 4].opcode != OpCodes.Ldc_I4_0 ||
       c[c.Count - 3].opcode != OpCodes.Ret ||
       c[c.Count - 2].opcode != OpCodes.Ldc_I4_1 ||
       c[c.Count - 1].opcode != OpCodes.Ret) {
    throw new InvalidOperationException("H4 unexpected GetPortal epilogue structure");
   }

   object trampolineLabel = c[found].operand;
   int trampolineIdx = c.FindIndex(delegate(CodeInstruction x) { return x.labels.Contains((Label)trampolineLabel); });
   if (trampolineIdx < 0 || c[trampolineIdx].opcode != OpCodes.Br)
    throw new InvalidOperationException("H4 unexpected original success trampoline target");

   // Verify that this trampoline has exactly 1 incoming reference across all instructions
   int incoming = 0;
   foreach (var instr in c) {
    if (Equals(instr.operand, trampolineLabel)) incoming++;
   }
   if (incoming != 1)
    throw new InvalidOperationException("H4 shared success trampoline in GetPortal; incoming count = " + incoming);

   // Mutate trampoline into:
   // ldc.i4.0
   // ret
   // This preserves label pointers and branches targeting trampolineLabel safely.
   c[trampolineIdx].opcode = OpCodes.Ldc_I4_0;
   c[trampolineIdx].operand = null;
   c.Insert(trampolineIdx + 1, new CodeInstruction(OpCodes.Ret, null));

   return c;
  }

  // Pure C# helpers called by Linecast transpiled code
  public static bool HasPoints(List<Vector3> left, List<Vector3> right) {
   return left != null && right != null && left.Count > 0 && right.Count > 0;
  }

  public static void Blocked(ref GraphHitInfo hit, GraphNode node) {
   hit.node = node;
   hit.point = hit.origin;
   hit.tangentOrigin = hit.origin;
   hit.tangent = default(Vector3);
  }

  public static IEnumerable<CodeInstruction> LineIL(IEnumerable<CodeInstruction> input, ILGenerator generator) {
   var c = new List<CodeInstruction>(input);
   int at = -1;
   object left = null, right = null;

   var portal = typeof(GraphNode).GetMethod("GetPortal", new Type[] {
    typeof(GraphNode),
    typeof(List<Vector3>),
    typeof(List<Vector3>),
    typeof(bool)
   });

   for (int i = 3; i + 3 < c.Count; i++) {
    if (c[i].Calls(portal) && c[i+1].opcode == OpCodes.Brtrue && c[i+2].opcode == OpCodes.Br) {
     if (at >= 0) throw new InvalidOperationException("H4 ambiguous Linecast portal call");
     at = i;
     left = c[i-3].operand;
     right = c[i-2].operand;
    }
   }

   if (at < 0 || Local(left) != 4 || Local(right) != 5)
    throw new InvalidOperationException("H4 Linecast portal locals mismatch");

   var targetLabel = (Label)c[at+1].operand;
   int entry = c.FindIndex(delegate(CodeInstruction x) { return x.labels.Contains(targetLabel); });
   if (entry < 0 || c[entry].opcode != OpCodes.Ldloc_S || Local(c[entry].operand) != 4)
    throw new InvalidOperationException("H4 Linecast portal read entry mismatch");

   // Find the infinite-loop / blocked return path that releases list & list2 and returns true:
   // IL_013D: ldloc.s V_4
   // IL_013F: call ListPool.Release
   // IL_0144: ldloc.s V_5
   // IL_0146: call ListPool.Release
   // IL_014B: ldc.i4.1
   // IL_014C: ret
   int cleanup = -1;
   for (int i = 0; i + 5 < c.Count; i++) {
    if (c[i].opcode == OpCodes.Ldloc_S && Local(c[i].operand) == 4 &&
        c[i+1].opcode == OpCodes.Call && c[i+1].operand is MethodInfo && ((MethodInfo)c[i+1].operand).Name == "Release" &&
        c[i+2].opcode == OpCodes.Ldloc_S && Local(c[i+2].operand) == 5 &&
        c[i+3].opcode == OpCodes.Call && c[i+3].operand is MethodInfo && ((MethodInfo)c[i+3].operand).Name == "Release" &&
        c[i+4].opcode == OpCodes.Ldc_I4_1 &&
        c[i+5].opcode == OpCodes.Ret) {
     cleanup = i;
     break;
    }
   }

   if (cleanup < 0) throw new InvalidOperationException("H4 Linecast blocked cleanup mismatch");

   Label blockedLabel = generator.DefineLabel();
   c[cleanup].labels.Add(blockedLabel);

   Label validLabel = generator.DefineLabel();
   c[entry].labels.Remove(targetLabel);
   c[entry].labels.Add(validLabel);

   // Construct guard sequence:
   // ldloc.s left
   // ldloc.s right
   // call HasPoints
   // brtrue validLabel
   // ldarg.s hit (index 4)
   // ldloc.2 (currentNode)
   // call Blocked
   // br blockedLabel
   var added = new List<CodeInstruction>();
   added.Add(new CodeInstruction(OpCodes.Ldloc_S, left));
   added.Add(new CodeInstruction(OpCodes.Ldloc_S, right));
   added.Add(new CodeInstruction(OpCodes.Call, typeof(H4).GetMethod("HasPoints", BindingFlags.Public | BindingFlags.Static)));
   added.Add(new CodeInstruction(OpCodes.Brtrue, validLabel));
   added.Add(new CodeInstruction(OpCodes.Ldarg_S, (byte)4));
   added.Add(new CodeInstruction(OpCodes.Ldloc_2, null));
   added.Add(new CodeInstruction(OpCodes.Call, typeof(H4).GetMethod("Blocked", BindingFlags.Public | BindingFlags.Static)));
   added.Add(new CodeInstruction(OpCodes.Br, blockedLabel));

   added[0].labels.Add(targetLabel);

   c.InsertRange(entry, added);
   return c;
  }
 }
}
