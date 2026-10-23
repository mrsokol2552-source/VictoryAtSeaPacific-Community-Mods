using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pathfinding;
using UnityEngine;

namespace VaspInterfaceTools
{
    internal static class ConvoyNavigation
    {
        private static readonly FieldInfo Markers = AccessTools.Field(typeof(DebugMapInfo), "convoyWaypointMarkers");
        private static readonly FieldInfo Links = AccessTools.Field(typeof(ConvoyWaypointMarker), "connectedTo");
        private static readonly FieldInfo Waypoint = AccessTools.Field(typeof(ConvoyWaypointMarker), "waypoint");
        private static readonly FieldInfo Count = AccessTools.Field(typeof(GlobalNavigationManager), "connectedConvoyWaypointCount");
        private static readonly FieldInfo GraphTime = AccessTools.Field(typeof(GlobalNavigationManager), "lastConvoyWaypointChangeTime");
        private static readonly FieldInfo Points = AccessTools.Field(typeof(FleetAIAgent), "convoyPathPoints");
        private static readonly FieldInfo Index = AccessTools.Field(typeof(FleetAIAgent), "convoyTGIndex");
        private static readonly FieldInfo LastTime = AccessTools.Field(typeof(FleetAIAgent), "lastConvoyPathTime");
        private static readonly FieldInfo LastFinal = AccessTools.Field(typeof(FleetAIAgent), "lastConvoyPathDestination");
        private static readonly MethodInfo SetNodeCount = AccessTools.Property(typeof(PointGraph), "nodeCount").GetSetMethod(true);
        private static readonly FieldInfo ShipAgent = AccessTools.Field(typeof(Ship), "aiAgent");
        private static readonly FieldInfo ShipData = AccessTools.Field(typeof(ShipAIAgent), "data");
        private static readonly FieldInfo FormationLeader = AccessTools.Field(typeof(ShipAIData), "formationLeader");
        private static readonly Dictionary<FleetAIAgent, RouteState> States = new Dictionary<FleetAIAgent, RouteState>();
        private static int revision;

        private sealed class RouteState
        {
            internal Fleet Fleet;
            internal Vector3 Final, Previous, Target, Direction;
            internal bool Valid = true, HavePrevious, HaveTarget;
            internal int Revision;
            internal Pathfinding.Path Pending;
        }
        internal static void Install(Harmony harmony)
        {
            foreach (FieldInfo field in new[] { Markers, Links, Waypoint, Count, GraphTime, Points, Index, LastTime, LastFinal, ShipAgent, ShipData, FormationLeader })
                if (field == null) throw new MissingFieldException("Convoy navigation schema mismatch.");
            if (SetNodeCount == null) throw new MissingMethodException("PointGraph nodeCount setter missing.");
            harmony.Patch(AccessTools.Method(typeof(GlobalNavigationManager), "SetConvoyWaypoints"), prefix: new HarmonyMethod(typeof(ConvoyNavigation), "Rebuild"));
            harmony.Patch(AccessTools.Method(typeof(GlobalNavigationManager), "Clear"), prefix: new HarmonyMethod(typeof(ConvoyNavigation), "Reset"));
            harmony.Patch(AccessTools.Method(typeof(FleetAIAgent), "MoveToDestination"),
                prefix: new HarmonyMethod(typeof(ConvoyNavigation), "Prepare"), transpiler: new HarmonyMethod(typeof(ConvoyRoutes), "ArrivalIL"));
            harmony.Patch(AccessTools.Method(typeof(FleetAIAgent), "StartConvoyPath"), prefix: new HarmonyMethod(typeof(ConvoyNavigation), "StartRoute"));
            harmony.Patch(AccessTools.Method(typeof(FleetAIAgent), "GetNextConvoyPathPosition"), prefix: new HarmonyMethod(typeof(ConvoyNavigation), "NextPoint"));
        }
        internal static void Reset()
        {
            revision++;
            foreach (RouteState state in States.Values) state.Valid = false;
            States.Clear();
        }

