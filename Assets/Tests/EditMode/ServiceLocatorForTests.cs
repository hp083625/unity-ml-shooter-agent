// Copyright 2026, hp083625. All Rights Reserved.

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <c>ServiceLocator.For(MonoBehaviour)</c>, the scoped lookup added per
    /// ADR-0006. Because <c>Tests.EditMode.asmdef</c> does not reference <c>Assembly-CSharp</c>
    /// (Unity 6 limitation, see issue #26), all production types are accessed via reflection.
    /// </summary>
    /// <remarks>
    /// Reminder: <c>AddComponent</c> does NOT invoke <c>MonoBehaviour.Awake</c> outside Play mode.
    /// Each test that adds an <c>AreaServiceLocator</c> manually invokes its <c>Awake</c> via
    /// reflection so the private <c>Locator</c> instance is populated before assertions run.
    /// </remarks>
    public class ServiceLocatorForTests
    {
        private const string AssemblyName = "Assembly-CSharp";
        private const string ServiceLocatorTypeName = "InfimaGames.LowPolyShooterPack.ServiceLocator";
        private const string AreaServiceLocatorTypeName = "InfimaGames.LowPolyShooterPack.AreaServiceLocator";

        private Type serviceLocatorType;
        private Type areaServiceLocatorType;
        private MethodInfo forMethod;
        private PropertyInfo currentProp;
        private PropertyInfo locatorProp;
        private MethodInfo initializeMethod;

        private GameObject createdRoot;

        [SetUp]
        public void SetUp()
        {
            serviceLocatorType = ResolveType(ServiceLocatorTypeName);
            areaServiceLocatorType = ResolveType(AreaServiceLocatorTypeName);

            Assert.IsNotNull(serviceLocatorType, $"Could not locate {ServiceLocatorTypeName} via reflection.");
            Assert.IsNotNull(areaServiceLocatorType, $"Could not locate {AreaServiceLocatorTypeName} via reflection.");

            forMethod = serviceLocatorType.GetMethod(
                "For",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(MonoBehaviour) },
                modifiers: null);
            Assert.IsNotNull(forMethod, "ServiceLocator.For(MonoBehaviour) not found.");

            currentProp = serviceLocatorType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(currentProp, "ServiceLocator.Current not found.");

            locatorProp = areaServiceLocatorType.GetProperty("Locator", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(locatorProp, "AreaServiceLocator.Locator not found.");

            initializeMethod = serviceLocatorType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(initializeMethod, "ServiceLocator.Initialize() not found.");

            //Make sure ServiceLocator.Current is populated for the fallback assertions.
            //Bootstraper normally does this via [RuntimeInitializeOnLoadMethod], but that does
            //not fire in EditMode unit tests, so we initialize defensively here.
            if (currentProp.GetValue(null) == null)
                initializeMethod.Invoke(null, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (createdRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(createdRoot);
                createdRoot = null;
            }
        }

        [Test]
        public void For_NoAreaServiceLocator_FallsBackToCurrent()
        {
            createdRoot = new GameObject("LonelyCaller");
            var caller = createdRoot.AddComponent<DummyCaller>();

            object result = forMethod.Invoke(null, new object[] { caller });
            object current = currentProp.GetValue(null);

            Assert.IsNotNull(current, "ServiceLocator.Current was unexpectedly null.");
            Assert.AreSame(current, result,
                "Expected fallback to ServiceLocator.Current when no AreaServiceLocator is on the parent chain.");
        }

        [Test]
        public void For_DirectParentHasAreaServiceLocator_ReturnsItsLocator()
        {
            createdRoot = new GameObject("AreaRoot");
            Component areaComponent = createdRoot.AddComponent(areaServiceLocatorType);
            InvokeAwake(areaComponent);

            var child = new GameObject("Child");
            child.transform.SetParent(createdRoot.transform);
            var caller = child.AddComponent<DummyCaller>();

            object result = forMethod.Invoke(null, new object[] { caller });
            object expected = locatorProp.GetValue(areaComponent);

            Assert.IsNotNull(expected, "AreaServiceLocator.Locator should be populated after Awake.");
            Assert.AreSame(expected, result,
                "Expected ServiceLocator.For to return the direct parent's AreaServiceLocator.Locator.");
        }

        [Test]
        public void For_ThreeLevelsDeep_StillFindsAreaServiceLocator()
        {
            createdRoot = new GameObject("AreaRoot");
            Component areaComponent = createdRoot.AddComponent(areaServiceLocatorType);
            InvokeAwake(areaComponent);

            var lvl1 = new GameObject("Lvl1"); lvl1.transform.SetParent(createdRoot.transform);
            var lvl2 = new GameObject("Lvl2"); lvl2.transform.SetParent(lvl1.transform);
            var lvl3 = new GameObject("Lvl3"); lvl3.transform.SetParent(lvl2.transform);
            var caller = lvl3.AddComponent<DummyCaller>();

            object result = forMethod.Invoke(null, new object[] { caller });
            object expected = locatorProp.GetValue(areaComponent);

            Assert.IsNotNull(expected, "AreaServiceLocator.Locator should be populated after Awake.");
            Assert.AreSame(expected, result,
                "Expected ServiceLocator.For to find the AreaServiceLocator three levels up.");
        }

        [Test]
        public void For_NullCaller_FallsBackToCurrentWithoutThrowing()
        {
            object result = null;
            Assert.DoesNotThrow(() =>
            {
                result = forMethod.Invoke(null, new object[] { null });
            }, "ServiceLocator.For(null) must not throw.");

            object current = currentProp.GetValue(null);
            Assert.IsNotNull(current, "ServiceLocator.Current was unexpectedly null.");
            Assert.AreSame(current, result,
                "Expected fallback to ServiceLocator.Current when caller is null.");
        }

        // --- helpers ---

        private static Type ResolveType(string fullName)
        {
            //Try the assembly-qualified path first; fall back to scanning loaded assemblies if Unity
            //hasn't surfaced Assembly-CSharp under that exact display name.
            Type t = Type.GetType($"{fullName}, {AssemblyName}");
            if (t != null)
                return t;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName, throwOnError: false);
                if (t != null)
                    return t;
            }
            return null;
        }

        private static void InvokeAwake(Component component)
        {
            //EditMode does not call MonoBehaviour.Awake automatically — invoke the private method
            //via reflection so AreaServiceLocator.Locator gets initialized before the test runs.
            MethodInfo awake = component.GetType().GetMethod(
                "Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(awake, "AreaServiceLocator.Awake not found via reflection.");
            awake.Invoke(component, null);
        }

        /// <summary>
        /// Minimal MonoBehaviour used as the <c>caller</c> argument to
        /// <c>ServiceLocator.For(MonoBehaviour)</c>. Lives in the test assembly so it never
        /// accidentally has an <c>AreaServiceLocator</c> sibling on its own GameObject in a way
        /// the production code would treat as the answer.
        /// </summary>
        private sealed class DummyCaller : MonoBehaviour { }
    }
}
