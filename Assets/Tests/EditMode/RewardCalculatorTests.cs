using NUnit.Framework;
using UnityEngine;
using UnityMLShooter.Agent;

namespace Tests.EditMode
{
    /// <summary>
    /// EditMode unit tests for <see cref="RewardCalculator"/>. Exercises the
    /// constants, anneal endpoints, and shaping math at the four cardinal
    /// aim angles plus distance-clamp behaviour. Pure CPU — no scene setup.
    /// </summary>
    public class RewardCalculatorTests
    {
        // Float comparison tolerance — Mathf.Cos(π) drifts to ~-1 + 1e-7
        // depending on the platform, so 1e-6 is safe and still tight enough
        // to catch real drift.
        private const float Tolerance = 1e-6f;

        // -- Constants -------------------------------------------------------

        [Test]
        public void Constants_RewardPerTarget_IsOne()
        {
            Assert.AreEqual(1.0f, RewardCalculator.RewardPerTarget, Tolerance);
        }

        [Test]
        public void Constants_RewardClearAll_IsFive()
        {
            Assert.AreEqual(5.0f, RewardCalculator.RewardClearAll, Tolerance);
        }

        [Test]
        public void Constants_PenaltyWastedShot_IsNegativeOneHundredth()
        {
            Assert.AreEqual(-0.01f, RewardCalculator.PenaltyWastedShot, Tolerance);
        }

        [Test]
        public void Constants_PenaltyTimeStep_IsNegativeThousandth()
        {
            Assert.AreEqual(-0.001f, RewardCalculator.PenaltyTimeStep, Tolerance);
        }

        [Test]
        public void Constants_PenaltyOutOfBounds_IsNegativeOne()
        {
            Assert.AreEqual(-1.0f, RewardCalculator.PenaltyOutOfBounds, Tolerance);
        }

        [Test]
        public void Constants_ShapingCoefficients_AreExact()
        {
            Assert.AreEqual(0.001f, RewardCalculator.ShapingAimCoeff, Tolerance);
            Assert.AreEqual(0.005f, RewardCalculator.ShapingDistanceCoeff, Tolerance);
            Assert.AreEqual(100_000, RewardCalculator.ShapingAnnealSteps);
        }

        // -- Per-event helpers -----------------------------------------------

        [Test]
        public void OnTargetHit_ReturnsRewardPerTarget()
        {
            Assert.AreEqual(RewardCalculator.RewardPerTarget, RewardCalculator.OnTargetHit(), Tolerance);
        }

        [Test]
        public void OnAllTargetsCleared_ReturnsRewardClearAll()
        {
            Assert.AreEqual(RewardCalculator.RewardClearAll, RewardCalculator.OnAllTargetsCleared(), Tolerance);
        }

        [Test]
        public void OnWastedShot_ReturnsPenaltyWastedShot()
        {
            Assert.AreEqual(RewardCalculator.PenaltyWastedShot, RewardCalculator.OnWastedShot(), Tolerance);
        }

        [Test]
        public void OnTimeStep_ReturnsPenaltyTimeStep()
        {
            Assert.AreEqual(RewardCalculator.PenaltyTimeStep, RewardCalculator.OnTimeStep(), Tolerance);
        }

        [Test]
        public void OnOutOfBounds_ReturnsPenaltyOutOfBounds()
        {
            Assert.AreEqual(RewardCalculator.PenaltyOutOfBounds, RewardCalculator.OnOutOfBounds(), Tolerance);
        }

        // -- AnnealCoefficient ----------------------------------------------

        [Test]
        public void AnnealCoefficient_AtStepZero_IsOne()
        {
            Assert.AreEqual(1.0f, RewardCalculator.AnnealCoefficient(0), Tolerance);
        }

        [Test]
        public void AnnealCoefficient_AtHalfway_IsHalf()
        {
            Assert.AreEqual(0.5f, RewardCalculator.AnnealCoefficient(50_000), Tolerance);
        }

