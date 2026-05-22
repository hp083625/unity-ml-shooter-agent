using UnityEngine;

namespace UnityMLShooter.Agent
{
    /// <summary>
    /// Step-side context delivered by the agent on every decision step. The
    /// <see cref="RewardCalculator"/> is intentionally Unity-free apart from the
    /// <see cref="UnityEngine.Vector3"/> that already lives in the agent's
    /// observation surface, so this struct can be constructed in EditMode tests
    /// without instantiating GameObjects.
    /// </summary>
    public struct StepContext
    {
        /// <summary>World-space position of the agent at the start of the step.</summary>
        public Vector3 agentPosition;

        /// <summary>
        /// Position of the closest unhit target. Caller MUST guard with
        /// <see cref="anyTargetsRemaining"/>; the value is meaningless when no
        /// targets remain.
        /// </summary>
        public Vector3 nearestUnhitTargetPosition;

        /// <summary>Angle (radians) between agent forward and the nearest unhit target.</summary>
        public float angleToNearestTargetRadians;

        /// <summary>True while at least one target is still alive.</summary>
        public bool anyTargetsRemaining;

        /// <summary>
        /// Global academy step count, used for shaped-reward annealing. Must be a
        /// monotonically non-decreasing counter shared across all parallel agents.
        /// </summary>
        public int academyStepCount;

        /// <summary>Game-time delta since the previous decision step.</summary>
        public float deltaTimeSeconds;

        /// <summary>True only on the final step of a max-step episode.</summary>
        public bool timedOut;
    }

    /// <summary>
    /// Pure-C# implementation of the reward function specified by ADR-0003.
    ///
    /// The class is stateful so it can model the stagnation timer (per-10s -0.5
    /// penalty without a hit) and the previous-distance term used in distance
    /// shaping. Reset that state by calling <see cref="OnEpisodeBegin"/> at the
    /// start of every episode.
    ///
    /// No <see cref="MonoBehaviour"/>, no <see cref="UnityEngine.Time"/> access
    /// and no Unity event hooks live in this class — it is exhaustively
    /// unit-testable from EditMode.
    /// </summary>
    public class RewardCalculator
    {
        // ADR-0003 reward magnitudes. Kept as constants so the test asserts can
        // reference the same source of truth.
        public const float TargetHitReward = 1.0f;
        public const float ClearAllBonus = 10.0f;
        public const float WastedShotPenalty = -0.05f;
        public const float StagnationPenalty = -0.5f;
        public const float StepPenalty = -0.001f;
        public const float TimeoutPenalty = -1.0f;
        public const float AimShapingScale = 0.05f;
        public const float DistanceShapingScale = 0.01f;

        public const float StagnationIntervalSeconds = 10.0f;
        public const int AnnealingHorizonSteps = 100_000;

        // --- Episode state ---
        private float _secondsSinceLastHit;
        private bool _hasLastDistance;
        private float _lastDistance;

        /// <summary>True after the +10 clear bonus has fired this episode.</summary>
        public bool ShouldEndEpisode { get; private set; }

        /// <summary>True after a step in which <c>StepContext.timedOut</c> was set.</summary>
        public bool TimedOutThisEpisode { get; private set; }

        public RewardCalculator()
        {
            OnEpisodeBegin();
        }

        /// <summary>
        /// Reset every piece of per-episode state. Call from
        /// <c>Agent.OnEpisodeBegin</c>.
        /// </summary>
        public void OnEpisodeBegin()
        {
            _secondsSinceLastHit = 0f;
            _hasLastDistance = false;
            _lastDistance = 0f;
            ShouldEndEpisode = false;
            TimedOutThisEpisode = false;
        }

        /// <summary>
        /// Award the per-target reward and (if the last target just fell) the
        /// clear bonus. Resets the stagnation accumulator.
        /// </summary>
        /// <param name="remainingTargets">
        /// The number of targets still alive AFTER this hit is applied.
        /// </param>
        /// <returns>+1.0 normally, or +11.0 when the episode just cleared.</returns>
        public float OnTargetHit(int remainingTargets)
        {
            // Stagnation timer always resets on a real hit, regardless of clear.
            _secondsSinceLastHit = 0f;

            if (remainingTargets <= 0)
            {
                ShouldEndEpisode = true;
                return TargetHitReward + ClearAllBonus;
            }

            return TargetHitReward;
        }

        /// <summary>
        /// 0 if the shot landed on a Target collider, otherwise the
        /// wasted-shot penalty.
        /// </summary>
        public float OnShotFired(bool hitTarget)
        {
            return hitTarget ? 0f : WastedShotPenalty;
        }

        /// <summary>
        /// Sum every per-step term: step penalty, stagnation, timeout, and the
        /// two annealed shaped rewards. Mutates the stagnation accumulator and
        /// the cached last distance.
        /// </summary>
        public float OnStep(StepContext ctx)
        {
            float reward = StepPenalty;

            // Stagnation: accumulate, then drain in 10s buckets so a long
            // step can fire multiple penalties at once and the carry survives
            // into the next step.
            _secondsSinceLastHit += ctx.deltaTimeSeconds;
            while (_secondsSinceLastHit >= StagnationIntervalSeconds)
            {
                reward += StagnationPenalty;
                _secondsSinceLastHit -= StagnationIntervalSeconds;
            }

            // Timeout penalty. Latch the flag so callers can read it later.
            if (ctx.timedOut)
            {
                reward += TimeoutPenalty;
                TimedOutThisEpisode = true;
            }

            // Shaped rewards anneal linearly from 1 at step 0 to 0 at the
            // horizon. Past the horizon the coefficient is clamped to 0.
            float anneal = AnnealingCoefficient(ctx.academyStepCount);

            if (anneal > 0f && ctx.anyTargetsRemaining)
            {
                // Aim shaping: +0.05 * cos(angle) per step, annealed.
                reward += AimShapingScale * Mathf.Cos(ctx.angleToNearestTargetRadians) * anneal;

                // Distance shaping: +0.01 * (lastDistance - currentDistance)
                // per step, annealed. The reward is clamped at zero so backing
                // away never accrues a negative shaping term — the brief
                // explicitly specifies "0 when distance increased". The first
                // step of an episode has no baseline, so it contributes nothing
                // to the reward but does seed the cache.
                float currentDistance = Vector3.Distance(ctx.agentPosition, ctx.nearestUnhitTargetPosition);
                if (_hasLastDistance)
                {
                    float delta = _lastDistance - currentDistance;
                    if (delta > 0f)
                    {
                        reward += DistanceShapingScale * delta * anneal;
                    }
                }
                _lastDistance = currentDistance;
                _hasLastDistance = true;
            }
            else
            {
                // No targets remaining → distance is meaningless. Drop the
                // baseline so a follow-up episode (or a target respawn) does
                // not borrow stale state.
                _hasLastDistance = false;
            }

            return reward;
        }

        /// <summary>
        /// Linear decay from 1 at step 0 to 0 at <see cref="AnnealingHorizonSteps"/>.
        /// Clamped so the coefficient never goes negative or above 1.
        /// </summary>
        public static float AnnealingCoefficient(int academyStepCount)
        {
            if (academyStepCount <= 0) return 1f;
            if (academyStepCount >= AnnealingHorizonSteps) return 0f;
            return 1f - (academyStepCount / (float)AnnealingHorizonSteps);
        }
    }
}
