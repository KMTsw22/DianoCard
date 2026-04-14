using UnityEngine;

namespace DianoCard.FX
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class GlowPulse : MonoBehaviour
    {
        public float minAlpha = 0.3f;
        public float maxAlpha = 0.9f;
        public float speed = 1.5f;
        public float phase = 0f;

        SpriteRenderer sr;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
        }

        void Update()
        {
            float t = (Mathf.Sin(Time.time * speed + phase) + 1f) * 0.5f;
            var c = sr.color;
            c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
            sr.color = c;
        }
    }
}
