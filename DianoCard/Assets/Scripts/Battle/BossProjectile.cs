using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DianoCard.Battle
{
    /// 보스 검 스윙에서 발사되는 검은 초승달(부메랑) 투사체.
    /// 라인하르트 화염강타(E) 톤 — 일직선 비행 + 휘날림(wobble) + 잔상(afterimage).
    /// 절차적 텍스처: crescent mask × tip-fade(양 뿔) × perlin noise → 가장자리 그라데이션.
    /// 도착 시 onHit 콜백 발사 → BattleUI가 DealAttack을 트리거하면서 PlayHit 자동 발동.
    public class BossProjectile : MonoBehaviour
    {
        // === 라이브 튜닝 (CheatUI 슬라이더 직결) ===
        public static Color TintColor = new Color(0.085f, 0.062f, 0.110f, 1f);
        public static float SizeMultiplier = 1f;
        public static float YScaleMultiplier = 1.54f;

        // 휘날림 — 매 프레임 sin 기반 미세 회전/스케일 흔들림.
        public static float WobbleIntensity = 0.6f;     // 0=없음, 1=강함

        // 잔상 — 본체 뒤로 stagger된 반투명 사본 N개. 본체 transform 따라감.
        public static int   AfterimageCount = 2;        // 0~5
        public static float AfterimageSpacing = 0.12f;  // 사본간 진행방향 반대로 어긋나는 거리(localScale 비례)
        public static float AfterimageAlpha = 0.55f;    // 첫 사본 알파, 뒤로 갈수록 점진 감소

        // 텍스처 그라데이션 — 양 뿔이 빨리 흐릿해지는 정도, 가장자리 노이즈 거침.
        public static float TipFadePower = 1.6f;        // 1=선형, 2=양 끝 빨리 사라짐
        public static float NoiseStrength = 0.18f;      // 0=매끈, 0.5=거친 천조각 느낌

        // 트레일(원형 잔상) — 사용자가 "동그라미"라고 부른 그것. 기본 OFF.
        public static bool  TrailEnabled = false;
        public static float TrailWidthRatio = 0.55f;
        public static float TrailTime = 0.40f;

        // === 텍스처 캐시 — 노이즈/팁페이드 값 바뀌면 무효화 ===
        private static Sprite _cachedCrescent;
        private static float _cachedTipFadePower = -1f;
        private static float _cachedNoiseStrength = -1f;

        // === 인스턴스 상태 ===
        private float _baseAngle;
        private Vector3 _baseLocalScale;
        private float _wobblePhase;
        private SpriteRenderer _sr;
        private List<SpriteRenderer> _afterimages;
        private List<float> _afterimageBaseAlpha;

        public static BossProjectile SpawnCrescent(
            Vector3 from, Vector3 to,
            float duration = 0.42f,
            float worldHeight = 1.0f,
            int sortingOrder = 110,
            Action onHit = null)
        {
            var go = new GameObject("BossProjectile_Crescent");
            go.transform.position = from;
            var p = go.AddComponent<BossProjectile>();
            p.Init(from, to, duration, worldHeight, sortingOrder, onHit);
            return p;
        }

        private void Init(Vector3 from, Vector3 to, float duration, float worldHeight, int sortingOrder, Action onHit)
        {
            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sprite = GetCrescentSprite();
            _sr.sortingOrder = sortingOrder;
            _sr.color = TintColor;

            float effectiveHeight = worldHeight * SizeMultiplier;
            float boundsH = _sr.sprite.bounds.size.y;
            float s = boundsH > 0.001f ? effectiveHeight / boundsH : 1f;
            _baseLocalScale = new Vector3(s, s * YScaleMultiplier, 1f);
            transform.localScale = _baseLocalScale;

            // 진행방향 정렬.
            Vector3 d = to - from;
            _baseAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(_baseAngle, Vector3.forward);

            _wobblePhase = UnityEngine.Random.value * Mathf.PI * 2f;

            // 잔상 사본 — 자식으로 붙여 본체 transform을 자동 추적. local -x로 stagger(진행 반대방향).
            int n = Mathf.Clamp(AfterimageCount, 0, 5);
            if (n > 0)
            {
                _afterimages = new List<SpriteRenderer>(n);
                _afterimageBaseAlpha = new List<float>(n);
                for (int i = 0; i < n; i++)
                {
                    var ghost = new GameObject($"Crescent_Ghost_{i}");
                    ghost.transform.SetParent(transform, false);
                    ghost.transform.localPosition = new Vector3(-(i + 1) * AfterimageSpacing, 0f, 0f);
                    ghost.transform.localRotation = Quaternion.identity;
                    ghost.transform.localScale = Vector3.one;
                    var gsr = ghost.AddComponent<SpriteRenderer>();
                    gsr.sprite = _sr.sprite;
                    gsr.sortingOrder = sortingOrder - (i + 1);
                    float ghostA = AfterimageAlpha * Mathf.Pow(0.6f, i);
                    Color tc = TintColor;
                    gsr.color = new Color(tc.r, tc.g, tc.b, tc.a * ghostA);
                    _afterimages.Add(gsr);
                    _afterimageBaseAlpha.Add(ghostA);
                }
            }

            if (TrailEnabled)
            {
                var trail = gameObject.AddComponent<TrailRenderer>();
                trail.time = TrailTime;
                trail.startWidth = effectiveHeight * TrailWidthRatio;
                trail.endWidth = 0f;
                trail.material = new Material(Shader.Find("Sprites/Default"));
                Color tc = TintColor;
                trail.startColor = new Color(tc.r, tc.g, tc.b, tc.a * 0.85f);
                trail.endColor   = new Color(tc.r, tc.g, tc.b, 0f);
                trail.sortingOrder = sortingOrder - 10;
                trail.minVertexDistance = 0.02f;
                trail.numCornerVertices = 2;
                trail.numCapVertices = 2;
            }

            StartCoroutine(FlyRoutine(from, to, duration, onHit));
        }

        private void LateUpdate()
        {
            // 휘날림 — 두 주파수가 다른 sin을 합쳐 단조롭지 않은 흔들림.
            if (WobbleIntensity > 0.001f)
            {
                float t = Time.time;
                float w1 = Mathf.Sin(t * 17f + _wobblePhase);
                float w2 = Mathf.Sin(t * 9f + _wobblePhase * 1.7f);
                float angleJitter = (w1 * 5.5f + w2 * 2.5f) * WobbleIntensity;
                float scaleJitter = 1f + (w2 * 0.06f + w1 * 0.025f) * WobbleIntensity;

                transform.rotation = Quaternion.AngleAxis(_baseAngle + angleJitter, Vector3.forward);
                transform.localScale = new Vector3(
                    _baseLocalScale.x * scaleJitter,
                    _baseLocalScale.y * (2f - scaleJitter), // x 늘면 y 줄여서 천 펄럭이는 느낌
                    1f);
            }
        }

        private IEnumerator FlyRoutine(Vector3 from, Vector3 to, float duration, Action onHit)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                transform.position = Vector3.Lerp(from, to, u);
                yield return null;
            }
            transform.position = to;

            onHit?.Invoke();

            float fadeDur = 0.22f;
            float fadeT = 0f;
            Color c0 = _sr.color;
            while (fadeT < fadeDur)
            {
                fadeT += Time.deltaTime;
                float k = fadeT / fadeDur;
                float a = Mathf.Lerp(c0.a, 0f, k);
                _sr.color = new Color(c0.r, c0.g, c0.b, a);

                // 잔상도 함께 페이드.
                if (_afterimages != null)
                {
                    for (int i = 0; i < _afterimages.Count; i++)
                    {
                        var gsr = _afterimages[i];
                        if (gsr == null) continue;
                        var gc = gsr.color;
                        gsr.color = new Color(gc.r, gc.g, gc.b, gc.a * (1f - k));
                    }
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        /// 절차 텍스처: 초승달 마스크 × 양 뿔 페이드(y) × perlin noise.
        /// TipFadePower / NoiseStrength 변경 시 자동 재생성.
        private static Sprite GetCrescentSprite()
        {
            bool fresh = _cachedCrescent != null
                && Mathf.Approximately(_cachedTipFadePower, TipFadePower)
                && Mathf.Approximately(_cachedNoiseStrength, NoiseStrength);
            if (fresh) return _cachedCrescent;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[size * size];

            // 볼록면이 +x를 향하도록 — 큰 원 오른쪽, 잘라내는 작은 원 왼쪽.
            const float r1 = 0.46f, cx1 = 0.54f, cy1 = 0.50f;
            const float r2 = 0.43f, cx2 = 0.38f, cy2 = 0.50f;
            const float edge = 0.012f;

            float tipPow = Mathf.Max(0.1f, TipFadePower);
            float noiseAmt = Mathf.Clamp01(NoiseStrength);
            // perlin 좌표 시드 — 같은 텍스처는 같은 노이즈 패턴 유지.
            const float noiseFreq = 6.5f;
            const float noiseSeedX = 13.37f;
            const float noiseSeedY = 7.91f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;

                    float d1 = Mathf.Sqrt((u - cx1) * (u - cx1) + (v - cy1) * (v - cy1));
                    float d2 = Mathf.Sqrt((u - cx2) * (u - cx2) + (v - cy2) * (v - cy2));
                    float a1 = Mathf.SmoothStep(0f, 1f, (r1 - d1) / edge + 0.5f);
                    float a2 = Mathf.SmoothStep(0f, 1f, (r2 - d2) / edge + 0.5f);
                    float crescent = Mathf.Clamp01(a1 - a2);

                    // 양 뿔(y축 위/아래) 페이드 — y가 0.5에서 멀수록 흐릿.
                    float yDist = Mathf.Abs(v - 0.5f) * 2f; // 0~1
                    float tipFade = Mathf.Clamp01(1f - Mathf.Pow(yDist, tipPow));

                    // 가장자리 노이즈 — 천 찢긴 듯 거친 가장자리.
                    float n = Mathf.PerlinNoise(u * noiseFreq + noiseSeedX, v * noiseFreq + noiseSeedY);
                    float noiseFactor = Mathf.Lerp(1f - noiseAmt, 1f, n);

                    float a = crescent * tipFade * noiseFactor;
                    a = Mathf.Clamp01(a);
                    byte ab = (byte)(a * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, ab);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            _cachedCrescent = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _cachedCrescent.name = "BossCrescentProcedural";
            _cachedTipFadePower = TipFadePower;
            _cachedNoiseStrength = NoiseStrength;
            return _cachedCrescent;
        }
    }
}
