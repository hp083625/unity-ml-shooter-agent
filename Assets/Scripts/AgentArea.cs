// Copyright 2026 unity-ml-shooter-agent contributors. Licensed per repo root.

using UnityEngine;
using UnityMLShooter.Agent; // for TargetWaveManager

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Per-area orchestrator that ties together the player rig, the area's
    /// <see cref="TargetWaveManager"/>, the <see cref="AIControllerBridge"/>, and
    /// the configured spawn points. Sits at the root of each <c>TrainingArea</c>
    /// prefab and exposes a single <see cref="ResetArea"/> entry point that the
    /// (Tier 4) <c>AgentShooter.OnEpisodeBegin</c> will call once per episode.
    ///
    /// <para><b>File placement.</b> This lives directly under <c>Assets/Scripts/</c>
    /// rather than inside the <c>UnityMLShooter.Agent</c> asmdef so it compiles
    /// into <c>Assembly-CSharp</c> alongside <see cref="CharacterBehaviour"/> and
    /// <see cref="AIControllerBridge"/>. The boundary policy committed to in
    /// PR #42's review reply (now on main) is: runtime glue with inspector
    /// references to <see cref="CharacterBehaviour"/> lives in
    /// <c>Assembly-CSharp</c>; pure logic / testable classes live in
    /// <c>UnityMLShooter.Agent</c>; the asmdef is consumed by
    /// <c>Assembly-CSharp</c>, never the other way (Unity 6 does not allow
    /// .asmdef-defined assemblies to reference <c>Assembly-CSharp</c> — see
    /// issue #26). <see cref="TargetWaveManager"/> lives inside the asmdef but
    /// exposes a public surface that is reachable from <c>Assembly-CSharp</c>
    /// via <c>using UnityMLShooter.Agent;</c>, so we hold onto it directly.</para>
    ///
    /// <para><b>Defensive null-check policy.</b> Acceptance criterion #3 on
    /// issue #16 demands "no exceptions when called multiple times in
    /// succession". Each step of <see cref="ResetArea"/> null-checks its target
    /// and bails loudly via <see cref="Log.warn_me"/> rather than throwing —
    /// the agent's decision loop must never NRE when the prefab is partially
    /// wired up.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class AgentArea : MonoBehaviour
    {
        #region FIELDS SERIALIZED

        [Tooltip("Player rig's CharacterBehaviour. Used for ammo refill via Character.FillAmmunition(0). The 0 sentinel fills to magazine total per Weapon.FillAmmunition semantics.")]
        [SerializeField] private CharacterBehaviour character;

        [Tooltip("The area's TargetWaveManager (UnityMLShooter.Agent assembly). ResetArea calls ResetWave() on this.")]
        [SerializeField] private TargetWaveManager targetWaveManager;

        [Tooltip("The AIControllerBridge sibling on the player rig. Held here so consumers (AgentShooter, etc.) can grab it via Bridge.")]
        [SerializeField] private AIControllerBridge bridge;

        [Tooltip("Spawn transforms inside the area; ResetArea picks one uniformly at random and teleports the player rigidbody there.")]
        [SerializeField] private Transform[] spawnPoints;

        [Tooltip("The player rigidbody. Teleport uses Rigidbody.position / MoveRotation, not Transform — physics integration is sensitive to non-physics teleports inside FixedUpdate.")]
        [SerializeField] private Rigidbody playerRigidbody;

        #endregion

        #region PROPERTIES

        /// <summary>The area's <see cref="TargetWaveManager"/> as wired in the inspector.</summary>
        public TargetWaveManager Targets => targetWaveManager;

        /// <summary>The area's player <see cref="CharacterBehaviour"/> as wired in the inspector.</summary>
        public CharacterBehaviour Character => character;

        /// <summary>The area's <see cref="AIControllerBridge"/> as wired in the inspector.</summary>
        public AIControllerBridge Bridge => bridge;

        #endregion

        #region UNITY

        /// <summary>
        /// Pin <see cref="Time.fixedDeltaTime"/> to 0.02f. This is what the
        /// <c>MaxStep = 600</c> math used by the (Tier 4) <c>AgentShooter</c>
        /// assumes about physics step frequency.
        ///
        /// Idempotent: setting it to the same value is a no-op, and every
        /// <see cref="AgentArea"/> in the scene can do this independently —
        /// the spec's "one-time" framing means "do not depend on
        /// initialization order", not "guard with a static flag".
        /// </summary>
        private void Awake()
        {
            // Idempotent: a no-op when already 0.02f. We set it unconditionally
            // so the value is correct even if some other component nudged it
            // before our Awake ran.
            Time.fixedDeltaTime = 0.02f;
        }

        #endregion

        #region PUBLIC API

        /// <summary>
        /// Per-episode reset. Performs, in order:
        /// <list type="number">
        ///   <item>Pick a uniform-random <see cref="Transform"/> from
        ///   <c>spawnPoints</c>.</item>
        ///   <item>Teleport <c>playerRigidbody</c> to the spawn position via
        ///   <see cref="Rigidbody.position"/>, zero its
        ///   <see cref="Rigidbody.linearVelocity"/> and
        ///   <see cref="Rigidbody.angularVelocity"/>, and rotate it via
        ///   <see cref="Rigidbody.MoveRotation(Quaternion)"/>.</item>
        ///   <item>Refill ammo via <c>character.FillAmmunition(0)</c>. The
        ///   <c>0</c> is the "fill to magazine total" sentinel per
        ///   <c>Weapon.FillAmmunition</c> semantics (any non-zero amount is
        ///   added to current ammo and clamped, so passing <c>-1</c> would
        ///   actually subtract one round).</item>
        ///   <item>Reset all targets via <c>targetWaveManager.ResetWave()</c>.</item>
        /// </list>
        ///
        /// <para>Steps are independent: a misconfigured spawn-points array
        /// will skip the teleport but ammo refill and target reset still run
        /// (issue #16 acceptance criterion #3). Each step's referenced object
        /// is null-checked and the failure is logged via
        /// <see cref="Log.warn_me"/> rather than thrown.</para>
        /// </summary>
        public void ResetArea()
        {
            //
            // Step 1 + 2: pick a spawn and teleport via the rigidbody.
            //
            // The teleport uses Rigidbody.position / MoveRotation rather than
            // Transform — these are the physics-aware setters and survive
            // being called from inside FixedUpdate without confusing the
            // physics integrator. Velocity zeroing is non-negotiable: a
            // residual velocity from the previous episode would otherwise
            // bleed through the teleport (Unity does not zero it for you).
            //
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                // Skip teleport but DO continue with ammo refill + target
                // reset. Acceptance criterion #3: never throw, never abort.
                Log.warn_me($"{nameof(AgentArea)}.{nameof(ResetArea)}: spawnPoints array is null or empty — skipping teleport.");
            }
            else if (playerRigidbody == null)
            {
                // Same fall-through reasoning. The agent might still want
                // ammo + targets reset even if the rigidbody ref is broken.
                Log.warn_me($"{nameof(AgentArea)}.{nameof(ResetArea)}: playerRigidbody is null — skipping teleport.");
            }
            else
            {
                // Random.Range(int, int) is exclusive on the upper bound, so
                // [0, Length) is exactly the index space we want.
                Transform spawn = spawnPoints[Random.Range(0, spawnPoints.Length)];
                if (spawn == null)
                {
                    Log.warn_me($"{nameof(AgentArea)}.{nameof(ResetArea)}: selected spawn point is null — skipping teleport.");
                }
                else
                {
                    // Use Rigidbody.position (physics-aware) rather than
                    // playerRigidbody.transform.position. Same reasoning for
                    // MoveRotation vs Transform.rotation.
                    playerRigidbody.position = spawn.position;
                    // Unity 6 renamed Rigidbody.velocity to linearVelocity.
                    // The repo-wide migration was applied in chore PR #27
                    // (see Movement.cs / Weapon.cs / ProjectileScript.cs).
                    playerRigidbody.linearVelocity = Vector3.zero;
                    playerRigidbody.angularVelocity = Vector3.zero;
                    playerRigidbody.MoveRotation(spawn.rotation);
                }
            }

            //
            // Step 3: refill ammo.
            //
            // Defensive null checks at each link of the chain. A partially
            // wired prefab (e.g. Inventory not yet initialised) must NOT
            // throw — see acceptance criterion #3.
            //
            if (character == null)
            {
                Log.warn_me($"{nameof(AgentArea)}.{nameof(ResetArea)}: character is null — skipping ammo refill.");
            }
            else
            {
                // 0 is the documented "fill to magazine total" sentinel per
                // Weapon.FillAmmunition (`amount != 0 ? clamp(current + amount)
                // : magazineTotal`). Use Character's own FillAmmunition wrapper
                // which already null-guards on equippedWeapon, instead of
                // walking the GetInventory().GetEquipped() chain ourselves.
                character.FillAmmunition(0);
            }

            //
            // Step 4: reset targets.
            //
            // ResetWave is itself a no-op when zero targets are tracked, so
            // calling it on a still-initialising area is safe.
            //
            if (targetWaveManager == null)
            {
                Log.warn_me($"{nameof(AgentArea)}.{nameof(ResetArea)}: targetWaveManager is null — skipping target reset.");
            }
            else
            {
                targetWaveManager.ResetWave();
            }
        }

        #endregion

        #region MANUAL SMOKE TEST

        /// <summary>
        /// Manual smoke test exposed via the Inspector context menu, satisfying
        /// issue #16 acceptance criterion #2. Right-click the
        /// <see cref="AgentArea"/> component and pick this entry: the player
        /// should teleport, the equipped weapon's ammo should refill (verify
        /// via <c>Weapon.GetAmmunitionCurrent()</c> in the inspector), and all
        /// targets should come back up.
        /// </summary>
        [ContextMenu("Test: ResetArea")]
        private void ContextMenuResetArea()
        {
            ResetArea();
        }

        #endregion
    }
}
