// EditMode tests for the AI-input setter surface added by ADR-0005 / issue #7.
//
// We verify the four new abstract methods exist on CharacterBehaviour with the
// expected names, parameter types, and void return type. Pure reflection is used
// (resolving the type via assembly-qualified name) so this test assembly does
// not need to reference Assembly-CSharp at the asmdef level.
//
// The concrete overrides land in issue #8; this test only proves the abstract
// surface is well-formed.

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class CharacterBehaviourSettersTest
    {
        // Resolve CharacterBehaviour from Assembly-CSharp at runtime — avoids an
        // asmdef-level reference that would force editing Tests.EditMode.asmdef.
        private static Type ResolveCharacterBehaviour()
        {
            const string typeName = "InfimaGames.LowPolyShooterPack.CharacterBehaviour, Assembly-CSharp";
            var t = Type.GetType(typeName, throwOnError: false);
            Assert.IsNotNull(t, $"Could not resolve type '{typeName}'. Did Assembly-CSharp compile?");
            return t;
        }

        private static MethodInfo GetDeclaredMethod(Type owner, string name)
        {
            // BindingFlags.DeclaredOnly so we don't accidentally pick up a base-class method
            // and miss a real declaration on CharacterBehaviour.
            return owner.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        }

        [Test]
        public void CharacterBehaviour_TypeIsAbstract()
        {
            var t = ResolveCharacterBehaviour();
            Assert.IsTrue(t.IsAbstract, "CharacterBehaviour must remain abstract.");
        }

        [Test]
        public void SetAxisLook_AbstractMethodExistsWithVector2Parameter()
        {
            var t = ResolveCharacterBehaviour();
            var m = GetDeclaredMethod(t, "SetAxisLook");

            Assert.IsNotNull(m, "SetAxisLook must be declared on CharacterBehaviour.");
            Assert.IsTrue(m.IsAbstract, "SetAxisLook must be abstract.");
            Assert.IsTrue(m.IsPublic, "SetAxisLook must be public.");
            Assert.AreEqual(typeof(void), m.ReturnType, "SetAxisLook must return void.");

            var parameters = m.GetParameters();
            Assert.AreEqual(1, parameters.Length, "SetAxisLook must take exactly one parameter.");
            Assert.AreEqual(typeof(Vector2), parameters[0].ParameterType,
                "SetAxisLook parameter must be UnityEngine.Vector2.");
        }

        [Test]
        public void SetAxisMovement_AbstractMethodExistsWithVector2Parameter()
        {
            var t = ResolveCharacterBehaviour();
            var m = GetDeclaredMethod(t, "SetAxisMovement");

            Assert.IsNotNull(m, "SetAxisMovement must be declared on CharacterBehaviour.");
            Assert.IsTrue(m.IsAbstract, "SetAxisMovement must be abstract.");
            Assert.IsTrue(m.IsPublic, "SetAxisMovement must be public.");
            Assert.AreEqual(typeof(void), m.ReturnType, "SetAxisMovement must return void.");

            var parameters = m.GetParameters();
            Assert.AreEqual(1, parameters.Length, "SetAxisMovement must take exactly one parameter.");
            Assert.AreEqual(typeof(Vector2), parameters[0].ParameterType,
                "SetAxisMovement parameter must be UnityEngine.Vector2.");
        }

        [Test]
        public void SetHoldingFire_AbstractMethodExistsWithBoolParameter()
        {
            var t = ResolveCharacterBehaviour();
            var m = GetDeclaredMethod(t, "SetHoldingFire");

            Assert.IsNotNull(m, "SetHoldingFire must be declared on CharacterBehaviour.");
            Assert.IsTrue(m.IsAbstract, "SetHoldingFire must be abstract.");
            Assert.IsTrue(m.IsPublic, "SetHoldingFire must be public.");
            Assert.AreEqual(typeof(void), m.ReturnType, "SetHoldingFire must return void.");

            var parameters = m.GetParameters();
            Assert.AreEqual(1, parameters.Length, "SetHoldingFire must take exactly one parameter.");
            Assert.AreEqual(typeof(bool), parameters[0].ParameterType,
                "SetHoldingFire parameter must be System.Boolean.");
        }

        [Test]
        public void SetUseAIInput_AbstractMethodExistsWithBoolParameter()
        {
            var t = ResolveCharacterBehaviour();
            var m = GetDeclaredMethod(t, "SetUseAIInput");

            Assert.IsNotNull(m, "SetUseAIInput must be declared on CharacterBehaviour.");
            Assert.IsTrue(m.IsAbstract, "SetUseAIInput must be abstract.");
            Assert.IsTrue(m.IsPublic, "SetUseAIInput must be public.");
            Assert.AreEqual(typeof(void), m.ReturnType, "SetUseAIInput must return void.");

            var parameters = m.GetParameters();
            Assert.AreEqual(1, parameters.Length, "SetUseAIInput must take exactly one parameter.");
            Assert.AreEqual(typeof(bool), parameters[0].ParameterType,
                "SetUseAIInput parameter must be System.Boolean.");
        }
    }
}
