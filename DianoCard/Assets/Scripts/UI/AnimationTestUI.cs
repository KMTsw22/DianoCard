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
    // 캐릭터는 Character_infield/{이름}/ 서브폴더, 몬스터는 Monsters/{이름}.png 단일 파일에서 로드한다.
    // 레거시 AnimationTest/{이름}/ 폴더가 남아 있으면 캐릭터로 추가 병합한다.
    private readonly List<string> _characters = new();
    private readonly List<string> _monsters = new();

    // 캐릭터 슬롯과 몬스터 슬롯을 동시에 띄울 수 있게 각 항목을 개별 오브젝트/클립/크기로 관리한다.
    private class EntitySlot
    {
        public readonly bool isMonster;
        public GameObject go;
        public SpriteRenderer sr;
        public BattleEntityView view;
        public string selectedName;
        public readonly List<AnimClip> clips = new();
        public int clipIdx = -1;
        public float worldHeight = 4f;       // 자유 배치 모드에서의 월드 높이
        public float sizeScale   = 1f;       // 전투 레이아웃에 곱해주는 배율 (크기 조절용)
        public float xOffsetGui  = 0f;       // 몬스터 좌우 위치 오프셋 (GUI px, 캐릭터는 0 고정)
        public EntitySlot(bool isMonster) { this.isMonster = isMonster; }
    }

    private readonly EntitySlot _charSlot = new EntitySlot(false);
    private readonly EntitySlot _monsterSlot = new EntitySlot(true);
    private bool _focusIsMonster;            // 어느 슬롯의 컨트롤이 우측 패널에 떠 있는지
    private EntitySlot Focus => _focusIsMonster ? _monsterSlot : _charSlot;

    private const string CharacterRoot = "Assets/Resources/Character_infield";
    private const string MonsterRoot = "Assets/Resources/Monsters";
    private const string LegacyCharacterRoot = "Assets/Resources/AnimationTest";

    // ── 재생 옵션 ───────────────────────────────────────────
    private float _attackDuration = 0.75f;         // BattleEntityView.PlayAttack와 동일 기본값
    private float _attackDistance = 0.7f;
    private float _strikeExtendedBoost = 1.2f;
    private bool  _autoReplay = false;             // 기본 OFF — 공격 버튼 눌러 수동 발동
    private float _replayGap = 0.4f;
    private PlayMode _lastMode = PlayMode.Attack;

    // 실제 전투 화면과 동일한 배치 사용 여부. ON이면 캐릭터 위치/크기가 BattleUI의
    // DrawPlayerNPC와 정확히 일치하도록 자동 계산 — GUI(180, 430)에 260px 높이.
    private bool _useBattlePose = true;
    private bool _showLeftPanel = true;
    private bool _showRightPanel = true;
    // BattleUI.cs와 동일한 상수 — 수정 시 양쪽 맞출 것.
    private const float BattleGroundY = 540f;
    private const float BattlePlayerHeight = 260f;
    private const float BattlePlayerCenterX = 180f;
    // 몬스터(적) 슬롯 — BattleUI.LayoutSlots의 첫 번째 적 위치와 동일.
    // _slotPositions[e] = (1070, GroundY - h/2), feet가 GroundY 에 닿도록 그려짐.
    private const float BattleEnemyCenterX = 1070f;
    private const float BattleEnemyHeight = 240f; // Normal 적 기본 높이 (Elite 320, Boss 400)

    private float _replayTimer; // 다음 자동 트리거까지 남은 시간

    // ── 프리뷰 월드 객체 ────────────────────────────────────
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
                if (_useBattlePose) ApplyAllPoses();
            }
        }

        // 그리드 토글
        if (_gridGO != null) _gridGO.SetActive(_showGrid);

        // 배경 이미지 스케일을 카메라에 맞춤
        UpdateBgImageScale();

        // 자동 반복 재생 — 포커스 슬롯 기준
        if (_autoReplay && Focus.clipIdx >= 0 && Focus.view != null)
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
            if (kb.rKey.wasPressedThisFrame) { ApplySelectedClipToSlot(_charSlot); ApplySelectedClipToSlot(_monsterSlot); }
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
        // 내 _bgImageSR/_gridSR/slot.sr 은 이 루프 이후에 새로 만들어지므로 잡히지 않는다.
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

        SetupSlot(_charSlot,    "[AnimTestPreview_Char]");
        SetupSlot(_monsterSlot, "[AnimTestPreview_Monster]");

        LoadBackgroundSprites();

        _previewActive = true;
        _replayTimer = 0f;

        // 전투 맵 기본값: BG_Ch1_Battle_이 있으면 자동 선택 (실제 전투와 동일 배경)
        int defaultBg = _bgNames.FindIndex(n => n.StartsWith("BG_Ch1_Battle_", System.StringComparison.OrdinalIgnoreCase));
        if (defaultBg >= 0) SelectBgImage(defaultBg);

        // 기본 위치 = 실제 전투 위치. 클립 선택 전이라도 BattlePose 좌표로 고정.
        _useBattlePose = true;
        ApplyPoseToSlot(_charSlot);
        ApplyPoseToSlot(_monsterSlot);

        // 이미 선택된 클립이 있으면 그대로 적용
        if (_charSlot.clipIdx    >= 0) ApplySelectedClipToSlot(_charSlot);
        if (_monsterSlot.clipIdx >= 0) ApplySelectedClipToSlot(_monsterSlot);
    }

    private void SetupSlot(EntitySlot slot, string goName)
    {
        slot.go = new GameObject(goName);
        slot.sr = slot.go.AddComponent<SpriteRenderer>();
        slot.view = slot.go.AddComponent<BattleEntityView>();
        slot.view.SetStrikeExtendedScaleBoost(_strikeExtendedBoost);
        slot.go.SetActive(false); // 선택 전에는 숨김 — 스프라이트 주입 후 ApplySelectedClipToSlot 에서 켠다.
    }

    private void TeardownSlot(EntitySlot slot)
    {
        if (slot.go != null) Destroy(slot.go);
        slot.go = null;
        slot.sr = null;
        slot.view = null;
    }

    private void TeardownPreview()
    {
        TeardownSlot(_charSlot);
        TeardownSlot(_monsterSlot);
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

    // 지정한 슬롯에 현재 선택된 클립 프레임을 주입. 다중 프레임이면 공격 시퀀스로도 등록.
    private void ApplySelectedClipToSlot(EntitySlot slot)
    {
        if (slot.view == null) return;
        if (slot.clipIdx < 0 || slot.clipIdx >= slot.clips.Count)
        {
            // 클립이 없는 슬롯은 비활성화 — 빈 GO가 원점에 떠 있으면 지저분해진다.
            if (slot.go != null) slot.go.SetActive(false);
            return;
        }
        var clip = slot.clips[slot.clipIdx];
        if (clip.frames.Count == 0) return;

        // 첫 프레임을 idle/기본 스프라이트로, 시퀀스로도 등록. summon용으로도 첫 프레임 재사용.
        slot.view.SetSprite(clip.frames[0]);
        slot.view.SetAttackSequence(clip.frames.ToArray());
        slot.view.SetSummonFrame(clip.frames[0]);
        if (slot.go != null) slot.go.SetActive(true);

        ApplyPoseToSlot(slot);
        _replayTimer = 0f;
    }

    private void ApplyAllPoses()
    {
        ApplyPoseToSlot(_charSlot);
        ApplyPoseToSlot(_monsterSlot);
    }

    // 슬롯의 월드 좌표 + 월드 높이를 결정. _useBattlePose에 따라 실제 전투 레이아웃
    // (BattleUI.DrawPlayerNPC / DrawEnemy와 동일 공식)을 쓰거나, 슬롯별 슬라이더 값을 따른다.
    // 몬스터 슬롯은 오른쪽(1070, GroundY), 캐릭터 슬롯은 왼쪽(180, 430) 앵커.
    private void ApplyPoseToSlot(EntitySlot slot)
    {
        if (slot.view == null || _cachedMainCam == null) return;

        Vector2 feetGui, topGui;
        if (slot.isMonster)
        {
            // 몬스터는 xOffsetGui 로 좌우 이동 허용. 캐릭터는 고정 앵커.
            float centerX = BattleEnemyCenterX + slot.xOffsetGui;
            feetGui = new Vector2(centerX, BattleGroundY);
            topGui  = new Vector2(centerX, BattleGroundY - BattleEnemyHeight);
        }
        else
        {
            feetGui = new Vector2(BattlePlayerCenterX, BattleGroundY - 110f + BattlePlayerHeight * 0.5f);
            topGui  = new Vector2(BattlePlayerCenterX, BattleGroundY - 110f - BattlePlayerHeight * 0.5f);
        }
        var feetWorld = GuiToWorld(feetGui);
        var topWorld  = GuiToWorld(topGui);
        float battleHeight = Mathf.Abs(feetWorld.y - topWorld.y);
        // 전투 레이아웃 ON이면 battleHeight × sizeScale, OFF면 slot.worldHeight 그대로.
        float targetHeight = _useBattlePose ? battleHeight * Mathf.Max(0.1f, slot.sizeScale) : slot.worldHeight;

        // pivot 보정: BattleEntityView는 transform.position = _basePosition (pivot 위치).
        // 스프라이트 pivot이 Center면 bounds.min.y 만큼 basePosition을 위로 올려 발이 feetWorld 에 오게 만듦.
        Vector3 pivotOffset = Vector3.zero;
        var sp = slot.sr != null ? slot.sr.sprite : null;
        if (sp != null && sp.bounds.size.y > 0.001f)
        {
            float scale = targetHeight / sp.bounds.size.y;
            pivotOffset = new Vector3(0f, -sp.bounds.min.y * scale, 0f);
        }

        slot.view.SetBasePosition(feetWorld + pivotOffset);
        slot.view.SetWorldHeight(targetHeight);
    }

    // 전투 레이아웃 적용 시 계산되는 월드 높이 (라벨 표시용). 슬롯 종류에 따라 플레이어/적 레이아웃.
    private float ComputeBattlePoseHeight(EntitySlot slot)
    {
        if (_cachedMainCam == null) return 0f;
        Vector2 feetGui, topGui;
        if (slot.isMonster)
        {
            feetGui = new Vector2(BattleEnemyCenterX, BattleGroundY);
            topGui  = new Vector2(BattleEnemyCenterX, BattleGroundY - BattleEnemyHeight);
        }
        else
        {
            feetGui = new Vector2(BattlePlayerCenterX, BattleGroundY - 110f + BattlePlayerHeight * 0.5f);
            topGui  = new Vector2(BattlePlayerCenterX, BattleGroundY - 110f - BattlePlayerHeight * 0.5f);
        }
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
        var slot = Focus;
        if (slot.view == null || string.IsNullOrEmpty(slot.selectedName)) return;
        _lastMode = mode;
        // 몬스터는 왼쪽(플레이어 방향)으로 찌르고, 캐릭터는 오른쪽(적 방향)으로 찌른다.
        Vector3 dir = slot.isMonster ? Vector3.left : Vector3.right;
        switch (mode)
        {
            case PlayMode.Attack:
                slot.view.PlayAttack(dir, _attackDistance, _attackDuration);
                break;
            case PlayMode.Summon:
                slot.view.PlaySummon(dir, 0.18f, _attackDuration);
                break;
            case PlayMode.Hit:
                slot.view.PlayHit(0.35f);
                break;
        }
    }

    // ───────────────────────── OnGUI ─────────────────────────

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.AnimationTest) return;

        EnsureStyles();

        if (_characters.Count == 0 && _monsters.Count == 0 && string.IsNullOrEmpty(_statusMessage))
            RefreshAll();

        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        var prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        DrawHeader(gsm);
        if (_showLeftPanel) DrawLeftPanel();
        if (_showRightPanel) DrawRightPanel();
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
            ApplyAllPoses();
        }

        // 왼쪽 패널 숨김 토글
        string leftLabel = _showLeftPanel ? "◀ 왼쪽 패널" : "▶ 왼쪽 패널";
        if (GUI.Button(new Rect(RefW - 630, 12, 150, 32), leftLabel,
                _showLeftPanel ? _selBtnStyle : _btnStyle))
        {
            _showLeftPanel = !_showLeftPanel;
        }

        // 오른쪽 패널 숨김 토글
        string rightLabel = _showRightPanel ? "오른쪽 패널 ▶" : "오른쪽 패널 ◀";
        if (GUI.Button(new Rect(RefW - 790, 12, 150, 32), rightLabel,
                _showRightPanel ? _selBtnStyle : _btnStyle))
        {
            _showRightPanel = !_showRightPanel;
        }
    }

    private Vector2 _leftScroll;
    private void DrawLeftPanel()
    {
        const float x = 12f, y = 64f, w = 230f, h = RefH - 100f;
        DrawPanelBg(new Rect(x, y, w, h));

        GUI.Label(new Rect(x + 12, y + 8, w - 24, 22), "캐릭터 / 몬스터", _smallStyle);

        float listY = y + 34f;
        float listH = h - 50f;
        _leftScroll = GUI.BeginScrollView(
            new Rect(x + 6, listY, w - 12, listH),
            _leftScroll,
            new Rect(0, 0, w - 32, ComputeLeftContentHeight()));

        float cy = 0f;

        if (_characters.Count > 0)
        {
            GUI.Label(new Rect(0, cy, w - 30, 20), "── 캐릭터 ──", _smallStyle);
            cy += 22f;
            for (int i = 0; i < _characters.Count; i++)
            {
                string ch = _characters[i];
                bool selected = ch == _charSlot.selectedName;
                if (GUI.Button(new Rect(0, cy, w - 30, 26), ch, selected ? _selBtnStyle : _btnStyle))
                    SelectEntry(ch, isMonster: false);
                cy += 28f;
            }
            // 캐릭터 해제 버튼 — 지금 선택된 캐릭터를 씬에서 내림.
            if (!string.IsNullOrEmpty(_charSlot.selectedName))
            {
                if (GUI.Button(new Rect(0, cy, w - 30, 22), "× 캐릭터 해제", _btnStyle))
                    ClearSlot(_charSlot);
                cy += 26f;
            }
            cy += 6f;
        }

        if (_monsters.Count > 0)
        {
            GUI.Label(new Rect(0, cy, w - 30, 20), "── 몬스터 ──", _smallStyle);
            cy += 22f;
            for (int i = 0; i < _monsters.Count; i++)
            {
                string m = _monsters[i];
                bool selected = m == _monsterSlot.selectedName;
                if (GUI.Button(new Rect(0, cy, w - 30, 26), m, selected ? _selBtnStyle : _btnStyle))
                    SelectEntry(m, isMonster: true);
                cy += 28f;
            }
            if (!string.IsNullOrEmpty(_monsterSlot.selectedName))
            {
                if (GUI.Button(new Rect(0, cy, w - 30, 22), "× 몬스터 해제", _btnStyle))
                    ClearSlot(_monsterSlot);
                cy += 26f;
            }
            cy += 6f;
        }

        // 포커스 슬롯의 클립 리스트 — 재생 버튼/단축키의 대상.
        var focus = Focus;
        if (!string.IsNullOrEmpty(focus.selectedName) && focus.clips.Count > 0)
        {
            cy += 4f;
            string focusLabel = _focusIsMonster ? "몬스터 애니메이션" : "캐릭터 애니메이션";
            GUI.Label(new Rect(0, cy, w - 30, 20), focusLabel, _smallStyle);
            cy += 22f;
            for (int i = 0; i < focus.clips.Count; i++)
            {
                var clip = focus.clips[i];
                bool sel = i == focus.clipIdx;
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
        float h = 0f;
        if (_characters.Count > 0) h += 22f + _characters.Count * 28f + (string.IsNullOrEmpty(_charSlot.selectedName) ? 0f : 26f) + 6f;
        if (_monsters.Count > 0)   h += 22f + _monsters.Count * 28f + (string.IsNullOrEmpty(_monsterSlot.selectedName) ? 0f : 26f) + 6f;
        var focus = Focus;
        if (!string.IsNullOrEmpty(focus.selectedName) && focus.clips.Count > 0)
            h += 4f + 22f + focus.clips.Count * 28f;
        return Mathf.Max(h, 100f);
    }

    private void DrawCenterHint()
    {
        // 두 슬롯 모두 비어있을 때만 안내 라벨.
        if (string.IsNullOrEmpty(_charSlot.selectedName) && string.IsNullOrEmpty(_monsterSlot.selectedName))
        {
            GUI.Label(new Rect(RefW * 0.5f - 200, RefH * 0.5f - 10, 400, 24),
                "← 왼쪽에서 캐릭터 / 몬스터 선택",
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
            new Rect(x, y, w - 16, 1120f));

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

        // 포커스 전환 — 재생/클립 조작이 어느 슬롯에 적용되는지 결정
        float fw = (w - 30) / 2f;
        if (GUI.Button(new Rect(x + 12, cy, fw, 26), "▶ 캐릭터 포커스",
                !_focusIsMonster ? _selBtnStyle : _btnStyle))
            _focusIsMonster = false;
        if (GUI.Button(new Rect(x + 12 + fw + 6, cy, fw, 26), "▶ 몬스터 포커스",
                _focusIsMonster ? _selBtnStyle : _btnStyle))
            _focusIsMonster = true;
        cy += 32;

        // 실제 전투 레이아웃 토글
        bool prevBattlePose = _useBattlePose;
        _useBattlePose = GUI.Toggle(new Rect(x + 12, cy, w - 24, 22), _useBattlePose, "  실제 전투 레이아웃", _smallStyle); cy += 28;
        if (prevBattlePose != _useBattlePose) ApplyAllPoses();

        GUI.Label(new Rect(x + 12, cy, w - 24, 20), $"지속 시간: {_attackDuration:0.00}s", _smallStyle); cy += 18;
        _attackDuration = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), _attackDuration, 0.15f, 2.5f); cy += 26;

        GUI.Label(new Rect(x + 12, cy, w - 24, 20), $"전진 거리: {_attackDistance:0.00}u", _smallStyle); cy += 18;
        _attackDistance = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), _attackDistance, 0f, 2f); cy += 26;

        // ── 크기 조절: 캐릭터 / 몬스터 각각 ──
        cy += 4;
        GUI.Label(new Rect(x + 12, cy, w - 24, 22), "크기 조절", _titleStyle); cy += 26;

        cy = DrawSlotSizeControls(x, cy, w, _charSlot,    "캐릭터");
        cy = DrawSlotSizeControls(x, cy, w, _monsterSlot, "몬스터");

        GUI.Label(new Rect(x + 12, cy, w - 24, 20), $"Strike-ext 배율: {_strikeExtendedBoost:0.00}x", _smallStyle); cy += 18;
        float prevBoost = _strikeExtendedBoost;
        _strikeExtendedBoost = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), _strikeExtendedBoost, 1f, 2f); cy += 26;
        if (!Mathf.Approximately(prevBoost, _strikeExtendedBoost))
        {
            if (_charSlot.view != null)    _charSlot.view.SetStrikeExtendedScaleBoost(_strikeExtendedBoost);
            if (_monsterSlot.view != null) _monsterSlot.view.SetStrikeExtendedScaleBoost(_strikeExtendedBoost);
        }

        // ── 배경 설정 ──
        cy += 6;
        GUI.Label(new Rect(x + 12, cy, w - 24, 22), "배경", _titleStyle); cy += 28;

        // 배경 이미지 선택 — Backgrounds/ 에 들어있는 전투/엘리트/보스 3종을 친숙한 이름으로.
        if (_bgNames.Count > 0)
        {
            if (GUI.Button(new Rect(x + 12, cy, w - 24, 26), "단색 (이미지 없음)",
                    _selectedBgIdx < 0 ? _selBtnStyle : _btnStyle))
                ClearBgImage();
            cy += 30;

            for (int i = 0; i < _bgNames.Count; i++)
            {
                bool sel = i == _selectedBgIdx;
                if (GUI.Button(new Rect(x + 12, cy, w - 24, 28), FriendlyBgLabel(_bgNames[i]), sel ? _selBtnStyle : _btnStyle))
                    SelectBgImage(i);
                cy += 32;
            }
            cy += 4;
        }

        // 단색일 때만 색 프리셋/RGB 슬라이더 노출 — 이미지 선택 중엔 가려둬서 UI 혼란 줄임.
        if (_selectedBgIdx < 0)
        {
            GUI.Label(new Rect(x + 12, cy, w - 24, 20), "단색 프리셋", _smallStyle); cy += 20;
            if (GUI.Button(new Rect(x + 12,  cy, 52, 26), "어둠", _btnStyle))   { _bgColor = new Color(0.12f, 0.12f, 0.14f, 1f); ClearBgImage(); }
            if (GUI.Button(new Rect(x + 68,  cy, 52, 26), "전투", _btnStyle))   { _bgColor = new Color(0.18f, 0.18f, 0.22f, 1f); ClearBgImage(); }
            if (GUI.Button(new Rect(x + 124, cy, 52, 26), "회색", _btnStyle))   { _bgColor = new Color(0.5f, 0.5f, 0.5f, 1f); ClearBgImage(); }
            if (GUI.Button(new Rect(x + 180, cy, 56, 26), "흰색", _btnStyle))   { _bgColor = Color.white; ClearBgImage(); }
            cy += 30;

            GUI.Label(new Rect(x + 12, cy, 30, 18), "R", _smallStyle);
            _bgColor.r = GUI.HorizontalSlider(new Rect(x + 30, cy + 2, w - 46, 16), _bgColor.r, 0f, 1f); cy += 20;
            GUI.Label(new Rect(x + 12, cy, 30, 18), "G", _smallStyle);
            _bgColor.g = GUI.HorizontalSlider(new Rect(x + 30, cy + 2, w - 46, 16), _bgColor.g, 0f, 1f); cy += 20;
            GUI.Label(new Rect(x + 12, cy, 30, 18), "B", _smallStyle);
            _bgColor.b = GUI.HorizontalSlider(new Rect(x + 30, cy + 2, w - 46, 16), _bgColor.b, 0f, 1f); cy += 24;
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

    // BG_Ch1_Battle_01 같은 파일명을 "1챕터 전투" 식 한글 라벨로. 매핑에 없으면 원본 이름 그대로.
    private static string FriendlyBgLabel(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return filename;
        string s = filename;
        // 챕터 치환
        s = Regex.Replace(s, @"^BG_Ch(\d+)_", m => $"{m.Groups[1].Value}챕터 ");
        // 상황 키워드
        s = s.Replace("Battle", "전투")
             .Replace("Elite",  "엘리트")
             .Replace("Boss",   "보스");
        // _01 같은 트레일링 인덱스 제거
        s = Regex.Replace(s, @"_\d+$", "");
        // 남은 언더스코어는 공백으로
        s = s.Replace('_', ' ').Trim();
        return s;
    }

    // 슬롯 하나의 크기 슬라이더. 전투 레이아웃 ON이면 sizeScale(×배율), OFF면 worldHeight 절대값.
    // 몬스터는 좌우 위치 슬라이더도 함께 노출한다.
    private float DrawSlotSizeControls(float x, float cy, float w, EntitySlot slot, string label)
    {
        bool hasEntry = !string.IsNullOrEmpty(slot.selectedName);
        string prefix = hasEntry ? $"{label} ({slot.selectedName})" : $"{label} (미선택)";

        if (_useBattlePose)
        {
            float autoH = ComputeBattlePoseHeight(slot) * Mathf.Max(0.1f, slot.sizeScale);
            GUI.Label(new Rect(x + 12, cy, w - 24, 20),
                $"{prefix} 배율: {slot.sizeScale:0.00}x  ({autoH:0.0}u)", _smallStyle); cy += 18;
            float prev = slot.sizeScale;
            slot.sizeScale = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), slot.sizeScale, 0.3f, 3f); cy += 26;
            if (!Mathf.Approximately(prev, slot.sizeScale)) ApplyPoseToSlot(slot);
        }
        else
        {
            GUI.Label(new Rect(x + 12, cy, w - 24, 20),
                $"{prefix} 높이(월드): {slot.worldHeight:0.0}u", _smallStyle); cy += 18;
            float prev = slot.worldHeight;
            slot.worldHeight = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), slot.worldHeight, 1f, 12f); cy += 26;
            if (!Mathf.Approximately(prev, slot.worldHeight)) ApplyPoseToSlot(slot);
        }

        if (slot.isMonster)
        {
            // 기본 앵커(1070) 기준 오프셋. -900~+200 사이면 스크린(1280) 안에서 거의 자유 이동.
            float actualX = BattleEnemyCenterX + slot.xOffsetGui;
            GUI.Label(new Rect(x + 12, cy, w - 24, 20),
                $"{label} 좌우: {slot.xOffsetGui:+0;-0;0}px  (X={actualX:0})", _smallStyle); cy += 18;
            float prevX = slot.xOffsetGui;
            slot.xOffsetGui = GUI.HorizontalSlider(new Rect(x + 12, cy, w - 24, 20), slot.xOffsetGui, -900f, 200f); cy += 26;
            if (!Mathf.Approximately(prevX, slot.xOffsetGui)) ApplyPoseToSlot(slot);
        }

        return cy;
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
        _monsters.Clear();
        _statusMessage = null;

