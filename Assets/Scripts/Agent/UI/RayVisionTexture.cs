using System;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace UnityMLShooter.Agent.UI
{
    /// <summary>
    /// Pure-C# class that converts the live hit data from a fixed set of three
    /// <see cref="RayPerceptionSensorComponent3D"/> components into a 17x3 RGBA32
    /// <see cref="Texture2D"/> for an in-game "AI vision" HUD overlay.
    ///
    /// Texture layout:
    ///   * 17 columns (one per ray; matches RaysPerDirection=8 -> 2*8+1)
    ///   * 3 rows (one per sensor; row 0 = first sensor in the list)
    ///   * RGBA32, FilterMode.Point so the HUD can show crisp blocks.
    ///
    /// Color encoding (see CONTEXT.md "AI vision debug overlay"):
    ///   * Hue picks the tag: red (Target), gray (Wall), orange (ExplosiveBarrel),
    ///     black for "no hit / unknown tag".
    ///   * Brightness encodes closeness via brightness = 1 - HitFraction (clamped).
    /// </summary>
    public sealed class RayVisionTexture : IDisposable
    {
        public const int Width = 17;
        public const int Height = 3;

        // Tag -> hue table. Anything not in this table paints black.
        // Defined as Color32 to avoid float->byte rounding error per pixel.
        internal static readonly IReadOnlyDictionary<string, Color32> TagHues =
            new Dictionary<string, Color32>
            {
                { "Target",          new Color32(255, 0,   0,   255) },
                { "Wall",            new Color32(180, 180, 180, 255) },
                { "ExplosiveBarrel", new Color32(255, 140, 0,   255) },
            };

        private static readonly Color32 NoHitColor = new Color32(0, 0, 0, 255);

        private readonly Color32[] _pixelBuffer;

        private Texture2D _texture;
        private bool _disposed;

        /// <summary>
        /// The 17x3 texture. Allocated once in the constructor and reused.
        /// Returns null after <see cref="Dispose"/>.
        /// </summary>
        public Texture2D Texture => _texture;

        /// <summary>
        /// Construct a new painter.
        /// </summary>
        /// <param name="orderedTags">
        /// Tag names in canonical order. The issue body specifies
        /// {"Target", "Wall", "ExplosiveBarrel"}, but only the names matter:
        /// painting looks up the hue by tag name (not by list index), so the order
        /// of this list is informational. The list is copied defensively.
        /// </param>
        public RayVisionTexture(IList<string> orderedTags)
        {
            if (orderedTags == null) throw new ArgumentNullException(nameof(orderedTags));
            // The parameter is part of the public contract (issue #12) but the painting
            // logic uses the static TagHues table to look up by tag NAME, so we don't
            // actually need to retain the list. Reading it is enough to validate that
            // callers passed something non-null.
            _ = orderedTags;

            _texture = new Texture2D(Width, Height, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "RayVisionTexture",
                hideFlags = HideFlags.DontSave,
            };

            _pixelBuffer = new Color32[Width * Height];
            // Initialise everything to opaque black so a freshly-constructed texture
            // doesn't show stale GPU memory before the first Update.
            for (int i = 0; i < _pixelBuffer.Length; i++)
            {
                _pixelBuffer[i] = NoHitColor;
            }
            _texture.SetPixels32(_pixelBuffer);
            _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        /// <summary>
        /// Read the latest <see cref="RayPerceptionOutput"/> from each of the three
        /// sensors and paint one row of the texture per sensor.
        /// </summary>
        /// <param name="sensors">Exactly three sensors in the order: top -> middle -> bottom.</param>
        public void Update(IList<RayPerceptionSensorComponent3D> sensors)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RayVisionTexture));
            if (sensors == null) throw new ArgumentNullException(nameof(sensors));
            if (sensors.Count != Height)
            {
                throw new ArgumentException(
                    $"Expected exactly {Height} sensors, got {sensors.Count}.",
                    nameof(sensors));
            }

            for (int row = 0; row < Height; row++)
            {
                var sensor = sensors[row];
                RayPerceptionOutput.RayOutput[] rayOutputs = null;
                IReadOnlyList<string> sensorTags = null;

                if (sensor != null)
                {
                    sensorTags = sensor.DetectableTags;
                    var raySensor = sensor.RaySensor;
                    if (raySensor != null && raySensor.RayPerceptionOutput != null)
                    {
                        rayOutputs = raySensor.RayPerceptionOutput.RayOutputs;
                    }
                }

                PaintRow(row, rayOutputs, sensorTags);
            }
        }

        /// <summary>
        /// Internal seam used by <see cref="Update"/>: write one row of the texture from a
        /// raw <see cref="RayPerceptionOutput.RayOutput"/> array. Pads/truncates to
        /// <see cref="Width"/> rays so callers don't have to size their inputs exactly.
        /// </summary>
        internal void PaintRow(
            int row,
            RayPerceptionOutput.RayOutput[] rayOutputs,
            IReadOnlyList<string> sensorDetectableTags)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RayVisionTexture));
            if (row < 0 || row >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            int rowOffset = row * Width;
            int rayCount = rayOutputs?.Length ?? 0;

            for (int col = 0; col < Width; col++)
            {
                Color32 pixel;
                if (col < rayCount)
                {
                    var ro = rayOutputs[col];
                    string tagName = ResolveTagName(ro, sensorDetectableTags);
                    pixel = ComputePixel(ro.HasHit, ro.HitFraction, tagName);
                }
                else
                {
                    // Sensor returned fewer rays than the texture is sized for: paint black.
                    pixel = NoHitColor;
                }

                _pixelBuffer[rowOffset + col] = pixel;
            }

            FlushBufferToTexture();
        }

        /// <summary>
        /// Test seam that does NOT depend on ML-Agents types. Tests pass a flat array of
        /// stub ray hits as <c>(hasHit, hitFraction, tagName)</c> tuples. Pads/truncates to
        /// <see cref="Width"/> entries. Flushes the buffer so tests can read the texture
        /// back via <see cref="Texture2D.GetPixels32()"/>.
        /// </summary>
        internal void PaintRowFromStubs(int row, IList<StubRay> stubs)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RayVisionTexture));
            if (row < 0 || row >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            int rowOffset = row * Width;
            int stubCount = stubs?.Count ?? 0;

            for (int col = 0; col < Width; col++)
            {
                Color32 pixel;
                if (col < stubCount)
                {
                    var s = stubs[col];
                    pixel = ComputePixel(s.HasHit, s.HitFraction, s.TagName);
                }
                else
                {
                    pixel = NoHitColor;
                }
                _pixelBuffer[rowOffset + col] = pixel;
            }

            FlushBufferToTexture();
        }

        private void FlushBufferToTexture()
        {
            _texture.SetPixels32(_pixelBuffer);
            _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        /// <summary>
        /// Test-only stand-in for a single ray's perception output. Mirrors the fields of
        /// <see cref="RayPerceptionOutput.RayOutput"/> that we actually paint with.
        /// </summary>
        internal readonly struct StubRay
        {
            public readonly bool HasHit;
            public readonly float HitFraction;
            public readonly string TagName;

            public StubRay(bool hasHit, float hitFraction, string tagName)
            {
                HasHit = hasHit;
                HitFraction = hitFraction;
                TagName = tagName;
            }
        }

        /// <summary>
        /// Pure painting logic, easy to unit-test without instantiating Texture2D.
        /// </summary>
        internal static Color32 ComputePixel(bool hasHit, float hitFraction, string hitTagName)
        {
            if (!hasHit)
            {
                return NoHitColor;
            }

            if (string.IsNullOrEmpty(hitTagName) || !TagHues.TryGetValue(hitTagName, out Color32 hue))
            {
                return NoHitColor;
            }

            float brightness = Mathf.Clamp01(1f - hitFraction);
            return new Color32(
                (byte)(hue.r * brightness),
                (byte)(hue.g * brightness),
                (byte)(hue.b * brightness),
                255);
        }

        private static string ResolveTagName(
            RayPerceptionOutput.RayOutput ro,
            IReadOnlyList<string> sensorDetectableTags)
        {
            if (!ro.HasHit || !ro.HitTaggedObject) return null;
            int idx = ro.HitTagIndex;
            if (sensorDetectableTags == null) return null;
            if (idx < 0 || idx >= sensorDetectableTags.Count) return null;
            return sensorDetectableTags[idx];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_texture != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_texture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_texture);
                }
                _texture = null;
            }
        }
    }
}
