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

    private readonly List<Action> _pending = new();

    // 로비 에셋
    private Texture2D _bgTexture;
    private Texture2D _titleTexture;
    private Texture2D _singlePlayTexture;
    private Texture2D _aiPlayTexture;
    private Texture2D _settingsTexture;
    private Texture2D _quitTexture;
    private Texture2D _glowTexture;

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

        DrawTitle();
        DrawButtons(gsm);
        DrawVersion();
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
                var glowRect = new Rect(drawRect.x - 32f, drawRect.y - 22f, drawRect.width + 64f, drawRect.height + 44f);
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
