using NUnit.Framework;
using UnityEngine;
using UnityMLShooter.Agent;

namespace Tests.EditMode
{
    /// <summary>
    /// Exhaustive unit tests for <see cref="RewardCalculator"/>. The class is
    /// intentionally Unity-free so every assertion runs deterministically in
    /// EditMode without spinning up a scene.
    /// </summary>
    public class RewardCalculatorTests
    {
        private const float Eps = 1e-6f;

        // Helper builds a StepContext at the origin with all fields neutral.
        // Tests override only the field they care about.
        private static StepContext MakeStep(
            float deltaTimeSeconds = 0f,
            int academyStepCount = 0,
            bool timedOut = false,
            bool anyTargetsRemaining = true,
            float angleRadians = 0f,
            Vector3 agentPosition = default,
            Vector3 nearestTargetPosition = default)
        {
            return new StepContext
            {
                agentPosition = agentPosition,
                nearestUnhitTargetPosition = nearestTargetPosition,
                angleToNearestTargetRadians = angleRadians,
                anyTargetsRemaining = anyTargetsRemaining,
                academyStepCount = academyStepCount,
                deltaTimeSeconds = deltaTimeSeconds,
                timedOut = timedOut,
            };
        }

        // --------------------------------------------------------------
        // Target-hit / clear bonus
        // --------------------------------------------------------------

        [Test]
        public void OnTargetHit_WithRemaining_ReturnsOnePoint_AndDoesNotEndEpisode()
        {
            var rc = new RewardCalculator();
            float r = rc.OnTargetHit(remainingTargets: 3);
            Assert.AreEqual(1.0f, r, Eps);
            Assert.IsFalse(rc.ShouldEndEpisode);
        }

        [Test]
        public void OnTargetHit_WithZeroRemaining_ReturnsElevenPoint_AndEndsEpisode()
        {
            var rc = new RewardCalculator();
            float r = rc.OnTargetHit(remainingTargets: 0);
            Assert.AreEqual(11.0f, r, Eps);
            Assert.IsTrue(rc.ShouldEndEpisode);
        }

        // --------------------------------------------------------------
        // Wasted-shot penalty
        // --------------------------------------------------------------

        [Test]
        public void OnShotFired_Hit_ReturnsZero()
        {
            var rc = new RewardCalculator();
            Assert.AreEqual(0.0f, rc.OnShotFired(hitTarget: true), Eps);
        }

        [Test]
        public void OnShotFired_Miss_ReturnsWastedShotPenalty()
        {
            var rc = new RewardCalculator();
            Assert.AreEqual(-0.05f, rc.OnShotFired(hitTarget: false), Eps);
        }

        // --------------------------------------------------------------
        // Step penalty
        // --------------------------------------------------------------

        [Test]
        public void OnStep_AppliesStepPenalty()
        {
            var rc = new RewardCalculator();
            // No targets, so shaping is skipped — only the step penalty fires.
            float r = rc.OnStep(MakeStep(anyTargetsRemaining: false));
            Assert.AreEqual(-0.001f, r, Eps);
        }

        // --------------------------------------------------------------
        // Stagnation timer
        // --------------------------------------------------------------

        [Test]
        public void OnStep_AccumulatesStagnationPenaltyAfterTenSeconds()
        {
            var rc = new RewardCalculator();
            // Push 9s — no stagnation yet. Disable shaping by clearing targets.
            float r1 = rc.OnStep(MakeStep(deltaTimeSeconds: 9.0f, anyTargetsRemaining: false));
            Assert.AreEqual(-0.001f, r1, Eps, "9s accumulated should not trigger stagnation.");

            // Push 1s more → cumulative 10s → -0.5 fires this step.
            float r2 = rc.OnStep(MakeStep(deltaTimeSeconds: 1.0f, anyTargetsRemaining: false));
            Assert.AreEqual(-0.001f + -0.5f, r2, Eps, "Should fire -0.5 once the 10s threshold is crossed.");
        }

        [Test]
        public void OnStep_DoesNotFireStagnationBefore10sAccumulated()
        {
            var rc = new RewardCalculator();
            float total = 0f;
            // 9 x 1s steps → 9 accumulated, no penalty.
            for (int i = 0; i < 9; i++)
            {
                total += rc.OnStep(MakeStep(deltaTimeSeconds: 1.0f, anyTargetsRemaining: false));
            }
            Assert.AreEqual(9 * -0.001f, total, 1e-5f);
        }

