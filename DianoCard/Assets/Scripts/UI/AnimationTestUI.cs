using System.Collections.Generic;
using System.Text.RegularExpressions;
using DianoCard.Battle;
using DianoCard.Game;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 애니메이션 프레임 프리뷰 UI — GameState == AnimationTest일 때만 활성.
///
/// ● 재생은 <see cref="BattleEntityView"/>를 그대로 사용해서 실제 전투와 100% 같은
///   프레임 스왑 + 위치 thrust + idle bob + 스케일 보정 동작을 보여준다.
///   즉 "테스트에서 잘 보이는데 전투에서 이상함" 같은 괴리 없음.
///
/// ● 캐릭터/애니 폴더 규칙:
///     Assets/Resources/AnimationTest/{캐릭터}/{애니이름}_f{NN}.png
///     예: idle_f01.png, attack_f01..f08.png
///
///   Unity가 PNG를 Multiple 모드로 자동 임포트해도 서브스프라이트 이름(`_0` 접미사)을
///   그대로 파싱해주므로 친구가 사진만 넣으면 된다.
/// </summary>
public class AnimationTestUI : MonoBehaviour
{
    private const float RefW = 1280f;
    private const float RefH = 720f;

    [SerializeField] private List<string> _manualCharacterFolders = new();

    private static readonly Regex FramePatternStrict = new Regex(@"^(.+?)_f(\d+)(?:_\d+)?$", RegexOptions.Compiled);
    private static readonly Regex FramePatternLoose = new Regex(@"^(.+?)_(\d+)(?:_\d+)?$", RegexOptions.Compiled);

    private class AnimClip
    {
        public string name;
        public List<Sprite> frames = new();
    }

    private enum PlayMode { Attack, Summon, Hit }

    // ── 스캔 결과 ───────────────────────────────────────────
    private readonly List<string> _characters = new();
    private string _selectedCharacter;
    private readonly List<AnimClip> _clips = new();
    private int _selectedClipIdx = -1;

    // ── 재생 옵션 ───────────────────────────────────────────
    private float _attackDuration = 0.75f;         // BattleEntityView.PlayAttack와 동일 기본값
    private float _attackDistance = 0.7f;
    private float _worldHeight    = 4f;            // 캐릭터의 월드 높이 (자유 배치용; 전투 레이아웃에선 자동 계산)
    private float _strikeExtendedBoost = 1.2f;
    private bool  _autoReplay = false;             // 기본 OFF — 공격 버튼 눌러 수동 발동
    private float _replayGap = 0.4f;
    private PlayMode _lastMode = PlayMode.Attack;

    // 실제 전투 화면과 동일한 배치 사용 여부. ON이면 캐릭터 위치/크기가 BattleUI의
    // DrawPlayerNPC와 정확히 일치하도록 자동 계산 — GUI(180, 430)에 260px 높이.
    private bool _useBattlePose = true;
    private bool _showLeftPanel = true;
    // BattleUI.cs와 동일한 상수 — 수정 시 양쪽 맞출 것.
    private const float BattleGroundY = 540f;
    private const float BattlePlayerHeight = 260f;
    private const float BattlePlayerCenterX = 180f;

    private float _replayTimer; // 다음 자동 트리거까지 남은 시간

    // ── 프리뷰 월드 객체 ────────────────────────────────────
    private GameObject _previewGO;
    private BattleEntityView _previewView;
    private SpriteRenderer _previewSR;
    private bool _previewActive;

    // Camera.main의 clear 설정을 덮어쓰므로 원래 값 복구용 캐시
    private Camera _cachedMainCam;
    private Color _cachedCamBg;
    private CameraClearFlags _cachedCamFlags;

    // 진입 시 씬에 이미 떠 있던 월드 스프라이트(BattleUI._playerView 등)를 임시로 숨겼다가
    // 나갈 때 원복한다. 안 그러면 원점에 거대한 기본 스케일 캐릭터가 겹쳐 보임.
    private readonly List<SpriteRenderer> _hiddenRenderers = new();

    // ── 뷰 옵션 ─────────────────────────────────────────────
    private Color _bgColor = new Color(0.18f, 0.18f, 0.22f, 1f);
    private float _cameraZoom = 5f;                   // orthographicSize

    // ── 배경 이미지 ───────────────────────────────────────────
    private readonly List<Sprite> _bgSprites = new();
    private readonly List<string> _bgNames = new();
    private int _selectedBgIdx = -1;                  // -1 = 단색 배경
    private GameObject _bgImageGO;
    private SpriteRenderer _bgImageSR;
    private bool _showGrid;
    private GameObject _gridGO;
    private SpriteRenderer _gridSR;
    private Texture2D _gridTex;

    // ── 상태 메시지 ─────────────────────────────────────────
    private string _statusMessage;

