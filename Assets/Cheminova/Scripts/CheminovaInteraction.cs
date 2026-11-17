using System;
using UnityEngine;

namespace Cheminova
{
    // Stores both local coordinates for restoration and world coordinates for inspection.
    internal readonly struct CheminovaTransformState
    {
        internal readonly Vector3 LocalPosition, LocalScale, WorldPosition, WorldScale;
        internal readonly Quaternion LocalRotation, WorldRotation;

        internal CheminovaTransformState(Transform target)
        {
            LocalPosition = target.localPosition;
            LocalRotation = target.localRotation;
            LocalScale = target.localScale;
            WorldPosition = target.position;
            WorldRotation = target.rotation;
            WorldScale = target.lossyScale;
        }

        internal void Restore(Transform target)
        {
            if (target == null) return;
            target.localPosition = LocalPosition;
            target.localRotation = LocalRotation;
            target.localScale = LocalScale;
        }
    }

    internal sealed class CheminovaInteractionWindow
    {
        private readonly double timeout;
        private double lastActivity;
        internal bool IsActive { get; private set; }

        internal CheminovaInteractionWindow(double timeout)
        {
            if (double.IsNaN(timeout) || double.IsInfinity(timeout) || timeout <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            this.timeout = timeout;
        }

        // Returns true only for the first interaction, so its original pose is kept.
        internal bool Record(double now)
        {
            ValidateTime(now);
            bool first = !IsActive;
            IsActive = true;
            lastActivity = now;
            return first;
        }

        internal bool HasExpired(double now)
        {
            ValidateTime(now);
            return IsActive && now - lastActivity >= timeout;
        }

        internal void Clear() { IsActive = false; }

        private static void ValidateTime(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
        }
    }
}
