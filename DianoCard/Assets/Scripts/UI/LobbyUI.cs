using System;
using System.Collections.Generic;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 메인 로비 화면. GameState == Lobby일 때만 그려짐.
///
/// Resources/Lobby/Main_Background 를 배경으로 깔고, 우측에 Cinzel 폰트로
/// Single Play / Settings / Quit 텍스트 메뉴를 렌더.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    // ── 불꽃 파티클 ─────────────────────────────────────────────
    [Serializable]
    public class EmberEmitter
    {
        public string name = "Ember";
        public bool enabled = true;

        [Header("Spawn Area (1280x720 가상 좌표)")]
        public Rect spawnRect = new Rect(200f, 290f, 20f, 18f);

        [Header("Count & Size")]
        [Range(0, 80)] public int count = 18;
        public Vector2 sizeRange = new Vector2(5f, 14f);

        [Header("Motion")]
        [Range(20f, 300f)] public float riseHeight = 90f;
        [Range(0.1f, 3f)] public float riseSpeed = 0.9f;
        [Range(0f, 40f)] public float swayAmount = 8f;
        [Range(0.1f, 6f)] public float swayFrequency = 1.2f;

        [Header("Color")]
        public Color innerColor = new Color(1f, 0.95f, 0.55f, 1f);
        public Color outerColor = new Color(1f, 0.35f, 0.08f, 1f);
        [Range(0f, 2f)] public float alphaMul = 1f;

        [Header("Flicker")]
        [Range(0f, 30f)] public float flickerSpeed = 14f;
        [Range(0f, 1f)] public float flickerDepth = 0.35f;

        [Header("Inner Glow")]
        [Range(1f, 6f), Tooltip("파티클 주변 부드러운 블룸 크기 (코어 대비 배수).")] public float bloomScale = 3.2f;
        [Range(0f, 1f), Tooltip("블룸 알파 배수.")] public float bloomAlphaMul = 0.35f;
        [Range(0f, 1f), Tooltip("중심 흰 하이라이트 크기 (코어 대비 비율).")] public float hotCoreRatio = 0.35f;
        [Range(0f, 2f), Tooltip("중심 흰 하이라이트 세기.")] public float hotCoreIntensity = 1.2f;

        [Header("Anchor Halo (이미터 중심 고정 헤일로)")]
        [Range(0f, 300f), Tooltip("0이면 비활성. 이미터 중심에 큰 글로우 원을 상시 드로우 (손의 불덩어리 느낌).")] public float haloSize = 0f;
        [Range(0f, 1f)] public float haloAlpha = 0.5f;
        public Color haloColor = new Color(1f, 0.55f, 0.15f, 1f);
        [Range(0f, 5f), Tooltip("헤일로 맥동 속도. 0이면 고정.")] public float haloPulseSpeed = 2.2f;
        [Range(0.3f, 3f), Tooltip("헤일로 세로/가로 비율. 1=원, >1=세로 타원(불덩어리 형태).")] public float haloAspect = 1f;

        [Header("Shape")]
        [Range(0.1f, 1f), Tooltip("파티클이 위로 올라갈수록 중앙으로 수렴하는 비율. 1=폭 유지, 0.3=상단 폭 30%.")] public float topWidthRatio = 1f;

        [Header("Flame Sprite (Particle Pack)")]
        [Tooltip("true면 코어를 실제 불꽃 스프라이트로 렌더. false면 원형 글로우.")]
        public bool useFlameSprite = false;
        [Range(1f, 3f), Tooltip("스프라이트 세로/가로 비율.")] public float flameAspect = 1.7f;
        [Range(0.5f, 4f), Tooltip("스프라이트 크기 배수 (코어 size 대비).")] public float flameScale = 1.8f;

        [Header("Seed")]
        public int seedOffset = 0;
    }

    [Header("Fire Ember Emitters")]
    [SerializeField]
    private List<EmberEmitter> _emberEmitters = new List<EmberEmitter>
    {
        new EmberEmitter
        {
            name = "Fire Hand",
            spawnRect = new Rect(135f, 295f, 110f, 35f),
            count = 0,
            haloSize = 140f,
            haloAlpha = 0.85f,
            haloColor = new Color(1f, 0.55f, 0.15f, 1f),
            haloPulseSpeed = 2.4f,
            haloAspect = 1.4f,
            useFlameSprite = false,
            seedOffset = 0,
        },
        new EmberEmitter
        {
            name = "Bottom Smoke",
            spawnRect = new Rect(0f, 695f, 1280f, 25f),
            count = 24,
            sizeRange = new Vector2(30f, 55f),
            riseHeight = 120f,
            riseSpeed = 0.15f,
            swayAmount = 25f,
            swayFrequency = 0.4f,
            innerColor = new Color(0.6f, 0.55f, 0.55f, 1f),
            outerColor = new Color(0.35f, 0.32f, 0.32f, 1f),
            alphaMul = 0.35f,
            flickerSpeed = 2f,
            flickerDepth = 0.2f,
            bloomScale = 4.5f,
            bloomAlphaMul = 0.55f,
            hotCoreRatio = 0f,
            hotCoreIntensity = 0f,
            haloSize = 0f,
            topWidthRatio = 1f,
            useFlameSprite = false,
            seedOffset = 500,
        },
        new EmberEmitter
        {
            name = "Title (Last Ember)",
            spawnRect = new Rect(540f, 80f, 620f, 40f),
            count = 30,
            sizeRange = new Vector2(3f, 3f),
            riseHeight = 80f,
            riseSpeed = 0.4f,
            swayAmount = 6f,
            swayFrequency = 0.8f,
            innerColor = new Color(1f, 0.9f, 0.55f, 1f),
            outerColor = new Color(1f, 0.45f, 0.12f, 1f),
            alphaMul = 0.75f,
            flickerSpeed = 9f,
            flickerDepth = 0.4f,
            bloomScale = 2.8f,
            bloomAlphaMul = 0.3f,
            hotCoreRatio = 0.3f,
            hotCoreIntensity = 1f,
            haloSize = 0f,
            seedOffset = 200,
        },
    };

    private readonly List<Action> _pending = new();

    private Texture2D _bgTexture;
    private Texture2D _emberTex;
    private Texture2D[] _flameTextures;
    private Font _displayFont;

    private GUIStyle _invisibleStyle;
    private GUIStyle _menuTextStyle;
    private GUIStyle _menuTextShadowStyle;
    private GUIStyle _devBtnStyle;
    private bool _stylesReady;
    private bool _assetsLoaded;

    private readonly Dictionary<string, float> _btnScales = new();
    private const float ScaleLerpSpeed = 14f;

    void Start()
    {
        LoadAssets();
    }

    void Update()
    {
        if (_pending.Count == 0) return;
        var snapshot = new List<Action>(_pending);
        _pending.Clear();
        foreach (var a in snapshot) a?.Invoke();
    }

    private void LoadAssets()
    {
        _bgTexture = Resources.Load<Texture2D>("Lobby/Main_Background");
        _displayFont = Resources.Load<Font>("Fonts/IMFellEnglish-Regular");

        var names = new[] { "Flame02", "Flame03", "Flame04", "MediumFlame01", "TinyFlame" };
        var flames = new List<Texture2D>(names.Length);
        foreach (var n in names)
        {
            var tex = Resources.Load<Texture2D>("FX/Flames/" + n);
            if (tex != null) flames.Add(tex);
        }
        _flameTextures = flames.ToArray();

        if (_bgTexture == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Lobby/Main_Background");
        if (_displayFont == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Fonts/IMFellEnglish-Regular");
        if (_flameTextures.Length == 0) Debug.LogWarning("[LobbyUI] Missing: Resources/FX/Flames/* (falling back to radial glow)");

        _assetsLoaded = true;
    }

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.Lobby) return;

        if (!_assetsLoaded) LoadAssets();
        EnsureStyles();

        GUI.matrix = Matrix4x4.identity;
        if (_bgTexture != null)
        {
            GUI.DrawTexture(
                new Rect(0, 0, Screen.width, Screen.height),
                _bgTexture,
                ScaleMode.ScaleAndCrop,
                alphaBlend: true);
        }
        else
        {
            var prev = GUI.color;
            GUI.color = new Color(0.08f, 0.06f, 0.12f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        DrawEmbers();
        DrawButtons(gsm);
        DrawVersion();
        DrawDevTools(gsm);
    }

    private void DrawEmbers()
    {
        if (_emberEmitters == null || _emberEmitters.Count == 0) return;
        if (_emberTex == null) _emberTex = MakeRadialGlow(64);
        if (_emberTex == null) return;

        float t = Time.unscaledTime;
        var prev = GUI.color;
        for (int i = 0; i < _emberEmitters.Count; i++)
        {
            DrawEmberEmitter(_emberEmitters[i], t);
        }
        GUI.color = prev;
    }

    private void DrawEmberEmitter(EmberEmitter em, float t)
    {
        if (em == null || !em.enabled) return;
        if (em.spawnRect.width <= 0f || em.spawnRect.height <= 0f) return;

        // 이미터 중심의 고정 헤일로 — 손의 불덩어리처럼 상시 빛을 발산
        if (em.haloSize > 0f && em.haloAlpha > 0f)
        {
            float haloCx = em.spawnRect.x + em.spawnRect.width * 0.5f;
            float haloCy = em.spawnRect.y + em.spawnRect.height * 0.5f;
            // 두 개의 사인파를 섞어 자연스러운 숨쉬기 — 깊이 0.7로 크게 피었다 사그라들게
            float haloPulse = 1f;
            float sizePulse = 1f;
            if (em.haloPulseSpeed > 0f)
            {
                float s1 = Mathf.Sin(t * em.haloPulseSpeed);
                float s2 = Mathf.Sin(t * em.haloPulseSpeed * 2.7f + 1.3f);
                float combined = s1 * 0.65f + s2 * 0.35f; // -1 ~ 1
                haloPulse = Mathf.Clamp01(0.5f + 0.5f * combined); // 0 ~ 1 전범위
                haloPulse = Mathf.Lerp(0.2f, 1.25f, haloPulse);    // 꺼졌다 ~ 피크
                sizePulse = 1f + combined * 0.1f;                   // ±10% 크기 숨쉬기
            }
            float haloS = em.haloSize * sizePulse;
            float haloW = haloS;
            float haloH = haloS * em.haloAspect;
            // 외곽 soft halo
            GUI.color = new Color(em.haloColor.r, em.haloColor.g, em.haloColor.b,
                em.haloColor.a * em.haloAlpha * 0.55f * haloPulse);
            GUI.DrawTexture(
                new Rect(haloCx - haloW * 0.5f, haloCy - haloH * 0.5f, haloW, haloH),
                _emberTex, ScaleMode.StretchToFill, alphaBlend: true);
            // 안쪽 집중 글로우 (더 작고 진하게)
            float innerW = haloW * 0.55f;
            float innerH = haloH * 0.55f;
            GUI.color = new Color(1f, 0.85f, 0.5f, em.haloAlpha * 0.85f * haloPulse);
            GUI.DrawTexture(
                new Rect(haloCx - innerW * 0.5f, haloCy - innerH * 0.5f, innerW, innerH),
                _emberTex, ScaleMode.StretchToFill, alphaBlend: true);
        }

        if (em.count <= 0) return;

        for (int i = 0; i < em.count; i++)
        {
            int idx = i + em.seedOffset;
            float seed = Hash01(idx * 0.6180339f + 0.13f);
            float speed = em.riseSpeed * (0.75f + seed * 0.6f);
            float phase = seed * 7.13f;
            float life = ((t * speed) + phase) % 1f;
            if (life < 0f) life += 1f;

            float spawnU = Hash01(idx * 12.9898f);
            float spawnV = Hash01(idx * 78.233f);
            float sway = Mathf.Sin(life * Mathf.PI * 2f * em.swayFrequency + seed * 6f) * em.swayAmount;

            float narrow = Mathf.Lerp(1f, em.topWidthRatio, life);
            float centerX = em.spawnRect.x + em.spawnRect.width * 0.5f;
            float x = centerX + (spawnU - 0.5f) * em.spawnRect.width * narrow + sway * narrow;
            float y = em.spawnRect.y + spawnV * em.spawnRect.height - life * em.riseHeight;

            float sizeT = Mathf.Sin(life * Mathf.PI);
            float baseSize = Mathf.Lerp(em.sizeRange.x, em.sizeRange.y, Hash01(idx * 37.719f));
            float size = baseSize * (0.45f + 0.55f * sizeT);

            float fade = Mathf.Sin(life * Mathf.PI);
            float flicker = (1f - em.flickerDepth) + em.flickerDepth * Mathf.Sin(t * em.flickerSpeed + seed * 17f);
            float alpha = Mathf.Clamp01(fade * flicker) * em.alphaMul;

            // 1) 가장 바깥 블룸 — 크고 흐리게 (내부 글로우 느낌)
            float bloomSize = size * em.bloomScale;
            GUI.color = new Color(em.outerColor.r, em.outerColor.g, em.outerColor.b,
                em.outerColor.a * alpha * em.bloomAlphaMul);
            GUI.DrawTexture(
                new Rect(x - bloomSize * 0.5f, y - bloomSize * 0.5f, bloomSize, bloomSize),
                _emberTex, ScaleMode.StretchToFill, alphaBlend: true);

            bool useFlame = em.useFlameSprite && _flameTextures != null && _flameTextures.Length > 0;
            if (useFlame)
            {
                // 코어를 실제 불꽃 스프라이트로 — 세로 길쭉, 파티클마다 다른 모양
                var flameTex = _flameTextures[idx % _flameTextures.Length];
                float flameW = size * em.flameScale;
                float flameH = flameW * em.flameAspect;
                // 스프라이트는 위쪽이 뾰족 → 중심을 살짝 아래로 (y는 불꽃 중앙, pivot을 bottom-center 느낌으로)
                float flameCy = y - flameH * 0.15f;
                GUI.color = new Color(em.innerColor.r, em.innerColor.g, em.innerColor.b,
                    em.innerColor.a * alpha);
                GUI.DrawTexture(
                    new Rect(x - flameW * 0.5f, flameCy - flameH * 0.5f, flameW, flameH),
                    flameTex, ScaleMode.StretchToFill, alphaBlend: true);
            }
            else
            {
                // 2) 중간 글로우 (오렌지→레드)
                float glowSize = size * 1.6f;
                GUI.color = new Color(em.outerColor.r, em.outerColor.g, em.outerColor.b,
                    em.outerColor.a * alpha * 0.7f);
                GUI.DrawTexture(
                    new Rect(x - glowSize * 0.5f, y - glowSize * 0.5f, glowSize, glowSize),
                    _emberTex, ScaleMode.StretchToFill, alphaBlend: true);

                // 3) 안쪽 컬러 코어
                GUI.color = new Color(em.innerColor.r, em.innerColor.g, em.innerColor.b,
                    em.innerColor.a * alpha);
                GUI.DrawTexture(
                    new Rect(x - size * 0.5f, y - size * 0.5f, size, size),
                    _emberTex, ScaleMode.StretchToFill, alphaBlend: true);
            }

            // 4) 중심 흰 하이라이트 — "뜨거운 심" 표현 (스프라이트 모드에선 살짝 약하게)
            if (em.hotCoreRatio > 0f && em.hotCoreIntensity > 0f)
            {
                float hotSize = size * em.hotCoreRatio;
                float hotMul = useFlame ? 0.7f : 1f;
                float hotA = Mathf.Clamp01(alpha * em.hotCoreIntensity * hotMul);
                GUI.color = new Color(1f, 0.98f, 0.85f, hotA);
                GUI.DrawTexture(
                    new Rect(x - hotSize * 0.5f, y - hotSize * 0.5f, hotSize, hotSize),
                    _emberTex, ScaleMode.StretchToFill, alphaBlend: true);
            }
        }
    }

    private static float Hash01(float x)
    {
        float s = Mathf.Sin(x) * 43758.5453f;
        s -= Mathf.Floor(s);
        return s;
    }

    private static Texture2D MakeRadialGlow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color[size * size];
        float c = (size - 1) * 0.5f;
        float maxR = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / maxR;
                float dy = (y - c) / maxR;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (_emberTex != null) Destroy(_emberTex);
    }

    private void DrawButtons(GameStateManager gsm)
    {
        const float btnW = 360f;
        const float btnH = 62f;
        const float gap = 18f;
        float startY = RefH * 0.55f;
        float x = RefW * 0.68f;

        if (DrawTextMenuItem(new Rect(x, startY, btnW, btnH), "Single Play", "SINGLE PLAY", true))
        {
            _pending.Add(() => gsm.StartNewRun());
        }

        DrawTextMenuItem(new Rect(x, startY + (btnH + gap) * 1, btnW, btnH), "Settings", "SETTINGS", false);

        if (DrawTextMenuItem(new Rect(x, startY + (btnH + gap) * 2, btnW, btnH), "Quit", "QUIT", true))
        {
            _pending.Add(() =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
        }
    }

    private bool DrawTextMenuItem(Rect rect, string label, string key, bool enabled)
    {
        if (!_btnScales.TryGetValue(key, out float curScale)) curScale = 1f;
        Rect drawRect = ScaleRectAroundCenter(rect, curScale);

        bool hovered = false;
        if (enabled && Event.current != null && Event.current.type == EventType.Repaint)
        {
            hovered = drawRect.Contains(Event.current.mousePosition);
            float targetScale = hovered ? 1.06f : 1f;
            float t = 1f - Mathf.Exp(-ScaleLerpSpeed * Time.unscaledDeltaTime);
            curScale = Mathf.Lerp(curScale, targetScale, t);
            if (Mathf.Abs(curScale - targetScale) < 0.001f) curScale = targetScale;
            _btnScales[key] = curScale;
            drawRect = ScaleRectAroundCenter(rect, curScale);
        }

        var prev = GUI.color;

        // 그림자 — 멀티 패스로 깊이 강화 (뒤로 갈수록 흐리게)
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.Label(new Rect(drawRect.x + 7f, drawRect.y + 8f, drawRect.width, drawRect.height), label, _menuTextShadowStyle);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.Label(new Rect(drawRect.x + 5f, drawRect.y + 6f, drawRect.width, drawRect.height), label, _menuTextShadowStyle);
        GUI.color = new Color(0f, 0f, 0f, 0.95f);
        GUI.Label(new Rect(drawRect.x + 3f, drawRect.y + 4f, drawRect.width, drawRect.height), label, _menuTextShadowStyle);
        GUI.Label(new Rect(drawRect.x + 4f, drawRect.y + 4f, drawRect.width, drawRect.height), label, _menuTextShadowStyle);
        GUI.color = prev;

        // 메인 텍스트 — 3중 오프셋 드로우로 두께 강화 (파치먼트 노란회색)
        if (enabled && hovered)
        {
            GUI.color = new Color(1f, 0.82f, 0.45f, 1f);
        }
        GUI.Label(drawRect, label, _menuTextStyle);
        GUI.color = prev;

        if (enabled)
        {
            return GUI.Button(drawRect, GUIContent.none, _invisibleStyle);
        }
        GUI.Button(drawRect, GUIContent.none, _invisibleStyle);
        return false;
    }

    private static Rect ScaleRectAroundCenter(Rect r, float s)
    {
        if (Mathf.Approximately(s, 1f)) return r;
        float w = r.width * s;
        float h = r.height * s;
        return new Rect(r.x - (w - r.width) * 0.5f, r.y - (h - r.height) * 0.5f, w, h);
    }

    private void DrawVersion()
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = new Color(0.85f, 0.85f, 0.7f, 0.85f) },
        };
        GUI.Label(new Rect(RefW - 130, RefH - 32, 110, 22), "v0.1 MVP", style);
    }

    private void DrawDevTools(GameStateManager gsm)
    {
        if (_devBtnStyle == null)
        {
            _devBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.88f, 0.55f), background = null },
                hover = { textColor = Color.white, background = null },
                active = { textColor = new Color(1f, 0.95f, 0.7f), background = null },
            };
            _devBtnStyle.border = new RectOffset(0, 0, 0, 0);
            _devBtnStyle.padding = new RectOffset(8, 8, 4, 4);
        }

        const float w = 130f, h = 26f;
        var rect = new Rect(RefW - w - 12f, 8f, w, h);

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;

        if (GUI.Button(rect, "[ 애니 테스트 ]", _devBtnStyle))
        {
            _pending.Add(() => gsm.EnterAnimationTest());
        }
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _invisibleStyle = new GUIStyle();

        _menuTextStyle = new GUIStyle(GUI.skin.label)
        {
            font = _displayFont,
            fontSize = 46,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Normal,
            // 파치먼트 노란회색 — 배경이 밝아도 눈에 띄는 따뜻한 오프화이트
            normal = { textColor = new Color(0.93f, 0.86f, 0.66f, 1f) },
        };
        _menuTextShadowStyle = new GUIStyle(_menuTextStyle)
        {
            normal = { textColor = Color.black },
        };
        _stylesReady = true;
    }
}