#if UNITY_EDITOR
        // 캐릭터: Character_infield/ 의 서브폴더 (예: Archaeologist). 실제 전투에 쓰이는 최신 시퀀스.
        if (AssetDatabase.IsValidFolder(CharacterRoot))
        {
            foreach (var sub in AssetDatabase.GetSubFolders(CharacterRoot))
            {
                string name = System.IO.Path.GetFileName(sub);
                if (!_characters.Contains(name)) _characters.Add(name);
            }
        }

        // 레거시 AnimationTest/ 하위 폴더도 병합 (옛 프리뷰 데이터가 남아 있을 수 있음)
        if (AssetDatabase.IsValidFolder(LegacyCharacterRoot))
        {
            foreach (var sub in AssetDatabase.GetSubFolders(LegacyCharacterRoot))
            {
                string name = System.IO.Path.GetFileName(sub);
                if (!_characters.Contains(name)) _characters.Add(name);
            }
        }

        // 몬스터: Dinos/ 의 PNG 하나당 한 항목 (기본 idle 1프레임)
        if (AssetDatabase.IsValidFolder(MonsterRoot))
        {
            string cwd = System.IO.Directory.GetCurrentDirectory();
            string abs = System.IO.Path.Combine(cwd, MonsterRoot);
            if (System.IO.Directory.Exists(abs))
            {
                foreach (var p in System.IO.Directory.GetFiles(abs, "*.png"))
                {
                    string n = System.IO.Path.GetFileNameWithoutExtension(p);
                    if (!_monsters.Contains(n)) _monsters.Add(n);
                }
            }
        }

        if (_characters.Count == 0 && _monsters.Count == 0)
            _statusMessage = $"'{CharacterRoot}' 또는 '{MonsterRoot}' 폴더가 비어 있음.";
