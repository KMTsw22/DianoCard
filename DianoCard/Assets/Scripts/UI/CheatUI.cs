using System.Collections.Generic;
using System.IO;
using System.Linq;
using DianoCard.Battle;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(2000)]
public class CheatUI : MonoBehaviour
{
    // 외부 파일 BG 프리뷰 — 디스크 어디서든 PNG/JPG를 골라 풀스크린으로 깔고,
    // 기존 상단 HUD 네비바는 OnGUI라 그대로 위에 올라옴. 전투 상태와 무관하게 동작.
    private SpriteRenderer _previewBgSr;
    private Texture2D _previewBgTex;
    private string _previewBgPath;
    private bool _previewBgVisible;
    // 격리 프리뷰 모드 — ON이면 OnGUI 풀스크린으로 BG + 상단 네비바만 그려서 다른 게임 UI를 가림.
    private bool _previewIsolateMode;
    private BattleUI.HudContext _previewHudCtx = BattleUI.HudContext.Battle;
    private RunState _previewDummyRun;
    private bool _open;
    private Rect _windowRect = new(20f, 20f, 260f, 720f);
    private Vector2 _windowScroll;
    private GUIStyle _btnStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _stateStyle;

    // 배경 리스트 캐시 — 전투 진입 시 1회만 Resources.LoadAll 수행
    private string[] _bgNames;
    private Vector2 _bgScroll;

    // 슬롯 강제 소환용 캐시 — SUMMON 카드 전체 목록.
    private List<CardData> _summonCards;
    private Vector2 _summonScroll;

    // 카드 프리뷰 (프레임 디자인 확인용)
    private bool _cardPreviewOpen;
    private int _cardPreviewIndex;
    private List<CardData> _cardPreviewList;
    private GUIStyle _previewLabelStyle;
    private float _cardPreviewHeight = 540f;   // 카드 세로 픽셀 (1280x720 가상 좌표). 슬라이더로 300~560 확대.
    private bool  _cardPreviewSlotOnly;        // true = 프레임 레이어만, 카드 데이터 숨김

