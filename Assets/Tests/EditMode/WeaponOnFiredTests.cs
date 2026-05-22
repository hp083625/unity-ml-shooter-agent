using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for the Weapon.OnFired event added in issue #10.
    ///
    /// We assert structural and dispatch behavior in EditMode without needing
    /// a fully-wired weapon scene (Fire() invocation requires animator,
    /// audioSource, magazine, projectile prefab, muzzle socket, and player
    /// camera — all configured on the prefab). Full Fire()-driven coverage
    /// will land in a PlayMode integration test when the agent + bridge are
    /// wired in Tier 4.
    /// </summary>
    public class WeaponOnFiredTests
    {
        private static Type ResolveWeaponType()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                .FirstOrDefault(t => t.FullName == "InfimaGames.LowPolyShooterPack.Weapon");
        }

        [Test]
        public void OnFired_EventExists_WithCorrectSignature()
        {
            var weaponType = ResolveWeaponType();
            Assert.IsNotNull(weaponType, "Weapon type must exist in Assembly-CSharp");

            var ev = weaponType.GetEvent("OnFired");
            Assert.IsNotNull(ev, "Weapon.OnFired event must exist");
            Assert.AreEqual(typeof(Action<RaycastHit?, bool>), ev.EventHandlerType,
                "OnFired must be Action<RaycastHit?, bool> per issue #10");
            Assert.IsTrue(ev.AddMethod.IsPublic, "OnFired must be publicly subscribable");
        }

        [Test]
        public void OnFired_BackingFieldAccessible_DispatchesToHandlers()
        {
            // Test the dispatch contract using reflection on the event's backing field.
            // We can't call Fire() directly in EditMode (it requires a fully-wired
            // weapon prefab), but we CAN construct a Weapon GameObject, subscribe to
            // OnFired, raise the event via reflection, and verify the dispatch shape
            // matches what subscribers will see in production.
            var weaponType = ResolveWeaponType();
            Assert.IsNotNull(weaponType);

            // Weapon : MonoBehaviour, so we need a GameObject host.
            var go = new GameObject("WeaponTestHost");
            try
            {
                var weapon = go.AddComponent(weaponType);

                // Capture the args the subscriber receives.
                bool fired = false;
                RaycastHit? receivedHit = null;
                bool receivedIsTarget = false;
                Action<RaycastHit?, bool> handler = (h, t) =>
                {
                    fired = true;
                    receivedHit = h;
                    receivedIsTarget = t;
                };

                // Subscribe via the public add accessor so we go through the same path
                // a real subscriber (e.g. AgentShooter) would.
                var ev = weaponType.GetEvent("OnFired");
                ev.AddEventHandler(weapon, handler);

                // Raise the event by invoking the backing delegate field directly.
                // Compiler-generated event backing field has the same name as the event.
                var backingField = weaponType.GetField("OnFired",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.IsNotNull(backingField, "OnFired backing delegate field must be reachable for dispatch");

                var del = (Delegate)backingField.GetValue(weapon);
                Assert.IsNotNull(del, "Subscribing should populate the backing delegate");

                // Case 1: missed shot — null hit, false isTarget.
                del.DynamicInvoke((RaycastHit?)null, false);
                Assert.IsTrue(fired, "Handler must fire on miss case");
                Assert.IsFalse(receivedHit.HasValue, "Miss case must produce a null hit");
                Assert.IsFalse(receivedIsTarget, "Miss case must report isTarget=false");

                // Case 2: target hit — non-null hit, true isTarget.
                fired = false;
                receivedHit = null;
                receivedIsTarget = false;
                var fakeHit = default(RaycastHit); // RaycastHit is a struct
                del.DynamicInvoke((RaycastHit?)fakeHit, true);
                Assert.IsTrue(fired, "Handler must fire on hit case");
                Assert.IsTrue(receivedHit.HasValue, "Hit case must produce a non-null hit");
                Assert.IsTrue(receivedIsTarget, "Hit case must report isTarget=true");

                // Unsubscribe and confirm no further dispatch.
                ev.RemoveEventHandler(weapon, handler);
                fired = false;
                del = (Delegate)backingField.GetValue(weapon);
                if (del != null) del.DynamicInvoke((RaycastHit?)null, false);
                Assert.IsFalse(fired, "Unsubscribed handler must not fire");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
