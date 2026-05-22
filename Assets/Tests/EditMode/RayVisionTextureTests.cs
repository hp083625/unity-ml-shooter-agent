using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityMLShooter.Agent.UI;

namespace Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="RayVisionTexture"/>.
    ///
    /// Painting is tested via the internal <c>PaintRowFromStubs</c> seam, which takes a
    /// flat (hasHit, hitFraction, tagName) tuple list. This deliberately avoids
    /// Unity.MLAgents types so the test asmdef does not need to reference ML-Agents.
    /// The pure helper <c>ComputePixel</c> is also probed directly. The live-sensor
    /// path (<see cref="RayVisionTexture.Update"/>) is exercised in a Tier 4 PlayMode
    /// test, not here.
    /// </summary>
    public class RayVisionTextureTests
    {
        private static readonly IList<string> CanonicalTags = new[]
        {
            "Target", "Wall", "ExplosiveBarrel"
        };

        private RayVisionTexture _sut;

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        [Test]
        public void Constructor_AllocatesA17x3RGBA32PointFilteredTexture()
        {
            _sut = new RayVisionTexture(CanonicalTags);

            Assert.IsNotNull(_sut.Texture, "Texture should be allocated by the constructor.");
            Assert.AreEqual(RayVisionTexture.Width,  _sut.Texture.width,  "Width should be 17.");
            Assert.AreEqual(RayVisionTexture.Height, _sut.Texture.height, "Height should be 3.");
            Assert.AreEqual(17, _sut.Texture.width);
            Assert.AreEqual(3,  _sut.Texture.height);
            Assert.AreEqual(TextureFormat.RGBA32, _sut.Texture.format);
            Assert.AreEqual(FilterMode.Point,     _sut.Texture.filterMode);
        }

        [Test]
        public void PaintRow_CloseTargetHit_ProducesBrightRedPixel()
        {
            _sut = new RayVisionTexture(CanonicalTags);

            _sut.PaintRowFromStubs(row: 0, stubs: new[]
            {
                new RayVisionTexture.StubRay(hasHit: true, hitFraction: 0f, tagName: "Target"),
            });

            Color32 pixel = ReadPixel(_sut, row: 0, col: 0);
            Assert.GreaterOrEqual(pixel.r, (byte)200, "Close Target should be bright red.");
            Assert.AreEqual((byte)0, pixel.g);
            Assert.AreEqual((byte)0, pixel.b);
            Assert.AreEqual((byte)255, pixel.a);
        }

        [Test]
        public void PaintRow_FarTargetHit_ProducesDimRedPixel()
        {
            _sut = new RayVisionTexture(CanonicalTags);

            _sut.PaintRowFromStubs(row: 0, stubs: new[]
            {
                new RayVisionTexture.StubRay(hasHit: true, hitFraction: 0.9f, tagName: "Target"),
            });

            Color32 pixel = ReadPixel(_sut, row: 0, col: 0);
            // brightness = 1 - 0.9 = 0.1 -> r ≈ 25
            Assert.Greater(pixel.r, (byte)0,  "Far Target should still be visible.");
            Assert.Less   (pixel.r, (byte)80, "Far Target should be much dimmer than close.");
            Assert.AreEqual((byte)0, pixel.g);
            Assert.AreEqual((byte)0, pixel.b);
            Assert.AreEqual((byte)255, pixel.a);
        }

        [Test]
        public void PaintRow_CloseWallHit_ProducesBrightGrayPixel()
        {
            _sut = new RayVisionTexture(CanonicalTags);

            _sut.PaintRowFromStubs(row: 1, stubs: new[]
            {
                new RayVisionTexture.StubRay(hasHit: true, hitFraction: 0f, tagName: "Wall"),
            });

            Color32 pixel = ReadPixel(_sut, row: 1, col: 0);
            Assert.GreaterOrEqual(pixel.r, (byte)170);
            Assert.AreEqual(pixel.r, pixel.g);
            Assert.AreEqual(pixel.r, pixel.b);
            Assert.AreEqual((byte)255, pixel.a);
        }

        [Test]
        public void PaintRow_NoHit_ProducesBlackPixel()
        {
            _sut = new RayVisionTexture(CanonicalTags);

            _sut.PaintRowFromStubs(row: 2, stubs: new[]
            {
                new RayVisionTexture.StubRay(hasHit: false, hitFraction: 1f, tagName: null),
            });

            Color32 pixel = ReadPixel(_sut, row: 2, col: 0);
            Assert.AreEqual((byte)0, pixel.r);
            Assert.AreEqual((byte)0, pixel.g);
            Assert.AreEqual((byte)0, pixel.b);
            Assert.AreEqual((byte)255, pixel.a);
        }

        [Test]
        public void PaintRow_ExplosiveBarrelHit_ProducesOrangePixel()
        {
            _sut = new RayVisionTexture(CanonicalTags);

            _sut.PaintRowFromStubs(row: 0, stubs: new[]
            {
                new RayVisionTexture.StubRay(hasHit: true, hitFraction: 0f, tagName: "ExplosiveBarrel"),
            });

            Color32 pixel = ReadPixel(_sut, row: 0, col: 0);
            // Orange tag color is (255, 140, 0).
            Assert.GreaterOrEqual(pixel.r, (byte)240, "Orange should be very red.");
            Assert.GreaterOrEqual(pixel.g, (byte)120, "Orange should have moderate green.");
            Assert.LessOrEqual   (pixel.g, (byte)160);
            Assert.AreEqual((byte)0, pixel.b);
            Assert.AreEqual((byte)255, pixel.a);
        }

        [Test]
        public void Dispose_IsIdempotent_AndReleasesTexture()
        {
            _sut = new RayVisionTexture(CanonicalTags);
            Assert.IsNotNull(_sut.Texture, "Texture should exist before Dispose.");

            _sut.Dispose();
            Assert.IsNull(_sut.Texture, "Texture should be released after Dispose.");

            // Calling Dispose a second time should be a no-op.
            Assert.DoesNotThrow(() => _sut.Dispose());

            // Calling PaintRowFromStubs after Dispose should throw cleanly rather than NRE.
            Assert.Throws<ObjectDisposedException>(() => _sut.PaintRowFromStubs(
                row: 0,
                stubs: new[] { new RayVisionTexture.StubRay(false, 1f, null) }));
        }

        // ---------- helpers ----------

        private static Color32 ReadPixel(RayVisionTexture sut, int row, int col)
        {
            // SetPixels32/GetPixels32 layout: index = row * width + col.
            return sut.Texture.GetPixels32()[row * RayVisionTexture.Width + col];
        }
    }
}
