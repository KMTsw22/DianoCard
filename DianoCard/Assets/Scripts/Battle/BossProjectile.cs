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
        private static Sprite _cachedFlame;
        private static Sprite _cachedCrescentSoft;

        // === 인스턴스 상태 ===
        private float _baseAngle;
        private Vector3 _baseLocalScale;
        private float _wobblePhase;
        private SpriteRenderer _sr;
        private List<SpriteRenderer> _afterimages;
        private List<float> _afterimageBaseAlpha;

        // 비행 중 Y 스케일이 자라는 옵션 — 1.0이면 변화 없음, 2.0이면 끝 시점에 두 배.
        // 진행방향이 local X이라 Y는 crescent의 두께(폭) → 비행할수록 부풀어 오르는 효과.
        private float _yGrowEnd = 1f;
        private float _flightProgress01 = 0f;

        // 비행 곡선 — 1=등속, 2=ease-out 약함, 3=ease-out 강함(처음 빠르고 끝 느림).
        private float _easeOutPower = 1f;

        // 비행 중 알파 배수 — 1.0이면 변화 없음, 0.3이면 끝 시점에 30%로 옅어짐.
        private float _alphaFadeEnd = 1f;

        // 커스텀 스프라이트(예: 불꽃 모양) — null이면 기본 crescent 사용.
        private Sprite _customSprite;

        // wobble(휘날림) 인스턴스 토글 — false면 검 펄럭이는 효과 꺼짐. 화구엔 안 어울림.
        private bool _enableWobble = true;

        // 잔상 개수 오버라이드 — -1이면 static AfterimageCount 사용. 0이면 잔상 없음.
        private int _afterimageCountOverride = -1;

        // X(진행방향) 베이스 스케일 멀티플라이어 — 1.0이면 변화 없음, 2.0이면 가로 2배 두꺼움.
        private float _xBaseScale = 1f;

        public static BossProjectile SpawnCrescent(
            Vector3 from, Vector3 to,
            float duration = 0.42f,
            float worldHeight = 1.0f,
            int sortingOrder = 110,
            Action onHit = null,
            float yGrowEnd = 1f,
            float easeOutPower = 1f,
            float alphaFadeEnd = 1f,
            Sprite customSprite = null,
            bool enableWobble = true,
            int afterimageCount = -1,
            float xBaseScale = 1f)
        {
            var go = new GameObject("BossProjectile_Crescent");
            go.transform.position = from;
            var p = go.AddComponent<BossProjectile>();
            p._yGrowEnd = yGrowEnd;
            p._easeOutPower = easeOutPower;
            p._alphaFadeEnd = alphaFadeEnd;
            p._customSprite = customSprite;
            p._enableWobble = enableWobble;
            p._afterimageCountOverride = afterimageCount;
            p._xBaseScale = xBaseScale;
            p.Init(from, to, duration, worldHeight, sortingOrder, onHit);
            return p;
        }

        private void Init(Vector3 from, Vector3 to, float duration, float worldHeight, int sortingOrder, Action onHit)
        {
            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sprite = _customSprite != null ? _customSprite : GetCrescentSprite();
            _sr.sortingOrder = sortingOrder;
            _sr.color = TintColor;

            float effectiveHeight = worldHeight * SizeMultiplier;
            float boundsH = _sr.sprite.bounds.size.y;
            float s = boundsH > 0.001f ? effectiveHeight / boundsH : 1f;
            _baseLocalScale = new Vector3(s * _xBaseScale, s * YScaleMultiplier, 1f);
            transform.localScale = _baseLocalScale;

            // 진행방향 정렬.
            Vector3 d = to - from;
            _baseAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(_baseAngle, Vector3.forward);

            _wobblePhase = UnityEngine.Random.value * Mathf.PI * 2f;

            // 잔상 사본 — 자식으로 붙여 본체 transform을 자동 추적. local -x로 stagger(진행 반대방향).
            int rawCount = _afterimageCountOverride >= 0 ? _afterimageCountOverride : AfterimageCount;
            int n = Mathf.Clamp(rawCount, 0, 5);
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
            // Y 자라남 — 비행 진행에 따라 Y(crescent 두께) 스케일 증가. 1.0이면 변화 없음.
            float yGrow = _yGrowEnd != 1f ? Mathf.Lerp(1f, _yGrowEnd, _flightProgress01) : 1f;

            // 휘날림 — 두 주파수가 다른 sin을 합쳐 단조롭지 않은 흔들림. 인스턴스 토글로 끌 수 있음.
            if (_enableWobble && WobbleIntensity > 0.001f)
            {
                float t = Time.time;
                float w1 = Mathf.Sin(t * 17f + _wobblePhase);
                float w2 = Mathf.Sin(t * 9f + _wobblePhase * 1.7f);
                float angleJitter = (w1 * 5.5f + w2 * 2.5f) * WobbleIntensity;
                float scaleJitter = 1f + (w2 * 0.06f + w1 * 0.025f) * WobbleIntensity;

                transform.rotation = Quaternion.AngleAxis(_baseAngle + angleJitter, Vector3.forward);
                transform.localScale = new Vector3(
                    _baseLocalScale.x * scaleJitter,
                    _baseLocalScale.y * (2f - scaleJitter) * yGrow, // x 늘면 y 줄여서 천 펄럭이는 느낌 + 진행에 따라 yGrow
                    1f);
            }
            else if (yGrow != 1f)
            {
                transform.localScale = new Vector3(
                    _baseLocalScale.x,
                    _baseLocalScale.y * yGrow,
                    1f);
            }
        }

        private IEnumerator FlyRoutine(Vector3 from, Vector3 to, float duration, Action onHit)
        {
            // 첫 프레임 — 외부에서 색 override할 시간 줌. 그 후 fade 기준 색 캐시.
            transform.position = from;
            yield return null;

            Color baseBodyColor = _sr.color;
            List<Color> baseGhostColors = null;
            if (_afterimages != null)
            {
                baseGhostColors = new List<Color>(_afterimages.Count);
                foreach (var g in _afterimages)
                    baseGhostColors.Add(g != null ? g.color : Color.white);
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                _flightProgress01 = u;

                // 위치 — easeOutPower>1이면 ease-out (처음 빠르고 끝 느림).
                float posT = _easeOutPower != 1f ? 1f - Mathf.Pow(1f - u, _easeOutPower) : u;
                transform.position = Vector3.Lerp(from, to, posT);

                // 알파 페이드 — 비행 중 점점 옅어짐.
                if (_alphaFadeEnd != 1f)
                {
                    float aMul = Mathf.Lerp(1f, _alphaFadeEnd, u);
                    _sr.color = new Color(baseBodyColor.r, baseBodyColor.g, baseBodyColor.b, baseBodyColor.a * aMul);
                    if (_afterimages != null)
                    {
                        for (int i = 0; i < _afterimages.Count; i++)
                        {
                            var g = _afterimages[i];
                            if (g == null) continue;
                            var gc0 = baseGhostColors[i];
                            g.color = new Color(gc0.r, gc0.g, gc0.b, gc0.a * aMul);
                        }
                    }
                }
                yield return null;
            }
            _flightProgress01 = 1f;
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

        /// 외부에서 절차 반달 스프라이트 재활용용 (예: 플레이어 화염구 streak 등).
        /// 같은 캐시를 공유하므로 추가 메모리 비용 없음.
        public static Sprite GetSharedCrescentSprite() => GetCrescentSprite();

        /// 보스 반달의 부드러운 변형 — 가운데(가로 중심선) 진함, 위아래로 cos² 곡선 페이드.
        /// 양 끝(top/bottom)이 거의 투명해져 배경에 자연스럽게 묻힘. 가장자리 노이즈 최소화.
        public static Sprite GetSharedCrescentSpriteSoft()
        {
            if (_cachedCrescentSoft != null) return _cachedCrescentSoft;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[size * size];

            // 같은 두 원 차이 마스크 (보스와 동일).
            const float r1 = 0.46f, cx1 = 0.54f, cy1 = 0.50f;
            const float r2 = 0.43f, cx2 = 0.38f, cy2 = 0.50f;
            const float edge = 0.025f;        // 부드러운 외곽 전환

            // 양방향 cos 페이드 — 중심이 가장 진하고 X/Y 모두 바깥으로 갈수록 부드럽게 옅어짐.
            // Y 1.5(완만 — 두꺼운 본체 보이게), X 1.2(거의 균일) → 가운데 두툼한 화염, 가장자리만 부드럽게 묻힘.
            const float yFadePow = 1.5f;
            const float xFadePow = 1.2f;

            const float noiseAmt = 0.06f;     // 거의 매끈 (배경 묻힘 방해 안 되도록)
            const float noiseFreq = 3.5f;
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

                    // cos 종 모양 페이드 — 중심 진함, 바깥으로 부드럽게 감소.
                    float yDist = Mathf.Abs(v - 0.5f) * 2f;
                    float xDist = Mathf.Abs(u - 0.5f) * 2f;
                    float yFade = Mathf.Pow(Mathf.Cos(yDist * Mathf.PI * 0.5f), yFadePow);
                    float xFade = Mathf.Pow(Mathf.Cos(xDist * Mathf.PI * 0.5f), xFadePow);
                    float radialFade = yFade * xFade;

                    float n = Mathf.PerlinNoise(u * noiseFreq + noiseSeedX, v * noiseFreq + noiseSeedY);
                    float noiseFactor = Mathf.Lerp(1f - noiseAmt, 1f, n);

                    float a = crescent * radialFade * noiseFactor;
                    a = Mathf.Clamp01(a);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            _cachedCrescentSoft = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _cachedCrescentSoft.name = "CrescentSoftCosFade";
            return _cachedCrescentSoft;
        }

        /// 절차 불꽃 스프라이트 — 깔끔한 타원형 화구.
        /// 가로로 살짝 긴 부드러운 oval + radial gradient(중심 진함→가장자리 부드럽) + 외곽에만 옅은 노이즈.
        /// 비대칭/너울거림 없이 안정적인 화구 모양 (불꽃 응축된 느낌).
        public static Sprite GetSharedFlameSprite()
        {
            if (_cachedFlame != null) return _cachedFlame;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[size * size];

            // 타원: 가로(rx)가 세로(ry)보다 살짝 큼 → 진행방향으로 약간 길쭉.
            const float cx = 0.50f, cy = 0.50f;
            const float rx = 0.42f, ry = 0.36f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    float v = (y + 0.5f) / size;

                    // 정규화 거리 (0=중심, 1=타원 가장자리)
                    float dx = (u - cx) / rx;
                    float dy = (v - cy) / ry;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // 부드러운 radial falloff — 가운데 60%는 꽉 차고, 60~100%에서 부드럽게 사라짐.
                    float bodyAlpha = 1f - Mathf.SmoothStep(0.60f, 1.0f, d);

                    // 외곽 부분에만 약한 perlin noise (중심은 깨끗하게 유지).
                    float noise = Mathf.PerlinNoise(u * 5f + 3.7f, v * 5f + 9.1f);
                    float edgeMask = Mathf.SmoothStep(0.40f, 0.95f, d); // 0=중심, 1=가장자리
                    float noiseFactor = Mathf.Lerp(1f, Mathf.Lerp(0.75f, 1.05f, noise), edgeMask);
                    noiseFactor = Mathf.Clamp01(noiseFactor);

                    float a = bodyAlpha * noiseFactor;
                    a = Mathf.Clamp01(a);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            _cachedFlame = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _cachedFlame.name = "FlameProcedural_Oval";
            return _cachedFlame;
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
