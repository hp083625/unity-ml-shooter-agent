// Copyright 2026, hp083625. All Rights Reserved.

using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Per-area service locator host. Lives at the root of a training-area prefab and exposes a
    /// private <see cref="ServiceLocator"/> instance scoped to that area's children.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per ADR-0006, when many training areas coexist in a single scene, every script that needs
    /// "the player" must resolve to the player belonging to its own area — not an arbitrary one
    /// returned by <c>FindObjectOfType&lt;CharacterBehaviour&gt;</c>. Call sites that want
    /// area-scoped services use <see cref="ServiceLocator.For(MonoBehaviour)"/>, which walks the
    /// caller's transform chain looking for one of these components.
    /// </para>
    /// <para>
    /// The <see cref="DefaultExecutionOrderAttribute"/> with a strongly negative value ensures
    /// this <c>Awake</c> runs before any sibling/child component that might call
    /// <c>ServiceLocator.For(this)</c> from its own <c>Awake</c>.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    public class AreaServiceLocator : MonoBehaviour
    {
        /// <summary>
        /// The area-scoped service locator. Populated in <see cref="Awake"/>; null beforehand.
        /// </summary>
        public ServiceLocator Locator { get; private set; }

        private void Awake()
        {
            //Spin up an empty per-area locator. Service registration is deferred to follow-on issues
            //(see #6 / #9) so this PR contains only the lookup plumbing.
            Locator = new ServiceLocator();

            // TODO(#9): register area-scoped IGameModeService here.
        }
    }
}