        [Test]
        public void StagnationTimer_ResetsOnTargetHit()
        {
            var rc = new RewardCalculator();

            // Drive the accumulator to 9s.
            rc.OnStep(MakeStep(deltaTimeSeconds: 9.0f, anyTargetsRemaining: false));
            // A hit clears the accumulator.
            rc.OnTargetHit(remainingTargets: 2);

            // Another 9s should NOT fire (would have if the timer carried over).
            float r = rc.OnStep(MakeStep(deltaTimeSeconds: 9.0f, anyTargetsRemaining: false));
            Assert.AreEqual(-0.001f, r, Eps, "Stagnation must reset on OnTargetHit.");
        }

        [Test]
        public void StagnationTimer_ResetsOnEpisodeBegin()
        {
            var rc = new RewardCalculator();
            // Drive to 9s, then start a new episode.
            rc.OnStep(MakeStep(deltaTimeSeconds: 9.0f, anyTargetsRemaining: false));
            rc.OnEpisodeBegin();

            // 9s into the new episode must not fire stagnation.
            float r = rc.OnStep(MakeStep(deltaTimeSeconds: 9.0f, anyTargetsRemaining: false));
            Assert.AreEqual(-0.001f, r, Eps, "Stagnation must reset on OnEpisodeBegin.");
        }

        // --------------------------------------------------------------
        // Aim shaping
        // --------------------------------------------------------------

        [Test]
        public void AimShaping_FullStrengthAtStepZero_AngleZero()
        {
            var rc = new RewardCalculator();
            // angle=0 → cos=1 → +0.05; academyStepCount=0 → coefficient=1.
            // Use identical positions so distance contribution is 0 (and no baseline yet anyway).
            float r = rc.OnStep(MakeStep(
                academyStepCount: 0,
                angleRadians: 0f,
                anyTargetsRemaining: true,
                agentPosition: Vector3.zero,
                nearestTargetPosition: Vector3.zero));
            // r = step penalty + aim shaping
            Assert.AreEqual(-0.001f + 0.05f, r, Eps);
        }

        [Test]
        public void AimShaping_AnnealedToZeroAtHorizon()
        {
            var rc = new RewardCalculator();
            // academyStepCount >= 100,000 → coefficient = 0 → no aim shaping at all.
            float r = rc.OnStep(MakeStep(
                academyStepCount: 100_000,
                angleRadians: 0f,
                anyTargetsRemaining: true));
            Assert.AreEqual(-0.001f, r, Eps);
        }

        [Test]
        public void AimShaping_AnnealedToZeroPastHorizon()
        {
            var rc = new RewardCalculator();
            float r = rc.OnStep(MakeStep(
                academyStepCount: 1_000_000,
                angleRadians: 0f,
                anyTargetsRemaining: true));
            Assert.AreEqual(-0.001f, r, Eps);
        }

        [Test]
        public void AimShaping_HalfStrengthAtMidHorizon()
        {
            var rc = new RewardCalculator();
            // academyStepCount = 50,000 → coefficient = 0.5; cos(0)=1 → +0.025.
            float r = rc.OnStep(MakeStep(
                academyStepCount: 50_000,
                angleRadians: 0f,
                anyTargetsRemaining: true,
                agentPosition: Vector3.zero,
                nearestTargetPosition: Vector3.zero));
            Assert.AreEqual(-0.001f + 0.025f, r, Eps);
        }

        // --------------------------------------------------------------
        // Distance shaping
        // --------------------------------------------------------------

        [Test]
        public void DistanceShaping_FirstStepHasNoBaselineAndContributesZero()
        {
            var rc = new RewardCalculator();
            // angle=π/2 so cos=0 → no aim shaping. Distance differs but no baseline → 0.
            float r = rc.OnStep(MakeStep(
                academyStepCount: 0,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: Vector3.zero,
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));
            Assert.AreEqual(-0.001f, r, Eps, "First step must not produce a distance reward.");
        }