        internal static void HandoverToNative()
        {
            revision++;
            foreach (KeyValuePair<FleetAIAgent, RouteState> kv in States)
            {
                FleetAIAgent agent = kv.Key;
                RouteState state = kv.Value;
                state.Valid = false;
                if (agent == null) continue;
                try
                {
                    Vector3 final = state.Final;
                    if (state.Fleet != null && final != Vector3.zero)
                    {
                        var points = Points.GetValue(agent) as List<Vector3>;
                        if (points != null)
                        {
                            points.Clear();
                            points.Add(final);
                        }
                        Index.SetValue(agent, 0);
                        LastFinal.SetValue(agent, final);
                        LastTime.SetValue(agent, -1f);
                        AccessTools.Method(typeof(FleetAIAgent), "MoveToDestination").Invoke(agent, new object[] { final });
                    }
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Handover to native failed for fleet agent", e);
                }
            }
            States.Clear();
        }

        // Native RemoveGraph leaves a null slot; DeserializeGraphsAdditive appends
        // another slot on EVERY edit. At slot 32, 1 << slot wraps to the sea graph.
        // Rebuild only this graph's nodes under an A* work item. Keep its identity
        // and slot; neither serialization nor other navigation graphs are touched.
        private static bool Rebuild(GlobalNavigationManager __instance)
        {
            AstarPath active = AstarPath.active;
            if (!active || !DebugMapInfo.instance) return true;
            PointGraph graph = active.astarData.pointGraph;
            if (graph == null) return true;
            int slot = Array.IndexOf(active.astarData.graphs, graph);
            if (slot < 0 || slot >= 32) throw new InvalidOperationException("Convoy graph is outside the 32-bit graph mask.");
            revision++;
            Count.SetValue(__instance, 0);
            __instance.pointGraphMask = 1 << slot;
            active.AddWorkItem(new AstarPath.AstarWorkItem(delegate(bool force)
            {
                graph.GetNodes(delegate(GraphNode node) { node.Destroy(); return true; });
                graph.nodes = new PointNode[0];
                SetNodeCount.Invoke(graph, new object[] { 0 });
                graph.RebuildNodeLookup();
                var nodes = new Dictionary<ConvoyWaypointMarker, PointNode>();
                var markers = Markers.GetValue(DebugMapInfo.instance) as List<ConvoyWaypointMarker>;
                if (markers != null) foreach (ConvoyWaypointMarker marker in markers)
                {
                    if (!marker) continue;
                    var links = Links.GetValue(marker) as List<ConvoyWaypointMarker>;
                    var data = Waypoint.GetValue(marker) as Navy.ConvoyWaypoint;
                    if (links != null && links.Count > 0 && data != null) nodes.Add(marker, graph.AddNode((Int3)data.position));
                }
                foreach (KeyValuePair<ConvoyWaypointMarker, PointNode> pair in nodes)
                    foreach (ConvoyWaypointMarker other in (List<ConvoyWaypointMarker>)Links.GetValue(pair.Key))
                    {
                        PointNode to;
                        if (other && other != pair.Key && nodes.TryGetValue(other, out to)) ConvoyRoutes.WeightedConnection(pair.Value, to, 0);
                    }
                graph.RebuildNodeLookup();
                Count.SetValue(__instance, nodes.Count);
                GraphTime.SetValue(__instance, GameTime.time);
                active.QueueWorkItemFloodFill();
                return true;
            }));
            return false;
        }
        private static bool Eligible(Fleet fleet)
        {
            return fleet != null && fleet.purpose == Fleet.Purpose.Supply && Player.instance != null
                && fleet.GetFaction() == Player.instance.faction;
        }
        private static RouteState Get(FleetAIAgent agent, Fleet fleet)
        {
            RouteState state;
            if (!States.TryGetValue(agent, out state))
            {
                state = new RouteState { Fleet = fleet, Revision = revision };
                States.Add(agent, state);
            }
            return state;
        }
        private static void Prepare(FleetAIAgent __instance, Fleet ___fleet, Vector3 destination,
            List<Vector3> ___convoyPathPoints, ref int ___convoyTGIndex, ref float ___lastConvoyPathTime)
        {
            if (!Eligible(___fleet)) return;
            RouteState state = Get(__instance, ___fleet);
            destination.y = 0;
            if (state.Final != destination || state.Revision != revision)
            {
                state.Final = destination; state.Revision = revision;
                state.HavePrevious = state.HaveTarget = false;
                // Old callbacks retain their own claim and release it on return.
                state.Pending = null;
                ___convoyPathPoints.Clear(); ___convoyTGIndex = 0; ___lastConvoyPathTime = -1;
            }
        }
        private static bool StartRoute(FleetAIAgent __instance, Fleet ___fleet, Vector3 globalStart, Vector3 globalEnd)
        {
            if (!Eligible(___fleet)) return true;
            RouteState state = Get(__instance, ___fleet);
            if (state.Pending != null) return false;
            var points = (List<Vector3>)Points.GetValue(__instance);
            if ((globalStart - globalEnd).sqrMagnitude < .0001f)
            {
                points.Clear(); points.Add(state.Final); Index.SetValue(__instance, 0);
                LastFinal.SetValue(__instance, state.Final); LastTime.SetValue(__instance, GameTime.time);
                return false;
            }
            Vector3 final = state.Final;
            int requestedRevision = revision;
            var request = ABPath.Construct(globalStart, globalEnd);
            request.nnConstraint.graphMask = GlobalNavigationManager.instance.pointGraphMask;
            request.Claim(state);
            request.callback = delegate(Pathfinding.Path completed)
            {
                try
                {
                    if (!state.Valid || state.Pending != completed || state.Final != final || revision != requestedRevision) return;
                    points.Clear();
                    if (!completed.error && completed.vectorPath != null)
                        foreach (Vector3 point in completed.vectorPath)
                        {
                            Vector3 sea = GlobalNavigationManager.ClosestPointOnNavmesh(point); sea.y = 0;
                            if (points.Count == 0 || (points[points.Count - 1] - sea).sqrMagnitude > .000001f) points.Add(sea);
                        }
                    else InterfacePlugin.Log("No connected convoy route for " + ___fleet.name + "; retaining native sea navigation. " + completed.errorLog);
                    if (points.Count == 0 || (points[points.Count - 1] - final).sqrMagnitude > .000001f) points.Add(final);
                    Index.SetValue(__instance, 0);
                    LastFinal.SetValue(__instance, final); LastTime.SetValue(__instance, GameTime.time);
                    state.HavePrevious = state.HaveTarget = false;
                }
                finally
                {
                    if (state.Pending == completed) state.Pending = null;
                    completed.Release(state);
                }
            };
            // A separate request: native StartPath uses agent.path for the sea leg.
            // Sharing that field cancels/overwrites the convoy callback during turns.
            state.Pending = request;
            AstarPath.StartPath(request);
            return false;
        }
        private static bool NextPoint(FleetAIAgent __instance, Fleet ___fleet, List<Vector3> ___convoyPathPoints,
            ref int ___convoyTGIndex, ref Vector3 ___currentConvoyTargetDirection, ref Vector3 ___lastConvoyTargetPoint,
            Vector3 ___currentTargetGlobalDestination, ref Vector3 __result)
        {
            if (!Eligible(___fleet) || ___convoyPathPoints == null) return true;
            if (___convoyPathPoints.Count == 0) { __result = ___currentTargetGlobalDestination; return false; }
            RouteState state = Get(__instance, ___fleet);
            Vector3 position = ___fleet.GetGlobalPosition();
            // Native MoveToDestination moves the flagship to waypoint + formation
            // offset. Test the same formation anchor, otherwise the fleet stops
            // beside the waypoint forever when the offset exceeds arrival radius.
            Ship flagship = ___fleet.flagShip;
            var shipAI = flagship ? ShipAgent.GetValue(flagship) as ShipAIAgent : null;
            var data = shipAI != null ? ShipData.GetValue(shipAI) as ShipAIData : null;
            if (data != null && FormationLeader.GetValue(data) as Ship)
                position -= data.GetFormationWorldOffset() / 5000f;
            position.y = 0;
            float radius = ___fleet.flagShip && ___fleet.flagShip.currentNode ? .25f : .025f;
            int i = Math.Max(0, Math.Min(___convoyTGIndex, ___convoyPathPoints.Count - 1));
            while (true)
            {
                Vector3 target = ___convoyPathPoints[i]; target.y = 0;
                if (!state.HaveTarget || state.Target != target)
                { state.Target = target; state.Direction = target - position; state.HaveTarget = true; }
                if (i >= ___convoyPathPoints.Count - 1 || !ConvoyProgress.Reached(position.x, position.z, target.x, target.z, radius,
                    state.HavePrevious, state.Previous.x, state.Previous.z, state.Direction.x, state.Direction.z)) break;
                i++; state.HaveTarget = false;
            }
            state.Previous = position; state.HavePrevious = true;
            ___convoyTGIndex = i;
            __result = ___convoyPathPoints[i];
            ___lastConvoyTargetPoint = __result;
            ___currentConvoyTargetDirection = state.Direction.normalized;
            return false;
        }
    }
}
