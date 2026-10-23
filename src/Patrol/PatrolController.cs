using System;
using System.Collections.Generic;
using UnityEngine;

namespace VaspScoutPatrol
{
    internal sealed class PatrolMission
    {
        public AircraftFlight Flight;
        public bool Maximum, Returning, Started, QueuedByUs;
        public double Angle, Radius;
        public CampaignOrder Order;
        public float NextRefuel;
        public int Sorties;
    }

    internal sealed class PatrolController
    {
        private readonly PatrolPlugin plugin;
        private readonly Dictionary<AircraftFlight, PatrolMission> missions = new Dictionary<AircraftFlight, PatrolMission>();
        private int issuing;
        public bool IssuingOrders { get { return issuing > 0; } }
        public string LastMessage = "";
        public PatrolController(PatrolPlugin p) { plugin = p; }
        internal bool Active(AircraftFlight f) { return f != null && missions.ContainsKey(f); }
        internal bool IsMaximum(AircraftFlight f) { PatrolMission m; return f != null && missions.TryGetValue(f, out m) && m.Maximum; }

        internal static bool Scout(AircraftFlight f)
        {
            return f != null && f.aircraft != null && f.aircraft.Count > 0 && f.leader && !NativeAccess.Crashed(f.leader)
                && f.leader.GetCurrentHealthAmount() > 0 && f.leader.type == Aircraft.AircraftType.OBSERVATION;
        }

        internal static bool Ready(AircraftFlight f)
        {
            if (!Scout(f) || f.ai == null || !Player.instance || f.leader.GetFaction() != Player.instance.faction) return false;
            GlobalCombatObject home = NativeAccess.Home(f.leader);
            if (!home) return false;
            Ship homeShip = home as Ship;
            CoastalStructure homeBase = home as CoastalStructure;
            Fleet fleet = homeShip ? NativeAccess.Fleet(homeShip) : null;
            if (home.GetFaction() != Player.instance.faction || home.GetCurrentHealthAmount() <= 0 || (homeShip && (NativeAccess.Sinking(homeShip) || (fleet != null && !fleet.playerControllable)))
                || (homeBase && NativeAccess.Destroyed(homeBase))) return false;
            foreach (Aircraft a in f.aircraft)
                if (!a || NativeAccess.Crashed(a) || a.GetCurrentHealthAmount() <= 0 || a.aiAgent == null || a.aiAgent.data == null
                    || NativeAccess.Home(a) != home || a.maxFuel <= 0 || a.range <= 0 || !PatrolGeometry.Finite(a.GetCurrentFuelAmount())
                    || !PatrolGeometry.Finite(a.maxFuel) || !PatrolGeometry.Finite(a.range)) return false;
            return homeShip || homeBase;
        }

        internal void Start(List<AircraftFlight> flights, bool maximum)
        {
            int started = 0;
            foreach (AircraftFlight f in flights)
            {
                if (!Ready(f)) continue;
                Cancel(f, "change patrol mode");
                var m = new PatrolMission { Flight = f, Maximum = maximum, Angle = 2 * Math.PI * started / Math.Max(1, flights.Count) };
                missions.Add(f, m);
                try
                {
                    issuing++;
                    try
                    {
                        f.ClearOrders();
                        foreach (Aircraft a in f.aircraft) a.aiAgent.data.ClearOrder();
                    }
                    finally { issuing--; }
                    // Match the native launch command: each patrol flight gets a normal formation,
                    // so a different flight's squadron leader cannot drag it to another sector.
                    EnsureFormation(f);
                    SafeTick(f, 0);
                    if (Active(f)) started++;
                }
                catch (Exception ex)
                {
                    Cancel(f, "start failure");
                    plugin.Error("Could not start scout patrol for " + f.id + ": " + ex);
                }
            }
            LastMessage = started > 0 ? "Автопатруль: " + started + " зв. — " + (maximum ? "макс." : "средний") : "Нет готовых разведчиков";
            plugin.Log(LastMessage);
        }

