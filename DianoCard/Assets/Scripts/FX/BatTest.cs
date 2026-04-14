using UnityEngine;

namespace DianoCard.FX
{
    public class BatTest : MonoBehaviour
    {
        [Header("Sprite (Resources path)")]
        public string spritePath = "FX/Normal_Battle/Bats/Bat";

        [Header("Test Bats")]
        public int batCount = 2;
        public float scale = 0.18f;
        public float alpha = 0.9f;

        [Header("Flight Area (world units)")]
        public float startX = -10f;
        public float endX = 10f;
        public float minY = 2f;
        public float maxY = 3.5f;

        [Header("Motion")]
        public float minSpeed = 2.5f;
        public float maxSpeed = 3.5f;
        public float wobbleAmplitude = 0.3f;
        public float wobbleFrequency = 3f;
        public float flapFrequency = 11f;
        public float flapDepth = 0.4f;

        [Header("Sorting")]
        public string sortingLayer = "Default";
        public int sortingOrder = 10;

        Sprite batSprite;

        void Start()
        {
            batSprite = Resources.Load<Sprite>(spritePath);
            if (batSprite == null)
            {
                Debug.LogError($"[BatTest] Sprite not found at Resources/{spritePath}");
                return;
            }

            SpawnSwarm();
        }

        void SpawnSwarm()
        {
            bool leftToRight = Random.value > 0.5f;
            float baseY = Random.Range(minY, maxY);

            for (int i = 0; i < batCount; i++)
            {
                float ox = -i * Random.Range(0.5f, 1.0f);
                float oy = Random.Range(-0.3f, 0.3f);
                float sx = leftToRight ? startX + ox : endX - ox;

                var go = new GameObject($"Bat_{i}");
                go.transform.SetParent(transform);
                go.transform.position = new Vector3(sx, baseY + oy, 0f);
                go.transform.localScale = Vector3.one * scale * Random.Range(0.9f, 1.1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = batSprite;
                sr.sortingLayerName = sortingLayer;
                sr.sortingOrder = sortingOrder;

                var flyer = go.AddComponent<BatFlyer>();
                flyer.Init(
                    leftToRight ? endX + 1f : startX - 1f,
                    leftToRight ? 1f : -1f,
                    Random.Range(minSpeed, maxSpeed),
                    wobbleAmplitude,
                    wobbleFrequency * Random.Range(0.85f, 1.15f),
                    Random.Range(0f, Mathf.PI * 2f),
                    flapFrequency * Random.Range(0.9f, 1.1f),
                    flapDepth,
                    alpha
                );
            }
        }
    }
}
