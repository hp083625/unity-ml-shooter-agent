using NUnit.Framework;
using System;
using System.Linq;
using UnityEngine;

namespace Tests.EditMode
{
    public class WeaponOnFiredTests
    {
        [Test]
        public void OnFired_EventExists_WithCorrectSignature()
        {
            var weaponType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                .FirstOrDefault(t => t.FullName == "InfimaGames.LowPolyShooterPack.Weapon");
            Assert.IsNotNull(weaponType, "Weapon type must exist");
            var ev = weaponType.GetEvent("OnFired");
            Assert.IsNotNull(ev, "Weapon.OnFired event must exist");
            Assert.AreEqual(typeof(Action<RaycastHit?, bool>), ev.EventHandlerType);
        }
    }
}