        internal void StopAndRecall(List<AircraftFlight> flights)
        {
            int count = 0;
            foreach (AircraftFlight f in flights)
            {
                if (!Active(f)) continue;
                Cancel(f, "stop button");
                if (Ready(f) && AnyFlying(f))
                {
                    issuing++;
                    try { f.GiveExplicitOrders(new List<CampaignOrder> { ReturnOrder(f) }); }
                    finally { issuing--; }
                }
                count++;
            }
            LastMessage = "Патруль отключён: " + count + ". Возврат на базу.";
        }

        internal void Cancel(AircraftFlight flight, string reason)
        {
            PatrolMission m;
            if (flight == null || !missions.TryGetValue(flight, out m)) return;
            missions.Remove(flight);
            if (flight.ai != null && m.Order != null) flight.ai.campaignOrderQueue.Remove(m.Order);
            GlobalCombatObject home = flight.leader ? NativeAccess.Home(flight.leader) : null;
            if (home && m.QueuedByUs)
            {
                Ship s = home as Ship;
                CoastalStructure c = home as CoastalStructure;
                // Remove only a pending launch owned by this patrol, never any other flight.
                if (!AnyFlying(flight))
                {
                    if (s && s.queuedFlights != null) s.queuedFlights.Remove(flight);
                    if (c && c.queuedFlights != null) c.queuedFlights.Remove(flight);
                    PatrolStrip.UpdateQueueUI(flight, false);
                }
            }
            plugin.Log("Patrol stopped for flight " + flight.id + ": " + reason);
        }

        internal void SafeTick(AircraftFlight f, float delta)
        {
            PatrolMission m;
            if (f == null || !missions.TryGetValue(f, out m)) return;
            try { Tick(m, delta); }
            catch (Exception ex)
            {
                // This catches only the add-on's work; the game's AIUpdate always runs normally.
                Cancel(f, "patrol error");
                plugin.Error("Patrol disabled for flight " + f.id + ": " + ex);
            }
        }

        internal void TickAll()
        {
            foreach (AircraftFlight f in new List<AircraftFlight>(missions.Keys)) SafeTick(f, 0);
        }
        internal void Shutdown()
        {
            var errors = new List<string>();
            foreach (AircraftFlight f in new List<AircraftFlight>(missions.Keys))
            {
                try { StopAndRecall(new List<AircraftFlight> { f }); }
                catch (Exception e) { errors.Add("Flight " + f.id + ": " + e); }
            }
            if (errors.Count > 0) throw new InvalidOperationException("Patrol shutdown incomplete: " + string.Join("; ", errors.ToArray()));
        }

        private static Point P(Vector3 v) { return new Point(v.x, v.z); }
        private static Vector3 V(Point p) { return new Vector3((float)p.X, 0, (float)p.Z); }
        private static bool AnyFlying(AircraftFlight f)
        {
            if (f == null || f.aircraft == null) return false;
            foreach (Aircraft a in f.aircraft)
                if (a && (a.IsFlying() || NativeAccess.State(a) == Aircraft.State.READYING_TAKEOFF || NativeAccess.State(a) == Aircraft.State.TAKEOFF)) return true;
            return false;
        }
        private static bool AllLanded(AircraftFlight f)
        {
            foreach (Aircraft a in f.aircraft) if (!a || NativeAccess.State(a) != Aircraft.State.LANDED) return false;
            return true;
        }
        private static float FuelFraction(AircraftFlight f)
        {
            float result = 1;
            foreach (Aircraft a in f.aircraft) result = Mathf.Min(result, a.GetCurrentFuelAmount());
            return result;
        }
        private static void Ranges(AircraftFlight f, out double full, out double remaining)
        {
            full = double.MaxValue;
            remaining = double.MaxValue;
            foreach (Aircraft a in f.aircraft)
            {
                full = Math.Min(full, a.range / 5000.0);
                remaining = Math.Min(remaining, a.GetFuelMovementRange() / 5000.0);
            }
        }

