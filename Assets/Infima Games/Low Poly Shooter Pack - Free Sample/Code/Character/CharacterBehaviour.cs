// Copyright 2021, Infima Games. All Rights Reserved.

using UnityEngine;

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Character Abstract Behaviour.
    /// </summary>
    public abstract class CharacterBehaviour : MonoBehaviour
    {
        #region UNITY

        /// <summary>
        /// Awake.
        /// </summary>
        protected virtual void Awake(){}

        /// <summary>
        /// Start.
        /// </summary>
        protected virtual void Start(){}

        /// <summary>
        /// Update.
        /// </summary>
        protected virtual void Update(){}

        /// <summary>
        /// Late Update.
        /// </summary>
        protected virtual void LateUpdate(){}

        #endregion
        
        #region GETTERS

        /// <summary>
        /// Returns the player character's main camera.
        /// </summary>
        public abstract Camera GetCameraWorld();
        
        /// <summary>
        /// Returns a reference to the Inventory component.
        /// </summary>
        public abstract InventoryBehaviour GetInventory();

        /// <summary>
        /// Returns true if the Crosshair should be visible.
        /// </summary>
        public abstract bool IsCrosshairVisible();
        /// <summary>
        /// Returns true if the character is running.
        /// </summary>
        public abstract bool IsRunning();
        
        /// <summary>
        /// Returns true if the character is aiming.
        /// </summary>
        public abstract bool IsAiming();
        /// <summary>
        /// Returns true if the game cursor is locked.
        /// </summary>
        public abstract bool IsCursorLocked();

        /// <summary>
        /// Returns true if the tutorial text should be visible on the screen.
        /// </summary>
        public abstract bool IsTutorialTextVisible();

        /// <summary>
        /// Returns the Movement Input.
        /// </summary>
        public abstract Vector2 GetInputMovement();
        /// <summary>
        /// Returns the Look Input.
        /// </summary>
        public abstract Vector2 GetInputLook();
        
        #endregion

        #region ANIMATION

        /// <summary>
        /// Ejects a casing from the equipped weapon.
        /// </summary>
        public abstract void EjectCasing();
        /// <summary>
        /// Fills the character's equipped weapon's ammunition by a certain amount, or fully if set to -1.
        /// </summary>
        public abstract void FillAmmunition(int amount);

        /// <summary>
        /// Sets the equipped weapon's magazine to be active or inactive!
        /// </summary>
        public abstract void SetActiveMagazine(int active);
        
        /// <summary>
        /// Reload Animation Ended.
        /// </summary>
        public abstract void AnimationEndedReload();

        /// <summary>
        /// Inspect Animation Ended.
        /// </summary>
        public abstract void AnimationEndedInspect();
        /// <summary>
        /// Holster Animation Ended.
        /// </summary>
        public abstract void AnimationEndedHolster();

        #endregion

        #region AI INPUT

        /// <summary>
        /// Sets the synthetic look axis fed by an AI agent.
        /// Per ADR-0005: the agent supplies (yaw_delta, pitch_delta) already scaled to degrees/decision-step.
        /// </summary>
        public abstract void SetAxisLook(Vector2 value);

        /// <summary>
        /// Sets the synthetic movement axis fed by an AI agent.
        /// Per ADR-0005: passed straight through; Movement applies its own speedWalking scaling.
        /// </summary>
        public abstract void SetAxisMovement(Vector2 value);

        /// <summary>
        /// Sets whether the AI agent is holding the fire button.
        /// </summary>
        public abstract void SetHoldingFire(bool held);

        /// <summary>
        /// Toggles AI input mode. When true, Input System callbacks early-return and the
        /// Set* methods above are the sole source of look/move/fire intent. Per ADR-0005.
        /// </summary>
        public abstract void SetUseAIInput(bool value);

        #endregion
    }
}