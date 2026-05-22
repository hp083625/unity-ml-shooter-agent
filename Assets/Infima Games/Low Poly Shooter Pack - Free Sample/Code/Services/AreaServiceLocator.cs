// Copyright 2021, Infima Games. All Rights Reserved.

using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Per-area scope marker for the <see cref="ServiceLocator"/>. When present
    /// on a parent <see cref="GameObject"/>, <see cref="ServiceLocator.For(Component)"/>
    /// returns this area's private <see cref="Locator"/> instead of the global
    /// <see cref="ServiceLocator.Current"/>.
    ///
    /// <para>
    /// Per ADR-0006, parallel ML training scenes use multiple <c>TrainingArea</c>
    /// roots; without per-area scoping, every <c>IGameModeService</c> would
    /// resolve the same arbitrary <see cref="CharacterBehaviour"/> via
    /// <c>FindObjectOfType</c>, breaking isolation. This component constructs a
    /// per-area <see cref="ServiceLocator"/> on <c>Awake</c> and registers an
    /// area-scoped <see cref="IGameModeService"/> keyed off the area's own
    /// <see cref="CharacterBehaviour"/> (resolved via
    /// <see cref="Component.GetComponentInChildren{T}()"/>).
    /// </para>
    ///
    /// <para>
    /// <see cref="DefaultExecutionOrderAttribute"/> with <c>-1000</c> is
    /// **required**: without it, Infima's <c>Weapon.Awake</c>,
    /// <c>Movement.Awake</c>, <c>CameraLook.Awake</c>, and
    /// <c>CharacterAnimationEventHandler.Awake</c> can race this component and
    /// fall through to <see cref="ServiceLocator.Current"/>.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class AreaServiceLocator : MonoBehaviour
    {
        /// <summary>
        /// The private <see cref="ServiceLocator"/> owned by this area. Populated
        /// in <see cref="Awake"/>. Returned by <see cref="ServiceLocator.For(Component)"/>
        /// for any descendant component.
        /// </summary>
        public ServiceLocator Locator { get; private set; }

        /// <summary>
        /// Constructs the per-area <see cref="ServiceLocator"/> and registers
        /// the area-scoped <see cref="IGameModeService"/> with the area's own
        /// <see cref="CharacterBehaviour"/>.
        /// </summary>
        private void Awake()
        {
            //Construct the area-private locator.
            Locator = new ServiceLocator();

            //Resolve the area's own player character. GetComponentInChildren
            //is bounded to this transform's hierarchy, so each area only sees
            //its own Character (rather than the global FindObjectOfType which
            //returns an arbitrary one across all areas).
            CharacterBehaviour character = GetComponentInChildren<CharacterBehaviour>();

            //Register the area-scoped IGameModeService keyed off this area's
            //Character. Uses the GameModeService(CharacterBehaviour) ctor added
            //in issue #6 (PR #28) — that PR must merge with or before this one.
            Locator.Register<IGameModeService>(new GameModeService(character));
        }
    }
}
