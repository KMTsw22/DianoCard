#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
using DianoCard.Data;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 디자인 도구 호스트 — 외부 파일 BG 프리뷰 / 카드 슬롯 프리뷰 오버레이.
/// 게임플레이 치트는 모두 에디터의 Cheat Panel(Tools/Cheat Panel)에서 직접 조작.
/// 이 컴포넌트는 디자인 검토 시 BG/카드 프리뷰 그리기를 담당.
/// 자동 스폰: CheatWindow가 처음 호출될 때 GetOrCreateCheatUI 로 씬에 추가.
/// 출시 빌드(Development Build OFF)에서는 컴파일 자체에서 제거됨.
/// </summary>
[DefaultExecutionOrder(2000)]
public class CheatUI : MonoBehaviour
{
    // 외부 파일 BG 프리뷰 — 디스크 어디서든 PNG/JPG를 골라 풀스크린으로 깔고,
    // 기존 상단 HUD 네비바는 OnGUI라 그대로 위에 올라옴.
    private SpriteRenderer _previewBgSr;
    private Texture2D _previewBgTex;
    private string _previewBgPath;
    private bool _previewBgVisible;

    // 격리 프리뷰 모드 — ON이면 OnGUI 풀스크린으로 BG + 상단 네비바만 그려서 다른 게임 UI를 가림.
    private bool _previewIsolateMode;
    private BattleUI.HudContext _previewHudCtx = BattleUI.HudContext.Battle;
    private DianoCard.Game.RunState _previewDummyRun;

    // 카드 프리뷰 (프레임 디자인 확인용)
    private bool _cardPreviewOpen;
    private int _cardPreviewIndex;
    private System.Collections.Generic.List<CardData> _cardPreviewList;
    private GUIStyle _previewLabelStyle;
    private GUIStyle _btnStyle;
    private float _cardPreviewHeight = 540f;   // 카드 세로 픽셀 (1280x720 가상 좌표). 슬라이더로 300~560 확대.
    private bool  _cardPreviewSlotOnly;        // true = 프레임 레이어만, 카드 데이터 숨김

    void OnGUI()
    {
        if (PauseMenuUI.IsOpen) return;
        if (!_cardPreviewOpen && !_previewIsolateMode) return;

        var matrix = GUI.matrix;
        GUI.matrix = DianoCard.UI.AspectScaler.GuiMatrix;

        EnsureStyles();

        // 격리 BG 프리뷰 — 다른 게임 UI를 풀스크린으로 가린다.
        if (_previewIsolateMode && _previewBgTex != null)
        {
            GUI.depth = -150;
            DrawIsolatedPreview();
        }

        // 카드 프리뷰는 BattleUI보다 위 (depth 낮을수록 앞).
        if (_cardPreviewOpen)
        {
            GUI.depth = -100;
            DrawCardPreviewOverlay();
        }

        GUI.matrix = matrix;
    }

