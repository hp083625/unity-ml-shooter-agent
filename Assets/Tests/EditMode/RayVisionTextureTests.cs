using NUnit.Framework;
using UnityEngine;
using UnityMLShooter.Agent.UI;

namespace Tests.EditMode
{
    public class RayVisionTextureTests
    {
        // Project tag conventions used in the agent's RayPerceptionSensor configuration.
        private const string TagTarget = "Target";
        private const string TagWall = "Wall";
        private const string TagExplosive = "ExplosiveBarrel";

        [Test]
        public void ColorForHit_MissReturnsBlack_RegardlessOfTag()
        {
            // hitFraction = 1.0 means no hit / ray reached its end.
            Color32 a = RayVisionTexture.ColorForHit(1.0f, TagTarget, TagTarget, TagWall, TagExplosive);
            Color32 b = RayVisionTexture.ColorForHit(1.0f, TagWall, TagTarget, TagWall, TagExplosive);
            Color32 c = RayVisionTexture.ColorForHit(1.0f, TagExplosive, TagTarget, TagWall, TagExplosive);
            Color32 d = RayVisionTexture.ColorForHit(1.0f, null, TagTarget, TagWall, TagExplosive);

            AssertColorEquals(new Color32(0, 0, 0, 255), a, "Target miss should be black");
            AssertColorEquals(new Color32(0, 0, 0, 255), b, "Wall miss should be black");
            AssertColorEquals(new Color32(0, 0, 0, 255), c, "Explosive miss should be black");
            AssertColorEquals(new Color32(0, 0, 0, 255), d, "null tag miss should be black");
        }

        [Test]
        public void ColorForHit_TargetAtZeroIsBrightRed()
        {
            // hitFraction = 0.0 -> brightness 1.0 -> full hue.
            Color32 c = RayVisionTexture.ColorForHit(0.0f, TagTarget, TagTarget, TagWall, TagExplosive);
            AssertColorEquals(new Color32(255, 0, 0, 255), c, "Target hit at 0 should be bright red");
        }

        [Test]
        public void ColorForHit_TargetAtHalfIsDimmedRed()
        {
            // hitFraction = 0.5 -> brightness 0.5 -> 255 * 0.5 = 127.
            Color32 c = RayVisionTexture.ColorForHit(0.5f, TagTarget, TagTarget, TagWall, TagExplosive);
            AssertColorEquals(new Color32(127, 0, 0, 255), c, "Target hit at 0.5 should be half-bright red");
        }

        [Test]
        public void ColorForHit_WallTagIsGrayHue()
        {
            // Wall hue is (180,180,180). At hitFraction=0 it should be the full hue.
            Color32 c = RayVisionTexture.ColorForHit(0.0f, TagWall, TagTarget, TagWall, TagExplosive);
            AssertColorEquals(new Color32(180, 180, 180, 255), c, "Wall hit at 0 should be full gray");

            // And it should remain neutral (R == G == B) when dimmed.
            Color32 dim = RayVisionTexture.ColorForHit(0.5f, TagWall, TagTarget, TagWall, TagExplosive);
            Assert.AreEqual(dim.r, dim.g, "Wall: R should equal G");
            Assert.AreEqual(dim.g, dim.b, "Wall: G should equal B");
            Assert.Greater(dim.r, 0, "Dimmed gray should not be black");
        }

        [Test]
        public void ColorForHit_ExplosiveTagIsOrangeHue()
        {
            // Explosive hue is (255,140,0). R should clearly dominate G; B should be 0.
            Color32 c = RayVisionTexture.ColorForHit(0.0f, TagExplosive, TagTarget, TagWall, TagExplosive);
            AssertColorEquals(new Color32(255, 140, 0, 255), c, "Explosive hit at 0 should be full orange");
            Assert.Greater(c.r, c.g, "Explosive: R should be greater than G (orange-ish)");
            Assert.AreEqual(0, c.b, "Explosive: B should be 0");
        }

        [Test]
        public void ColorForHit_UnknownTagIsBlack()
        {
            // A hit on an object whose tag isn't in the configured set -> black.
            Color32 c = RayVisionTexture.ColorForHit(0.3f, "Untagged", TagTarget, TagWall, TagExplosive);
            AssertColorEquals(new Color32(0, 0, 0, 255), c, "Unknown tag should be black");

            Color32 c2 = RayVisionTexture.ColorForHit(0.3f, "SomeOtherTag", TagTarget, TagWall, TagExplosive);
            AssertColorEquals(new Color32(0, 0, 0, 255), c2, "Other unknown tag should be black");
        }

        private static void AssertColorEquals(Color32 expected, Color32 actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, $"{message} (R)");
            Assert.AreEqual(expected.g, actual.g, $"{message} (G)");
            Assert.AreEqual(expected.b, actual.b, $"{message} (B)");
            Assert.AreEqual(expected.a, actual.a, $"{message} (A)");
        }
    }
}