        private void Tick(PatrolMission m, float delta)
        {
            AircraftFlight f = m.Flight;
            if (!Ready(f) || !GameManager.instance) { Cancel(f, "aircraft or base unavailable"); return; }
            CampaignOrder current = f.ai.GetCurrentOrder();
            if (current != null && current != m.Order)
            {
                // Native AI may independently order resupply; let it finish, then repeat.
                if (current.orderType == CampaignOrder.OrderType.CollectSupplies) m.Returning = true;
                else { Cancel(f, "order changed outside patrol"); return; }
            }
            if (AllLanded(f))
            {
                if (m.Started || m.Returning)
                {
                    m.Started = false;
                    m.Returning = false;
                    m.QueuedByUs = false;
                    m.Angle += Math.PI / 4;
                    m.Sorties++;
                    if (m.Order != null) f.ai.campaignOrderQueue.Remove(m.Order);
                    m.Order = null;
                }
                if (FuelFraction(f) < plugin.RefuelThreshold.Value)
                {
                    if (Time.unscaledTime >= m.NextRefuel)
                    {
                        f.AllRefuel();
                        m.NextRefuel = Time.unscaledTime + 5;
                    }
                    return;
                }
                Launch(m);
                return;
            }
            if (!AnyFlying(f)) { Cancel(f, "flight is no longer airborne"); return; }
            if (m.Returning) return;
            m.QueuedByUs = false;
            Point home = P(NativeAccess.Home(f.leader).globalPosition), position = P(f.GetGlobalPosition());
            double full, remaining;
            Ranges(f, out full, out remaining);
            // The complete outbound + return route is budgeted by Fits(); don't count
            // an arbitrarily accelerated whole frame a second time as another route.
            double margin = Math.Min(full * 0.02, Math.Max(0, f.GetMaxSpeed() / 5000.0 * delta));
            if (PatrolGeometry.MustReturn(position, home, full, remaining, plugin.Reserve.Value, margin)) { Return(m); return; }
            if (!m.Started)
            {
                EnsureFormation(f);
                m.Radius = PatrolGeometry.Radius(full, remaining, plugin.Reserve.Value, m.Maximum);
                if (!NextWaypoint(m, position, home, full, remaining, margin)) Return(m);
                m.Started = true;
            }
            else if (m.Order == null || current == null || PatrolGeometry.Distance(position, P(m.Order.destination)) <= PatrolGeometry.ArrivalDistance)
            {
                m.Angle += Math.PI / 4;
                if (!NextWaypoint(m, position, home, full, remaining, margin)) Return(m);
            }
            else if (!PatrolGeometry.Fits(position, P(m.Order.destination), home, full, remaining, plugin.Reserve.Value, margin))
                Return(m);
        }

        private bool NextWaypoint(PatrolMission m, Point position, Point home, double full, double remaining, double margin)
        {
            Rect bounds = GameManager.instance.playableArea;
            Point target;
            // Try successive sectors if a map edge clips the next point onto the current position.
            for (int i = 0; i < 8; i++)
            {
                if (PatrolGeometry.TryWaypoint(home, position, m.Radius, m.Angle, bounds.xMin, bounds.xMax, bounds.yMin, bounds.yMax,
                        full, remaining, plugin.Reserve.Value, margin, out target))
                {
                    var order = new CampaignOrder { isAircraftOrder = true, orderType = CampaignOrder.OrderType.Scouting, orderPriority = 2f };
                    order.destination = V(target);
                    Issue(m, order);
                    return true;
                }
                m.Angle += Math.PI / 4;
            }
            return false;
        }

        private void Issue(PatrolMission m, CampaignOrder order)
        {
            issuing++;
            try { m.Flight.GiveExplicitOrders(new List<CampaignOrder> { order }); m.Order = order; }
            finally { issuing--; }
        }

        private static CampaignOrder ReturnOrder(AircraftFlight f)
        {
            var order = new CampaignOrder { isAircraftOrder = true, orderType = CampaignOrder.OrderType.CollectSupplies,
                orderPriority = 2f, supplyTarget = NativeAccess.Home(f.leader) };
            order.destination = NativeAccess.Home(f.leader).globalPosition;
            return order;
        }

