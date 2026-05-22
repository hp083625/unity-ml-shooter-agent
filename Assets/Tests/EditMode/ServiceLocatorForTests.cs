// Reflection-based EditMode tests for ServiceLocator.For(Component) and
// AreaServiceLocator.
//
// We resolve types via reflection because Tests.EditMode.asmdef does NOT
// reference Assembly-CSharp (Unity 6 limitation, see issue #26). A direct
// `using InfimaGames.LowPolyShooterPack;` would not compile here.
//
// EditMode gotcha: AddComponent does NOT call Awake outside Play mode. We
// invoke Awake() reflectively after AddComponent so AreaServiceLocator.Locator
// is populated before assertions.

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class ServiceLocatorForTests
    {
        private const string AssemblyName = "Assembly-CSharp";
        private const string Namespace = "InfimaGames.LowPolyShooterPack";
        private const string ServiceLocatorFullName = Namespace + ".ServiceLocator";
        private const string AreaServiceLocatorFullName = Namespace + ".AreaServiceLocator";

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

        private static Type GetServiceLocatorType()
        {
            Type type = GetGameAssembly().GetType(ServiceLocatorFullName);
            Assert.IsNotNull(
                type,
                $"Type '{ServiceLocatorFullName}' not found in '{AssemblyName}'."
            );
            return type;
        }

        private static Type GetAreaServiceLocatorType()
        {
            Type type = GetGameAssembly().GetType(AreaServiceLocatorFullName);
            Assert.IsNotNull(
                type,
                $"Type '{AreaServiceLocatorFullName}' not found in '{AssemblyName}'."
            );
            return type;
        }

        /// <summary>
        /// Invokes the private <c>Awake</c> method on a freshly-added MonoBehaviour
        /// because <see cref="GameObject.AddComponent"/> does not run Awake in
        /// EditMode tests outside Play mode.
        /// </summary>
        private static void InvokeAwake(MonoBehaviour mb)
        {
            MethodInfo awake = mb.GetType().GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            Assert.IsNotNull(awake, $"{mb.GetType().Name}.Awake not found via reflection.");
            awake.Invoke(mb, null);
        }

        /// <summary>
        /// Calls <c>ServiceLocator.For(Component)</c> via reflection.
        /// </summary>
        private static object InvokeFor(Component caller)
        {
            Type slType = GetServiceLocatorType();
            MethodInfo forMethod = slType.GetMethod(
                "For",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Component) },
                modifiers: null
            );
            Assert.IsNotNull(
                forMethod,
                "ServiceLocator.For(Component) static method not found. " +
                "It must take a Component (not MonoBehaviour) per issue #5."
            );
            return forMethod.Invoke(null, new object[] { caller });
        }

        /// <summary>
        /// Reads the static <c>ServiceLocator.Current</c> property via reflection.
        /// </summary>
        private static object GetCurrent()
        {
            Type slType = GetServiceLocatorType();
            PropertyInfo current = slType.GetProperty(
                "Current",
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.IsNotNull(current, "ServiceLocator.Current static property not found.");
            return current.GetValue(null);
        }

        /// <summary>
        /// Reads the <c>AreaServiceLocator.Locator</c> instance property via reflection.
        /// </summary>
        private static object GetAreaLocator(MonoBehaviour area)
        {
            PropertyInfo locator = area.GetType().GetProperty(
                "Locator",
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.IsNotNull(locator, "AreaServiceLocator.Locator property not found.");
            return locator.GetValue(area);
        }

        // ---------------- Surface tests (existence / shape) ----------------

        [Test]
        public void ServiceLocator_For_TakesComponent_NotMonoBehaviour()
        {
            // Issue #5 spec: "static ServiceLocator For(Component c)".
            // The previous attempt was rejected for using MonoBehaviour.
            Type slType = GetServiceLocatorType();
            MethodInfo forComponent = slType.GetMethod(
                "For",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Component) },
                modifiers: null
            );
            Assert.IsNotNull(
                forComponent,
                "ServiceLocator.For(Component) overload must exist."
            );
            Assert.AreEqual(
                slType,
                forComponent.ReturnType,
                "ServiceLocator.For(Component) must return ServiceLocator."
            );
        }

        [Test]
        public void AreaServiceLocator_HasDefaultExecutionOrderMinus1000()
        {
            Type areaType = GetAreaServiceLocatorType();
            DefaultExecutionOrder attr = (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                areaType,
                typeof(DefaultExecutionOrder)
            );
            Assert.IsNotNull(
                attr,
                "AreaServiceLocator must carry [DefaultExecutionOrder(-1000)]."
            );
            Assert.AreEqual(
                -1000,
                attr.order,
                "AreaServiceLocator [DefaultExecutionOrder] must be -1000 to win the Awake race."
            );
        }

        [Test]
        public void AreaServiceLocator_IsMonoBehaviour()
        {
            Type areaType = GetAreaServiceLocatorType();
            Assert.IsTrue(
                typeof(MonoBehaviour).IsAssignableFrom(areaType),
                "AreaServiceLocator must derive from MonoBehaviour."
            );
        }

        [Test]
        public void AreaServiceLocator_HasLocatorProperty()
        {
            Type areaType = GetAreaServiceLocatorType();
            PropertyInfo locator = areaType.GetProperty(
                "Locator",
                BindingFlags.Public | BindingFlags.Instance
            );
            Assert.IsNotNull(locator, "AreaServiceLocator.Locator must be public-readable.");
            Assert.AreEqual(
                GetServiceLocatorType(),
                locator.PropertyType,
                "AreaServiceLocator.Locator must be typed as ServiceLocator."
            );
            Assert.IsTrue(locator.CanRead, "AreaServiceLocator.Locator must have a getter.");
        }

        // ---------------- Behavioral tests ----------------

        [Test]
        public void For_NullCaller_ReturnsCurrent()
        {
            object current = GetCurrent();
            object result = InvokeFor(null);

            // Note: Current may legitimately be null in EditMode (Bootstraper runs
            // BeforeSceneLoad, not in EditMode tests). We assert that For(null)
            // behaves identically to Current — both null or same reference.
            Assert.AreSame(
                current,
                result,
                "ServiceLocator.For(null) must fall through to ServiceLocator.Current."
            );
        }

        [Test]
        public void For_OrphanComponent_ReturnsCurrent()
        {
            // Acceptance criterion: For(orphan) returns Current.
            // An "orphan" here is a component on a GameObject with no parent
            // and no AreaServiceLocator anywhere.
            var orphanGo = new GameObject("Orphan");
            try
            {
                // Use a stock Unity component (Camera) so we don't leak any
                // domain types into the test setup.
                Component caller = orphanGo.AddComponent<Camera>();

                object current = GetCurrent();
                object result = InvokeFor(caller);

                Assert.AreSame(
                    current,
                    result,
                    "ServiceLocator.For(orphan) must fall through to ServiceLocator.Current."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(orphanGo);
            }
        }

        [Test]
        public void For_DirectChildOfArea_ReturnsAreaLocator()
        {
            // Acceptance criterion: For(child of AreaServiceLocator) returns
            // the area's locator.
            var areaGo = new GameObject("Area");
            var childGo = new GameObject("Child");
            try
            {
                Type areaType = GetAreaServiceLocatorType();
                MonoBehaviour area = (MonoBehaviour)areaGo.AddComponent(areaType);

                // EditMode: Awake doesn't run automatically. Invoke it.
                // GetComponentInChildren<CharacterBehaviour>() will return null
                // (no CharacterBehaviour exists on areaGo or its children), and
                // the GameModeService(null) ctor must tolerate that — see
                // GameModeServiceTests.CharacterBehaviourConstructor_WithNull_DoesNotThrow.
                InvokeAwake(area);

                childGo.transform.SetParent(areaGo.transform);
                Component caller = childGo.AddComponent<Camera>();

                object expected = GetAreaLocator(area);
                Assert.IsNotNull(
                    expected,
                    "AreaServiceLocator.Awake should have populated Locator."
                );

                object result = InvokeFor(caller);
                Assert.AreSame(
                    expected,
                    result,
                    "ServiceLocator.For(child) must return the parent AreaServiceLocator's Locator."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(childGo);
                UnityEngine.Object.DestroyImmediate(areaGo);
            }
        }

        [Test]
        public void For_DeepDescendantOfArea_ReturnsAreaLocator()
        {
            // Three-level walk: Area -> Mid -> Leaf. For(Leaf-component) must
            // walk past Mid and find Area's locator.
            var areaGo = new GameObject("Area");
            var midGo = new GameObject("Mid");
            var leafGo = new GameObject("Leaf");
            try
            {
                Type areaType = GetAreaServiceLocatorType();
                MonoBehaviour area = (MonoBehaviour)areaGo.AddComponent(areaType);
                InvokeAwake(area);

                midGo.transform.SetParent(areaGo.transform);
                leafGo.transform.SetParent(midGo.transform);
                Component caller = leafGo.AddComponent<Camera>();

                object expected = GetAreaLocator(area);
                Assert.IsNotNull(expected, "Area locator should be populated.");

                object result = InvokeFor(caller);
                Assert.AreSame(
                    expected,
                    result,
                    "ServiceLocator.For must walk 3 levels up to find AreaServiceLocator."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(leafGo);
                UnityEngine.Object.DestroyImmediate(midGo);
                UnityEngine.Object.DestroyImmediate(areaGo);
            }
        }

        [Test]
        public void For_ComponentOnAreaItself_ReturnsCurrent()
        {
            // Spec: "walks c.transform.parent upward" — an AreaServiceLocator
            // on the caller's own GameObject is NOT a parent and so should not
            // be picked up. This boundary case ensures we don't over-resolve.
            var areaGo = new GameObject("AreaSelf");
            try
            {
                Type areaType = GetAreaServiceLocatorType();
                MonoBehaviour area = (MonoBehaviour)areaGo.AddComponent(areaType);
                InvokeAwake(area);

                Component caller = areaGo.AddComponent<Camera>();

                object current = GetCurrent();
                object result = InvokeFor(caller);

                Assert.AreSame(
                    current,
                    result,
                    "ServiceLocator.For(component on AreaServiceLocator itself) " +
                    "should fall through to Current — only PARENTS are walked."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(areaGo);
            }
        }

        [Test]
        public void For_NestedAreas_ReturnsNearestAncestor()
        {
            // Outer area is ignored when an inner area exists between it and
            // the leaf. Confirms we stop at the FIRST AreaServiceLocator we
            // hit while walking up (nearest wins).
            var outerGo = new GameObject("Outer");
            var innerGo = new GameObject("Inner");
            var leafGo = new GameObject("Leaf");
            try
            {
                Type areaType = GetAreaServiceLocatorType();
                MonoBehaviour outer = (MonoBehaviour)outerGo.AddComponent(areaType);
                MonoBehaviour inner = (MonoBehaviour)innerGo.AddComponent(areaType);
                InvokeAwake(outer);
                InvokeAwake(inner);

                innerGo.transform.SetParent(outerGo.transform);
                leafGo.transform.SetParent(innerGo.transform);
                Component caller = leafGo.AddComponent<Camera>();

                object outerLocator = GetAreaLocator(outer);
                object innerLocator = GetAreaLocator(inner);
                Assert.AreNotSame(
                    outerLocator,
                    innerLocator,
                    "Each AreaServiceLocator must own a distinct ServiceLocator instance."
                );

                object result = InvokeFor(caller);
                Assert.AreSame(
                    innerLocator,
                    result,
                    "ServiceLocator.For must return the NEAREST AreaServiceLocator " +
                    "(inner), not an outer ancestor."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(leafGo);
                UnityEngine.Object.DestroyImmediate(innerGo);
                UnityEngine.Object.DestroyImmediate(outerGo);
            }
        }
    }
}
