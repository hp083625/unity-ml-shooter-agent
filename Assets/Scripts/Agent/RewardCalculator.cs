using UnityEngine;

namespace UnityMLShooter.Agent
{
    /// <summary>
    /// Pure reward function for the PPO agent. No MonoBehaviour, no Unity API
    /// beyond <see cref="Mathf"/>; fully deterministic and unit-testable.
    ///
    /// Anchored to ADR-0003 ("Episode termination and reward shape"). See the
    /// PR body / issue #11 for any deltas between these constants and the
    /// figures in the ADR — those are flagged for the maintainer to ratify
    /// rather than silently applied here.
    /// </summary>
    public sealed class RewardCalculator
    {
        // -- Sparse, hand-designed extrinsic reward terms --------------------

        /// <summary>Reward emitted on every successful Target hit.</summary>
        public const float RewardPerTarget = 1.0f;

        /// <summary>Bonus emitted once when all 4 targets are cleared.</summary>
        public const float RewardClearAll = 5.0f;

        /// <summary>Penalty for a fired shot that did not hit a Target collider.</summary>
        public const float PenaltyWastedShot = -0.01f;

        /// <summary>Per-FixedUpdate-step time penalty.</summary>
        public const float PenaltyTimeStep = -0.001f;

        /// <summary>Terminal penalty for leaving the training area bounds.</summary>
        public const float PenaltyOutOfBounds = -1.0f;

        // -- Annealed shaping coefficients -----------------------------------

        /// <summary>Per-step coefficient for the cos(aim_angle) shaping term.</summary>
        public const float ShapingAimCoeff = 0.001f;

        /// <summary>Per-step coefficient for the closing-distance shaping term.</summary>
        public const float ShapingDistanceCoeff = 0.005f;

        /// <summary>
        /// Number of global steps over which both shaping terms decay
        /// linearly to zero. After this point shaping is pure noise that
        /// would just bias the converged policy, so it is forced off.
        /// </summary>
        public const int ShapingAnnealSteps = 100_000;

        // -- Public API ------------------------------------------------------

        /// <summary>
        /// Linear anneal from 1.0 at <paramref name="globalStep"/> = 0 to 0.0
        /// at <see cref="ShapingAnnealSteps"/>. Clamped at zero past that
        /// point — never returns a negative coefficient.
        /// </summary>
        public static float AnnealCoefficient(int globalStep)
        {
            if (globalStep <= 0) return 1.0f;
            if (globalStep >= ShapingAnnealSteps) return 0.0f;
            return 1.0f - (float)globalStep / ShapingAnnealSteps;
        }

        /// <summary>Reward for a single Target hit.</summary>
        public static float OnTargetHit() => RewardPerTarget;

        /// <summary>Bonus reward when the final remaining Target is cleared.</summary>
        public static float OnAllTargetsCleared() => RewardClearAll;

        /// <summary>Penalty for a fired shot that missed every Target collider.</summary>
        public static float OnWastedShot() => PenaltyWastedShot;

        /// <summary>Per-step time penalty applied every FixedUpdate.</summary>
        public static float OnTimeStep() => PenaltyTimeStep;

        /// <summary>Terminal penalty for going out of bounds.</summary>
        public static float OnOutOfBounds() => PenaltyOutOfBounds;

        /// <summary>
        /// Per-step shaping signal. Pulls the agent toward facing and
        /// approaching the nearest unhit target. Linearly annealed to zero
        /// over <see cref="ShapingAnnealSteps"/> so the converged policy is
        /// not biased by scaffolding rewards.
        /// </summary>
        /// <param name="globalStep">Academy step counter (per ADR-0003).</param>
        /// <param name="angleToNearestTargetRadians">
        /// Angle between the agent's aim direction and the vector to the
        /// nearest unhit target, in radians. 0 = looking directly at it.
        /// </param>
        /// <param name="deltaDistance">
        /// Decrease in distance to the nearest unhit target this step
        /// (i.e. <c>previousDistance - currentDistance</c>). Only positive
        /// deltas (closing the gap) are rewarded; negative deltas clamp to 0.
        /// </param>
        public static float StepShaping(int globalStep, float angleToNearestTargetRadians, float deltaDistance)
        {
            float k = AnnealCoefficient(globalStep);
            if (k <= 0f) return 0f;

            float aimTerm = Mathf.Cos(angleToNearestTargetRadians) * ShapingAimCoeff;
            float distTerm = Mathf.Max(0f, deltaDistance) * ShapingDistanceCoeff;
            return k * (aimTerm + distTerm);
        }
    }
}
