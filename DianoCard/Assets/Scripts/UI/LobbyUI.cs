using System;
using System.Collections.Generic;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 메인 로비 화면. GameState == Lobby일 때만 그려짐.
///
/// Resources/Lobby/ 에서 로드되는 이미지:
/// - Main_Background: 배경
/// - DinoCard: 타이틀 로고
/// - SinglePlay / AIPlay / Settings / Quit: 버튼 이미지 (텍스트 내장)
///
/// 버튼은 GUI.DrawTexture로 이미지를 그리고, 같은 Rect로 투명 GUI.Button을 덮어서
/// 클릭을 감지하는 패턴. 호버 시 살짝 밝아짐.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    // ── Inspector 노출 글로우 위치 (1280x720 가상 좌표계) ─────────────
    [Header("Center Beam Glow")]
    [SerializeField] private Rect _centerBeamOuterRect = new Rect(540f, 5f, 200f, 545f);
    [SerializeField] private Rect _centerBeamCoreRect = new Rect(614f, 25f, 52f, 510f);
    [SerializeField] private Rect _centerHaloRect = new Rect(490f, 320f, 300f, 240f);

    [Header("Left Gem Glow (Orange Crystal)")]
    [SerializeField] private Rect _leftGemOuterRect = new Rect(60f, 300f, 230f, 230f);
    [SerializeField] private Rect _leftGemHotspotRect = new Rect(135f, 365f, 90f, 90f);

    [Header("Right Gem Glow (Blue Runestone)")]
    [SerializeField] private Rect _rightGemOuterRect = new Rect(940f, 140f, 220f, 220f);
    [SerializeField] private Rect _rightGemHotspotRect = new Rect(1010f, 205f, 80f, 80f);

    [Header("Button Glow Padding")]
    [SerializeField] private Vector2 _buttonGlowPadding = new Vector2(32f, 22f);

    [Header("Left Sparkles (Rising Glints)")]
    [SerializeField] private Rect _leftSparkleArea = new Rect(40f, 200f, 280f, 380f);
    [SerializeField, Range(0, 60)] private int _leftSparkleCount = 22;
    [SerializeField] private Color _leftSparkleColor = new Color(1f, 0.78f, 0.38f, 1f);
    [SerializeField] private Color _leftSparkleCoreColor = new Color(1f, 0.96f, 0.7f, 1f);
    [SerializeField, Range(0.2f, 4f)] private float _leftSparkleSpeed = 0.9f;
    [SerializeField] private Vector2 _leftSparkleSizeRange = new Vector2(6f, 18f);

    private readonly List<Action> _pending = new();

    // 로비 에셋
    private Texture2D _bgTexture;
    private Texture2D _titleTexture;
    private Texture2D _singlePlayTexture;
    private Texture2D _aiPlayTexture;
    private Texture2D _settingsTexture;
    private Texture2D _quitTexture;
    private Texture2D _glowTexture;

    // 배경 오버레이용 절차적 텍스처 (보석/빛기둥 펄스)
    private Texture2D _radialGlowTex;
    private Texture2D _verticalBeamTex;
    private Texture2D _sparkleTex;

    // 투명 버튼용 빈 스타일 (배경 없음)
    private GUIStyle _invisibleStyle;
    private GUIStyle _comingSoonStyle;
    private bool _stylesReady;
    private bool _assetsLoaded;

    // 버튼별 호버 확대 애니메이션 스케일
    private readonly Dictionary<string, float> _btnScales = new();
    private const float HoverScale = 1.08f;
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
        _titleTexture = Resources.Load<Texture2D>("Lobby/DinoCard");
        _singlePlayTexture = Resources.Load<Texture2D>("Lobby/SinglePlay");
        _aiPlayTexture = Resources.Load<Texture2D>("Lobby/AIPlay");
        _settingsTexture = Resources.Load<Texture2D>("Lobby/Settings");
        _quitTexture = Resources.Load<Texture2D>("Lobby/Quit");
        _glowTexture = Resources.Load<Texture2D>("Lobby/ButtonGlow");

        if (_bgTexture == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Lobby/Main_Background");
        if (_titleTexture == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Lobby/DinoCard");
        if (_singlePlayTexture == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Lobby/SinglePlay");
        if (_aiPlayTexture == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Lobby/AIPlay");
        if (_settingsTexture == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Lobby/Settings");
        if (_quitTexture == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Lobby/Quit");
        if (_glowTexture == null) Debug.LogWarning("[LobbyUI] Missing: Resources/Lobby/ButtonGlow");

        _assetsLoaded = true;
    }

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.Lobby) return;

        if (!_assetsLoaded) LoadAssets();
        EnsureStyles();

        // 1) 배경은 스크린 원본 좌표로 꽉 채움
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

        // 2) 이후는 1280x720 가상 좌표로 스케일링
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        DrawAmbientGlow();
        DrawTitle();
        DrawButtons(gsm);
        DrawVersion();
    }

    // ---------------------------------------------------------
    // 배경의 보석/빛기둥 위에 펄스하는 오버레이를 깔아 생동감 부여.
    // 위치는 1280x720 기준. 16:9가 아닐 경우 약간 어긋날 수 있음.
    private void DrawAmbientGlow()
    {
        EnsureGlowTextures();
        if (_radialGlowTex == null || _verticalBeamTex == null) return;

        float t = Time.unscaledTime;
        var prev = GUI.color;

        // ── 가운데 빛기둥 ────────────────────────────────────────
        // 느리게 숨쉬는 황금빛. 외곽 + 더 밝은 코어 두 겹.
        float beamPulse = 0.55f + 0.35f * Mathf.Sin(t * 1.25f);
        GUI.color = new Color(1f, 0.86f, 0.45f, 0.55f * beamPulse);
        GUI.DrawTexture(_centerBeamOuterRect, _verticalBeamTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = new Color(1f, 0.96f, 0.72f, 0.5f * beamPulse);
        GUI.DrawTexture(_centerBeamCoreRect, _verticalBeamTex, ScaleMode.StretchToFill, alphaBlend: true);

        // 흐릿한 헤일로 (빛기둥 베이스에서 살짝 부풀어 오름)
        float halo = 0.5f + 0.5f * Mathf.Sin(t * 1.6f + 0.4f);
        GUI.color = new Color(1f, 0.88f, 0.5f, 0.35f * halo);
        GUI.DrawTexture(_centerHaloRect, _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);

        // ── 왼쪽 보석 (주황 크리스탈) ───────────────────────────
        float leftPulse = 0.6f + 0.4f * Mathf.Sin(t * 2.7f + 0.7f);
        GUI.color = new Color(1f, 0.62f, 0.22f, 0.85f * leftPulse);
        GUI.DrawTexture(_leftGemOuterRect, _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        // 안쪽 핫스팟
        GUI.color = new Color(1f, 0.92f, 0.55f, 0.75f * leftPulse);
        GUI.DrawTexture(_leftGemHotspotRect, _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);

        // ── 오른쪽 보석 (푸른 룬스톤) ────────────────────────────
        float rightPulse = 0.6f + 0.4f * Mathf.Sin(t * 2.05f + 1.9f);
        GUI.color = new Color(0.45f, 0.85f, 1f, 0.8f * rightPulse);
        GUI.DrawTexture(_rightGemOuterRect, _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = new Color(0.78f, 0.96f, 1f, 0.7f * rightPulse);
        GUI.DrawTexture(_rightGemHotspotRect, _radialGlowTex, ScaleMode.StretchToFill, alphaBlend: true);

        GUI.color = prev;

        DrawLeftSparkles(t);
    }

    private void EnsureGlowTextures()
    {
        if (_radialGlowTex == null) _radialGlowTex = MakeRadialGlow(128);
        if (_verticalBeamTex == null) _verticalBeamTex = MakeVerticalBeam(32, 256);
        if (_sparkleTex == null) _sparkleTex = MakeSparkle(64);
    }

    // 왼쪽 보석 주변에서 위로 떠오르는 주황톤 반짝이.
    // 상태 없이 시간 기반 결정 함수로 각 파티클의 진행도를 계산한다.
    private void DrawLeftSparkles(float t)
    {
        if (_sparkleTex == null || _leftSparkleCount <= 0) return;
        if (_leftSparkleArea.width <= 0f || _leftSparkleArea.height <= 0f) return;

        var prev = GUI.color;
        for (int i = 0; i < _leftSparkleCount; i++)
        {
            // 파티클별 결정적 시드 (위치/속도/주기 분산용)
            float seed = (i * 0.6180339f) % 1f;
            float speed = _leftSparkleSpeed * (0.7f + seed * 0.8f);
            float phase = seed * 7.13f;
            float life = ((t * speed) + phase) % 1f;

            // 수평 위치: 살짝 좌우로 흔들리며 올라감
            float hBase = Hash01(i * 12.9898f);
            float sway = Mathf.Sin(life * Mathf.PI * 2f + seed * 6f) * 0.06f;
            float x = _leftSparkleArea.x + (hBase + sway) * _leftSparkleArea.width;

            // 수직 위치: 아래에서 위로 상승
            float y = _leftSparkleArea.yMax - life * _leftSparkleArea.height;

            // 크기: 시작은 작게 → 중간에 커짐 → 위로 갈수록 다시 작아짐
            float sizeT = Mathf.Sin(life * Mathf.PI);
            float baseSize = Mathf.Lerp(_leftSparkleSizeRange.x, _leftSparkleSizeRange.y, Hash01(i * 37.719f));
            float size = baseSize * (0.5f + 0.5f * sizeT);

            // 알파: 페이드 인/아웃 + 짧은 트윙클
            float fade = Mathf.Sin(life * Mathf.PI);
            float twinkle = 0.65f + 0.35f * Mathf.Sin(t * 8f + seed * 17f);
            float alpha = fade * twinkle;

            var rect = new Rect(x - size * 0.5f, y - size * 0.5f, size, size);

            // 외곽 글로우 (보석 톤)
            GUI.color = new Color(_leftSparkleColor.r, _leftSparkleColor.g, _leftSparkleColor.b,
                _leftSparkleColor.a * alpha * 0.9f);
            GUI.DrawTexture(rect, _sparkleTex, ScaleMode.StretchToFill, alphaBlend: true);

            // 내부 코어 (작고 밝게)
            var coreRect = new Rect(rect.x + size * 0.3f, rect.y + size * 0.3f, size * 0.4f, size * 0.4f);
            GUI.color = new Color(_leftSparkleCoreColor.r, _leftSparkleCoreColor.g, _leftSparkleCoreColor.b,
                _leftSparkleCoreColor.a * alpha);
            GUI.DrawTexture(coreRect, _sparkleTex, ScaleMode.StretchToFill, alphaBlend: true);
        }
        GUI.color = prev;
    }

    private static float Hash01(float x)
    {
        float s = Mathf.Sin(x) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    // 네 갈래 빛살 + 부드러운 중심 코어를 가진 반짝이 스프라이트.
    private static Texture2D MakeSparkle(int size)
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
                float r = Mathf.Sqrt(dx * dx + dy * dy);

                // 중심 코어 (radial soft falloff)
                float core = Mathf.Clamp01(1f - r);
                core = core * core * (3f - 2f * core);

                // 십자 빛살 (축에 가까울수록 밝게, 반지름에 따라 감쇠)
                float ax = Mathf.Abs(dx);
                float ay = Mathf.Abs(dy);
                float streakH = Mathf.Clamp01(1f - ay / 0.08f) * Mathf.Clamp01(1f - ax);
                float streakV = Mathf.Clamp01(1f - ax / 0.08f) * Mathf.Clamp01(1f - ay);
                float streak = Mathf.Max(streakH, streakV);
                streak *= streak;

                float a = Mathf.Clamp01(core * 0.85f + streak * 0.9f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
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
                a = a * a * (3f - 2f * a); // smoothstep 형태의 부드러운 폴오프
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeVerticalBeam(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color[w * h];
        float cx = (w - 1) * 0.5f;
        float maxX = w * 0.5f;
        for (int y = 0; y < h; y++)
        {
            float vy = y / (float)(h - 1);
            float vAlpha = Mathf.Sin(vy * Mathf.PI); // 위/아래 페이드, 가운데 최대
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs(x - cx) / maxX;
                float hAlpha = Mathf.Clamp01(1f - dx);
                hAlpha = hAlpha * hAlpha;
                px[y * w + x] = new Color(1f, 1f, 1f, hAlpha * vAlpha);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (_radialGlowTex != null) Destroy(_radialGlowTex);
        if (_verticalBeamTex != null) Destroy(_verticalBeamTex);
        if (_sparkleTex != null) Destroy(_sparkleTex);
    }

    // ---------------------------------------------------------

    private void DrawTitle()
    {
        if (_titleTexture == null) return;

        // Main.png 레퍼런스: 폭 ~33%, 상단 ~3%
        const float targetW = 420f;
        float aspect = (float)_titleTexture.height / _titleTexture.width;
        float h = targetW * aspect;

        var rect = new Rect((RefW - targetW) / 2f, 22f, targetW, h);
        GUI.DrawTexture(rect, _titleTexture, ScaleMode.ScaleToFit, alphaBlend: true);
    }

    private void DrawButtons(GameStateManager gsm)
    {
        // 버튼 배치 (1280x720 기준) — Main.png 레퍼런스 하단 중앙 스택
        const float btnW = 240f;
        float btnH = 58f;
        if (_singlePlayTexture != null)
        {
            // 텍스처 종횡비를 그대로 따른다 (왜곡 방지)
            btnH = btnW * (float)_singlePlayTexture.height / _singlePlayTexture.width;
        }

        const float gap = 10f;
        float spacing = btnH + gap;
        float totalH = btnH * 4f + gap * 3f;
        float startY = RefH - totalH - 50f; // 하단에서 50px 여백
        float x = (RefW - btnW) / 2f;

        // 1) Single Play — 전투 시작
        if (DrawImageButton(new Rect(x, startY, btnW, btnH), _singlePlayTexture, "SINGLE PLAY", true))
        {
            _pending.Add(() => gsm.StartNewRun());
        }

        // 2) AI Play — MVP에서는 비활성화
        DrawImageButton(new Rect(x, startY + spacing * 1, btnW, btnH), _aiPlayTexture, "AI PLAY", false);
        DrawComingSoonOverlay(new Rect(x, startY + spacing * 1, btnW, btnH));

        // 3) Settings — MVP에서는 비활성화
        DrawImageButton(new Rect(x, startY + spacing * 2, btnW, btnH), _settingsTexture, "SETTINGS", false);
        DrawComingSoonOverlay(new Rect(x, startY + spacing * 2, btnW, btnH));

        // 4) Quit
        if (DrawImageButton(new Rect(x, startY + spacing * 3, btnW, btnH), _quitTexture, "QUIT", true))
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

    /// <summary>
    /// 이미지 버튼. texture가 null이면 폴백 텍스트 버튼.
    /// enabled == false면 어둡게 그리고 클릭 무시.
    /// </summary>
    private bool DrawImageButton(Rect rect, Texture2D texture, string fallbackText, bool enabled)
    {
        if (!_btnScales.TryGetValue(fallbackText, out float curScale)) curScale = 1f;
        Rect drawRect = ScaleRectAroundCenter(rect, curScale);

        bool hovered = false;
        if (enabled && Event.current != null && Event.current.type == EventType.Repaint)
        {
            hovered = drawRect.Contains(Event.current.mousePosition);
            float targetScale = hovered ? HoverScale : 1f;
            float t = 1f - Mathf.Exp(-ScaleLerpSpeed * Time.unscaledDeltaTime);
            curScale = Mathf.Lerp(curScale, targetScale, t);
            if (Mathf.Abs(curScale - targetScale) < 0.001f) curScale = targetScale;
            _btnScales[fallbackText] = curScale;
            drawRect = ScaleRectAroundCenter(rect, curScale);
        }

        var prevColor = GUI.color;

        if (texture != null)
        {
            // 글로우는 enabled일 때만, 텍스처보다 먼저 (뒤쪽에) 그린다.
            if (enabled && _glowTexture != null)
            {
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 2.2f);
                float alpha = hovered ? 0.95f : 0.45f * pulse;
                var glowRect = new Rect(
                    drawRect.x - _buttonGlowPadding.x,
                    drawRect.y - _buttonGlowPadding.y,
                    drawRect.width + _buttonGlowPadding.x * 2f,
                    drawRect.height + _buttonGlowPadding.y * 2f);
                GUI.color = new Color(1f, 0.85f, 0.4f, alpha);
                GUI.DrawTexture(glowRect, _glowTexture, ScaleMode.StretchToFill, alphaBlend: true);
                GUI.color = prevColor;
            }

            if (!enabled)
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.75f);
            else if (hovered)
                GUI.color = new Color(1.18f, 1.18f, 1.18f, 1f);

            GUI.DrawTexture(drawRect, texture, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.color = prevColor;

            // 투명 클릭 영역
            if (enabled)
            {
                return GUI.Button(drawRect, GUIContent.none, _invisibleStyle);
            }
            else
            {
                // 무시되는 클릭: consume해서 뒤에 깔린 버튼에 가지 않게 함
                GUI.Button(drawRect, GUIContent.none, _invisibleStyle);
                return false;
            }
        }

        // 폴백: 일반 텍스트 버튼
        GUI.enabled = enabled;
        bool clicked = GUI.Button(drawRect, fallbackText);
        GUI.enabled = true;
        return clicked;
    }

    private static Rect ScaleRectAroundCenter(Rect r, float s)
    {
        if (Mathf.Approximately(s, 1f)) return r;
        float w = r.width * s;
        float h = r.height * s;
        return new Rect(r.x - (w - r.width) * 0.5f, r.y - (h - r.height) * 0.5f, w, h);
    }

    private void DrawComingSoonOverlay(Rect buttonRect)
    {
        // 버튼 우측 바깥에 작은 리본. 버튼 영역을 가리지 않게 8px 띄움.
        const float w = 86f;
        const float h = 20f;
        var label = new Rect(buttonRect.xMax + 8f, buttonRect.center.y - h / 2f, w, h);

        var prev = GUI.color;
        GUI.color = new Color(0.08f, 0.06f, 0.04f, 0.85f);
        GUI.DrawTexture(label, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.85f, 0.4f, 1f);
        GUI.Label(label, "Coming Soon", _comingSoonStyle);
        GUI.color = prev;
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

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _invisibleStyle = new GUIStyle(); // 배경/테두리 없음
        _comingSoonStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.9f, 0.5f) },
        };
        _stylesReady = true;
    }
}