#endif

        foreach (var m in _manualCharacterFolders)
            if (!string.IsNullOrEmpty(m) && !_characters.Contains(m)) _characters.Add(m);

        _characters.Sort();
        _monsters.Sort();

        // 이전 선택 복원 — 같은 이름이 남아 있으면 유지
        if (!string.IsNullOrEmpty(_charSlot.selectedName) && _characters.Contains(_charSlot.selectedName))
            SelectEntry(_charSlot.selectedName, isMonster: false);
        else if (_characters.Count > 0)
            SelectEntry(_characters[0], isMonster: false);

        if (!string.IsNullOrEmpty(_monsterSlot.selectedName) && _monsters.Contains(_monsterSlot.selectedName))
            SelectEntry(_monsterSlot.selectedName, isMonster: true);
    }

    private void ClearSlot(EntitySlot slot)
    {
        slot.selectedName = null;
        slot.clips.Clear();
        slot.clipIdx = -1;
        if (slot.go != null) slot.go.SetActive(false);
    }

    private void SelectEntry(string name, bool isMonster)
    {
        var slot = isMonster ? _monsterSlot : _charSlot;
        // 이 슬롯에 포커스 맞춤 — 클립 리스트/재생 타겟이 바로 이 슬롯을 따라간다.
        _focusIsMonster = isMonster;

        slot.selectedName = name;
        slot.clips.Clear();
        slot.clipIdx = -1;

        if (string.IsNullOrEmpty(name))
        {
            _statusMessage = "선택된 항목이 없음";
            if (slot.go != null) slot.go.SetActive(false);
            return;
        }

        var sprites = isMonster ? LoadMonsterSprites(name) : LoadCharacterSprites(name);
        string kindLabel = isMonster ? "몬스터" : "캐릭터";
        if (sprites == null || sprites.Count == 0)
        {
            _statusMessage = isMonster
                ? $"몬스터 '{name}' 스프라이트를 로드하지 못했음. Monsters/{name}.png 확인."
                : $"캐릭터 '{name}' 폴더에 Sprite가 없음. PNG가 Texture Type=Sprite로 임포트됐는지 확인.";
            if (slot.go != null) slot.go.SetActive(false);
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
            slot.clips.Add(clip);
        }
        slot.clips.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        if (slot.clips.Count == 0)
        {
            _statusMessage = $"'{name}' 폴더의 파일명이 규칙({{이름}}_f01.png)에 맞는지 확인.";
            return;
        }

        SelectClip(0);
        _statusMessage = $"{kindLabel} '{name}' 로드 완료 — 애니 {slot.clips.Count}개";
    }

    // 포커스 슬롯의 클립만 바꿈. 다른 슬롯은 기존 상태 유지.
    private void SelectClip(int idx)
    {
        var slot = Focus;
        slot.clipIdx = idx;
        ApplySelectedClipToSlot(slot);
    }

    // 스프라이트 로딩 — Editor에서는 파일 시스템 + AssetDatabase로 직접 읽어
    // Resources 인덱스 갱신 이슈 회피. 빌드에서는 Resources.LoadAll 로 폴백.
    // 캐릭터는 Character_infield/{이름}/ (현재 전투에서 쓰이는 최신 폴더)에서 먼저 찾고,
    // 비어 있으면 레거시 AnimationTest/{이름}/ 로 폴백한다.
    private List<Sprite> LoadCharacterSprites(string ch)
    {
        var result = new List<Sprite>();
#if UNITY_EDITOR
        string[] folderCandidates =
        {
            $"Assets/Resources/Character_infield/{ch}",
            $"Assets/Resources/AnimationTest/{ch}",
        };

        foreach (var folder in folderCandidates)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            int pngCount = 0;
            string cwd = System.IO.Directory.GetCurrentDirectory();
            var absFolder = System.IO.Path.Combine(cwd, folder);
            if (!System.IO.Directory.Exists(absFolder)) continue;

            foreach (var absPath in System.IO.Directory.GetFiles(absFolder, "*.png"))
            {
                pngCount++;
                string rel = absPath.Substring(cwd.Length + 1).Replace('\\', '/');

                var mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(rel);
                if (mainSprite != null && !result.Contains(mainSprite)) result.Add(mainSprite);

                foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(rel))
                {
                    if (rep is Sprite sp && !result.Contains(sp)) result.Add(sp);
                }

                if (mainSprite == null &&
                    AssetDatabase.LoadAllAssetRepresentationsAtPath(rel).Length == 0)
                {
                    var mainObj = AssetDatabase.LoadMainAssetAtPath(rel);
                    Debug.LogWarning(
                        $"[AnimTest] {rel}: no Sprite. mainAssetType={mainObj?.GetType().Name ?? "null"}. " +
                        $"PNG가 Texture Type=Sprite로 임포트됐는지 확인 (Inspector → Texture Type).");
                }
            }
            Debug.Log($"[AnimTest] char '{ch}' via {folder}: pngs={pngCount} sprites={result.Count}");
            if (result.Count > 0) return result;
        }
