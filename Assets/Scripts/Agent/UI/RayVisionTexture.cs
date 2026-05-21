using UnityEngine;
using UnityEngine.UI;
using Unity.MLAgents.Sensors;

namespace UnityMLShooter.Agent.UI
{
    /// <summary>
    /// Paints what the agent's <see cref="RayPerceptionSensorComponent3D"/> sees
    /// into a small 2D <see cref="Texture2D"/> displayed on a <see cref="RawImage"/>.
    /// One row per sensor component, one column per ray. Hue encodes the hit tag
    /// (red Target, gray Wall, orange ExplosiveBarrel, black no-hit) and brightness
    /// encodes closeness (1 - normalized hit distance).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class RayVisionTexture : MonoBehaviour
    {
        [Tooltip("Sensor components to visualize. Each becomes one row of the texture.")]
        [SerializeField] private RayPerceptionSensorComponent3D[] sensors;

        [Tooltip("Tag string painted as red.")]
        [SerializeField] private string tagTarget = "Target";

        [Tooltip("Tag string painted as gray.")]
        [SerializeField] private string tagWall = "Wall";

        [Tooltip("Tag string painted as orange.")]
        [SerializeField] private string tagExplosive = "ExplosiveBarrel";

        private RawImage rawImage;
        private Texture2D texture;
        private Color32[] pixelBuffer;
        private int textureWidth;
        private int textureHeight;

        private void Awake()
        {
            rawImage = GetComponent<RawImage>();

            // Determine dimensions from configured sensors.
            int sensorCount = sensors != null ? sensors.Length : 0;
            int rayCount = ResolveRayCount();

            // Always allocate at least 1x1 to avoid zero-sized textures.
            textureWidth = Mathf.Max(1, rayCount);
            textureHeight = Mathf.Max(1, sensorCount);

            texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "RayVisionTexture"
            };

            pixelBuffer = new Color32[textureWidth * textureHeight];
            // Initialize black so the texture has defined contents before LateUpdate runs.
            for (int i = 0; i < pixelBuffer.Length; i++)
            {
                pixelBuffer[i] = new Color32(0, 0, 0, 255);
            }
            texture.SetPixels32(pixelBuffer);
            texture.Apply(updateMipmaps: false);

            rawImage.texture = texture;
        }

        private void OnDestroy()
        {
            if (texture != null)
            {
                Destroy(texture);
                texture = null;
            }
        }

        private int ResolveRayCount()
        {
            if (sensors == null) return 0;
            int max = 0;
            for (int i = 0; i < sensors.Length; i++)
            {
                if (sensors[i] == null) continue;
                // RayPerceptionSensorComponentBase exposes RaysPerDirection; total rays = 2*N + 1.
                int rays = (sensors[i].RaysPerDirection * 2) + 1;
                if (rays > max) max = rays;
            }
            return max;
        }

        private void LateUpdate()
        {
            if (texture == null || pixelBuffer == null || sensors == null) return;

            for (int row = 0; row < textureHeight; row++)
            {
                RayPerceptionSensorComponent3D sensor =
                    (row < sensors.Length) ? sensors[row] : null;

                RayPerceptionOutput.RayOutput[] rayOutputs = null;
                if (sensor != null)
                {
                    // The sensor's RayPerceptionSensor instance is created when the Agent
                    // calls CreateSensors(). Its RayPerceptionOutput holds the most recent
                    // raycast results (populated during ML-Agents' Update step).
                    var raySensor = sensor.RaySensor;
                    rayOutputs = raySensor?.RayPerceptionOutput?.RayOutputs;
                }

                for (int col = 0; col < textureWidth; col++)
                {
                    Color32 pixel;
                    if (rayOutputs != null && col < rayOutputs.Length)
                    {
                        var r = rayOutputs[col];
                        string tag = (r.HasHit && r.HitGameObject != null)
                            ? r.HitGameObject.tag
                            : null;
                        float hitFraction = r.HasHit ? r.HitFraction : 1f;
                        pixel = ColorForHit(hitFraction, tag, tagTarget, tagWall, tagExplosive);
                    }
                    else
                    {
                        pixel = new Color32(0, 0, 0, 255);
                    }

                    pixelBuffer[row * textureWidth + col] = pixel;
                }
            }

            texture.SetPixels32(pixelBuffer);
            texture.Apply(updateMipmaps: false);
        }

        /// <summary>
        /// Pure color computation for a single ray hit. Exposed for unit testing.
        /// hitFraction is the normalized distance to the hit (0 = at agent, 1 = max range / no hit).
        /// </summary>
        public static Color32 ColorForHit(
            float hitFraction,
            string tag,
            string tagTarget,
            string tagWall,
            string tagExplosive)
        {
            if (hitFraction >= 1f) return new Color32(0, 0, 0, 255);

            float brightness = Mathf.Clamp01(1f - hitFraction);

            Color32 hue;
            if (tag == tagTarget)
            {
                hue = new Color32(255, 0, 0, 255);
            }
            else if (tag == tagWall)
            {
                hue = new Color32(180, 180, 180, 255);
            }
            else if (tag == tagExplosive)
            {
                hue = new Color32(255, 140, 0, 255);
            }
            else
            {
                return new Color32(0, 0, 0, 255);
            }

            return new Color32(
                (byte)(hue.r * brightness),
                (byte)(hue.g * brightness),
                (byte)(hue.b * brightness),
                255);
        }
    }
}
