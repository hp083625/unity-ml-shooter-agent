// Copyright 2026 unity-ml-shooter-agent contributors. Licensed per repo root.

using System.Collections;
using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Sibling component to <see cref="Character"/> that scales policy outputs from the
    /// canonical [-1, 1] action space into the values <see cref="CharacterBehaviour"/>'s
    /// AI input setters expect, then forwards them to the character. Per ADR-0005 and the
    /// brief on issue #15 (Tier 2 / Compose).
    ///
    /// File placement: this lives directly under <c>Assets/Scripts/</c> rather than in the
    /// <c>UnityMLShooter.Agent</c> asmdef so it compiles into <c>Assembly-CSharp</c>
    /// alongside <see cref="CharacterBehaviour"/>. See issue #15 implementation notes
    /// (Option C) for the rationale: this class is runtime glue, not testable pure logic,
    /// so co-locating with the rest of the gameplay assembly keeps the dependency graph
    /// trivial and avoids needing the agent asmdef to reference Assembly-CSharp (which
    /// Unity 6 does not allow directly).
    ///
    /// Defensive null check strategy: <see cref="character"/> is cached in
    /// <see cref="Awake"/> via <c>GetComponent</c>, but every <c>Set*</c> entry point
    /// also null-checks before forwarding. Belt-and-braces: Unity's component-add order
    /// usually means the sibling <see cref="Character"/> already had its <c>Awake</c>
    /// called by the time anything calls into us, but if the bridge ever gets enabled
    /// before its sibling resolves we silently no-op rather than throwing into the
    /// agent's decision loop. Spec acceptance criterion #4 explicitly calls for this.
    /// </summary>
    [DisallowMultipleComponent]
    public class AIControllerBridge : MonoBehaviour
    {
        #region FIELDS SERIALIZED

        [Tooltip("Degrees per decision step at full stick. CameraLook applies the value as a per-frame delta in degrees, then multiplies by its own sensitivity field. 5 keeps the per-frame turn modest. Per ADR-0005 / issue #15.")]
        [SerializeField] private float lookSpeedScale = 5.0f;

        [Tooltip("If true, Awake calls character.SetUseAIInput(true) so human Input System callbacks are suppressed for the lifetime of this bridge. Disable to drive the bridge alongside human input (debug only).")]
        [SerializeField] private bool autoEnableAIInput = true;

        #endregion

        #region FIELDS

        /// <summary>Cached sibling <see cref="CharacterBehaviour"/>, resolved in <see cref="Awake"/>.</summary>
        private CharacterBehaviour character;

        #endregion

        #region UNITY

        private void Awake()
        {
            //Resolve the sibling Character. Unity guarantees siblings on the same GameObject
            //have their Awake called in component-add order, so by the time anything outside
            //the editor pokes our setters, the Character.Awake should already have run.
            character = GetComponent<CharacterBehaviour>();

            if (character == null)
            {
                //Loud failure: the bridge is useless without a sibling Character. Log and let
                //the Set* defensive guards keep us from spamming NREs at runtime.
                Log.kill($"{nameof(AIControllerBridge)} requires a sibling {nameof(CharacterBehaviour)} on the same GameObject.");
                return;
            }

            //Flip the AI input gate up-front so any Input System callbacks fired during the
            //first frame are already suppressed before the agent's first decision lands.
            if (autoEnableAIInput)
                character.SetUseAIInput(true);
        }

        #endregion

        #region PUBLIC API

        /// <summary>
        /// Forward the policy's look action to <see cref="CharacterBehaviour.SetAxisLook"/>,
        /// scaling by <see cref="lookSpeedScale"/> so the value lands in the
        /// degrees-per-decision-step space CameraLook consumes.
        /// </summary>
        /// <param name="yawPitchInUnitRange">Yaw (x) and pitch (y), each expected in [-1, 1].</param>
        public void SetLookAction(Vector2 yawPitchInUnitRange)
        {
            if (character == null)
                return;

            character.SetAxisLook(yawPitchInUnitRange * lookSpeedScale);
        }

        /// <summary>
        /// Forward the policy's movement action to <see cref="CharacterBehaviour.SetAxisMovement"/>
        /// straight through. Movement.cs multiplies by <c>speedWalking = 5.0</c> internally,
        /// so no extra scaling is applied here.
        /// </summary>
        /// <param name="xyInUnitRange">Strafe (x) and forward (y), each expected in [-1, 1].</param>
        public void SetMoveAction(Vector2 xyInUnitRange)
        {
            if (character == null)
                return;

            character.SetAxisMovement(xyInUnitRange);
        }

        /// <summary>
        /// Forward the policy's fire action to <see cref="CharacterBehaviour.SetHoldingFire"/>.
        /// </summary>
        /// <param name="firing">True while the agent is holding the fire button.</param>
        public void SetFireAction(bool firing)
        {
            if (character == null)
                return;

            character.SetHoldingFire(firing);
        }

        #endregion

        #region MANUAL SMOKE TEST

        /// <summary>
        /// Manual smoke test exposed via the Inspector context menu. While in Play mode,
        /// right-click the component and pick this entry: for one second the bridge sets
        /// look = (0, +0.5), move = (0, +0.5), fire = true; then resets all three to zero.
        /// The player should visibly look up, walk forward, and fire for ~1 second, then
        /// stop. Acceptance criterion #2 on issue #15.
        /// </summary>
        [ContextMenu("Test: Look up + walk forward + fire")]
        private void TestForward()
        {
            //StartCoroutine only works in Play mode. Bail loudly if invoked from edit mode
            //so the human running the test sees why nothing happened.
            if (!Application.isPlaying)
            {
                Log.warn_me($"{nameof(AIControllerBridge)}.{nameof(TestForward)} only works in Play mode.");
                return;
            }

            StartCoroutine(TestForwardRoutine());
        }

        private IEnumerator TestForwardRoutine()
        {
            //Drive the three actions for one second, then zero out so the character stops
            //moving/firing once the test window closes.
            SetLookAction(new Vector2(0f, 0.5f));
            SetMoveAction(new Vector2(0f, 0.5f));
            SetFireAction(true);

            yield return new WaitForSeconds(1.0f);

            SetLookAction(Vector2.zero);
            SetMoveAction(Vector2.zero);
            SetFireAction(false);
        }

        #endregion
    }
}