    // ── GUI ─────────────────────────────────────────────────
    private GUIStyle _titleStyle, _btnStyle, _smallStyle, _selBtnStyle, _statusStyle, _modeBtnStyle;
    private bool _stylesReady;

    // ───────────────────────── Lifecycle ─────────────────────────

    void Update()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        bool inState = gsm.State == GameState.AnimationTest;
        if (inState && !_previewActive) SetupPreview();
        else if (!inState && _previewActive) TeardownPreview();

        if (!inState) return;

        // 배경색 / 줌 실시간 반영
        if (_cachedMainCam != null)
        {
            _cachedMainCam.backgroundColor = _bgColor;
            if (!Mathf.Approximately(_cachedMainCam.orthographicSize, _cameraZoom))
            {
                _cachedMainCam.orthographicSize = _cameraZoom;
                // 전투 포즈는 카메라 기준이므로 줌 변경 시 재계산
                if (_useBattlePose) ApplyPoseToPreview();
            }
        }

        // 그리드 토글
        if (_gridGO != null) _gridGO.SetActive(_showGrid);

        // 배경 이미지 스케일을 카메라에 맞춤
        UpdateBgImageScale();

        // 자동 반복 재생
        if (_autoReplay && _selectedClipIdx >= 0 && _previewView != null)
        {
            _replayTimer -= Time.unscaledDeltaTime;
            if (_replayTimer <= 0f)
            {
                TriggerPlayback(_lastMode);
                _replayTimer = _attackDuration + _replayGap;
            }
        }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame)
            {
                TriggerPlayback(_lastMode);
                _replayTimer = _attackDuration + _replayGap;
            }
            if (kb.digit1Key.wasPressedThisFrame) { TriggerPlayback(PlayMode.Attack); _replayTimer = _attackDuration + _replayGap; }
            if (kb.digit2Key.wasPressedThisFrame) { TriggerPlayback(PlayMode.Summon); _replayTimer = _attackDuration + _replayGap; }
            if (kb.digit3Key.wasPressedThisFrame) { TriggerPlayback(PlayMode.Hit);    _replayTimer = 0.35f + _replayGap; }
            if (kb.rKey.wasPressedThisFrame) ApplySelectedClipToPreview();
            if (kb.gKey.wasPressedThisFrame) _showGrid = !_showGrid;
            if (kb.f5Key.wasPressedThisFrame) RefreshAll();
            if (kb.escapeKey.wasPressedThisFrame && gsm != null) gsm.ExitAnimationTest();
        }
    }

    private void SetupPreview()
    {
        // BattleUI 가 미리 만들어둔 PlayerView 등 씬의 기존 월드 스프라이트를 숨김.
        // BattleUI.LoadCardSprites() 가 카드 로딩 시점에 EnsurePlayerView()를 호출해서
        // 전투가 아닐 때도 PlayerView GameObject가 원점에 거대한 기본 스케일로 떠 있음.
        // 내 _bgImageSR/_gridSR/_previewSR 은 이 루프 이후에 새로 만들어지므로 잡히지 않는다.
        _hiddenRenderers.Clear();
        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr != null && sr.enabled)
            {
                sr.enabled = false;
                _hiddenRenderers.Add(sr);
            }
        }

        _cachedMainCam = Camera.main;
        if (_cachedMainCam != null)
        {
            _cachedCamBg = _cachedMainCam.backgroundColor;
            _cachedCamFlags = _cachedMainCam.clearFlags;
            _cachedMainCam.clearFlags = CameraClearFlags.SolidColor;
            _cachedMainCam.backgroundColor = _bgColor;
            // 전투 화면 공식이 orthographicSize=5 기준이므로 진입 시 강제로 5로 맞춤.
            // 이후 사용자가 슬라이더로 바꾸면 따름.
            _cameraZoom = 5f;
            _cachedMainCam.orthographicSize = 5f;
        }

        // 배경 이미지 오브젝트 (캐릭터 뒤에 렌더)
        _bgImageGO = new GameObject("[AnimTestBg]");
        _bgImageSR = _bgImageGO.AddComponent<SpriteRenderer>();
        _bgImageSR.sortingOrder = -10;
        _bgImageGO.transform.position = new Vector3(0, 0, 1f);
        _bgImageGO.SetActive(false);

        // 그리드 오버레이
        _gridGO = new GameObject("[AnimTestGrid]");
        _gridSR = _gridGO.AddComponent<SpriteRenderer>();
        _gridSR.sortingOrder = -5;
        _gridGO.transform.position = new Vector3(0, 0, 0.5f);
        _gridGO.SetActive(false);
        CreateGridTexture();

        _previewGO = new GameObject("[AnimTestPreview]");
        _previewSR = _previewGO.AddComponent<SpriteRenderer>();
        _previewView = _previewGO.AddComponent<BattleEntityView>();
        _previewView.SetStrikeExtendedScaleBoost(_strikeExtendedBoost);

        LoadBackgroundSprites();

        _previewActive = true;
        _replayTimer = 0f;

        // 전투 맵 기본값: Normal_Battle이 있으면 자동 선택 (실제 전투와 동일 배경)
        int defaultBg = _bgNames.FindIndex(n => n.StartsWith("Normal_Battle", System.StringComparison.OrdinalIgnoreCase));
        if (defaultBg >= 0) SelectBgImage(defaultBg);

        // 기본 위치 = 실제 전투 위치. 클립 선택 전이라도 BattlePose 좌표로 고정.
        _useBattlePose = true;
        ApplyPoseToPreview();

        // 이미 선택된 클립이 있으면 그대로 적용
        if (_selectedClipIdx >= 0) ApplySelectedClipToPreview();
    }

    private void TeardownPreview()
    {
        if (_previewGO != null) Destroy(_previewGO);
        _previewGO = null; _previewView = null; _previewSR = null;
        if (_bgImageGO != null) Destroy(_bgImageGO);
        _bgImageGO = null; _bgImageSR = null;
        if (_gridGO != null) Destroy(_gridGO);
        _gridGO = null; _gridSR = null;
        if (_gridTex != null) Destroy(_gridTex);
        _gridTex = null;
        if (_cachedMainCam != null)
        {
            _cachedMainCam.backgroundColor = _cachedCamBg;
            _cachedMainCam.clearFlags = _cachedCamFlags;
            _cachedMainCam = null;
        }

        // 진입 시 숨겼던 월드 스프라이트 복구.
        foreach (var sr in _hiddenRenderers)
            if (sr != null) sr.enabled = true;
        _hiddenRenderers.Clear();

        _previewActive = false;
    }

    void OnDisable() { if (_previewActive) TeardownPreview(); }

    // 프리뷰 엔티티에 현재 선택된 클립을 주입. 다중 프레임이면 공격 시퀀스로도 등록.
    private void ApplySelectedClipToPreview()
    {
        if (_previewView == null) return;
        if (_selectedClipIdx < 0 || _selectedClipIdx >= _clips.Count) return;
        var clip = _clips[_selectedClipIdx];
        if (clip.frames.Count == 0) return;

        // 첫 프레임을 idle/기본 스프라이트로, 시퀀스로도 등록. summon용으로도 첫 프레임 재사용.
        _previewView.SetSprite(clip.frames[0]);
        _previewView.SetAttackSequence(clip.frames.ToArray());
        _previewView.SetSummonFrame(clip.frames[0]);

        ApplyPoseToPreview();
        _replayTimer = 0f;
    }

    // 캐릭터의 월드 좌표 + 월드 높이를 결정. _useBattlePose에 따라 실제 전투 레이아웃
    // (BattleUI.DrawPlayerNPC와 동일 공식)을 쓰거나, 슬라이더 값을 따른다.
    private void ApplyPoseToPreview()
    {
        if (_previewView == null || _cachedMainCam == null) return;

        // 전투 화면의 DrawPlayerNPC와 동일: GUI (180, 430) 중심, 높이 260
        var feetGui = new Vector2(BattlePlayerCenterX, BattleGroundY - 110f + BattlePlayerHeight * 0.5f);
        var topGui  = new Vector2(BattlePlayerCenterX, BattleGroundY - 110f - BattlePlayerHeight * 0.5f);
        var feetWorld = GuiToWorld(feetGui);
        var topWorld  = GuiToWorld(topGui);
        float battleHeight = Mathf.Abs(feetWorld.y - topWorld.y);
        float targetHeight = _useBattlePose ? battleHeight : _worldHeight;

        // pivot 보정: BattleEntityView는 transform.position = _basePosition (pivot 위치).
        // 로드한 PNG의 pivot이 Center면 스프라이트가 아래로 쏠리므로,
        // 현재 스프라이트의 bounds.min.y (pivot→바닥 거리, 대개 음수) 만큼 basePosition을 위로 올려
        // "발"이 정확히 feetWorld에 오게 만든다. pivot=Bottom 스프라이트면 bounds.min.y≈0이라 영향 없음.
        Vector3 pivotOffset = Vector3.zero;
        var sp = _previewSR != null ? _previewSR.sprite : null;
        if (sp != null && sp.bounds.size.y > 0.001f)
        {
            float scale = targetHeight / sp.bounds.size.y;
            pivotOffset = new Vector3(0f, -sp.bounds.min.y * scale, 0f);
        }

        // 자유 배치 모드여도 앵커 좌표(발 위치)는 전투와 동일 — 높이만 슬라이더로 바뀜.
        _previewView.SetBasePosition(feetWorld + pivotOffset);
        _previewView.SetWorldHeight(targetHeight);
    }

    // 전투 레이아웃 적용 시 계산되는 월드 높이 (라벨 표시용).
    private float ComputeBattlePoseHeight()
    {
        if (_cachedMainCam == null) return 0f;
        var feetGui = new Vector2(BattlePlayerCenterX, BattleGroundY - 110f + BattlePlayerHeight * 0.5f);
        var topGui  = new Vector2(BattlePlayerCenterX, BattleGroundY - 110f - BattlePlayerHeight * 0.5f);
        return Mathf.Abs(GuiToWorld(feetGui).y - GuiToWorld(topGui).y);
    }

    // BattleUI.GuiToWorld와 동일 공식. 1280x720 GUI 좌표를 월드로 변환.
    private Vector3 GuiToWorld(Vector2 guiPos)
    {
        var cam = _cachedMainCam != null ? _cachedMainCam : Camera.main;
        if (cam == null) return Vector3.zero;
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        float sx = guiPos.x * scale;
        float sy = Screen.height - guiPos.y * scale;
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(sx, sy, 10f));
        world.z = 0f;
        return world;
    }

    private void TriggerPlayback(PlayMode mode)
    {
        if (_previewView == null) return;
        _lastMode = mode;
        switch (mode)
        {
            case PlayMode.Attack:
                _previewView.PlayAttack(Vector3.right, _attackDistance, _attackDuration);
                break;
            case PlayMode.Summon:
                _previewView.PlaySummon(Vector3.right, 0.18f, _attackDuration);
                break;
            case PlayMode.Hit:
                _previewView.PlayHit(0.35f);
                break;
        }
    }

    // ───────────────────────── OnGUI ─────────────────────────

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.AnimationTest) return;

        EnsureStyles();

        if (_characters.Count == 0 && string.IsNullOrEmpty(_statusMessage))
            RefreshAll();

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        var prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        DrawHeader(gsm);
        if (_showLeftPanel) DrawLeftPanel();
        DrawRightPanel();
        DrawCenterHint();
        DrawStatusBar();

        GUI.matrix = prevMatrix;
    }

    private void DrawHeader(GameStateManager gsm)
    {
        // 상단 바에 약한 박스 배경
        DrawPanelBg(new Rect(0, 0, RefW, 54));

        GUI.Label(new Rect(20, 14, 600, 30), "Animation Test — 실시간 프리뷰 (BattleEntityView)", _titleStyle);

        if (GUI.Button(new Rect(RefW - 160, 12, 140, 32), "로비로 (Esc)", _btnStyle))
            gsm.ExitAnimationTest();
        if (GUI.Button(new Rect(RefW - 310, 12, 140, 32), "재스캔 (F5)", _btnStyle))
            RefreshAll();

        // 전투 위치 토글 — 실제 전투 레이아웃과 즉시 비교
        string poseLabel = _useBattlePose ? "● 전투 위치" : "○ 전투 위치";
        if (GUI.Button(new Rect(RefW - 470, 12, 150, 32), poseLabel,
                _useBattlePose ? _selBtnStyle : _btnStyle))
        {
            _useBattlePose = !_useBattlePose;
            ApplyPoseToPreview();
        }

        // 왼쪽 패널 숨김 토글
        string leftLabel = _showLeftPanel ? "◀ 왼쪽 패널" : "▶ 왼쪽 패널";
        if (GUI.Button(new Rect(RefW - 630, 12, 150, 32), leftLabel,
                _showLeftPanel ? _selBtnStyle : _btnStyle))
        {
            _showLeftPanel = !_showLeftPanel;
        }
    }

    private Vector2 _leftScroll;
    private void DrawLeftPanel()
    {
        const float x = 12f, y = 64f, w = 230f, h = RefH - 100f;
        DrawPanelBg(new Rect(x, y, w, h));

        GUI.Label(new Rect(x + 12, y + 8, w - 24, 22), "캐릭터", _smallStyle);

        float listY = y + 34f;
        float listH = h - 50f;
        _leftScroll = GUI.BeginScrollView(
            new Rect(x + 6, listY, w - 12, listH),
            _leftScroll,
            new Rect(0, 0, w - 32, ComputeLeftContentHeight()));

        float cy = 0f;
        for (int i = 0; i < _characters.Count; i++)
        {
            string ch = _characters[i];
            bool selected = ch == _selectedCharacter;
            if (GUI.Button(new Rect(0, cy, w - 30, 26), ch, selected ? _selBtnStyle : _btnStyle))
                SelectCharacter(ch);
            cy += 28f;
        }

        if (_characters.Count > 0 && !string.IsNullOrEmpty(_selectedCharacter))
        {
            cy += 8f;
            GUI.Label(new Rect(0, cy, w - 30, 20), "애니메이션", _smallStyle);
            cy += 22f;
            for (int i = 0; i < _clips.Count; i++)
            {
                var clip = _clips[i];
                bool sel = i == _selectedClipIdx;
                string label = $"{clip.name}  ({clip.frames.Count}f)";
                if (GUI.Button(new Rect(0, cy, w - 30, 26), label, sel ? _selBtnStyle : _btnStyle))
                    SelectClip(i);
                cy += 28f;
            }
        }

        GUI.EndScrollView();
    }

    private float ComputeLeftContentHeight()
    {
        float h = _characters.Count * 28f + 8f;
        if (!string.IsNullOrEmpty(_selectedCharacter))
            h += 22f + _clips.Count * 28f;
        return Mathf.Max(h, 100f);
    }

    private void DrawCenterHint()
    {
        // 중앙은 Camera.main이 BattleEntityView를 직접 렌더링 — OnGUI 오버레이 없음.
        // 클립 미선택 시에만 안내 라벨을 겹쳐서 표시.
        if (_selectedClipIdx < 0 || _selectedClipIdx >= _clips.Count || _clips[_selectedClipIdx].frames.Count == 0)
        {
            GUI.Label(new Rect(RefW * 0.5f - 200, RefH * 0.5f - 10, 400, 24),
                "← 왼쪽에서 캐릭터/애니 선택",
                _statusStyle);
        }
    }

    private Vector2 _rightScroll;
    private void DrawRightPanel()
    {
        const float x = RefW - 262f, y = 64f, w = 250f, h = RefH - 100f;
        DrawPanelBg(new Rect(x, y, w, h));

        _rightScroll = GUI.BeginScrollView(
            new Rect(x, y, w, h),
            _rightScroll,
            new Rect(x, y, w - 16, 900f));

        float cy = y + 12;

        GUI.Label(new Rect(x + 12, cy, w - 24, 22), "재생 (전투와 동일)", _titleStyle); cy += 30;

        // 공격 버튼 — 큰 전용 버튼 (1 / Space)
        if (GUI.Button(new Rect(x + 12, cy, w - 24, 54),
                "⚔  공격 (1 / Space)",
                _lastMode == PlayMode.Attack ? _selBtnStyle : _modeBtnStyle))
        {
            TriggerPlayback(PlayMode.Attack);
            _replayTimer = _autoReplay ? _attackDuration + _replayGap : float.PositiveInfinity;
        }
        cy += 60;

        // 소환 / 피격 보조 버튼
        float bw = (w - 30) / 2f;
        if (GUI.Button(new Rect(x + 12, cy, bw, 30), "소환 (2)",
                _lastMode == PlayMode.Summon ? _selBtnStyle : _modeBtnStyle))
        {
            TriggerPlayback(PlayMode.Summon);
            _replayTimer = _autoReplay ? _attackDuration + _replayGap : float.PositiveInfinity;
        }
        if (GUI.Button(new Rect(x + 12 + bw + 6, cy, bw, 30), "피격 (3)",
                _lastMode == PlayMode.Hit ? _selBtnStyle : _modeBtnStyle))
        {
            TriggerPlayback(PlayMode.Hit);
            _replayTimer = _autoReplay ? 0.35f + _replayGap : float.PositiveInfinity;
        }
        cy += 36;

        _autoReplay = GUI.Toggle(new Rect(x + 12, cy, w - 24, 22), _autoReplay, "  자동 반복 재생", _smallStyle); cy += 28;

        // 실제 전투 레이아웃 토글
        bool prevBattlePose = _useBattlePose;
        _useBattlePose = GUI.Toggle(new Rect(x + 12, cy, w - 24, 22), _useBattlePose, "  실제 전투 레이아웃", _smallStyle); cy += 28;
        if (prevBattlePose != _useBattlePose) ApplyPoseToPreview();

        GUI.Label(new Rect(x + 12, cy, w - 24, 20), $"지속 시간: {_attackDuration:0.00}s", _smallStyle); cy += 18;
        _attackDuration = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), _attackDuration, 0.15f, 2.5f); cy += 26;

        GUI.Label(new Rect(x + 12, cy, w - 24, 20), $"전진 거리: {_attackDistance:0.00}u", _smallStyle); cy += 18;
        _attackDistance = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), _attackDistance, 0f, 2f); cy += 26;

        string hLabel = _useBattlePose
            ? $"캐릭터 높이: 전투 자동 ({ComputeBattlePoseHeight():0.0}u)"
            : $"캐릭터 높이(월드): {_worldHeight:0.0}u";
        GUI.Label(new Rect(x + 12, cy, w - 24, 20), hLabel, _smallStyle); cy += 18;
        GUI.enabled = !_useBattlePose;
        float prevH = _worldHeight;
        _worldHeight = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), _worldHeight, 1f, 8f); cy += 26;
        GUI.enabled = true;
        if (!Mathf.Approximately(prevH, _worldHeight) && _previewView != null && !_useBattlePose)
            _previewView.SetWorldHeight(_worldHeight);

        GUI.Label(new Rect(x + 12, cy, w - 24, 20), $"Strike-ext 배율: {_strikeExtendedBoost:0.00}x", _smallStyle); cy += 18;
        float prevBoost = _strikeExtendedBoost;
        _strikeExtendedBoost = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), _strikeExtendedBoost, 1f, 2f); cy += 26;
        if (!Mathf.Approximately(prevBoost, _strikeExtendedBoost) && _previewView != null)
            _previewView.SetStrikeExtendedScaleBoost(_strikeExtendedBoost);

        // ── 배경 설정 ──
        cy += 6;
        GUI.Label(new Rect(x + 12, cy, w - 24, 22), "배경 설정", _titleStyle); cy += 28;

        // 프리셋 색상 버튼
        GUI.Label(new Rect(x + 12, cy, w - 24, 20), "프리셋 색상", _smallStyle); cy += 20;
        if (GUI.Button(new Rect(x + 12,  cy, 52, 26), "어둠", _btnStyle))   { _bgColor = new Color(0.12f, 0.12f, 0.14f, 1f); ClearBgImage(); }
        if (GUI.Button(new Rect(x + 68,  cy, 52, 26), "전투", _btnStyle))   { _bgColor = new Color(0.18f, 0.18f, 0.22f, 1f); ClearBgImage(); }
        if (GUI.Button(new Rect(x + 124, cy, 52, 26), "회색", _btnStyle))   { _bgColor = new Color(0.5f, 0.5f, 0.5f, 1f); ClearBgImage(); }
        if (GUI.Button(new Rect(x + 180, cy, 56, 26), "흰색", _btnStyle))   { _bgColor = Color.white; ClearBgImage(); }
        cy += 30;

        // RGB 슬라이더
        GUI.Label(new Rect(x + 12, cy, 30, 18), "R", _smallStyle);
        _bgColor.r = GUI.HorizontalSlider(new Rect(x + 30, cy + 2, w - 46, 16), _bgColor.r, 0f, 1f); cy += 20;
        GUI.Label(new Rect(x + 12, cy, 30, 18), "G", _smallStyle);
        _bgColor.g = GUI.HorizontalSlider(new Rect(x + 30, cy + 2, w - 46, 16), _bgColor.g, 0f, 1f); cy += 20;
        GUI.Label(new Rect(x + 12, cy, 30, 18), "B", _smallStyle);
        _bgColor.b = GUI.HorizontalSlider(new Rect(x + 30, cy + 2, w - 46, 16), _bgColor.b, 0f, 1f); cy += 24;

        // 배경 이미지 선택
        if (_bgNames.Count > 0)
        {
            GUI.Label(new Rect(x + 12, cy, w - 24, 20), "배경 이미지", _smallStyle); cy += 20;
            if (GUI.Button(new Rect(x + 12, cy, w - 24, 24), "없음 (단색)", _selectedBgIdx < 0 ? _selBtnStyle : _btnStyle))
                ClearBgImage();
            cy += 26;
            for (int i = 0; i < _bgNames.Count; i++)
            {
                bool sel = i == _selectedBgIdx;
                if (GUI.Button(new Rect(x + 12, cy, w - 24, 24), _bgNames[i], sel ? _selBtnStyle : _btnStyle))
                    SelectBgImage(i);
                cy += 26;
            }
            cy += 4;
        }

        // 카메라 줌
        GUI.Label(new Rect(x + 12, cy, w - 24, 20), $"카메라 줌: {_cameraZoom:0.0}", _smallStyle); cy += 18;
        _cameraZoom = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), _cameraZoom, 1f, 12f); cy += 24;

        // 그리드 토글
        _showGrid = GUI.Toggle(new Rect(x + 12, cy, w - 24, 22), _showGrid, "  그리드 표시", _smallStyle); cy += 28;

        // 단축키
        GUI.Label(new Rect(x + 12, cy, w - 24, 22), "단축키", _smallStyle); cy += 22;
        foreach (var t in new[]
        {
            "1 — 공격 재생",
            "2 — 소환 재생",
            "3 — 피격 재생",
            "Space — 마지막 모드 재실행",
            "R — 프리뷰 리셋",
            "G — 그리드 토글",
            "F5 — 폴더 재스캔",
            "Esc — 로비로",
        })
        {
            GUI.Label(new Rect(x + 12, cy, w - 24, 18), t, _smallStyle);
            cy += 18;
        }

        GUI.EndScrollView();
    }

    private void DrawStatusBar()
    {
        if (string.IsNullOrEmpty(_statusMessage)) return;
        var r = new Rect(12, RefH - 30, RefW - 24, 22);
        var prev = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
        GUI.Label(new Rect(r.x + 8, r.y + 2, r.width - 16, r.height), _statusMessage, _statusStyle);
    }

    // ───────────────────────── 배경 / 그리드 ─────────────────────────

    private void LoadBackgroundSprites()
    {
        _bgSprites.Clear();
        _bgNames.Clear();
        var sprites = Resources.LoadAll<Sprite>("Backgrounds");
        if (sprites != null)
        {
            foreach (var sp in sprites)
            {
                _bgSprites.Add(sp);
                _bgNames.Add(sp.name);
            }
        }
    }

    private void SelectBgImage(int idx)
    {
        if (idx < 0 || idx >= _bgSprites.Count) return;
        _selectedBgIdx = idx;
        if (_bgImageSR != null)
        {
            _bgImageSR.sprite = _bgSprites[idx];
            _bgImageGO.SetActive(true);
            UpdateBgImageScale();
        }
    }

    private void ClearBgImage()
    {
        _selectedBgIdx = -1;
        if (_bgImageGO != null) _bgImageGO.SetActive(false);
    }

    private void UpdateBgImageScale()
    {
        if (_bgImageSR == null || _bgImageSR.sprite == null || !_bgImageGO.activeSelf) return;
        if (_cachedMainCam == null) return;

        // 배경을 카메라 뷰에 꽉 채움
        float camH = _cachedMainCam.orthographicSize * 2f;
        float camW = camH * _cachedMainCam.aspect;
        var bounds = _bgImageSR.sprite.bounds;
        float scaleX = camW / bounds.size.x;
        float scaleY = camH / bounds.size.y;
        float scale = Mathf.Max(scaleX, scaleY); // cover 방식
        _bgImageGO.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void CreateGridTexture()
    {
        const int size = 256;
        const int cell = 16;
        _gridTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _gridTex.filterMode = FilterMode.Point;
        var c0 = new Color(1, 1, 1, 0.08f);
        var c1 = new Color(1, 1, 1, 0.03f);
        for (int y = 0; y < size; y++)
            for (int xp = 0; xp < size; xp++)
            {
                bool checker = ((xp / cell) + (y / cell)) % 2 == 0;
                _gridTex.SetPixel(xp, y, checker ? c0 : c1);
            }
        _gridTex.Apply();

        var rect = new Rect(0, 0, size, size);
        var pivot = new Vector2(0.5f, 0.5f);
        var sp = Sprite.Create(_gridTex, rect, pivot, 16f); // 16 ppu → 큰 타일
        if (_gridSR != null)
        {
            _gridSR.sprite = sp;
            _gridGO.transform.localScale = Vector3.one * 4f;
        }
    }

    // ───────────────────────── 스캔 / 로드 ─────────────────────────

    private void RefreshAll()
    {
        _characters.Clear();
        _clips.Clear();
        _selectedClipIdx = -1;
        _statusMessage = null;

#if UNITY_EDITOR
        const string root = "Assets/Resources/AnimationTest";
        if (AssetDatabase.IsValidFolder(root))
        {
            foreach (var sub in AssetDatabase.GetSubFolders(root))
            {
                string name = System.IO.Path.GetFileName(sub);
                if (!_characters.Contains(name)) _characters.Add(name);
            }
        }
        else
        {
            _statusMessage = $"'{root}' 폴더가 없어 — 캐릭터 폴더를 그 아래에 만들면 여기 뜸.";
        }
#endif

        foreach (var m in _manualCharacterFolders)
            if (!string.IsNullOrEmpty(m) && !_characters.Contains(m)) _characters.Add(m);

        _characters.Sort();

        if (!string.IsNullOrEmpty(_selectedCharacter) && _characters.Contains(_selectedCharacter))
            SelectCharacter(_selectedCharacter);
        else if (_characters.Count > 0)
            SelectCharacter(_characters[0]);
    }

    private void SelectCharacter(string ch)
    {
        _selectedCharacter = ch;
        _clips.Clear();
        _selectedClipIdx = -1;

        if (string.IsNullOrEmpty(ch))
        {
            _statusMessage = "캐릭터가 선택되지 않음";
            return;
        }

        var sprites = LoadCharacterSprites(ch);
        if (sprites == null || sprites.Count == 0)
        {
            _statusMessage = $"'{ch}' 폴더에 Sprite가 없음. PNG가 Texture Type=Sprite로 임포트됐는지 확인.";
            return;
        }

        // 중복 제거 — AssetDatabase와 Resources 둘 다에서 잡힐 수 있음.
        var uniqueSprites = new HashSet<Sprite>(sprites);
        var groups = new Dictionary<string, List<(int frame, Sprite sp)>>();
        foreach (var sp in uniqueSprites)
        {
            if (sp == null) continue;
            var m = FramePatternStrict.Match(sp.name);
            if (!m.Success) m = FramePatternLoose.Match(sp.name);
            if (!m.Success)
            {
                const string key = "⚠ naming-invalid";
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new();
                l.Add((9999, sp));
                continue;
            }
            string baseName = m.Groups[1].Value.ToLowerInvariant();
            int frameNum = int.Parse(m.Groups[2].Value);
            if (!groups.TryGetValue(baseName, out var list)) groups[baseName] = list = new();
            list.Add((frameNum, sp));
        }

        foreach (var kv in groups)
        {
            kv.Value.Sort((a, b) => a.frame.CompareTo(b.frame));
            var clip = new AnimClip { name = kv.Key };
            foreach (var (_, sp) in kv.Value) clip.frames.Add(sp);
            _clips.Add(clip);
        }
        _clips.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        if (_clips.Count == 0)
        {
            _statusMessage = $"'{ch}' 폴더의 파일명이 규칙({{이름}}_f01.png)에 맞는지 확인.";
            return;
        }

        SelectClip(0);
        _statusMessage = $"'{ch}' 로드 완료 — 애니 {_clips.Count}개";
    }

    private void SelectClip(int idx)
    {
        _selectedClipIdx = idx;
        ApplySelectedClipToPreview();
    }

    // 스프라이트 로딩 — Editor에서는 파일 시스템 + AssetDatabase로 직접 읽어
    // Resources 인덱스 갱신 이슈 회피. 빌드에서는 Resources.LoadAll 로 폴백.
    private List<Sprite> LoadCharacterSprites(string ch)
    {
        var result = new List<Sprite>();
#if UNITY_EDITOR
        // 파일 시스템으로 PNG 직접 열거 → AssetDatabase.LoadAllAssetsAtPath 로 sub-sprite까지 수집.
        // FindAssets("t:Sprite")는 PNG의 메인 에셋이 Texture2D라 놓치는 경우가 있음.
        string folder = $"Assets/Resources/AnimationTest/{ch}";
        int pngCount = 0;
        if (AssetDatabase.IsValidFolder(folder))
        {
            string cwd = System.IO.Directory.GetCurrentDirectory();
            var absFolder = System.IO.Path.Combine(cwd, folder);
            if (System.IO.Directory.Exists(absFolder))
            {
                foreach (var absPath in System.IO.Directory.GetFiles(absFolder, "*.png"))
                {
                    pngCount++;
                    string rel = absPath.Substring(cwd.Length + 1).Replace('\\', '/');

                    // Single 모드: 메인 에셋이 Sprite
                    var mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(rel);
                    if (mainSprite != null) result.Add(mainSprite);

                    // Multiple 모드: sub-asset들이 Sprite. LoadAllAssetRepresentationsAtPath는
                    // 메인 에셋을 제외한 sub-asset만 리턴하므로 중복 걱정 X.
                    foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(rel))
                    {
                        if (rep is Sprite sp && !result.Contains(sp)) result.Add(sp);
                    }

                    // 여전히 비어 있으면 이 파일이 어떤 타입으로 임포트됐는지 진단 로그
                    if (mainSprite == null &&
                        AssetDatabase.LoadAllAssetRepresentationsAtPath(rel).Length == 0)
                    {
                        var mainObj = AssetDatabase.LoadMainAssetAtPath(rel);
                        Debug.LogWarning(
                            $"[AnimTest] {rel}: no Sprite. mainAssetType={mainObj?.GetType().Name ?? "null"}. " +
                            $"PNG가 Texture Type=Sprite로 임포트됐는지 확인 (Inspector → Texture Type).");
                    }
                }
            }
        }
        Debug.Log($"[AnimTest] '{ch}' via AssetDatabase: folder={folder} validFolder={AssetDatabase.IsValidFolder(folder)} pngs={pngCount} sprites={result.Count}");
        if (result.Count > 0) return result;
#endif
        var viaResources = Resources.LoadAll<Sprite>($"AnimationTest/{ch}");
        if (viaResources != null) result.AddRange(viaResources);
        Debug.Log($"[AnimTest] '{ch}' via Resources: {result.Count} sprites");
        return result;
    }

    // ───────────────────────── 스타일 ─────────────────────────

    private void DrawPanelBg(Rect r)
    {
        var prev = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.45f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18, fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.95f, 0.7f) },
        };
        _smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
        };
        _statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13, alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.95f, 0.7f) },
        };
        _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12 };
        _modeBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        _selBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.92f, 0.4f), background = GUI.skin.button.active.background },
        };
        _stylesReady = true;
    }
}
