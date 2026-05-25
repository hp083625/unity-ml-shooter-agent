using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityMLShooter.Agent;

namespace Tests.EditMode
{
    /// <summary>
    /// EditMode coverage for <see cref="TargetWaveManager"/> (issue #14).
    ///
    /// The asset's <c>TargetScript</c> compiles into <c>Assembly-CSharp</c>
    /// which Tests.EditMode cannot reference (Unity 6 limitation, see #26), so
    /// every test resolves it via <see cref="Type.GetType(string)"/> and goes
    /// through the manager's internal seam <c>InjectTargetsForTesting</c>. This
    /// avoids spinning up the full hierarchy under
    /// <c>GetComponentsInChildren</c> and lets the assertions stay
    /// deterministic.
    /// </summary>
    public class TargetWaveManagerTests
    {
        private static readonly Type TargetScriptType =
            Type.GetType("TargetScript, Assembly-CSharp");

        private static readonly FieldInfo IsHitField =
            TargetScriptType?.GetField("isHit", BindingFlags.Public | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            _spawned.Clear();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Skip the test if the host project doesn't ship <c>TargetScript</c>
        /// (e.g. running tests in a stripped harness). Runs once per call so
        /// each [Test] explicitly bails before touching reflection state.
        /// </summary>
        private static void RequireTargetScript()
        {
            if (TargetScriptType == null)
            {
                Assert.Ignore("TargetScript type not present in Assembly-CSharp; skipping.");
            }
            if (IsHitField == null)
            {
                Assert.Ignore("TargetScript.isHit field not found via reflection; skipping.");
            }
        }

        private MonoBehaviour MakeTarget(Vector3 worldPosition, bool isHit = false)
        {
            var go = new GameObject("FakeTarget");
            _spawned.Add(go);
            go.transform.position = worldPosition;

            // Animation component is required by the up-clip path inside
            // ResetWave; AddComponent is safe in EditMode.
            go.AddComponent<Animation>();

            var component = go.AddComponent(TargetScriptType) as MonoBehaviour;
            Assert.IsNotNull(component, "AddComponent(TargetScriptType) returned null.");
            IsHitField.SetValue(component, isHit);
            return component;
        }

        private TargetWaveManager MakeManager()
        {
            var go = new GameObject("TargetWaveManager");
            _spawned.Add(go);
            return go.AddComponent<TargetWaveManager>();
        }

        private static bool GetIsHit(MonoBehaviour target)
        {
            return (bool)IsHitField.GetValue(target);
        }

        private static void SetIsHit(MonoBehaviour target, bool value)
        {
            IsHitField.SetValue(target, value);
        }

        // ------------------------------------------------------------------
        // Acceptance criterion: ResetWave clears every target's isHit.
        // ------------------------------------------------------------------

        [Test]
        public void ResetWave_ClearsIsHitOnEveryTarget()
        {
            RequireTargetScript();

            var manager = MakeManager();
            var t1 = MakeTarget(new Vector3(0f, 0f, 0f), isHit: true);
            var t2 = MakeTarget(new Vector3(5f, 0f, 0f), isHit: true);
            var t3 = MakeTarget(new Vector3(0f, 0f, 5f), isHit: false);

            manager.InjectTargetsForTesting(new List<MonoBehaviour> { t1, t2, t3 });

            manager.ResetWave();

            Assert.IsFalse(GetIsHit(t1), "Target 1 should be unhit after ResetWave.");
            Assert.IsFalse(GetIsHit(t2), "Target 2 should be unhit after ResetWave.");
            Assert.IsFalse(GetIsHit(t3), "Target 3 should be unhit after ResetWave.");
        }

        // ------------------------------------------------------------------
        // Acceptance criterion: ResetWave randomises positions inside bounds.
        // ------------------------------------------------------------------

        [Test]
        public void ResetWave_PlacesTargetsInsideConfiguredBounds()
        {
            RequireTargetScript();

            var manager = MakeManager();
            // Tighten bounds to make the assertion a little crisper than the
            // ±7 default.
            var x = new Vector2(-3f, 3f);
            var y = new Vector2(0.5f, 1.5f);
            var z = new Vector2(-2f, 2f);
            manager.SetBoundsForTesting(x, y, z);

            // The target needs to be parented under the manager so local-space
            // bounds resolve through the same transform the manager uses.
            var t1 = MakeTarget(new Vector3(99f, 99f, 99f), isHit: false);
            var t2 = MakeTarget(new Vector3(-99f, -99f, -99f), isHit: false);
            t1.transform.SetParent(manager.transform, worldPositionStays: false);
            t2.transform.SetParent(manager.transform, worldPositionStays: false);

            manager.InjectTargetsForTesting(new List<MonoBehaviour> { t1, t2 });

            manager.ResetWave();

            foreach (var t in new[] { t1, t2 })
            {
                var p = t.transform.localPosition;
                Assert.GreaterOrEqual(p.x, x.x);
                Assert.LessOrEqual(p.x, x.y);
                Assert.GreaterOrEqual(p.y, y.x);
                Assert.LessOrEqual(p.y, y.y);
                Assert.GreaterOrEqual(p.z, z.x);
                Assert.LessOrEqual(p.z, z.y);
            }
        }

        // ------------------------------------------------------------------
        // Acceptance criterion: GetPositionOfNearestUnhit returns the closest
        // unhit target's position.
        // ------------------------------------------------------------------

        [Test]
        public void GetPositionOfNearestUnhit_ReturnsNearestUnhitPosition()
        {
            RequireTargetScript();

            var manager = MakeManager();
            var near = MakeTarget(new Vector3(2f, 0f, 0f), isHit: false);
            var mid = MakeTarget(new Vector3(5f, 0f, 0f), isHit: false);
            var far = MakeTarget(new Vector3(20f, 0f, 0f), isHit: false);

            manager.InjectTargetsForTesting(new List<MonoBehaviour> { far, mid, near });

            // Query from origin → "near" wins.
            var result = manager.GetPositionOfNearestUnhit(Vector3.zero);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(near.transform.position, result.Value);

            // After "near" is hit, "mid" becomes the closest unhit target.
            SetIsHit(near, true);
            result = manager.GetPositionOfNearestUnhit(Vector3.zero);
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(mid.transform.position, result.Value);
        }

        // ------------------------------------------------------------------
        // Acceptance criterion: GetPositionOfNearestUnhit is null when all
        // targets are hit.
        // ------------------------------------------------------------------

        [Test]
        public void GetPositionOfNearestUnhit_ReturnsNullWhenAllHit()
        {
            RequireTargetScript();

            var manager = MakeManager();
            var t1 = MakeTarget(new Vector3(2f, 0f, 0f), isHit: true);
            var t2 = MakeTarget(new Vector3(5f, 0f, 0f), isHit: true);
            manager.InjectTargetsForTesting(new List<MonoBehaviour> { t1, t2 });

            var result = manager.GetPositionOfNearestUnhit(Vector3.zero);
            Assert.IsFalse(result.HasValue, "Expected null when every target is hit.");
        }

        // ------------------------------------------------------------------
        // Acceptance criterion: RemainingCount drops by 1 when a target's
        // isHit is flipped to true.
        // ------------------------------------------------------------------

        [Test]
        public void RemainingCount_DecreasesWhenTargetMarkedHit()
        {
            RequireTargetScript();

            var manager = MakeManager();
            var t1 = MakeTarget(new Vector3(0f, 0f, 0f), isHit: false);
            var t2 = MakeTarget(new Vector3(5f, 0f, 0f), isHit: false);
            var t3 = MakeTarget(new Vector3(0f, 0f, 5f), isHit: false);
            manager.InjectTargetsForTesting(new List<MonoBehaviour> { t1, t2, t3 });

            Assert.AreEqual(3, manager.RemainingCount, "All three targets should be alive.");

            SetIsHit(t2, true);
            Assert.AreEqual(2, manager.RemainingCount,
                "RemainingCount should drop by 1 after a single isHit flip.");
        }

        // ------------------------------------------------------------------
        // Acceptance criterion: TotalCount is invariant across ResetWave.
        // ------------------------------------------------------------------

        [Test]
        public void TotalCount_IsInvariantAcrossResetWave()
        {
            RequireTargetScript();

            var manager = MakeManager();
            var t1 = MakeTarget(new Vector3(0f, 0f, 0f), isHit: true);
            var t2 = MakeTarget(new Vector3(5f, 0f, 0f), isHit: false);
            t1.transform.SetParent(manager.transform, worldPositionStays: false);
            t2.transform.SetParent(manager.transform, worldPositionStays: false);
            manager.InjectTargetsForTesting(new List<MonoBehaviour> { t1, t2 });

            int before = manager.TotalCount;
            Assert.AreEqual(2, before);

            manager.ResetWave();
            Assert.AreEqual(before, manager.TotalCount, "TotalCount must not change after ResetWave.");

            // And again — repeated calls keep the invariant.
            manager.ResetWave();
            Assert.AreEqual(before, manager.TotalCount);
        }

        // ------------------------------------------------------------------
        // Acceptance criterion: empty hierarchy → safe no-op behaviour.
        // ------------------------------------------------------------------

        [Test]
        public void EmptyHierarchy_TotalCountZero_NearestNull_ResetIsNoop()
        {
            // No reflection lookup needed — exercising the empty path.
            var go = new GameObject("EmptyManager");
            _spawned.Add(go);
            var manager = go.AddComponent<TargetWaveManager>();

            Assert.AreEqual(0, manager.TotalCount, "Empty hierarchy → TotalCount must be 0.");
            Assert.AreEqual(0, manager.RemainingCount, "Empty hierarchy → RemainingCount must be 0.");

            Vector3? result = null;
            Assert.DoesNotThrow(() => result = manager.GetPositionOfNearestUnhit(Vector3.zero));
            Assert.IsFalse(result.HasValue, "Empty hierarchy → GetPositionOfNearestUnhit must return null.");

            Assert.DoesNotThrow(() => manager.ResetWave(), "Empty hierarchy → ResetWave must be a safe no-op.");
        }
    }
}
