// Copyright 2021, Infima Games. All Rights Reserved.

namespace InfimaGames.LowPolyShooterPack
{
    /// <summary>
    /// Game Mode Service.
    /// </summary>
    public class GameModeService : IGameModeService
    {
        #region FIELDS

        /// <summary>
        /// The Player Character.
        /// </summary>
        private CharacterBehaviour playerCharacter;

        #endregion

        #region CONSTRUCTORS

        /// <summary>
        /// Default constructor. Player character is resolved lazily via
        /// <see cref="UnityEngine.Object.FindObjectOfType{T}()"/> on first
        /// call to <see cref="GetPlayerCharacter"/>. Used by the global
        /// <c>Bootstraper</c> flow.
        /// </summary>
        public GameModeService()
        {
        }

        /// <summary>
        /// Scoped constructor that injects an explicit player character.
        /// Per ADR-0006, scoped service locators (parallel training areas)
        /// must inject their own per-area player so that <see cref="GetPlayerCharacter"/>
        /// does not <c>FindObjectOfType</c> across all areas.
        /// </summary>
        /// <param name="playerCharacter">The player character for this scope. May be null.</param>
        public GameModeService(CharacterBehaviour playerCharacter)
        {
            this.playerCharacter = playerCharacter;
        }

        #endregion

        #region FUNCTIONS

        public CharacterBehaviour GetPlayerCharacter()
        {
            //Make sure we have a player character that is good to go!
            if (playerCharacter == null)
                playerCharacter = UnityEngine.Object.FindObjectOfType<CharacterBehaviour>();

            //Return.
            return playerCharacter;
        }

        #endregion
    }
}