    private void EnsureStyles()
    {
        if (_btnStyle != null) return;
        _btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            fixedHeight = 30f,
        };
        // GUI.skin 기본 hover/active state의 색·배경 swap을 차단해
        // 호버 시 텍스트가 미세하게 흔들리는 느낌을 없앤다.
        var c = _btnStyle.normal.textColor;
        var bg = _btnStyle.normal.background;
        _btnStyle.hover.textColor = c;     _btnStyle.hover.background = bg;
        _btnStyle.active.textColor = c;    _btnStyle.active.background = bg;
        _btnStyle.focused.textColor = c;   _btnStyle.focused.background = bg;
    }

    // BG + 상단 네비바만 풀스크린으로. 1280x720 가상 좌표.
    private void DrawIsolatedPreview()
    {
        // 1) BG 풀스크린 (1280x720 가상 캔버스).
        GUI.DrawTexture(new Rect(0, 0, 1280f, 720f), _previewBgTex, ScaleMode.ScaleAndCrop);

        // 2) 상단 네비바만. BattleUI가 씬에 떠있어야 함 — Inspector 튜닝값을 그대로 씀.
        var battleUi = Object.FindFirstObjectByType<BattleUI>();
        if (battleUi == null) return;

        var run = ResolvePreviewRun();
        // 휴식 노드(15층 보스 직전이라고 가정). 인자값은 디자인 확인 목적이라 적당히.
        battleUi.DrawTopBar(_previewHudCtx, run, run.currentFloor, 15);
    }

    // 격리 프리뷰용 RunState — 진행 중인 run이 있으면 그걸 쓰고, 없으면 더미 표시값.
    private DianoCard.Game.RunState ResolvePreviewRun()
    {
        var gsm = DianoCard.Game.GameStateManager.Instance;
        if (gsm != null && gsm.CurrentRun != null) return gsm.CurrentRun;

        if (_previewDummyRun == null)
        {
            _previewDummyRun = new DianoCard.Game.RunState
            {
                playerMaxHp = 70,
                playerCurrentHp = 52,
                gold = 240,
                currentFloor = 7,
                chapterId = "CH01",
                characterId = "CH001",
            };
        }
        return _previewDummyRun;
    }

    // ===== Editor CheatWindow가 호출하는 공개 API =====
    public bool IsCardPreviewOpen => _cardPreviewOpen;

    public void OpenCardPreview(bool slotOnly)
    {
        _cardPreviewOpen = true;
        _cardPreviewSlotOnly = slotOnly;
        EnsureCardPreviewList();
    }

    public void CloseCardPreview() => _cardPreviewOpen = false;

    public bool HasPreviewBg => _previewBgTex != null;
    public string PreviewBgFileName =>
        string.IsNullOrEmpty(_previewBgPath) ? null : Path.GetFileName(_previewBgPath);
    public bool IsPreviewIsolateOn => _previewIsolateMode;
    public BattleUI.HudContext PreviewHudCtx
    {
        get => _previewHudCtx;
        set => _previewHudCtx = value;
    }

    public void PickPreviewBgFile() => PickAndLoadPreviewBg();
    public void ClearPreviewBackground() => ClearPreviewBg();
    public void SetPreviewIsolateMode(bool on)
    {
        _previewIsolateMode = on && _previewBgTex != null;
    }

    // 모든 카드 한 번 캐시 (id 정렬).
    private void EnsureCardPreviewList()
    {
        if (_cardPreviewList != null) return;
        if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();
        _cardPreviewList = new System.Collections.Generic.List<CardData>(DataManager.Instance.Cards.Values);
        _cardPreviewList.Sort((a, b) => string.Compare(a.id, b.id, System.StringComparison.Ordinal));
        _cardPreviewIndex = Mathf.Clamp(_cardPreviewIndex, 0, Mathf.Max(0, _cardPreviewList.Count - 1));
    }

    private void DrawCardPreviewOverlay()
    {
        if (_cardPreviewList == null || _cardPreviewList.Count == 0) return;

        if (_previewLabelStyle == null)
        {
            _previewLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
        }

        // 화면 어둡게 깔기.
        var fullRect = new Rect(0, 0, 1280, 720);
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.DrawTexture(fullRect, Texture2D.whiteTexture);
        GUI.color = prev;

        // 카드 크기 — 슬라이더로 조절. 상단 정보 바(56px) + 하단 버튼 바(~96px) 공간을 남긴다.
        float cardH = Mathf.Clamp(_cardPreviewHeight, 300f, 560f);
        float cardW = cardH * (3f / 4f);
        // 세로 중심을 약간 위로 — 하단 버튼 자리 확보.
        var cardRect = new Rect(640f - cardW * 0.5f, 340f - cardH * 0.5f, cardW, cardH);

        var ui = Object.FindFirstObjectByType<BattleUI>();
        if (ui == null)
        {
            GUI.Label(cardRect, "BattleUI not in scene.\n전투 한 번 들어갔다 나오면 OK", _previewLabelStyle);
        }
        else if (_cardPreviewSlotOnly)
        {
            ui.DrawCardPreview(cardRect, null, slotOnly: true);

            GUI.Label(new Rect(0, 8f, 1280f, 24f), "[ 카드 슬롯 프리뷰 — 빈 프레임 ]", _previewLabelStyle);
            GUI.Label(new Rect(0, 32f, 1280f, 20f),
                "BattleUI Inspector · Card Frame rect 실시간 반영", _previewLabelStyle);
        }
        else
        {
            var card = _cardPreviewList[_cardPreviewIndex];
            ui.DrawCardPreview(cardRect, card);

            GUI.Label(new Rect(0, 8f, 1280f, 24f), $"{card.id}  {card.nameKr}", _previewLabelStyle);
            GUI.Label(new Rect(0, 32f, 1280f, 20f),
                $"{card.cardType} / {card.subType}  · {card.rarity}",
                _previewLabelStyle);
        }

        // 확대 슬라이더 (상단 우측) — 슬롯/카드 모드 공통.
        GUI.Label(new Rect(1000f, 8f, 240f, 20f), $"확대: {_cardPreviewHeight:0}px", _previewLabelStyle);
        _cardPreviewHeight = GUI.HorizontalSlider(new Rect(1000f, 32f, 240f, 24f), _cardPreviewHeight, 300f, 560f);

        const float bottomRow = 620f;
        const float bottomRow2 = 668f;

        if (!_cardPreviewSlotOnly)
        {
            // 카드 모드: 이전/다음 버튼은 카드 옆에.
            var prevBtn = new Rect(cardRect.x - 80f, cardRect.center.y - 30f, 70f, 60f);
            var nextBtn = new Rect(cardRect.xMax + 10f, cardRect.center.y - 30f, 70f, 60f);
            if (GUI.Button(prevBtn, "◀\nPrev", _btnStyle))
                _cardPreviewIndex = (_cardPreviewIndex - 1 + _cardPreviewList.Count) % _cardPreviewList.Count;
            if (GUI.Button(nextBtn, "Next\n▶", _btnStyle))
                _cardPreviewIndex = (_cardPreviewIndex + 1) % _cardPreviewList.Count;
        }

        string modeLabel = _cardPreviewSlotOnly ? "▶ 카드 모드로" : "▶ 슬롯 모드로";
        if (GUI.Button(new Rect(680f, bottomRow, 180f, 36f), modeLabel, _btnStyle))
            _cardPreviewSlotOnly = !_cardPreviewSlotOnly;

        if (GUI.Button(new Rect(580f, bottomRow2, 120f, 36f), "닫기", _btnStyle))
            _cardPreviewOpen = false;
    }

    // ===== 외부 파일 BG 프리뷰 헬퍼 =====

    // 프로젝트 루트 옆 _cheat_bg 폴더 — 여기에 PNG/JPG 넣어두면 다이얼로그가 바로 그 폴더로 열림.
    // Application.dataPath = ".../DianoCard/DianoCard/Assets" → 두 단계 위가 코딩 루트.
    private static string CheatBgFolder()
    {
        var root = Directory.GetParent(Application.dataPath)?.Parent?.FullName;
        if (string.IsNullOrEmpty(root)) return Application.dataPath;
        var dir = Path.Combine(root, "_cheat_bg");
        if (!Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); }
            catch { /* 권한 문제 시 그냥 무시 — 다이얼로그가 부모 폴더에서 열림 */ }
        }
        return dir;
    }

    private void PickAndLoadPreviewBg()
    {
        string path = null;
#if UNITY_EDITOR
        path = EditorUtility.OpenFilePanelWithFilters(
            "BG 프리뷰 이미지 선택", CheatBgFolder(),
            new[] { "Image", "png,jpg,jpeg" });
#else
        var dir = CheatBgFolder();
        if (!Directory.Exists(dir))
            dir = Path.Combine(Application.persistentDataPath, "cheat_bg");
        if (Directory.Exists(dir))
        {
            var files = System.IO.Directory.GetFiles(dir);
            foreach (var f in files)
            {
                if (f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                {
                    path = f;
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(path))
            Debug.LogWarning($"[Cheat] 빌드 모드: {dir} 에 이미지 넣어주세요.");
#endif
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Debug.LogWarning($"[Cheat] 이미지 로드 실패: {path}");
                Object.Destroy(tex);
                return;
            }
            tex.name = Path.GetFileNameWithoutExtension(path);

            if (_previewBgTex != null) Object.Destroy(_previewBgTex);
            _previewBgTex = tex;
            _previewBgPath = path;
            _previewBgVisible = true;

            ApplyPreviewBg();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Cheat] BG 프리뷰 로드 에러: {ex.Message}");
        }
    }

    private void ApplyPreviewBg()
    {
        if (_previewBgTex == null) return;
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[Cheat] BG 프리뷰: Camera.main 없음");
            return;
        }

        if (_previewBgSr == null)
        {
            var go = new GameObject("_CheatPreviewBackground");
            // 다른 씬 전환에도 살아있게 — 전투 ↔ 맵 옮겨다니며 비교 가능.
            Object.DontDestroyOnLoad(go);
            _previewBgSr = go.AddComponent<SpriteRenderer>();
            _previewBgSr.sortingOrder = 1000;
        }

        var tex = _previewBgTex;
        _previewBgSr.sprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
        _previewBgSr.enabled = _previewBgVisible;

        FitPreviewBgToCamera();
    }

    private void FitPreviewBgToCamera()
    {
        if (_previewBgSr == null || _previewBgTex == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        if (cam.orthographic)
        {
            float camH = cam.orthographicSize * 2f;
            float camW = camH * cam.aspect;
            float spriteW = _previewBgTex.width / 100f;
            float spriteH = _previewBgTex.height / 100f;
            // ScaleAndCrop 동작 — 짧은 축에 맞춰 가득 채움.
            float s = Mathf.Max(camW / spriteW, camH / spriteH);
            _previewBgSr.transform.localScale = new Vector3(s, s, 1f);
        }
        var camPos = cam.transform.position;
        _previewBgSr.transform.position = new Vector3(camPos.x, camPos.y, 0f);
    }

    private void ClearPreviewBg()
    {
        if (_previewBgSr != null)
        {
            Object.Destroy(_previewBgSr.gameObject);
            _previewBgSr = null;
        }
        if (_previewBgTex != null)
        {
            Object.Destroy(_previewBgTex);
            _previewBgTex = null;
        }
        _previewBgPath = null;
        _previewBgVisible = false;
        _previewIsolateMode = false;
    }

    void OnDestroy()
    {
        ClearPreviewBg();
    }
}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
