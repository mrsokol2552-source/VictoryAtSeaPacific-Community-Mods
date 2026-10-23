using System;

namespace VaspScoutPatrol
{
    // Game global coordinates: AircraftFlight divides its physical range by 5000.
    // This class is also linked into the host tests; no Unity substitutes are used.
    public struct Point
    {
        public double X, Z;
        public Point(double x, double z) { X = x; Z = z; }
    }

    public static class PatrolGeometry
    {
        public const double ReturnAllowance = 1.10;
        // Must be larger than the native Scout() completion threshold (0.05).
        public const double ArrivalDistance = 0.06;
        public static bool Finite(double v) { return !Double.IsNaN(v) && !Double.IsInfinity(v); }
        public static bool Valid(Point p) { return Finite(p.X) && Finite(p.Z); }
        public static double Distance(Point a, Point b)
        {
            double x = a.X - b.X, z = a.Z - b.Z;
            return Math.Sqrt(x * x + z * z);
        }

        public static double Radius(double full, double remaining, double reserve, bool maximum)
        {
            if (!Finite(full) || !Finite(remaining) || !Finite(reserve) || full <= 0 || remaining <= 0 || reserve < 0 || reserve >= 1)
                return 0;
            double budget = Math.Min(full, remaining) - full * reserve;
            // Keep a little headroom for update latency and a moving home ship.
            return Math.Max(0, budget / (1 + ReturnAllowance)) * 0.95 * (maximum ? 1.0 : 0.5);
        }

        public static bool Fits(Point current, Point target, Point home, double full, double remaining, double reserve, double margin)
        {
            if (!Valid(current) || !Valid(target) || !Valid(home) || !Finite(full) || !Finite(remaining) || !Finite(reserve) || !Finite(margin)
                || full <= 0 || remaining <= 0 || reserve < 0 || reserve >= 1 || margin < 0)
                return false;
            double required = Distance(current, target) + Distance(target, home) * ReturnAllowance + full * reserve + margin;
            return Finite(required) && required <= remaining + 1e-7;
        }

        public static bool MustReturn(Point current, Point home, double full, double remaining, double reserve, double margin)
        {
            return !Fits(current, current, home, full, remaining, reserve, margin);
        }

        public static bool TryWaypoint(Point home, Point current, double radius, double angle,
            double minX, double maxX, double minZ, double maxZ,
            double full, double remaining, double reserve, double margin, out Point target)
        {
            target = home;
            if (!Valid(home) || !Valid(current) || !Finite(radius) || radius <= ArrivalDistance * 2 || !Finite(angle)
                || !Finite(minX) || !Finite(maxX) || !Finite(minZ) || !Finite(maxZ) || minX >= maxX || minZ >= maxZ)
                return false;
            target = new Point(Math.Max(minX, Math.Min(maxX, home.X + Math.Cos(angle) * radius)),
                               Math.Max(minZ, Math.Min(maxZ, home.Z + Math.Sin(angle) * radius)));
            // Zero is a sentinel in the native flight AI, never a usable order destination.
            return !(target.X == 0 && target.Z == 0) && Distance(current, target) > ArrivalDistance
                && Distance(home, target) > ArrivalDistance
                && Fits(current, target, home, full, remaining, reserve, margin);
        }
    }
}
