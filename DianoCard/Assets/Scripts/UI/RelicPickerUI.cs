using System.Collections.Generic;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 시작 유물 선택 화면. GameState == RelicPick 일 때만 그려짐.
/// 진입 시 bgVariantRoots 중 하나를 랜덤 선택해 배경으로 사용.
///
/// 에셋 경로:
///   Resources/UI/RelicPicker_{1|2|3}/BGFrames/frame_001.png ~ frame_NNN.png — 배경 프레임 시퀀스
///   Resources/UI/RelicPicker_{1|2|3}/{fallbackName}.png                    — 프레임 없을 때 정적 폴백
///   Resources/UI/RelicPicker_1/RelicPickerRowBg.png                        — 행 배경 바
///   Resources/InGame/RelicArt/{id}.png                                     — 유물 아이콘
/// </summary>
[DefaultExecutionOrder(1050)]
public class RelicPickerUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    // ─── Background Animation ────────────────────────────────────
    [Header("Background Animation")]
    [Tooltip("기본 프레임 재생 속도 (fps). variant별 오버라이드 없을 때 사용.")]
    [SerializeField, Range(1f, 30f)] private float bgFps = 12f;

    [Tooltip("랜덤 선택 후보 폴더 루트들. Resources/ 이하 상대경로. 진입 시 하나 랜덤 선택.")]
    [SerializeField] private string[] bgVariantRoots = new[]
    {
        "UI/RelicPicker_1",
        "UI/RelicPicker_2",
        "UI/RelicPicker_3",
    };

    [Tooltip("variant별 fps 오버라이드. bgVariantRoots와 같은 길이/순서. 0 이하면 bgFps 사용.")]
    [SerializeField] private float[] bgVariantFps = new[]
    {
        0f,    // RelicPicker_1: default
        6f,    // RelicPicker_2: 절반 속도 (느리게)
        0f,    // RelicPicker_3: default
    };

    [Tooltip("variant root 하위에서 프레임을 찾을 폴더명.")]
    [SerializeField] private string bgFrameSubfolder = "BGFrames";

    [Tooltip("variant root 하위 정적 폴백 텍스처 파일명들. 첫 발견 사용.")]
    [SerializeField] private string[] bgFallbackNames = new[]
    {
        "RelicPickerBackground",
        "유물을_선택하세요_1",
        "유물을_선택하세요_2",
        "황혼_절벽_—_바람의_정령_1",
    };

    [Tooltip("프레임 파일명 prefix. 예: frame_ → frame_001.png, frame_002.png ...")]
    [SerializeField] private string bgFramePrefix = "frame_";

    [Tooltip("배경 이미지 렌더 모드.")]
    [SerializeField] private ScaleMode bgScaleMode = ScaleMode.ScaleAndCrop;

    [Tooltip("배경 오프셋 (px).")]
    [SerializeField] private Vector2 bgOffset = Vector2.zero;

    [Tooltip("배경 크기 배율. 1 = 화면 꽉 채움.")]
    [SerializeField, Range(0.5f, 2f)] private float bgScale = 1f;

    [Tooltip("재생할 마지막 프레임 번호 (0 = 전체 재생). 1회 깜빡임 끝 프레임을 여기 입력.")]
    [SerializeField] private int lastFrame = 0;

    [Tooltip("마지막 프레임에서 루프 재시작까지 대기 시간 (초).")]
    [SerializeField, Range(0f, 10f)] private float loopPauseDuration = 0f;

    // ─── Dark overlay ────────────────────────────────────────────
    [Header("Dark Overlay")]
    [SerializeField, Range(0f, 1f)] private float overlayAlpha = 0f;

    // ─── Vignette ────────────────────────────────────────────────
    [Header("Vignette")]
    [Tooltip("비네팅 활성화.")]
    [SerializeField] private bool vignetteEnabled = true;

    [Tooltip("비네팅 최대 알파 (가장자리 어두움 강도).")]
    [SerializeField, Range(0f, 1f)] private float vignetteAlpha = 0.75f;

    [Tooltip("비네팅 시작 지점. 1에 가까울수록 중앙까지 파고듦.")]
    [SerializeField, Range(0.1f, 1f)] private float vignetteRadius = 0.55f;

    [Tooltip("비네팅 부드러움. 클수록 경계가 넓고 자연스러움.")]
    [SerializeField, Range(0.5f, 4f)] private float vignetteSoftness = 2f;

    // ─── Row Block ───────────────────────────────────────────────
    [Header("Row Block — 위치 & 크기")]
    [SerializeField] private float rowWidth = 580f;
    [SerializeField] private float rowHeight = 68f;
    [SerializeField] private float rowGap = 14f;
    [Tooltip("행 블록 세로 중심. 0 = 상단, 1 = 하단.")]
    [SerializeField, Range(0f, 1f)] private float rowBlockCenterY = 0.82f;
    [Tooltip("행 블록 가로 중심. 0.5 = 중앙.")]
    [SerializeField, Range(0f, 1f)] private float rowBlockCenterX = 0.5f;

    // ─── Row Panel backdrop ─────────────────────────────────────
    [Header("Row Panel Backdrop")]
    [SerializeField, Range(0f, 1f)] private float panelAlpha = 0.0f;
    [SerializeField] private float panelPadding = 20f;

    // ─── Medallion & Icon ────────────────────────────────────────
    [Header("Medallion & Icon")]
    [SerializeField, Range(0.5f, 2.0f)] private float medallionSizeFactor = 0.9f;
    [SerializeField, Range(0f, 1.5f)] private float medallionCenterXFactor = 0.675f;
    [Tooltip("아이콘 Y 오프셋 (px). 양수 = 아래로, 음수 = 위로.")]
    [SerializeField, Range(-40f, 40f)] private float iconYOffset = 0f;
    [SerializeField, Range(0.3f, 0.95f)] private float iconSizeFactor = 0.72f;
    [SerializeField, Range(0.5f, 2.5f)] private float labelStartXFactor = 1.2f;

    // ─── Text ────────────────────────────────────────────────────
    [Header("Text — 이름")]
    [SerializeField] private int nameFontSize = 17;
    [SerializeField] private Color nameColor = new Color(0.99f, 0.95f, 0.78f, 1f);
    [Tooltip("이름 텍스트 X 오프셋 (px). 양수 = 오른쪽.")]
    [SerializeField, Range(-100f, 100f)] private float nameOffsetX = 20f;
    [Tooltip("이름 텍스트 Y 오프셋 (px). 양수 = 아래.")]
    [SerializeField, Range(-40f, 40f)] private float nameOffsetY = 4f;

    [Header("Text — 설명")]
    [SerializeField] private int descFontSize = 13;
    [SerializeField] private Color descColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    [Tooltip("설명 텍스트 X 오프셋 (px). 양수 = 오른쪽.")]
    [SerializeField, Range(-100f, 100f)] private float descOffsetX = 20f;
    [Tooltip("설명 텍스트 Y 오프셋 (px). 양수 = 아래.")]
    [SerializeField, Range(-40f, 40f)] private float descOffsetY = -2f;

    // ─── Prompt ──────────────────────────────────────────────────
    [Header("Prompt Text")]
    [SerializeField] private bool showPrompt = true;
    [SerializeField] private float promptYAboveRows = 55f;
    [SerializeField] private int promptFontSize = 15;
    [SerializeField] private Color promptColor = new Color(0.88f, 0.88f, 0.88f, 1f);
    [Tooltip("페이드 인/아웃 한 사이클 시간 (초).")]
    [SerializeField, Range(1f, 8f)] private float promptFadeCycle = 3f;

    // ─── Hover ───────────────────────────────────────────────────
    [Header("Hover Effect")]
    [SerializeField, Range(1f, 1.15f)] private float hoverScale = 1.04f;
    [SerializeField] private Color hoverTint = new Color(1f, 0.88f, 0.45f, 1f);

    // ─── Runtime state ──────────────────────────────────────────
    private Texture2D[] _bgFrames;
    private Texture2D _bgFallback;
    private int _currentFrame;
    private float _frameTimer;
    private float _pauseTimer;
    private bool _framesLoaded;
    private float _activeFps;

    private Texture2D _vignetteTex;
    private float _cachedVigRadius = -1f;
    private float _cachedVigSoftness = -1f;

    private Texture2D _rowBgTex;
    private Texture2D _defaultRelicIcon;
    private readonly Dictionary<string, Texture2D> _iconCache = new();

    private GUIStyle _nameStyle;
    private GUIStyle _descStyle;
    private GUIStyle _promptStyle;
    private bool _stylesBuilt;

    private readonly List<System.Action> _pending = new();

    void Start()
    {
        _rowBgTex = Resources.Load<Texture2D>("UI/RelicPicker_1/RelicPickerRowBg");
        _defaultRelicIcon = Resources.Load<Texture2D>("InGame/Icon/relic_default");
        PickVariantAndLoad();
    }

    private void PickVariantAndLoad()
    {
        if (bgVariantRoots == null || bgVariantRoots.Length == 0)
        {
            _bgFrames = null;
            _bgFallback = null;
            _framesLoaded = true;
            _activeFps = bgFps;
            return;
        }

        int idx = Random.Range(0, bgVariantRoots.Length);
        LoadVariant(idx);
    }

    private void HandleVariantCheatKeys()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null || bgVariantRoots == null) return;

        // 숫자 1/2/3 (메인열) 또는 numpad 1/2/3 → 강제 variant 전환
        var keys = kb;
        if (keys.digit1Key.wasPressedThisFrame || keys.numpad1Key.wasPressedThisFrame)
            ForceVariant(0);
        else if (keys.digit2Key.wasPressedThisFrame || keys.numpad2Key.wasPressedThisFrame)
            ForceVariant(1);
        else if (keys.digit3Key.wasPressedThisFrame || keys.numpad3Key.wasPressedThisFrame)
            ForceVariant(2);
    }

    private void ForceVariant(int idx)
    {
        if (bgVariantRoots == null || idx < 0 || idx >= bgVariantRoots.Length) return;
        Debug.Log($"[RelicPickerUI] CHEAT: force variant {idx} ({bgVariantRoots[idx]})");
        LoadVariant(idx);
    }

    private void LoadVariant(int idx)
    {
        if (bgVariantRoots == null || idx < 0 || idx >= bgVariantRoots.Length) return;
        string root = bgVariantRoots[idx];
        float fpsOverride = (bgVariantFps != null && idx < bgVariantFps.Length) ? bgVariantFps[idx] : 0f;
        _activeFps = fpsOverride > 0f ? fpsOverride : bgFps;

        Debug.Log($"[RelicPickerUI] Picked bg variant [{idx}]: {root} @ {_activeFps}fps");

        LoadBgFrames(root);
        _bgFallback = LoadFallback(root);
        _currentFrame = 0;
        _frameTimer = 0f;
        _pauseTimer = 0f;
    }

    private void LoadBgFrames(string root)
    {
        var frames = new List<Texture2D>();
        int i = 1;
        while (true)
        {
            string path = $"{root}/{bgFrameSubfolder}/{bgFramePrefix}{i:D3}";
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) break;
            frames.Add(tex);
            i++;
        }
        _bgFrames = frames.Count > 0 ? frames.ToArray() : null;
        _framesLoaded = true;
        if (_bgFrames != null)
            Debug.Log($"[RelicPickerUI] Loaded {_bgFrames.Length} bg frames from {root}/{bgFrameSubfolder}");
        else
            Debug.Log($"[RelicPickerUI] No bg frames in {root}/{bgFrameSubfolder}, using static fallback.");
    }

    private Texture2D LoadFallback(string root)
    {
        if (bgFallbackNames == null) return null;
        foreach (var name in bgFallbackNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var tex = Resources.Load<Texture2D>($"{root}/{name}");
            if (tex != null) return tex;
        }
        return null;
    }

    void Update()
    {
        if (GameStateManager.Instance != null && GameStateManager.Instance.State == GameState.RelicPick)
            HandleVariantCheatKeys();

        if (_bgFrames == null || _bgFrames.Length == 0) return;

        // 마지막 프레임에서 대기 중
        if (_pauseTimer > 0f)
        {
            _pauseTimer -= Time.deltaTime;
            return;
        }

        _frameTimer += Time.deltaTime;
        float frameDuration = 1f / Mathf.Max(0.01f, _activeFps);
        int maxFrame = (lastFrame > 0 && lastFrame < _bgFrames.Length) ? lastFrame : _bgFrames.Length - 1;
        while (_frameTimer >= frameDuration)
        {
            _frameTimer -= frameDuration;
            _currentFrame++;
            if (_currentFrame > maxFrame)
            {
                _currentFrame = 0;
                _pauseTimer = loopPauseDuration;
                break;
            }
        }
    }

    private void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        _nameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = nameFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
        };
        _nameStyle.normal.textColor = nameColor;

        _descStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = descFontSize,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
        };
        _descStyle.normal.textColor = descColor;

        _promptStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = promptFontSize,
            fontStyle = FontStyle.Italic,
            alignment = TextAnchor.MiddleCenter,
        };
        _promptStyle.normal.textColor = promptColor;
    }

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.RelicPick) return;

        foreach (var a in _pending) a();
        _pending.Clear();

        BuildStyles();
        _nameStyle.fontSize = nameFontSize;
        _descStyle.fontSize = descFontSize;
        _promptStyle.fontSize = promptFontSize;
        _nameStyle.normal.textColor = nameColor;
        _descStyle.normal.textColor = descColor;
        _promptStyle.normal.textColor = promptColor;

        float scaleX = Screen.width / RefW;
        float scaleY = Screen.height / RefH;
        var matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scaleX, scaleY, 1f));

        DrawBackground();
        DrawRows(gsm);

        GUI.matrix = matrix;
    }

    private void DrawBackground()
    {
        Texture2D tex = null;
        if (_bgFrames != null && _bgFrames.Length > 0)
            tex = _bgFrames[_currentFrame];
        if (tex == null)
            tex = _bgFallback;

        if (tex != null)
        {
            float w = RefW * bgScale;
            float h = RefH * bgScale;
            var bgRect = new Rect(
                (RefW - w) * 0.5f + bgOffset.x,
                (RefH - h) * 0.5f + bgOffset.y,
                w, h);
            GUI.DrawTexture(bgRect, tex, bgScaleMode);
        }
        else
        {
            DrawFilledRect(new Rect(0, 0, RefW, RefH), new Color(0.06f, 0.05f, 0.12f, 1f));
        }

        if (overlayAlpha > 0f)
            DrawFilledRect(new Rect(0, 0, RefW, RefH), new Color(0f, 0f, 0f, overlayAlpha));

        if (vignetteEnabled)
            DrawVignette();
    }

    private void DrawVignette()
    {
        // 파라미터가 바뀌면 텍스처 재생성
        if (_vignetteTex == null
            || _cachedVigRadius != vignetteRadius
            || _cachedVigSoftness != vignetteSoftness)
        {
            BuildVignetteTex();
        }

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, vignetteAlpha);
        GUI.DrawTexture(new Rect(0, 0, RefW, RefH), _vignetteTex, ScaleMode.StretchToFill);
        GUI.color = prev;
    }

    private void BuildVignetteTex()
    {
        const int Size = 128;
        if (_vignetteTex != null) Destroy(_vignetteTex);
        _vignetteTex = new Texture2D(Size, Size, TextureFormat.Alpha8, false);
        _vignetteTex.wrapMode = TextureWrapMode.Clamp;

        float r = vignetteRadius;
        float soft = vignetteSoftness;
        var pixels = new Color[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // -1 ~ 1 정규화, 종횡비 보정
                float nx = (x / (float)(Size - 1)) * 2f - 1f;
                float ny = (y / (float)(Size - 1)) * 2f - 1f;
                float dist = Mathf.Sqrt(nx * nx + ny * ny); // 0 = 중앙, ~1.41 = 모서리
                // r 이내는 0, r 바깥으로 갈수록 1로
                float a = Mathf.Pow(Mathf.Clamp01((dist - r) / (1.42f - r)), soft);
                pixels[y * Size + x] = new Color(0f, 0f, 0f, a);
            }
        }

        _vignetteTex.SetPixels(pixels);
        _vignetteTex.Apply();
        _cachedVigRadius = vignetteRadius;
        _cachedVigSoftness = vignetteSoftness;
    }

    private void DrawRows(GameStateManager gsm)
    {
        var choices = gsm.RelicPickChoices;
        if (choices == null || choices.Count == 0)
        {
            _pending.Add(() => gsm.ConfirmRelicPick(null));
            return;
        }

        int n = choices.Count;
        float totalH = n * rowHeight + (n - 1) * rowGap;
        float startY = RefH * rowBlockCenterY - totalH * 0.5f;
        float rowX = RefW * rowBlockCenterX - rowWidth * 0.5f;

        if (panelAlpha > 0f)
        {
            DrawFilledRect(new Rect(rowX - panelPadding, startY - panelPadding,
                rowWidth + panelPadding * 2f, totalH + panelPadding * 2f),
                new Color(0f, 0f, 0f, panelAlpha));
        }

        if (showPrompt)
        {
            // 0→1→0 사인파 페이드 (Ping-Pong)
            float t = (Mathf.Sin(Time.time * Mathf.PI * 2f / promptFadeCycle) + 1f) * 0.5f;
            var fadeColor = promptColor;
            fadeColor.a = promptColor.a * t;
            _promptStyle.normal.textColor = fadeColor;
            GUI.Label(new Rect(rowX, startY - promptYAboveRows, rowWidth, 34f),
                "...시작 유물을 선택하세요...", _promptStyle);
            _promptStyle.normal.textColor = promptColor; // 복원
        }

        for (int i = 0; i < n; i++)
        {
            var relic = choices[i];
            float y = startY + i * (rowHeight + rowGap);
            var baseRect = new Rect(rowX, y, rowWidth, rowHeight);
            bool hovered = baseRect.Contains(Event.current.mousePosition);

            Rect drawRect = baseRect;
            if (hovered)
            {
                float s = hoverScale;
                drawRect = new Rect(
                    baseRect.center.x - baseRect.width * s * 0.5f,
                    baseRect.center.y - baseRect.height * s * 0.5f,
                    baseRect.width * s, baseRect.height * s);
            }

            DrawRow(drawRect, relic, hovered);

            if (GUI.Button(baseRect, GUIContent.none, GUIStyle.none))
            {
                var picked = relic;
                _pending.Add(() => gsm.ConfirmRelicPick(picked));
            }
        }
    }

    private void DrawRow(Rect rect, RelicData relic, bool hovered)
    {
        if (_rowBgTex != null)
        {
            var prev = GUI.color;
            GUI.color = hovered ? hoverTint : Color.white;
            GUI.DrawTexture(rect, _rowBgTex, ScaleMode.StretchToFill);
            GUI.color = prev;
        }
        else
        {
            DrawFilledRect(rect, hovered
                ? new Color(0.28f, 0.40f, 0.55f, 0.95f)
                : new Color(0.10f, 0.18f, 0.28f, 0.90f));
        }

        float medallionSize = rect.height * medallionSizeFactor;
        float mcx = rect.x + rect.height * medallionCenterXFactor;
        float mcy = rect.y + rect.height * 0.5f + iconYOffset;
        float iconSize = medallionSize * iconSizeFactor;
        var iconRect = new Rect(mcx - iconSize * 0.5f, mcy - iconSize * 0.5f, iconSize, iconSize);
        var icon = GetRelicIcon(relic.id);
        if (icon != null)
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

        float textX = rect.x + rect.height * labelStartXFactor;
        float textW = rect.width - (textX - rect.x) - 12f;
        float nameH = rect.height * 0.44f;
        float descH = rect.height * 0.48f;

        GUI.Label(new Rect(textX + nameOffsetX, rect.y + 4f + nameOffsetY, textW, nameH), relic.name ?? relic.id, _nameStyle);
        GUI.Label(new Rect(textX + descOffsetX, rect.y + nameH + 2f + descOffsetY, textW, descH), relic.description ?? string.Empty, _descStyle);
    }

    private Texture2D GetRelicIcon(string id)
    {
        if (string.IsNullOrEmpty(id)) return _defaultRelicIcon;
        if (_iconCache.TryGetValue(id, out var cached)) return cached;
        var tex = Resources.Load<Texture2D>($"InGame/RelicArt/{id}");
        if (tex == null) tex = _defaultRelicIcon;
        _iconCache[id] = tex;
        return tex;
    }

    private static void DrawFilledRect(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.color = prev;
    }
}
