using UnityEngine;

namespace DianoCard.FX
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class GodRayFX : MonoBehaviour
    {
        [Header("Alpha Pulse")]
        [Range(0f, 1f)] public float minAlpha = 0.25f;
        [Range(0f, 1f)] public float maxAlpha = 0.75f;
        public float pulseSpeed = 0.6f;

        [Header("Sway (degrees)")]
        public float swayAngle = 2f;
        public float swaySpeed = 0.4f;

        [Header("Phase Offset")]
        public float phaseOffset = 0f;

        SpriteRenderer sr;
        float baseZ;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            baseZ = transform.localEulerAngles.z;
        }

        void Update()
        {
            float t = Time.time + phaseOffset;

            float a01 = (Mathf.Sin(t * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, a01);
            var c = sr.color;
            c.a = alpha;
            sr.color = c;

            float angle = baseZ + Mathf.Sin(t * swaySpeed * Mathf.PI * 2f) * swayAngle;
            var e = transform.localEulerAngles;
            e.z = angle;
            transform.localEulerAngles = e;
        }
    }
}
