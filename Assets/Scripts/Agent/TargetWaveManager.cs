using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityMLShooter.Agent
{
    /// <summary>
    /// Owns the lifecycle of an area's <c>TargetScript</c> instances:
    /// <list type="bullet">
    ///   <item>Discovers them on <see cref="Awake"/> via <see cref="Component.GetComponentsInChildren{T}(bool)"/>.</item>
    ///   <item>Polls their <c>isHit</c> field every <see cref="Update"/> and fires
    ///   <see cref="TargetHit"/> on rising-edge transitions.</item>
    ///   <item>Resets the whole wave synchronously: <c>isHit</c> back to false,
    ///   any active repop coroutine is stopped, the "up" animation clip is
    ///   re-assigned and played, and the target is repositioned uniformly inside
    ///   the configured local-space bounds with a rotation that faces the
    ///   local-space origin.</item>
    ///   <item>Answers "nearest unhit target" queries by squared distance.</item>
    /// </list>
    ///
    /// <para><b>Why reflection?</b> The Infima asset's <c>TargetScript</c> compiles
    /// into the implicit <c>Assembly-CSharp</c> assembly. Unity 6 forbids an
    /// <c>.asmdef</c>-defined assembly (this one is <c>UnityMLShooter.Agent</c>)
    /// from referencing <c>Assembly-CSharp</c>, so the type can only be reached
    /// reflectively from here (see issue #26). All reflection metadata is cached
    /// once on <see cref="Awake"/>.</para>
    ///
    /// <para><b>Public surface:</b> two read-only counters, one event, two
    /// methods. Event payload is typed as <see cref="MonoBehaviour"/> because
    /// <c>TargetScript</c> itself is invisible at this layer; consumers in
    /// <c>Assembly-CSharp</c> can downcast freely (per spec issue #14, with
    /// Option A asmdef-preserving implementation).</para>
    /// </summary>
    public class TargetWaveManager : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Configurable bounds for ResetWave randomisation. Spec defaults.
        // ------------------------------------------------------------------

        [Header("Reset Wave - Local-Space Bounds")]
        [SerializeField] private Vector2 xRange = new Vector2(-7f, 7f);
        [SerializeField] private Vector2 yRange = new Vector2(0.5f, 2.5f);
        [SerializeField] private Vector2 zRange = new Vector2(-7f, 7f);

        // ------------------------------------------------------------------
        // Cached target list + previous-frame isHit snapshot for edge detection.
        // Both arrays are kept in lockstep — index i in _targets matches the
        // boolean snapshot at index i in _previousIsHit.
        // ------------------------------------------------------------------

        private MonoBehaviour[] _targets = Array.Empty<MonoBehaviour>();
        private bool[] _previousIsHit = Array.Empty<bool>();

        // Cached reflection metadata for TargetScript. Resolved lazily so unit
        // tests that never spin up a real TargetScript don't pay the cost.
        private static Type _targetScriptType;
        private static FieldInfo _isHitField;
        private static FieldInfo _targetUpField;

        /// <summary>Number of targets that have <c>isHit == false</c>.</summary>
        public int RemainingCount
        {
            get
            {
                int remaining = 0;
                if (_targets == null) return 0;
                for (int i = 0; i < _targets.Length; i++)
                {
                    var target = _targets[i];
                    if (target == null) continue;
                    if (!ReadIsHit(target)) remaining++;
                }
                return remaining;
            }
        }

        /// <summary>Total number of targets discovered at <see cref="Awake"/> (or
        /// most recently injected for tests). Invariant across <see cref="ResetWave"/>.</summary>
        public int TotalCount => _targets?.Length ?? 0;

        /// <summary>
        /// Fired once per rising-edge transition of any target's <c>isHit</c>
        /// field (false → true). Payload is the <see cref="MonoBehaviour"/>
        /// that just got hit; cast to <c>TargetScript</c> at the call site if
        /// needed.
        /// </summary>
        public event Action<MonoBehaviour> TargetHit;

        // ------------------------------------------------------------------
        // Unity lifecycle
        // ------------------------------------------------------------------

        protected virtual void Awake()
        {
            EnsureReflectionResolved();
            CacheTargetsFromHierarchy();
        }

        protected virtual void Update()
        {
            if (_targets == null || _targets.Length == 0) return;

            for (int i = 0; i < _targets.Length; i++)
            {
                var target = _targets[i];
                if (target == null) continue;

                bool currentlyHit = ReadIsHit(target);
                bool previouslyHit = _previousIsHit[i];

                // Rising edge: false → true. Fire once.
                if (currentlyHit && !previouslyHit)
                {
                    var handler = TargetHit;
                    if (handler != null)
                    {
                        try { handler(target); }
                        catch (Exception ex) { Debug.LogException(ex); }
                    }
                }

                _previousIsHit[i] = currentlyHit;
            }
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Synchronously bring every cached target back to "alive" state:
        /// clear <c>isHit</c>, stop any running repop coroutine, replay the
        /// "up" animation clip, and randomise position inside the local-space
        /// bounds with rotation facing local origin.
        /// No-op when zero targets are tracked.
        /// </summary>
        public void ResetWave()
        {
            if (_targets == null || _targets.Length == 0) return;

            for (int i = 0; i < _targets.Length; i++)
            {
                var target = _targets[i];
                if (target == null) continue;

                // Stop the repop coroutine that may currently be ticking down a
                // Random.Range delay. StopAllCoroutines on the target's
                // GameObject covers anything started via the MonoBehaviour.
                if (target.gameObject != null)
                {
                    target.StopAllCoroutines();
                }

                // isHit must be false BEFORE the up animation plays, otherwise
                // TargetScript.Update will immediately set it back to "down".
                WriteIsHit(target, false);

                // Replay the "up" clip directly. Mirrors what TargetScript's
                // own coroutine does when its timer expires.
                PlayUpAnimation(target);

                // Randomise position within local-space bounds, then face the
                // local origin so the target is always shootable from the
                // arena centre.
                RandomisePositionAndOrientation(target);
            }

            // Re-snapshot so a target that was hit BEFORE the reset doesn't
            // cause a phantom rising-edge in the next Update.
            ResetEdgeSnapshot();
        }

        /// <summary>
        /// World-space position of the unhit target whose squared distance to
        /// <paramref name="fromWorldPosition"/> is smallest. <c>null</c> when
        /// every target is hit (or no targets are tracked).
        /// </summary>
        public Vector3? GetPositionOfNearestUnhit(Vector3 fromWorldPosition)
        {
            if (_targets == null || _targets.Length == 0) return null;

            float bestSqr = float.PositiveInfinity;
            Vector3? best = null;

            for (int i = 0; i < _targets.Length; i++)
            {
                var target = _targets[i];
                if (target == null) continue;
                if (ReadIsHit(target)) continue;

                Vector3 pos = target.transform.position;
                float sqr = (pos - fromWorldPosition).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = pos;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------
        // Internal seam for EditMode tests.
        //
        // Tests live in Tests.EditMode (a separate asmdef that references
        // UnityMLShooter.Agent). They cannot type-reference TargetScript any
        // more than this assembly can, so the seam takes the same erased
        // MonoBehaviour list the runtime path uses. Tests are responsible for
        // providing real TargetScript instances (constructed via
        // AddComponent(Type.GetType("TargetScript, Assembly-CSharp"))) so the
        // reflection lookup succeeds.
        //
        // Visibility is `internal`. AssemblyInfo.cs in this folder grants
        // [InternalsVisibleTo("Tests.EditMode")].
        // ------------------------------------------------------------------

        internal void InjectTargetsForTesting(IList<MonoBehaviour> injectedTargets)
        {
            EnsureReflectionResolved();

            int count = injectedTargets?.Count ?? 0;
            _targets = new MonoBehaviour[count];
            _previousIsHit = new bool[count];

            for (int i = 0; i < count; i++)
            {
                _targets[i] = injectedTargets[i];
                _previousIsHit[i] = injectedTargets[i] != null && ReadIsHit(injectedTargets[i]);
            }
        }

        /// <summary>Test-only accessor: read the live local-space bounds.</summary>
        internal void GetBoundsForTesting(out Vector2 x, out Vector2 y, out Vector2 z)
        {
            x = xRange;
            y = yRange;
            z = zRange;
        }

        /// <summary>Test-only setter: override the local-space bounds before <see cref="ResetWave"/>.</summary>
        internal void SetBoundsForTesting(Vector2 x, Vector2 y, Vector2 z)
        {
            xRange = x;
            yRange = y;
            zRange = z;
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private void CacheTargetsFromHierarchy()
        {
            // No type → no targets. Empty hierarchy is a valid state per the
            // acceptance criteria: TotalCount == 0, queries return null,
            // ResetWave is a no-op.
            if (_targetScriptType == null)
            {
                _targets = Array.Empty<MonoBehaviour>();
                _previousIsHit = Array.Empty<bool>();
                return;
            }

            // GetComponentsInChildren(Type, includeInactive) is the
            // reflection-friendly overload — it returns Component[] which we
            // can store as MonoBehaviour[] because TargetScript : MonoBehaviour.
            var components = GetComponentsInChildren(_targetScriptType, includeInactive: true);
            _targets = new MonoBehaviour[components.Length];
            _previousIsHit = new bool[components.Length];

            for (int i = 0; i < components.Length; i++)
            {
                _targets[i] = components[i] as MonoBehaviour;
                _previousIsHit[i] = _targets[i] != null && ReadIsHit(_targets[i]);
            }
        }

        private void ResetEdgeSnapshot()
        {
            for (int i = 0; i < _targets.Length; i++)
            {
                var target = _targets[i];
                _previousIsHit[i] = target != null && ReadIsHit(target);
            }
        }

        private void RandomisePositionAndOrientation(MonoBehaviour target)
        {
            float x = UnityEngine.Random.Range(xRange.x, xRange.y);
            float y = UnityEngine.Random.Range(yRange.x, yRange.y);
            float z = UnityEngine.Random.Range(zRange.x, zRange.y);

            // Local-space bounds. The manager's transform anchors the bounds.
            target.transform.localPosition = new Vector3(x, y, z);

            // Face the local-space origin. Vector points from the target back
            // to (0,0,0) in this manager's frame.
            Vector3 toOrigin = -target.transform.localPosition;
            if (toOrigin.sqrMagnitude > 1e-6f)
            {
                target.transform.localRotation = Quaternion.LookRotation(toOrigin);
            }
        }

        private void PlayUpAnimation(MonoBehaviour target)
        {
            if (_targetUpField == null) return;

            // TargetScript.targetUp is a public AnimationClip field. Mirror the
            // exact API the asset uses internally:
            //     gameObject.GetComponent<Animation>().clip = targetUp;
            //     gameObject.GetComponent<Animation>().Play();
            var clipObject = _targetUpField.GetValue(target);
            if (!(clipObject is AnimationClip clip)) return;

            var animation = target.GetComponent<Animation>();
            if (animation == null) return;

            animation.clip = clip;
            animation.Play();
        }

        // ------------------------------------------------------------------
        // Reflection helpers — keep all Type.GetField / GetType calls in one
        // place so the manager body stays readable.
        // ------------------------------------------------------------------

        private static void EnsureReflectionResolved()
        {
            if (_targetScriptType != null) return;

            // The asset's TargetScript lives in the global namespace inside
            // Assembly-CSharp. Look it up by AQN; tolerate the type missing
            // (e.g. test environments without the asset) so behaviour collapses
            // cleanly to "no targets".
            _targetScriptType = Type.GetType("TargetScript, Assembly-CSharp", throwOnError: false);
            if (_targetScriptType == null) return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            _isHitField    = _targetScriptType.GetField("isHit",    flags);
            _targetUpField = _targetScriptType.GetField("targetUp", flags);
        }

        private static bool ReadIsHit(MonoBehaviour target)
        {
            if (_isHitField == null) return false;
            object boxed = _isHitField.GetValue(target);
            return boxed is bool b && b;
        }

        private static void WriteIsHit(MonoBehaviour target, bool value)
        {
            if (_isHitField == null) return;
            _isHitField.SetValue(target, value);
        }
    }
}
