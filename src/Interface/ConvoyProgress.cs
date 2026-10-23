using System;

namespace VaspInterfaceTools
{
    // Shared with deterministic regression tests; no Unity/native calls.
    internal static class ConvoyProgress
    {
        internal static bool Reached(double x, double z, double targetX, double targetZ, double radius,
            bool havePrevious, double previousX, double previousZ, double directionX, double directionZ)
        {
            double dx = targetX - x, dz = targetZ - z;
            double squaredRadius = radius * radius;
            if (dx * dx + dz * dz <= squaredRadius) return true;
            if (havePrevious)
            {
                double sx = x - previousX, sz = z - previousZ;
                double length = sx * sx + sz * sz;
                if (length > 1e-12)
                {
                    double t = ((targetX - previousX) * sx + (targetZ - previousZ) * sz) / length;
                    if (t >= 0 && t <= 1)
                    {
                        double qx = previousX + t * sx - targetX, qz = previousZ + t * sz - targetZ;
                        if (qx * qx + qz * qz <= squaredRadius) return true;
                    }
                }
            }
            // Keep the initial leg direction fixed. A small overshoot must not
            // command a turn back, but a distant parallel track must not skip a leg.
            double directionSquared = directionX * directionX + directionZ * directionZ;
            double along = dx * directionX + dz * directionZ;
            double cross = dx * directionZ - dz * directionX;
            return directionSquared > 1e-12 && along <= 0 && cross * cross <= 4 * squaredRadius * directionSquared;
        }
    }
}