#endif
        foreach (var sub in new[] { "Character_infield", "AnimationTest" })
        {
            var viaResources = Resources.LoadAll<Sprite>($"{sub}/{ch}");
            if (viaResources != null && viaResources.Length > 0)
            {
                result.AddRange(viaResources);
                Debug.Log($"[AnimTest] char '{ch}' via Resources/{sub}: {result.Count} sprites");
                return result;
            }
        }
        return result;
    }

    // 몬스터는 Monsters/{이름}.png 단일 파일. Multiple 임포트면 sub-sprite가 {이름}_0 로 잡혀
    // 기존 프레임 파서(loose)가 그대로 먹는다. Sprite 로드 실패 시 Texture2D → Sprite 변환으로 폴백.
    private List<Sprite> LoadMonsterSprites(string monsterName)
    {
        var result = new List<Sprite>();
#if UNITY_EDITOR
        string rel = $"Assets/Resources/Monsters/{monsterName}.png";
        string abs = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), rel);
        if (System.IO.File.Exists(abs))
        {
            // Single 모드면 mainSprite가 잡히고, Multiple 모드면 Representations 에서 sub-sprite가 잡힌다.
            var mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(rel);
            if (mainSprite != null) result.Add(mainSprite);
            foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(rel))
                if (rep is Sprite sp && !result.Contains(sp)) result.Add(sp);

            // 둘 다 실패하면 Texture2D 로 읽어서 직접 Sprite 생성.
            if (result.Count == 0)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                if (tex != null) result.Add(MakeSpriteFromTex(tex, $"{monsterName}_f01"));
            }
        }
        if (result.Count > 0) return result;
#endif
        var resSprites = Resources.LoadAll<Sprite>($"Monsters/{monsterName}");
        if (resSprites != null && resSprites.Length > 0) { result.AddRange(resSprites); return result; }

        var resTex = Resources.Load<Texture2D>($"Monsters/{monsterName}");
        if (resTex != null) result.Add(MakeSpriteFromTex(resTex, $"{monsterName}_f01"));
        return result;
    }

    private static Sprite MakeSpriteFromTex(Texture2D tex, string name)
    {
        var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f);
        sp.name = name; // frame 파서가 "xxx_f01" / "xxx_0" 이름을 요구하므로
        return sp;
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
