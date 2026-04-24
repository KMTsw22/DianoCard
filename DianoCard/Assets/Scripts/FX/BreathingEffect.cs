using UnityEngine;

namespace DianoCard.FX
{
    public class BreathingEffect : MonoBehaviour
    {
        public float speed = 1.5f;
        public float amount = 0.02f;
        public float phase = 0f;

        Vector3 baseScale;

        void Awake()
        {
            baseScale = transform.localScale;
        }

        void Update()
        {
            float pulse = 1f + Mathf.Sin(Time.time * speed + phase) * amount;
            transform.localScale = new Vector3(baseScale.x, baseScale.y * pulse, baseScale.z);
        }
    }
}