        [Test]
        public void AnnealCoefficient_AtEnd_IsZero()
        {
            Assert.AreEqual(0.0f, RewardCalculator.AnnealCoefficient(100_000), Tolerance);
        }

        [Test]
        public void AnnealCoefficient_BeyondEnd_IsClampedToZero()
        {
            Assert.AreEqual(0.0f, RewardCalculator.AnnealCoefficient(200_000), Tolerance);
            Assert.AreEqual(0.0f, RewardCalculator.AnnealCoefficient(int.MaxValue), Tolerance);
        }

        [Test]
        public void AnnealCoefficient_NegativeStep_IsClampedToOne()
        {
            // Defensive: callers should never pass negatives, but if they do
            // we should not amplify shaping above 1.0.
            Assert.AreEqual(1.0f, RewardCalculator.AnnealCoefficient(-1), Tolerance);
        }

        // -- StepShaping at step 0 (full coefficient) ------------------------

        [Test]
        public void StepShaping_LookingAtTarget_NoMovement_ReturnsAimCoeff()
        {
            // angle = 0 → cos = 1, deltaDist = 0 → only aim term contributes.
            float r = RewardCalculator.StepShaping(0, 0f, 0f);
            Assert.AreEqual(0.001f, r, Tolerance);
        }

        [Test]
        public void StepShaping_NinetyDegreesOff_NoMovement_IsApproximatelyZero()
        {
            // angle = π/2 → cos ≈ 0 → both terms zero.
            float r = RewardCalculator.StepShaping(0, Mathf.PI / 2f, 0f);
            Assert.AreEqual(0.0f, r, Tolerance);
        }

        [Test]
        public void StepShaping_LookingAway_NoMovement_ReturnsNegativeAimCoeff()
        {
            // angle = π → cos = -1 → only aim term, negated.
            float r = RewardCalculator.StepShaping(0, Mathf.PI, 0f);
            Assert.AreEqual(-0.001f, r, Tolerance);
        }

        [Test]
        public void StepShaping_LookingForward_ClosingOneUnit_RewardsBothTerms()
        {
            // angle = 0, deltaDist = +1 → 0.001 (aim) + 0.005 (distance) = 0.006.
            float r = RewardCalculator.StepShaping(0, 0f, 1.0f);
            Assert.AreEqual(0.006f, r, Tolerance);
        }

        [Test]
        public void StepShaping_LookingForward_Retreating_DistanceClampsAtZero()
        {
            // angle = 0, deltaDist = -1 → distance term clamped to 0,
            // aim term still pays out → 0.001.
            float r = RewardCalculator.StepShaping(0, 0f, -1.0f);
            Assert.AreEqual(0.001f, r, Tolerance);
        }

        // -- StepShaping past anneal -----------------------------------------

        [Test]
        public void StepShaping_AtAnnealEnd_IsZero()
        {
            // Even maximally favourable inputs produce zero once annealed.
            float r = RewardCalculator.StepShaping(100_000, 0f, 1.0f);
            Assert.AreEqual(0f, r, Tolerance);
        }

        [Test]
        public void StepShaping_PastAnnealEnd_IsZeroRegardlessOfInputs()
        {
            Assert.AreEqual(0f, RewardCalculator.StepShaping(150_000, 0f, 5.0f), Tolerance);
            Assert.AreEqual(0f, RewardCalculator.StepShaping(1_000_000, Mathf.PI, -10.0f), Tolerance);
        }

        // -- StepShaping mid-anneal ------------------------------------------

        [Test]
        public void StepShaping_AtHalfAnneal_HalvesTheRewards()
        {
            // At step 50k, k = 0.5. Looking forward + closing 1 unit:
            // 0.5 * (1.0 * 0.001 + 1.0 * 0.005) = 0.003
            float r = RewardCalculator.StepShaping(50_000, 0f, 1.0f);
            Assert.AreEqual(0.003f, r, Tolerance);
        }
    }
}
