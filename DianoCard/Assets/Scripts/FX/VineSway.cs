using UnityEngine;

namespace DianoCard.FX
{
    public class VineSway : MonoBehaviour
    {
        public float angle = 2f;
        public float speed = 0.5f;
        public float phase = 0f;

        void Update()
        {
            float t = Time.time * speed * Mathf.PI * 2f + phase;
            var e = transform.localEulerAngles;
            e.z = Mathf.Sin(t) * angle;
            transform.localEulerAngles = e;
        }
    }
}