        [Test]
        public void DistanceShaping_PositiveWhenDistanceDecreased()
        {
            var rc = new RewardCalculator();
            // Step 1 sets baseline at 10. Use angle=π/2 to neutralise aim shaping.
            rc.OnStep(MakeStep(
                academyStepCount: 0,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: Vector3.zero,
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            // Step 2 closes 4 units. delta = 10 - 6 = 4 → +0.04 distance shaping.
            float r = rc.OnStep(MakeStep(
                academyStepCount: 0,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: new Vector3(4f, 0f, 0f),
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            Assert.AreEqual(-0.001f + 0.04f, r, 1e-5f);
        }

        [Test]
        public void DistanceShaping_ZeroWhenDistanceIncreased()
        {
            var rc = new RewardCalculator();
            rc.OnStep(MakeStep(
                academyStepCount: 0,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: Vector3.zero,
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            // Move further from the target → distance INCREASED. The brief
            // requires the shaping term to clamp to 0 in that case (so backing
            // away is never rewarded but is also never punished).
            float r = rc.OnStep(MakeStep(
                academyStepCount: 0,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: new Vector3(-5f, 0f, 0f),
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            Assert.AreEqual(-0.001f, r, Eps, "Distance shaping must be 0 when distance increased.");
        }

        [Test]
        public void DistanceShaping_AnnealedAtMidHorizon()
        {
            var rc = new RewardCalculator();
            rc.OnStep(MakeStep(
                academyStepCount: 50_000,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: Vector3.zero,
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            // delta = 4, anneal = 0.5 → 0.5 * 0.01 * 4 = 0.02
            float r = rc.OnStep(MakeStep(
                academyStepCount: 50_000,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: new Vector3(4f, 0f, 0f),
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            Assert.AreEqual(-0.001f + 0.02f, r, 1e-5f);
        }

        [Test]
        public void DistanceShaping_AnnealedToZeroAtHorizon()
        {
            var rc = new RewardCalculator();
            rc.OnStep(MakeStep(
                academyStepCount: 100_000,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: Vector3.zero,
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            float r = rc.OnStep(MakeStep(
                academyStepCount: 100_000,
                angleRadians: Mathf.PI / 2f,
                anyTargetsRemaining: true,
                agentPosition: new Vector3(4f, 0f, 0f),
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            Assert.AreEqual(-0.001f, r, Eps, "Distance shaping must be 0 once annealed past the horizon.");
        }

        // --------------------------------------------------------------
        // Timeout
        // --------------------------------------------------------------

        [Test]
        public void OnStep_TimedOut_AddsTimeoutPenalty()
        {
            var rc = new RewardCalculator();
            // Disable shaping (no targets), single step → step penalty + timeout penalty.
            float r = rc.OnStep(MakeStep(timedOut: true, anyTargetsRemaining: false));
            Assert.AreEqual(-0.001f + -1.0f, r, Eps);
            Assert.IsTrue(rc.TimedOutThisEpisode);
        }

        [Test]
        public void TimedOutThisEpisode_FalseAtStart_TrueAfterTimedOutStep()
        {
            var rc = new RewardCalculator();
            Assert.IsFalse(rc.TimedOutThisEpisode);
            rc.OnStep(MakeStep(timedOut: true, anyTargetsRemaining: false));
            Assert.IsTrue(rc.TimedOutThisEpisode);
        }

        [Test]
        public void TimedOutThisEpisode_ResetsOnEpisodeBegin()
        {
            var rc = new RewardCalculator();
            rc.OnStep(MakeStep(timedOut: true, anyTargetsRemaining: false));
            Assert.IsTrue(rc.TimedOutThisEpisode);

            rc.OnEpisodeBegin();
            Assert.IsFalse(rc.TimedOutThisEpisode);
        }

        // --------------------------------------------------------------
        // Combined / cross-cutting
        // --------------------------------------------------------------

        [Test]
        public void OnStep_TimedOut_PlusShaping_AreAdditive()
        {
            var rc = new RewardCalculator();
            // First step seeds distance baseline at 10.
            rc.OnStep(MakeStep(
                academyStepCount: 0,
                angleRadians: 0f,
                anyTargetsRemaining: true,
                agentPosition: Vector3.zero,
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            // Second step: timed out, full anneal, angle=0 (cos=1 → +0.05),
            // closed 4 units (+0.04 distance), step penalty (-0.001), timeout (-1.0).
            float r = rc.OnStep(MakeStep(
                academyStepCount: 0,
                timedOut: true,
                angleRadians: 0f,
                anyTargetsRemaining: true,
                agentPosition: new Vector3(4f, 0f, 0f),
                nearestTargetPosition: new Vector3(10f, 0f, 0f)));

            Assert.AreEqual(-0.001f + -1.0f + 0.05f + 0.04f, r, 1e-5f);
            Assert.IsTrue(rc.TimedOutThisEpisode);
        }
    }
}