        private void Return(PatrolMission m)
        {
            m.Returning = true;
            Issue(m, ReturnOrder(m.Flight));
            plugin.Log("Scout flight " + m.Flight.id + " returning to refuel (sortie " + (m.Sorties + 1) + ").");
        }

        private void Launch(PatrolMission m)
        {
            AircraftFlight f = m.Flight;
            // Wait for the stock queue and its cooldown; never directly change fuel or flight state.
            GlobalCombatObject baseObject = NativeAccess.Home(f.leader);
            Ship s = baseObject as Ship;
            CoastalStructure c = baseObject as CoastalStructure;
            if (s && s.queuedFlights != null && s.queuedFlights.Contains(f))
            {
                // Off-screen ships are updated infrequently by the stock manager.
                // Service only our flight at the head, under the same readiness test.
                if (m.QueuedByUs && s.queuedFlights[0] == f && s.CanLaunchFlight())
                {
                    s.queuedFlights.RemoveAt(0);
                    PatrolStrip.UpdateQueueUI(f, false);
                    f.TakeOff(s);
                    m.Started = true; m.QueuedByUs = false;
                    plugin.Log("Scout flight " + f.id + " launched from patrol queue.");
                }
                return;
            }
            if (c && c.queuedFlights != null && c.queuedFlights.Contains(f))
            {
                if (m.QueuedByUs && c.queuedFlights[0] == f && c.CanLaunchFlight())
                {
                    c.queuedFlights.RemoveAt(0);
                    PatrolStrip.UpdateQueueUI(f, false);
                    f.TakeOff(c);
                    m.Started = true; m.QueuedByUs = false;
                    plugin.Log("Scout flight " + f.id + " launched from patrol queue.");
                }
                return;
            }
            EnsureFormation(f);
            double full, remaining;
            Ranges(f, out full, out remaining);
            m.Radius = PatrolGeometry.Radius(full, remaining, plugin.Reserve.Value, m.Maximum);
            Point home = P(baseObject.globalPosition);
            if (!NextWaypoint(m, home, home, full, remaining, 0)) { Cancel(f, "no reachable patrol sector"); return; }
            bool launched = false;
            if (s)
            {
                if (s.queuedFlights == null) s.queuedFlights = new List<AircraftFlight>();
                if (s.queuedFlights.Count == 0 && s.CanLaunchFlight())
                {
                    f.TakeOff(s);
                    launched = m.Started = true;
                }
                else
                {
                    s.QueueFlight(f);
                    m.QueuedByUs = true;
                    PatrolStrip.UpdateQueueUI(f, true);
                }
            }
            else if (c)
            {
                if (c.queuedFlights == null) c.queuedFlights = new List<AircraftFlight>();
                if (c.queuedFlights.Count == 0 && c.CanLaunchFlight())
                {
                    f.TakeOff(c);
                    launched = m.Started = true;
                }
                else
                {
                    c.queuedFlights.Add(f);
                    m.QueuedByUs = true;
                    PatrolStrip.UpdateQueueUI(f, true);
                }
            }
            // Pending flights retain Started=false until takeoff has actually begun.
            plugin.Log("Scout flight " + f.id + (launched ? " launched for " : " queued for ") + (m.Maximum ? "MAX" : "medium") + " patrol.");
        }

        private static void EnsureFormation(AircraftFlight f)
        {
            Aircraft leader = f.leader;
            Squadron current = NativeAccess.Squadron(leader);
            if (current != null && current.members.Count == 1 && current.members[0] == leader) return;
            if (!UISquadronList.instance || !FactionManager.instance) throw new InvalidOperationException("Native squadron UI is not ready.");
            Navy navy = FactionManager.instance.GetNavy(f.GetFaction());
            if (navy == null) throw new InvalidOperationException("Flight has no navy.");
            Fleet fleet;
            Port port;
            f.GetHomeFleetOrPort(out fleet, out port);
            Squadron squadron = navy.CreateSquadron(Squadron.Type.Aircraft, fleet, leader.type);
            squadron.AddMember(leader);
            leader.SetIndependent(false);
            squadron.GenerateFormationPositions();
            if (port != null) port.AddSquadron(squadron);
        }
    }
}
