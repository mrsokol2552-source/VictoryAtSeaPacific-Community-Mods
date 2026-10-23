using System;
using System.Reflection;
using HarmonyLib;

namespace VaspScoutPatrol
{
    // Resolve the game's internal fields once. Do not publicize or rewrite the game assembly.
    internal static class NativeAccess
    {
        private static readonly FieldInfo HomeField = Field(typeof(Aircraft), "home", typeof(GlobalCombatObject));
        private static readonly FieldInfo StateField = Field(typeof(Aircraft), "currentState", typeof(Aircraft.State));
        private static readonly FieldInfo CrashField = Field(typeof(Aircraft), "crashed", typeof(bool));
        private static readonly FieldInfo FlightField = Field(typeof(Aircraft), "flight", typeof(AircraftFlight));
        private static readonly FieldInfo SquadronField = Field(typeof(GlobalCombatObject), "squadron", typeof(Squadron));
        private static readonly FieldInfo FleetField = Field(typeof(Ship), "currentFleet", typeof(Fleet));
        private static readonly FieldInfo SinkingField = Field(typeof(Ship), "sinking", typeof(bool));
        private static readonly FieldInfo DestroyedField = Field(typeof(CoastalStructure), "destroyed", typeof(bool));
        private static FieldInfo Field(Type owner, string name, Type type)
        {
            FieldInfo f = AccessTools.Field(owner, name);
            if (f == null || f.FieldType != type) throw new MissingFieldException(owner.FullName, name);
            return f;
        }
        internal static void Validate() { if (HomeField == null) throw new MissingFieldException(); }
        internal static GlobalCombatObject Home(Aircraft a) { return HomeField.GetValue(a) as GlobalCombatObject; }
        internal static Aircraft.State State(Aircraft a) { return (Aircraft.State)StateField.GetValue(a); }
        internal static bool Crashed(Aircraft a) { return (bool)CrashField.GetValue(a); }
        internal static AircraftFlight Flight(Aircraft a) { return FlightField.GetValue(a) as AircraftFlight; }
        internal static Squadron Squadron(Aircraft a) { return SquadronField.GetValue(a) as Squadron; }
        internal static Fleet Fleet(Ship s) { return FleetField.GetValue(s) as Fleet; }
        internal static bool Sinking(Ship s) { return (bool)SinkingField.GetValue(s); }
        internal static bool Destroyed(CoastalStructure c) { return (bool)DestroyedField.GetValue(c); }
    }
}
