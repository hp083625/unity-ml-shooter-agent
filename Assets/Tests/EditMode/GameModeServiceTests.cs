// Reflection-based EditMode tests for GameModeService.
//
// We resolve types via reflection because Tests.EditMode.asmdef does NOT
// reference Assembly-CSharp (Unity 6 limitation, see issue #26). A direct
// `using InfimaGames.LowPolyShooterPack;` would not compile here.
//
// Out of scope for EditMode: verifying that the parameterless constructor's
// lazy FindObjectOfType<CharacterBehaviour>() actually returns the player
// from the demo scene. That requires the scene to be loaded and is best
// covered by a PlayMode or scene-driven test.

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Tests.EditMode
{
    public class GameModeServiceTests
    {
        private const string AssemblyName = "Assembly-CSharp";
        private const string Namespace = "InfimaGames.LowPolyShooterPack";
        private const string GameModeServiceFullName = Namespace + ".GameModeService";
        private const string GameModeServiceInterfaceFullName = Namespace + ".IGameModeService";
        private const string CharacterBehaviourFullName = Namespace + ".CharacterBehaviour";

        private static Assembly GetGameAssembly()
        {
            Assembly assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == AssemblyName);

            Assert.IsNotNull(
                assembly,
                $"Could not locate '{AssemblyName}' in the current AppDomain. " +
                "Has the project failed to compile?"
            );
            return assembly;
        }

        private static Type GetGameModeServiceType()
        {
            Type type = GetGameAssembly().GetType(GameModeServiceFullName);
            Assert.IsNotNull(
                type,
                $"Type '{GameModeServiceFullName}' not found in '{AssemblyName}'."
            );
            return type;
        }

        private static Type GetCharacterBehaviourType()
        {
            Type type = GetGameAssembly().GetType(CharacterBehaviourFullName);
            Assert.IsNotNull(
                type,
                $"Type '{CharacterBehaviourFullName}' not found in '{AssemblyName}'."
            );
            return type;
        }

        [Test]
        public void GameModeService_TypeExistsInAssemblyCSharp()
        {
            Type type = GetGameModeServiceType();
            Assert.IsTrue(type.IsClass, "GameModeService should be a class.");
            Assert.IsFalse(type.IsAbstract, "GameModeService should not be abstract.");
        }

        [Test]
        public void GameModeService_ImplementsIGameModeService()
        {
            Type type = GetGameModeServiceType();
            Type interfaceType = GetGameAssembly().GetType(GameModeServiceInterfaceFullName);

            Assert.IsNotNull(
                interfaceType,
                $"Interface '{GameModeServiceInterfaceFullName}' not found."
            );
            Assert.IsTrue(
                interfaceType.IsAssignableFrom(type),
                "GameModeService should implement IGameModeService."
            );
        }

        [Test]
        public void GameModeService_HasParameterlessConstructor()
        {
            Type type = GetGameModeServiceType();
            ConstructorInfo ctor = type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );

            Assert.IsNotNull(
                ctor,
                "GameModeService must keep a public parameterless constructor for the Bootstraper flow."
            );
        }

        [Test]
        public void GameModeService_HasCharacterBehaviourConstructor()
        {
            Type type = GetGameModeServiceType();
            Type characterBehaviourType = GetCharacterBehaviourType();
            ConstructorInfo ctor = type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { characterBehaviourType },
                modifiers: null
            );

            Assert.IsNotNull(
                ctor,
                "GameModeService must expose a public constructor accepting a CharacterBehaviour " +
                "for scoped service locators (ADR-0006)."
            );
        }

        [Test]
        public void DefaultConstructor_DoesNotThrow()
        {
            Type type = GetGameModeServiceType();
            Assert.DoesNotThrow(
                () =>
                {
                    object instance = Activator.CreateInstance(type);
                    Assert.IsNotNull(instance, "Default constructor should produce an instance.");
                },
                "Default constructor must not throw."
            );
        }

        [Test]
        public void CharacterBehaviourConstructor_WithNull_DoesNotThrow()
        {
            Type type = GetGameModeServiceType();
            Type characterBehaviourType = GetCharacterBehaviourType();
            ConstructorInfo ctor = type.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { characterBehaviourType },
                modifiers: null
            );

            Assert.IsNotNull(ctor, "CharacterBehaviour constructor must exist.");
            Assert.DoesNotThrow(
                () =>
                {
                    object instance = ctor.Invoke(new object[] { null });
                    Assert.IsNotNull(
                        instance,
                        "Constructor invocation should produce an instance even with a null player."
                    );
                },
                "GameModeService(null) must not throw."
            );
        }

        [Test]
        public void GetPlayerCharacter_ReturnsCharacterBehaviourOrDerived()
        {
            Type type = GetGameModeServiceType();
            Type characterBehaviourType = GetCharacterBehaviourType();
            MethodInfo method = type.GetMethod(
                "GetPlayerCharacter",
                BindingFlags.Public | BindingFlags.Instance
            );

            Assert.IsNotNull(method, "GameModeService.GetPlayerCharacter() must exist.");
            Assert.IsTrue(
                characterBehaviourType.IsAssignableFrom(method.ReturnType),
                $"GetPlayerCharacter() should return CharacterBehaviour (or derived); " +
                $"actual return type was '{method.ReturnType.FullName}'."
            );
        }
    }
}