    // 인텐트 프리뷰 — 첫 살아있는 적의 intentAction을 EnemyAction 순환으로 강제 변경.
    private int _intentPreviewIdx = 1; // 0=UNKNOWN이라 ATTACK부터 시작

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        // 백쿼트(`) — 한글 IME에선 가로채일 수 있어 F12도 동일하게 토글로 작동.
        bool toggle = kb.backquoteKey.wasPressedThisFrame || kb.f12Key.wasPressedThisFrame;
        if (toggle)
        {
            _open = !_open;
            _bgNames = null;
            _summonCards = null;
        }
    }

    void OnGUI()
    {
        // 카드 프리뷰 / 격리 BG 프리뷰는 패널이 닫혀 있어도 동작해야 함.
        if (!_open && !_cardPreviewOpen && !_previewIsolateMode) return;

        var matrix = GUI.matrix;
        float scale = Screen.width / 1280f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        // 격리 BG 프리뷰 — 다른 게임 UI를 풀스크린으로 가린다. depth는 -150 (게임 UI=0보다 앞, 패널=-200보다 뒤).
        if (_previewIsolateMode && _previewBgTex != null)
        {
            GUI.depth = -150;
            DrawIsolatedPreview();
        }

        // 프리뷰는 BattleUI보다 위 (depth 낮을수록 앞).
        if (_cardPreviewOpen)
        {
            EnsureCheatStyles();          // DrawWindow 호출 안 거쳐도 스타일 확보
            GUI.depth = -100;
            DrawCardPreviewOverlay();
        }

        if (_open)
        {
            // BattleUI 위에 확실히 그리도록 depth를 음수로. 더 작을수록 앞.
            GUI.depth = -200;
            _windowRect = GUI.Window(9999, _windowRect, DrawWindow, "");
        }

        GUI.matrix = matrix;
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
    private RunState ResolvePreviewRun()
    {
        var gsm = GameStateManager.Instance;
        if (gsm != null && gsm.CurrentRun != null) return gsm.CurrentRun;

        if (_previewDummyRun == null)
        {
            _previewDummyRun = new RunState
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

    // 스타일 생성을 DrawWindow 밖으로 뺀다 — DrawCardPreviewOverlay 도 _btnStyle 쓰기 때문.
    // GUI.skin 은 OnGUI 안에서만 안전하게 접근 가능하므로, 스타일 초기화도 OnGUI 경로에서만 호출.
    private void EnsureCheatStyles()
    {
        if (_btnStyle != null) return;
        _btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            fixedHeight = 30f,
        };
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.85f, 0.4f) },
        };
        _stateStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.7f, 0.9f, 1f) },
        };
    }

    // 인텐트 프리뷰 — 액션을 적용하고 intentType과 기본 값을 적절히 매핑.
    private static void ApplyIntentPreview(EnemyInstance e, EnemyAction action)
    {
        e.intentAction = action;
        switch (action)
        {
            case EnemyAction.ATTACK:
            case EnemyAction.MULTI_ATTACK:
            case EnemyAction.COUNTDOWN_ATTACK:
                e.intentType = EnemyIntentType.ATTACK;
                if (e.intentValue <= 0) e.intentValue = 5;
                break;
            case EnemyAction.DEFEND:
            case EnemyAction.BLOCK_BOSS:
            case EnemyAction.ARMOR_UP:
                e.intentType = EnemyIntentType.DEFEND;
                if (e.intentValue <= 0) e.intentValue = 5;
                break;
            case EnemyAction.BUFF_SELF:
            case EnemyAction.EMPOWER_BOSS:
                e.intentType = EnemyIntentType.BUFF;
                if (e.intentValue <= 0) e.intentValue = 2;
                break;
            case EnemyAction.SUMMON:
            case EnemyAction.REFILL_MOSS:
                e.intentType = EnemyIntentType.SUMMON;
                e.intentValue = 0;
                break;
            case EnemyAction.STEAL_SUMMON:
            case EnemyAction.SILENCE:
            case EnemyAction.IDLE:
            case EnemyAction.UNKNOWN:
                e.intentType = EnemyIntentType.UNKNOWN;
                e.intentValue = 0;
                break;
            case EnemyAction.COUNTDOWN_AOE:
                e.intentType = EnemyIntentType.COUNTDOWN;
                if (e.intentValue <= 0) e.intentValue = 3;
                break;
            default: // POISON / WEAK / DRAIN / VULNERABLE / CLOG_DECK / HEAL_BOSS
                e.intentType = EnemyIntentType.DEBUFF;
                if (e.intentValue <= 0) e.intentValue = 3;
                break;
        }
    }

    private void DrawWindow(int id)
    {
        EnsureCheatStyles();

        var gsm = GameStateManager.Instance;
        string state = gsm != null ? gsm.State.ToString() : "N/A";

        GUILayout.Space(4f);
        GUILayout.Label("CHEAT PANEL", _titleStyle);
        GUILayout.Label($"Current: {state}", _stateStyle);
        GUILayout.Space(8f);

        if (gsm == null)
        {
            GUILayout.Label("GameStateManager not found");
            GUI.DragWindow();
            return;
        }

        // 창 본체는 헤더(약 60px) 아래로 스크롤. 항목 늘어나도 화면 밖으로 잘리지 않도록.
        _windowScroll = GUILayout.BeginScrollView(_windowScroll, GUILayout.Height(_windowRect.height - 70f));

        if (GUILayout.Button("Lobby", _btnStyle))
            gsm.ReturnToLobby();

        if (GUILayout.Button("Character Select", _btnStyle))
            gsm.StartNewRun();

        if (GUILayout.Button("Reward", _btnStyle))
            gsm.Cheat_TriggerReward();

        if (GUILayout.Button("Shop", _btnStyle))
            gsm.Cheat_EnterShop();

        if (GUILayout.Button("Village", _btnStyle))
            gsm.Cheat_EnterVillage();

        // 미지 노드 — 진입 시 StS 방식으로 무작위 해석되는 ? 노드.
        // 랜덤 / 강제 결과 4종 (Combat/Treasure/Shop/Rest)을 즉석에서 검증.
        GUILayout.Label("미지 (Unknown)", _stateStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("? 랜덤", _btnStyle))
            gsm.Cheat_TriggerUnknown(null);
        if (GUILayout.Button("→ 전투", _btnStyle))
            gsm.Cheat_TriggerUnknown(GameStateManager.UnknownOutcome.Combat);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("→ 보물", _btnStyle))
            gsm.Cheat_TriggerUnknown(GameStateManager.UnknownOutcome.Treasure);
        if (GUILayout.Button("→ 상점", _btnStyle))
            gsm.Cheat_TriggerUnknown(GameStateManager.UnknownOutcome.Shop);
        if (GUILayout.Button("→ 휴식", _btnStyle))
            gsm.Cheat_TriggerUnknown(GameStateManager.UnknownOutcome.Rest);
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        GUILayout.Label("— 훈련장 입장 (BG 자동) —", _stateStyle);

        // 일반 적 — 첫 적이 NORMAL이면 랜덤 Battle BG 자동 로드
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("E001 슬라임", _btnStyle)) gsm.Cheat_StartBattleWith("E001");
        if (GUILayout.Button("E008 정령",   _btnStyle)) gsm.Cheat_StartBattleWith("E008");
        GUILayout.EndHorizontal();

        // 엘리트 — 첫 적이 ELITE이면 Elite BG 자동 로드
        GUILayout.Label("엘리트", _stateStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("E101 골렘", _btnStyle)) gsm.Cheat_StartBattleWith("E101");
        if (GUILayout.Button("E102 사제", _btnStyle)) gsm.Cheat_StartBattleWith("E102");
        GUILayout.EndHorizontal();
        if (GUILayout.Button("E103 쌍둥이 (2체)", _btnStyle))
            gsm.Cheat_StartBattleWith("E103", "E103");

        // 보스 — Boss BG 자동 로드
        GUILayout.Label("보스", _stateStyle);
        if (GUILayout.Button("E901 폐허의 군주", _btnStyle))
            gsm.Cheat_StartBossBattle();

        // ===== 전투 중에만 보이는 제어 =====
        var battle = GetActiveBattle();
        if (gsm.State == GameState.Battle && battle != null && battle.state != null)
        {
            GUILayout.Space(10f);
            GUILayout.Label("— 전투 중 제어 —", _stateStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체 즉사", _btnStyle)) battle.Cheat_KillAllEnemies();
            if (GUILayout.Button("쫄류만 즉사", _btnStyle)) battle.Cheat_ClearAddsOnly();
            GUILayout.EndHorizontal();

            GUILayout.Label("보스 HP 설정", _stateStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("70%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.70f);
            if (GUILayout.Button("50%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.50f);
            if (GUILayout.Button("30%", _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.30f);
            if (GUILayout.Button("5%",  _btnStyle)) battle.Cheat_SetPrimaryHpRatio(0.05f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("풀 회복", _btnStyle)) battle.Cheat_FullHeal();
            string invLabel = battle.state.player.cheatInvincible ? "무적 OFF" : "무적 ON";
            if (GUILayout.Button(invLabel, _btnStyle)) battle.Cheat_ToggleInvincible();
            GUILayout.EndHorizontal();

            // 공격 모션 미리보기 — 카드 소비/데미지 없이 시전+화염구만 재생.
            if (GUILayout.Button("공격 모션 재생", _btnStyle))
            {
                var ui = Object.FindFirstObjectByType<BattleUI>();
                if (ui != null) ui.Cheat_PlayPlayerAttack();
            }

            // ===== 인텐트 프리뷰 — 첫 살아있는 적의 인텐트를 EnemyAction 전체에서 순환 =====
            GUILayout.Space(8f);
            GUILayout.Label("— 인텐트 프리뷰 —", _stateStyle);
            var aliveEnemy = battle.state.enemies.FirstOrDefault(en => !en.IsDead);
            if (aliveEnemy != null)
            {
                var actions = (EnemyAction[])System.Enum.GetValues(typeof(EnemyAction));
                if (_intentPreviewIdx < 0 || _intentPreviewIdx >= actions.Length) _intentPreviewIdx = 0;
                GUILayout.Label($"{actions[_intentPreviewIdx]}  (값 {aliveEnemy.intentValue})", _stateStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀ 이전", _btnStyle))
                {
                    _intentPreviewIdx = (_intentPreviewIdx - 1 + actions.Length) % actions.Length;
                    ApplyIntentPreview(aliveEnemy, actions[_intentPreviewIdx]);
                }
                if (GUILayout.Button("다음 ▶", _btnStyle))
                {
                    _intentPreviewIdx = (_intentPreviewIdx + 1) % actions.Length;
                    ApplyIntentPreview(aliveEnemy, actions[_intentPreviewIdx]);
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("값 -1", _btnStyle))
                    aliveEnemy.intentValue = Mathf.Max(0, aliveEnemy.intentValue - 1);
                if (GUILayout.Button("값 +1", _btnStyle))
                    aliveEnemy.intentValue = aliveEnemy.intentValue + 1;
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("(살아있는 적 없음)", _stateStyle);
            }

            // ===== 배경 전환 =====
            GUILayout.Space(8f);
            GUILayout.Label("— 배경 전환 —", _stateStyle);
            if (_bgNames == null)
            {
                var all = Resources.LoadAll<Texture2D>("Backgrounds");
                var list = new System.Collections.Generic.List<string>();
                foreach (var t in all)
                {
                    if (t != null) list.Add(t.name);
                }
                list.Sort();
                _bgNames = list.ToArray();
            }

            var battleUi = Object.FindFirstObjectByType<BattleUI>();
            _bgScroll = GUILayout.BeginScrollView(_bgScroll, GUILayout.Height(180f));
            foreach (var name in _bgNames)
            {
                if (GUILayout.Button(name, _btnStyle) && battleUi != null)
                    battleUi.Cheat_SetBackground($"Backgrounds/{name}");
            }
            GUILayout.EndScrollView();

            // ===== 공룡 슬롯 강제 지정 =====
            GUILayout.Space(8f);
            GUILayout.Label("— 공룡 슬롯 강제 지정 —", _stateStyle);

            // 현재 슬롯 상태 표시 + 비우기 버튼.
            string slot1 = battle.state.field.Count > 0 ? battle.state.field[0].data.nameKr : "(비어있음)";
            string slot2 = battle.state.field.Count > 1 ? battle.state.field[1].data.nameKr : "(비어있음)";
            GUILayout.BeginHorizontal();
            GUILayout.Label($"1번: {slot1}", _stateStyle);
            if (GUILayout.Button("X", _btnStyle, GUILayout.Width(30f))) battle.Cheat_ClearFieldSlot(0);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"2번: {slot2}", _stateStyle);
            if (GUILayout.Button("X", _btnStyle, GUILayout.Width(30f))) battle.Cheat_ClearFieldSlot(1);
            GUILayout.EndHorizontal();

            // SUMMON 카드 전체 캐시 — 카드 데이터는 전투 중 바뀌지 않으므로 한 번만 로드.
            if (_summonCards == null)
            {
                if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();
                _summonCards = DataManager.Instance.Cards.Values
                    .Where(c => c.cardType == CardType.SUMMON)
                    .OrderBy(c => c.subType)
                    .ThenBy(c => c.id)
                    .ToList();
            }

            _summonScroll = GUILayout.BeginScrollView(_summonScroll, GUILayout.Height(220f));
            foreach (var c in _summonCards)
            {
                GUILayout.BeginHorizontal();
                string label = $"{c.nameKr}";
                GUILayout.Label(label, GUILayout.Width(140f));
                if (GUILayout.Button("→1", _btnStyle, GUILayout.Width(40f))) battle.Cheat_SetFieldSlot(0, c.id);
                if (GUILayout.Button("→2", _btnStyle, GUILayout.Width(40f))) battle.Cheat_SetFieldSlot(1, c.id);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            // ===== 페어 자동 패킹 슬라이더 — 2마리 배치일 때 가로 겹침·세로 스태거 라이브 튜닝.
            if (battleUi != null)
            {
                GUILayout.Space(8f);
                GUILayout.Label("— 페어 패킹 (2마리 배치) —", _stateStyle);

                GUILayout.Label($"가로 겹침: {battleUi.PairOverlapPct:F2}");
                battleUi.PairOverlapPct = GUILayout.HorizontalSlider(battleUi.PairOverlapPct, 0f, 0.7f);

                GUILayout.Label($"세로 스태거: {battleUi.PairStaggerYPct:F2}");
                battleUi.PairStaggerYPct = GUILayout.HorizontalSlider(battleUi.PairStaggerYPct, 0f, 0.5f);

                GUILayout.Label($"가로 최소 간격: {battleUi.PairMinSpacingPct:F2}");
                battleUi.PairMinSpacingPct = GUILayout.HorizontalSlider(battleUi.PairMinSpacingPct, 0f, 0.6f);

                GUILayout.Label($"사이즈차 Y 부스트: {battleUi.PairSizeStaggerBoost:F2}");
                battleUi.PairSizeStaggerBoost = GUILayout.HorizontalSlider(battleUi.PairSizeStaggerBoost, 0f, 1.5f);
            }

            // ===== 보스 부메랑 튜닝 — 색/투명도/크기/Y비율 라이브 조정 + 테스트 발사.
            if (battleUi != null)
            {
                GUILayout.Space(8f);
                GUILayout.Label("— 보스 부메랑 튜닝 —", _stateStyle);

                var c = BossProjectile.TintColor;
                GUILayout.Label($"R: {c.r:F2}");
                c.r = GUILayout.HorizontalSlider(c.r, 0f, 1f);
                GUILayout.Label($"G: {c.g:F2}");
                c.g = GUILayout.HorizontalSlider(c.g, 0f, 1f);
                GUILayout.Label($"B: {c.b:F2}");
                c.b = GUILayout.HorizontalSlider(c.b, 0f, 1f);
                GUILayout.Label($"투명도(A): {c.a:F2}");
                c.a = GUILayout.HorizontalSlider(c.a, 0f, 1f);
                BossProjectile.TintColor = c;

                // 색 프리셋 — 자주 쓸 만한 톤 빠르게 적용.
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("잉크차콜", _btnStyle))
                    BossProjectile.TintColor = new Color(0.085f, 0.062f, 0.110f, 1f);
                if (GUILayout.Button("순흑", _btnStyle))
                    BossProjectile.TintColor = new Color(0f, 0f, 0f, 1f);
                if (GUILayout.Button("보라", _btnStyle))
                    BossProjectile.TintColor = new Color(0.30f, 0.10f, 0.45f, 1f);
                GUILayout.EndHorizontal();

                GUILayout.Label($"전체 크기 배율: {BossProjectile.SizeMultiplier:F2}");
                BossProjectile.SizeMultiplier = GUILayout.HorizontalSlider(BossProjectile.SizeMultiplier, 0.3f, 3f);

                GUILayout.Label($"Y 비율(양 뿔 길이): {BossProjectile.YScaleMultiplier:F2}");
                BossProjectile.YScaleMultiplier = GUILayout.HorizontalSlider(BossProjectile.YScaleMultiplier, 0.5f, 3f);

                // 그라데이션 — 텍스처 자체에 적용. 값 바뀌면 다음 발사에서 재생성.
                GUILayout.Label($"양 뿔 페이드: {BossProjectile.TipFadePower:F2}  (↑일수록 끝이 빨리 사라짐)");
                BossProjectile.TipFadePower = GUILayout.HorizontalSlider(BossProjectile.TipFadePower, 0.5f, 4f);
                GUILayout.Label($"가장자리 노이즈: {BossProjectile.NoiseStrength:F2}  (천 찢김 거침)");
                BossProjectile.NoiseStrength = GUILayout.HorizontalSlider(BossProjectile.NoiseStrength, 0f, 0.6f);

                // 휘날림 — 매 프레임 sin 흔들림 + 잔상 사본.
                GUILayout.Label($"휘날림 강도: {BossProjectile.WobbleIntensity:F2}");
                BossProjectile.WobbleIntensity = GUILayout.HorizontalSlider(BossProjectile.WobbleIntensity, 0f, 1.5f);
                GUILayout.Label($"잔상 개수: {BossProjectile.AfterimageCount}");
                BossProjectile.AfterimageCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(BossProjectile.AfterimageCount, 0f, 5f));
                if (BossProjectile.AfterimageCount > 0)
                {
                    GUILayout.Label($"잔상 간격: {BossProjectile.AfterimageSpacing:F2}");
                    BossProjectile.AfterimageSpacing = GUILayout.HorizontalSlider(BossProjectile.AfterimageSpacing, 0f, 0.4f);
                    GUILayout.Label($"잔상 알파: {BossProjectile.AfterimageAlpha:F2}");
                    BossProjectile.AfterimageAlpha = GUILayout.HorizontalSlider(BossProjectile.AfterimageAlpha, 0f, 1f);
                }

                GUILayout.BeginHorizontal();
                string trailLabel = BossProjectile.TrailEnabled ? "트레일 ON" : "트레일 OFF";
                if (GUILayout.Button(trailLabel, _btnStyle))
                    BossProjectile.TrailEnabled = !BossProjectile.TrailEnabled;
                if (GUILayout.Button("테스트 발사", _btnStyle))
                    battleUi.Cheat_FireBossCrescent();
                GUILayout.EndHorizontal();

                if (BossProjectile.TrailEnabled)
                {
                    GUILayout.Label($"트레일 두께: {BossProjectile.TrailWidthRatio:F2}");
                    BossProjectile.TrailWidthRatio = GUILayout.HorizontalSlider(BossProjectile.TrailWidthRatio, 0f, 1.5f);
                    GUILayout.Label($"트레일 시간: {BossProjectile.TrailTime:F2}");
                    BossProjectile.TrailTime = GUILayout.HorizontalSlider(BossProjectile.TrailTime, 0f, 1f);
                }
            }
        }

        GUILayout.Space(12f);
        GUILayout.Label("— Gold —", _stateStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+100G", _btnStyle) && gsm.CurrentRun != null)
            gsm.CurrentRun.gold += 100;
        if (GUILayout.Button("+500G", _btnStyle) && gsm.CurrentRun != null)
            gsm.CurrentRun.gold += 500;
        GUILayout.EndHorizontal();

        // ===== 외부 파일 BG 프리뷰 — 상단 네비 유지한 채 임의 사진 풀스크린 깔기.
        GUILayout.Space(12f);
        GUILayout.Label("— BG 프리뷰 (외부 파일) —", _stateStyle);
        if (!string.IsNullOrEmpty(_previewBgPath))
        {
            GUILayout.Label(Path.GetFileName(_previewBgPath), _stateStyle);
        }
        if (GUILayout.Button("파일에서 로드…", _btnStyle))
            PickAndLoadPreviewBg();

        if (_previewBgTex != null)
        {
            GUILayout.BeginHorizontal();
            string visLabel = _previewBgVisible ? "숨기기" : "보이기";
            if (GUILayout.Button(visLabel, _btnStyle)) TogglePreviewBgVisible();
            if (GUILayout.Button("끄기", _btnStyle)) ClearPreviewBg();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("다시 맞추기 (카메라 변경시)", _btnStyle))
                FitPreviewBgToCamera();

            // 격리 모드 — BG + 상단 네비바만 보이게 (다른 UI 가림).
            GUILayout.Space(4f);
            string isoLabel = _previewIsolateMode ? "● 격리 모드 ON (BG+네비만)" : "○ 격리 모드 OFF";
            if (GUILayout.Button(isoLabel, _btnStyle))
                _previewIsolateMode = !_previewIsolateMode;

            if (_previewIsolateMode)
            {
                GUILayout.Label("네비바 컨텍스트 (색)", _stateStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_previewHudCtx == BattleUI.HudContext.Battle ? "▶전투" : "전투", _btnStyle))
                    _previewHudCtx = BattleUI.HudContext.Battle;
                if (GUILayout.Button(_previewHudCtx == BattleUI.HudContext.Map ? "▶맵" : "맵", _btnStyle))
                    _previewHudCtx = BattleUI.HudContext.Map;
                if (GUILayout.Button(_previewHudCtx == BattleUI.HudContext.Village ? "▶마을" : "마을", _btnStyle))
                    _previewHudCtx = BattleUI.HudContext.Village;
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(12f);
        GUILayout.Label("— 카드 프리뷰 —", _stateStyle);
        string previewLabel = _cardPreviewOpen ? "프리뷰 닫기" : "카드 프리뷰 열기";
        if (GUILayout.Button(previewLabel, _btnStyle))
        {
            _cardPreviewOpen = !_cardPreviewOpen;
            if (_cardPreviewOpen) EnsureCardPreviewList();
        }
        // 슬롯 프리뷰 — 카드 데이터 없이 프레임 레이어만 큰 화면으로. Inspector rect 튜닝용.
        string slotLabel = _cardPreviewOpen && _cardPreviewSlotOnly ? "슬롯 모드 ON" : "카드 슬롯 프리뷰 (빈 프레임)";
        if (GUILayout.Button(slotLabel, _btnStyle))
        {
            _cardPreviewSlotOnly = !_cardPreviewSlotOnly;
            if (!_cardPreviewOpen)
            {
                _cardPreviewOpen = true;
                EnsureCardPreviewList();
            }
        }

        GUILayout.EndScrollView();

        GUI.DragWindow();
    }

    // Editor CheatWindow에서 호출하는 공개 API — 플레이 모드에서 오버레이 토글.
    public bool IsCardPreviewOpen => _cardPreviewOpen;

    public void OpenCardPreview(bool slotOnly)
    {
        _cardPreviewOpen = true;
        _cardPreviewSlotOnly = slotOnly;
        EnsureCardPreviewList();
    }

    public void CloseCardPreview() => _cardPreviewOpen = false;

    // 모든 카드 한 번 캐시 (id 정렬).
    private void EnsureCardPreviewList()
    {
        if (_cardPreviewList != null) return;
        if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();
        _cardPreviewList = DataManager.Instance.Cards.Values
            .OrderBy(c => c.id)
            .ToList();
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
            // 빈 슬롯 프리뷰 — 카드 데이터 없음. BattleUI Inspector에서 rect 조정하며 실시간 확인.
            ui.DrawCardPreview(cardRect, null, slotOnly: true);

            // 정보 라벨은 화면 최상단 고정 — 카드가 커져도 안 가려짐.
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

        // 컨트롤 — 캔버스 하단 고정 바. 카드 크기와 무관하게 항상 같은 자리.
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

        // 슬롯/카드 모드 토글 — 하단 우측.
        string modeLabel = _cardPreviewSlotOnly ? "▶ 카드 모드로" : "▶ 슬롯 모드로";
        if (GUI.Button(new Rect(680f, bottomRow, 180f, 36f), modeLabel, _btnStyle))
            _cardPreviewSlotOnly = !_cardPreviewSlotOnly;

        // 닫기 — 가장 하단 중앙.
        if (GUI.Button(new Rect(580f, bottomRow2, 120f, 36f), "닫기", _btnStyle))
            _cardPreviewOpen = false;
    }

    /// <summary>현재 씬에 떠있는 BattleUI에서 BattleManager 인스턴스를 획득.</summary>
    private DianoCard.Battle.BattleManager GetActiveBattle()
    {
        var ui = Object.FindFirstObjectByType<BattleUI>();
        return ui != null ? ui.Battle : null;
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
        // 에디터: 표준 파일 다이얼로그 — _cheat_bg 폴더로 기본 진입.
        path = EditorUtility.OpenFilePanelWithFilters(
            "BG 프리뷰 이미지 선택", CheatBgFolder(),
            new[] { "Image", "png,jpg,jpeg" });
#else
        // 빌드: _cheat_bg 폴더(없으면 persistentDataPath/cheat_bg) 안의 첫 이미지 자동 선택.
        var dir = CheatBgFolder();
        if (!Directory.Exists(dir))
            dir = Path.Combine(Application.persistentDataPath, "cheat_bg");
        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg"))
                .ToArray();
            if (files.Length > 0) path = files[0];
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

            // 기존 프리뷰 텍스처 정리.
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
            // 전투 BG(-100)/기타 월드 스프라이트보다 위, OnGUI(HUD)는 어차피 더 위에.
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

    private void TogglePreviewBgVisible()
    {
        _previewBgVisible = !_previewBgVisible;
        if (_previewBgSr != null) _previewBgSr.enabled = _previewBgVisible;
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
