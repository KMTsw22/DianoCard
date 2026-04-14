using System.Collections;
using UnityEngine;

namespace DianoCard.FX
{
    public class BatSwarmSpawner : MonoBehaviour
    {
        [Header("Bat Prefab")]
        public GameObject batPrefab;

        [Header("Spawn Timing")]
        public float minInterval = 15f;
        public float maxInterval = 35f;
        public float firstDelay = 5f;

        [Header("Swarm")]
        public int minBats = 3;
        public int maxBats = 7;
        public float spawnSpread = 1.5f;

        [Header("Flight Area (world units)")]
        public float startX = -12f;
        public float endX = 12f;
        public float minY = 1.5f;
        public float maxY = 4f;

        [Header("Flight Motion")]
        public float minSpeed = 2.5f;
        public float maxSpeed = 4.5f;
        public float wobbleAmplitude = 0.4f;
        public float wobbleFrequency = 4f;
        public float flapFrequency = 10f;
        public float flapDepth = 0.35f;
        public float scale = 0.3f;
        public float alpha = 0.85f;

        void Start()
        {
            StartCoroutine(SpawnLoop());
        }

        IEnumerator SpawnLoop()
        {
            yield return new WaitForSeconds(firstDelay);
            while (true)
            {
                SpawnSwarm();
                yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
            }
        }

        void SpawnSwarm()
        {
            if (batPrefab == null) return;

            bool leftToRight = Random.value > 0.5f;
            int count = Random.Range(minBats, maxBats + 1);
            float baseY = Random.Range(minY, maxY);

            for (int i = 0; i < count; i++)
            {
                float ox = -i * Random.Range(0.4f, 0.9f);
                float oy = Random.Range(-spawnSpread, spawnSpread) * 0.5f;
                float sx = leftToRight ? startX + ox : endX - ox;
                var pos = new Vector3(sx, baseY + oy, 0f);

                var bat = Instantiate(batPrefab, pos, Quaternion.identity, transform);
                bat.transform.localScale = Vector3.one * scale * Random.Range(0.85f, 1.15f);

                var flyer = bat.GetComponent<BatFlyer>();
                if (flyer == null) flyer = bat.AddComponent<BatFlyer>();
                flyer.Init(
                    leftToRight ? endX + 1f : startX - 1f,
                    leftToRight ? 1f : -1f,
                    Random.Range(minSpeed, maxSpeed),
                    wobbleAmplitude * Random.Range(0.7f, 1.3f),
                    wobbleFrequency * Random.Range(0.8f, 1.2f),
                    Random.Range(0f, Mathf.PI * 2f),
                    flapFrequency * Random.Range(0.9f, 1.1f),
                    flapDepth,
                    alpha
                );
            }
        }
    }

    public class BatFlyer : MonoBehaviour
    {
        float targetX;
        float dir;
        float speed;
        float amp;
        float freq;
        float phase;
        float flapFreq;
        float flapDepth;
        float baseY;
        Vector3 baseScale;

        public void Init(float targetX, float dir, float speed, float amp, float freq, float phase, float flapFreq, float flapDepth, float alpha)
        {
            this.targetX = targetX;
            this.dir = dir;
            this.speed = speed;
            this.amp = amp;
            this.freq = freq;
            this.phase = phase;
            this.flapFreq = flapFreq;
            this.flapDepth = flapDepth;
            this.baseY = transform.position.y;
            this.baseScale = transform.localScale;

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var c = sr.color;
                c.a = alpha;
                sr.color = c;
                if (dir < 0f) sr.flipX = true;
            }
        }

        void Update()
        {
            var p = transform.position;
            p.x += dir * speed * Time.deltaTime;
            p.y = baseY + Mathf.Sin(Time.time * freq + phase) * amp;
            transform.position = p;

            float flap = Mathf.Abs(Mathf.Sin(Time.time * flapFreq + phase));
            var s = baseScale;
            s.y = baseScale.y * (1f - flapDepth + flap * flapDepth);
            transform.localScale = s;

            if ((dir > 0f && p.x >= targetX) || (dir < 0f && p.x <= targetX))
                Destroy(gameObject);
        }
    }
}
