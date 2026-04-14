using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DianoCard.Battle;
using DianoCard.Data;
using DianoCard.Game;
using UnityEngine;

/// <summary>
/// 전투 화면 IMGUI 프로토타입.
/// GameStateManager가 있을 때만 동작하며, State == Battle일 때만 그려짐.
///
/// 진입: GameStateManager.StartNewRun() 또는 ProceedAfterReward()가
///       State를 Battle로 바꾸면, 이 컴포넌트가 CurrentRun을 바탕으로
///       BattleManager를 초기화함.
///
/// 종료: _battle.state.IsOver가 감지되면 1.5초 대기 후
///       GameStateManager.EndBattle(won, hp)로 결과 전달 → 상태 전환.
/// </summary>
public class BattleUI : MonoBehaviour
{
    // 가상 해상도 — 실제 화면 크기에 맞춰 스케일링됨
    private const float RefW = 1280f;
    private const float RefH = 720f;

    private BattleManager _battle;
    private bool _battleInitialized;
    private bool _battleEndQueued;
    private float _battleEndDelay;

    // 타겟팅 모드: 공격 카드 클릭 후 적 클릭 대기 중 (-1 = 비활성)
    private int _targetingCardIndex = -1;

    // EndTurn 애니메이션: 소환수→적 순차 lunge 모션
    private bool _endTurnAnimating;
    private object _attackingUnit;       // 현재 lunge 중인 SummonInstance 또는 EnemyInstance
    private float _attackProgress;       // 0..1
    private const float LungePixels = 70f;
    private const float LungeDuration = 0.70f;
    private const float BetweenAttacksPause = 0.30f;

    // OnGUI에서 state를 즉시 변경하면 Layout/Repaint 이벤트 간 불일치로
    // ArgumentException이 뜨므로, 버튼 클릭 시에는 액션을 지연시켜 Update에서 실행.
    private readonly List<Action> _pending = new();

    // 배경 텍스처 (적 타입에 따라 자동 선택)
    private Texture2D _backgroundTexture;

    // 배경을 world-space로 렌더링해서 파티클이 배경 위에 나오게 한다.
    // (IMGUI는 world 렌더링 뒤에 그려지므로, OnGUI로 배경을 그리면 파티클이 가려짐)
    private SpriteRenderer _worldBgSr;

    // 카드 프레임 텍스처 (손패 공통)
    private Texture2D _cardFrameTexture;
    private Texture2D _cardBgTexture;
    private Texture2D _cardBorderTexture;
    private Texture2D _manaFrameTexture;

    // 상단 HUD 아이콘
    private Texture2D _iconHP;
    private Texture2D _iconGold;
    private Texture2D _iconMana;
    private Texture2D _iconPotion;
    private Texture2D _iconRelic;
    private Texture2D _iconDeck;
    private Texture2D _iconDiscard;
    private Texture2D _iconFloor;
    private Texture2D _iconTurn;
    private Texture2D _iconShield;
    private Texture2D _topBarBg;
    private Texture2D _endTurnButtonTex;
    private float _endTurnHoverScale = 1f;

    // 카드 위에 표시되는 일러스트 (카드 id → 텍스처). 카테고리별 CardArt/{Spell|Summon|Utility}/.
    private readonly Dictionary<string, Texture2D> _cardSprites = new();
    // 필드 위에 그려지는 공룡 스프라이트 (투명 배경). Dinos/ 폴더.
    private readonly Dictionary<string, Texture2D> _fieldDinoSprites = new();

    // 적 스프라이트 (적 id → 텍스처). Start()에서 한 번만 로드.
    private readonly Dictionary<string, Texture2D> _enemySprites = new();

    // 플레이어 캐릭터 스프라이트 (필드 위에 서있는 모습)
    private Texture2D _playerSprite;
    // 애니메이션용 world-space 뷰 (Phase 1)
    private BattleEntityView _playerView;
    private Sprite _playerWorldSprite;

    // 적 애니메이션 뷰 (적 id → world Sprite, EnemyInstance → view)
    private readonly Dictionary<string, Sprite> _enemyWorldSprites = new();
    private readonly Dictionary<EnemyInstance, BattleEntityView> _enemyViews = new();

    // 데미지 시 스폰되는 VFX 프리팹 (Inspector에서 할당)
    // 기본값으로 Resources 또는 AssetDatabase로는 못 불러오므로 SerializeField로 노출.
    [Header("Damage VFX Prefabs")]
    [SerializeField] private GameObject _vfxHitA;
    [SerializeField] private GameObject _vfxHitD;
    [SerializeField] private GameObject _vfxSmokeF;
    [SerializeField] private float _vfxZDistance = 10f;

    // 전투 배경 앰비언스 VFX (전투 시작 시 스폰, 종료 시 파괴)
    // 각 엔트리는 특정 배경(backgroundName)에만 스폰된다.
    // backgroundName이 비어있으면 모든 배경에 스폰.
    [Serializable]
    public class BackgroundAmbienceEntry
    {
        public string backgroundName;
        public GameObject prefab;
        public Vector2 guiPos = new Vector2(640f, 360f);
        [Range(0.05f, 2f)] public float scale = 0.25f;
        [Range(0.05f, 2f)] public float intensity = 0.3f;
    }

    [Header("Battle Background Ambience")]
    [SerializeField] private List<BackgroundAmbienceEntry> _bgFxEntries = new();
    private readonly List<GameObject> _spawnedBgFx = new();

    // 배경에 오버레이되는 살랑거리는 덩굴 (SpriteRenderer + VineSway)
    [Serializable]
    public class BackgroundVineEntry
    {
        public string backgroundName;
        public string resourcePath;          // 예: "FX/Vines/Vine1"
        public Vector2 guiPos = new Vector2(640f, 50f);
        public float scale = 1f;
        public int sortingOrder = -50;        // 배경(-100)과 파티클(0) 사이
        [Range(0f, 20f)] public float swayAngle = 2f;
        [Range(0f, 5f)] public float swaySpeed = 0.5f;
        public float swayPhase = 0f;
        public bool flipX = false;
        public Color color = Color.white;

        // true면 VineSway 대신 GodRayFX 를 사용 (알파 펄스 + 회전 흔들림)
        public bool useGodRay = false;
        [Range(0f, 1f)] public float godRayMinAlpha = 0.15f;
        [Range(0f, 1f)] public float godRayMaxAlpha = 0.45f;
        public float godRayPulseSpeed = 0.6f;
    }

    [Header("Battle Background Vines")]
    [SerializeField] private List<BackgroundVineEntry> _bgVineEntries = new();
    private readonly List<GameObject> _spawnedVines = new();

    // HP 변화 감지용 (unit reference → 직전 프레임 hp)
    private readonly Dictionary<object, int> _lastKnownHp = new();
    // HP 바 위치별 '표시 fraction' — 실제 hp가 내려가면 이 값이 천천히 따라내려가며 pale trail을 만든다
    private readonly Dictionary<Vector2, float> _hpBarDisplayedFrac = new();
    private readonly HashSet<object> _seenThisFrame = new();

    // 떠오르는 데미지 플로터
    private readonly List<DamageFloater> _floaters = new();

    // 캐릭터 슬롯 위치 (매 OnGUI 시작 시 갱신 → 플로터가 참조)
    private readonly Dictionary<object, Vector2> _slotPositions = new();

    // 방패(블록) 이펙트 — 플레이어 block이 증가한 프레임에 트리거, 일정 시간 동안 재생
    private int _prevPlayerBlock;
    private float _playerShieldFxStartTime = -1f;
    private const float ShieldFxDuration = 1.2f;

    private GUIStyle _boxStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _centerStyle;
    private GUIStyle _damageStyle;
    private GUIStyle _intentStyle;
    private GUIStyle _targetHintStyle;
    private GUIStyle _cardCostStyle;
    private GUIStyle _cardNameStyle;
    private GUIStyle _cardTypeStyle;
    private GUIStyle _cardDescStyle;
    private bool _stylesReady;

    private class DamageFloater
    {
        public object anchor;
        public int amount;
        public float delay;
        public float age;
        public const float LifeTime = 1.2f;
    }

    // =========================================================
    // Lifecycle
    // =========================================================

    void Start()
    {
        if (!DataManager.Instance.IsLoaded) DataManager.Instance.Load();
        LoadCardSprites();
        LoadEnemySprites();
        _cardFrameTexture = Resources.Load<Texture2D>("CardSlot/CardFrame");
        if (_cardFrameTexture == null)
            Debug.LogWarning("[BattleUI] CardFrame texture not found: Resources/CardSlot/CardFrame");
        _cardBgTexture = Resources.Load<Texture2D>("CardSlot/CardBg");
        if (_cardBgTexture == null)
            Debug.LogWarning("[BattleUI] CardBg texture not found: Resources/CardSlot/CardBg");
        _cardBorderTexture = Resources.Load<Texture2D>("CardSlot/CardBorder");
        if (_cardBorderTexture == null)
            Debug.LogWarning("[BattleUI] CardBorder texture not found: Resources/CardSlot/CardBorder");

        _manaFrameTexture = Resources.Load<Texture2D>("CardSlot/ManaFrame");
        if (_manaFrameTexture == null)
            Debug.LogWarning("[BattleUI] ManaFrame texture not found: Resources/CardSlot/ManaFrame");

        _iconHP     = Resources.Load<Texture2D>("InGame/Icon/HP");
        _iconGold   = Resources.Load<Texture2D>("InGame/Icon/Gold");
        _iconMana   = Resources.Load<Texture2D>("InGame/Icon/Mana");
        _iconPotion = Resources.Load<Texture2D>("InGame/Icon/Potion_Bottle");
        _iconRelic  = Resources.Load<Texture2D>("InGame/Icon/Relic");
        _iconDeck    = Resources.Load<Texture2D>("InGame/Icon/Deck");
        _iconDiscard = Resources.Load<Texture2D>("InGame/Icon/Discard");
        _iconFloor   = Resources.Load<Texture2D>("InGame/Icon/Floor");
        _iconTurn    = Resources.Load<Texture2D>("InGame/Icon/Turn");
        _iconShield  = Resources.Load<Texture2D>("InGame/Icon/Shield");
        _topBarBg   = Resources.Load<Texture2D>("InGame/TopBar");
        _endTurnButtonTex = Resources.Load<Texture2D>("InGame/EndTurnButton");
        if (_endTurnButtonTex == null)
            Debug.LogWarning("[BattleUI] EndTurnButton texture not found: Resources/InGame/EndTurnButton");
        if (_iconHP     == null) Debug.LogWarning("[BattleUI] HP icon not found: Resources/InGame/Icon/HP");
        if (_iconGold   == null) Debug.LogWarning("[BattleUI] Gold icon not found: Resources/InGame/Icon/Gold");
        if (_iconMana   == null) Debug.LogWarning("[BattleUI] Mana icon not found: Resources/InGame/Icon/Mana");
        if (_iconPotion == null) Debug.LogWarning("[BattleUI] Potion icon not found: Resources/InGame/Icon/Potion_Bottle");
        if (_iconRelic  == null) Debug.LogWarning("[BattleUI] Relic icon not found: Resources/InGame/Icon/Relic");
        if (_iconDeck    == null) Debug.LogWarning("[BattleUI] Deck icon not found: Resources/InGame/Icon/Deck");
        if (_iconDiscard == null) Debug.LogWarning("[BattleUI] Discard icon not found: Resources/InGame/Icon/Discard");
        if (_iconFloor   == null) Debug.LogWarning("[BattleUI] Floor icon not found: Resources/InGame/Icon/Floor");
        if (_iconTurn    == null) Debug.LogWarning("[BattleUI] Turn icon not found: Resources/InGame/Icon/Turn");
        if (_iconShield  == null) Debug.LogWarning("[BattleUI] Shield icon not found: Resources/InGame/Icon/Shield");
    }

    void Update()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        // Battle 상태가 아닐 때는 다음 전투를 위해 리셋
        if (gsm.State != GameState.Battle)
        {
            if (_battleInitialized)
            {
                _battleInitialized = false;
                _battleEndQueued = false;
                _battle = null;
                _lastKnownHp.Clear();
                _hpBarDisplayedFrac.Clear();
                _floaters.Clear();
                _targetingCardIndex = -1;
                _endTurnAnimating = false;
                _attackingUnit = null;
                _attackProgress = 0;
                _prevPlayerBlock = 0;
                _playerShieldFxStartTime = -1f;
                StopAllCoroutines();
                DespawnBackgroundFX();
                DespawnBackgroundVines();
                DestroyWorldBackground();
                DestroyAllEnemyViews();
            }
            return;
        }

        // Battle 상태로 진입한 첫 프레임 → 초기화
        if (!_battleInitialized)
        {
            InitBattleFromRunState();
            _battleInitialized = true;
            return;
        }

        // 지연 실행 액션
        if (_pending.Count > 0)
        {
            var snapshot = new List<Action>(_pending);
            _pending.Clear();
            foreach (var a in snapshot) a?.Invoke();
        }

        // HP 변화 감지 & 플로터 진행
        if (_battle?.state != null)
        {
            DetectDamage();
            AdvanceFloaters();
            CleanupDeadEnemyViews();

            // 플레이어 block 증가 감지 → 방패 이펙트 트리거
            int curBlock = _battle.state.player.block;
            if (curBlock > _prevPlayerBlock)
                _playerShieldFxStartTime = Time.time;
            _prevPlayerBlock = curBlock;
        }

        // 전투 종료 감지 → 1.5초 뒤 GSM에 결과 전달
        if (!_battleEndQueued && _battle?.state?.IsOver == true)
        {
            _battleEndQueued = true;
            _battleEndDelay = 1.5f;
        }
        if (_battleEndQueued)
        {
            _battleEndDelay -= Time.deltaTime;
            if (_battleEndDelay <= 0f)
            {
                NotifyBattleEnd();
            }
        }
    }

    private void LoadCardSprites()
    {
        foreach (var card in DataManager.Instance.Cards.Values)
        {
            if (string.IsNullOrEmpty(card.image)) continue;

            string filename = Path.GetFileNameWithoutExtension(card.image);

            // 카드 표시용 일러스트 — 타입별 서브폴더
            string subfolder = card.cardType switch
            {
                CardType.SUMMON => "Summon",
                CardType.MAGIC  => "Spell",
                _               => "Utility", // BUFF / UTILITY / RITUAL
            };
            var tex = Resources.Load<Texture2D>($"CardArt/{subfolder}/{filename}");
            if (tex != null) _cardSprites[card.id] = tex;
            else Debug.LogWarning($"[BattleUI] Card sprite not found: CardArt/{subfolder}/{filename}");

            // 필드용 공룡 스프라이트 (투명 배경) — SUMMON만
            if (card.cardType == CardType.SUMMON)
            {
                var fieldTex = Resources.Load<Texture2D>("Dinos/" + filename);
                if (fieldTex != null) _fieldDinoSprites[card.id] = fieldTex;
                else Debug.LogWarning($"[BattleUI] Field dino sprite not found: Dinos/{filename}");
            }
        }

        _playerSprite = Resources.Load<Texture2D>("Character_infield/Char_Archaeologist_Field");
        if (_playerSprite == null)
            Debug.LogWarning("[BattleUI] Player sprite not found: Character_infield/Char_Archaeologist_Field");
        else
            EnsurePlayerView();
    }

    private void EnsurePlayerView()
    {
        if (_playerView != null || _playerSprite == null) return;

        // Archaeologist 프레임 세트 로드 — 있으면 idle로 사용, 없으면 기존 Char_Archaeologist_Field 폴백
        var idleTex           = Resources.Load<Texture2D>("Character_infield/Archaeologist/Idle");
        var windupTex         = Resources.Load<Texture2D>("Character_infield/Archaeologist/Windup");
        var strikeTex         = Resources.Load<Texture2D>("Character_infield/Archaeologist/Strike");
        var strikeExtendedTex = Resources.Load<Texture2D>("Character_infield/Archaeologist/StrikeExtended");

        var baseTex = idleTex != null ? idleTex : _playerSprite;
        _playerWorldSprite = TexToSprite(baseTex);

        var go = new GameObject("PlayerView");
        go.transform.SetParent(transform, worldPositionStays: false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _playerWorldSprite;
        _playerView = go.AddComponent<BattleEntityView>();
        _playerView.SetSprite(_playerWorldSprite);
        _playerView.SetSortingOrder(50);

        Sprite windupSprite         = windupTex != null ? TexToSprite(windupTex) : null;
        Sprite strikeSprite         = strikeTex != null ? TexToSprite(strikeTex) : null;
        Sprite strikeExtendedSprite = strikeExtendedTex != null
            ? Sprite.Create(
                strikeExtendedTex,
                new Rect(0, 0, strikeExtendedTex.width, strikeExtendedTex.height),
                new Vector2(0.12f, 0f),
                100f)
            : null;
        _playerView.SetAttackFrames(null, windupSprite, strikeSprite, strikeExtendedSprite);

        if (idleTex == null)
            Debug.LogWarning("[BattleUI] Archaeologist/Idle not found, falling back to Char_Archaeologist_Field");
        if (windupTex == null || strikeTex == null)
            Debug.LogWarning("[BattleUI] Archaeologist windup/strike frames missing");
        if (strikeExtendedTex == null)
            Debug.LogWarning("[BattleUI] Archaeologist/StrikeExtended not found — attack will skip extended phase");
    }

    private static Sprite TexToSprite(Texture2D tex)
    {
        return Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0f),
            100f);
    }

    private void OnDestroy()
    {
        if (_playerView != null && _playerView.gameObject != null)
            Destroy(_playerView.gameObject);
        DestroyAllEnemyViews();
    }

    private void LoadEnemySprites()
    {
        foreach (var enemy in DataManager.Instance.Enemies.Values)
        {
            if (string.IsNullOrEmpty(enemy.image)) continue;

            string filename = Path.GetFileNameWithoutExtension(enemy.image);
            var tex = Resources.Load<Texture2D>("Monsters/" + filename);
            if (tex != null)
            {
                _enemySprites[enemy.id] = tex;
                _enemyWorldSprites[enemy.id] = TexToSprite(tex);
            }
            else Debug.LogWarning($"[BattleUI] Enemy sprite not found: Monsters/{filename}");
        }
    }

    /// <summary>
    /// 지정된 EnemyInstance에 대응하는 BattleEntityView를 보장. 이미 있으면 no-op.
    /// 적 id별 world Sprite가 로드돼 있어야 작동 (없으면 IMGUI 폴백).
    /// </summary>
    private void EnsureEnemyView(EnemyInstance e)
    {
        if (e == null || _enemyViews.ContainsKey(e)) return;
        if (!_enemyWorldSprites.TryGetValue(e.data.id, out var sprite)) return;

        var go = new GameObject($"EnemyView_{e.data.id}");
        go.transform.SetParent(transform, worldPositionStays: false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        var view = go.AddComponent<BattleEntityView>();
        view.SetSprite(sprite);
        view.SetSortingOrder(50);
        _enemyViews[e] = view;
    }

    private void CleanupDeadEnemyViews()
    {
        if (_enemyViews.Count == 0) return;
        List<EnemyInstance> toRemove = null;
        foreach (var kv in _enemyViews)
        {
            if (kv.Key.IsDead)
            {
                if (kv.Value != null && kv.Value.gameObject != null)
                    Destroy(kv.Value.gameObject);
                (toRemove ??= new List<EnemyInstance>()).Add(kv.Key);
            }
        }
        if (toRemove != null)
            foreach (var k in toRemove) _enemyViews.Remove(k);
    }

    private void DestroyAllEnemyViews()
    {
        foreach (var kv in _enemyViews)
        {
            if (kv.Value != null && kv.Value.gameObject != null)
                Destroy(kv.Value.gameObject);
        }
        _enemyViews.Clear();
    }

    private void InitBattleFromRunState()
    {
        var gsm = GameStateManager.Instance;
        var run = gsm.CurrentRun;
        var enemies = gsm.CurrentEnemies;

        Debug.Log($"[BattleUI] InitBattleFromRunState: enemies={enemies?.Count ?? 0}, hp={run?.playerCurrentHp ?? -1}");

        if (run == null || enemies == null || enemies.Count == 0)
        {
            Debug.LogError("[BattleUI] Cannot init battle — run is null or enemies empty");
            return;
        }

        _backgroundTexture = LoadBackgroundFor(enemies[0]);
        UpdateWorldBackground();
        DestroyAllEnemyViews();
        _lastKnownHp.Clear();
        _hpBarDisplayedFrac.Clear();
        _floaters.Clear();
        _pending.Clear();
        _battleEndQueued = false;
        _targetingCardIndex = -1;

        var chapter = DataManager.Instance.GetChapter(run.chapterId);
        int mana = chapter?.mana ?? 3;

        _battle = new BattleManager();
        _battle.StartBattle(
            new List<CardData>(run.deck),
            new List<EnemyData>(enemies), // 복사본 전달
            mana,
            run.playerMaxHp);

        // 현재 run의 HP로 플레이어 초기화 (이전 전투 잔존 HP 반영)
        _battle.state.player.hp = Mathf.Clamp(run.playerCurrentHp, 1, run.playerMaxHp);

        PrepareEnemyViews();
        SpawnBackgroundFX();
        SpawnBackgroundVines();
    }

    /// <summary>
    /// 전투 시작 직후 적 뷰를 생성하고 올바른 world 위치로 초기화.
    /// 이걸 안 하면 OnGUI 전까지 (0,0,0)에서 한 프레임 깜빡이는 현상이 생긴다.
    /// </summary>
    private void PrepareEnemyViews()
    {
        if (_battle?.state == null || Camera.main == null) return;
        ComputeSlotPositions(_battle.state);
        foreach (var e in _battle.state.enemies)
        {
            if (e.IsDead) continue;
            EnsureEnemyView(e);
            if (!_enemyViews.TryGetValue(e, out var view)) continue;
            if (!_slotPositions.TryGetValue(e, out var center)) continue;

            const float w = 200f, h = 200f;
            var rect = new Rect(center.x - w / 2f, center.y - h / 2f, w, h);
            Vector3 feetWorld = GuiToWorld(new Vector2(center.x, rect.yMax));
            Vector3 topWorld  = GuiToWorld(new Vector2(center.x, rect.y));
            float worldHeight = Mathf.Abs(feetWorld.y - topWorld.y);
            view.SetBasePosition(feetWorld);
            view.SetWorldHeight(worldHeight);
        }
    }

    private void SpawnBackgroundVines()
    {
        DespawnBackgroundVines();
        if (Camera.main == null || _backgroundTexture == null) return;

        string bgName = _backgroundTexture.name;
        foreach (var v in _bgVineEntries)
        {
            if (v == null || string.IsNullOrEmpty(v.resourcePath)) continue;
            if (!string.IsNullOrEmpty(v.backgroundName) && v.backgroundName != bgName) continue;

            var tex = Resources.Load<Texture2D>(v.resourcePath);
            if (tex == null)
            {
                Debug.LogWarning($"[BattleUI] Vine texture not found: {v.resourcePath}");
                continue;
            }

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 1.0f),
                100f);

            var go = new GameObject($"_Vine ({System.IO.Path.GetFileName(v.resourcePath)})");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = v.sortingOrder;
            sr.flipX = v.flipX;
            sr.color = v.color;

            go.transform.position = GuiToWorld(v.guiPos);
            go.transform.localScale = Vector3.one * v.scale;

            if (v.useGodRay)
            {
                var god = go.AddComponent<DianoCard.FX.GodRayFX>();
                god.minAlpha = v.godRayMinAlpha;
                god.maxAlpha = v.godRayMaxAlpha;
                god.pulseSpeed = v.godRayPulseSpeed;
                god.swayAngle = v.swayAngle;
                god.swaySpeed = v.swaySpeed;
                god.phaseOffset = v.swayPhase;
            }
            else
            {
                var sway = go.AddComponent<DianoCard.FX.VineSway>();
                sway.angle = v.swayAngle;
                sway.speed = v.swaySpeed;
                sway.phase = v.swayPhase;
            }

            _spawnedVines.Add(go);
        }
    }

    private void DespawnBackgroundVines()
    {
        for (int i = 0; i < _spawnedVines.Count; i++)
            if (_spawnedVines[i] != null) Destroy(_spawnedVines[i]);
        _spawnedVines.Clear();
    }

    private void SpawnBackgroundFX()
    {
        DespawnBackgroundFX();
        if (Camera.main == null || _backgroundTexture == null)
        {
            Debug.LogWarning($"[BattleUI] SpawnBackgroundFX skipped: cam={Camera.main}, bg={_backgroundTexture}");
            return;
        }

        string bgName = _backgroundTexture.name;
        Debug.Log($"[BattleUI] SpawnBackgroundFX: bg='{bgName}', entryCount={_bgFxEntries.Count}");

        int spawned = 0;
        foreach (var e in _bgFxEntries)
        {
            if (e == null || e.prefab == null) continue;
            if (!string.IsNullOrEmpty(e.backgroundName) && e.backgroundName != bgName) continue;

            Vector3 world = GuiToWorld(e.guiPos);
            var go = Instantiate(e.prefab, world, Quaternion.identity);
            ApplyScaleAndIntensity(go, e.scale, e.intensity);
            _spawnedBgFx.Add(go);
            spawned++;
            Debug.Log($"[BattleUI]   spawned '{e.prefab.name}' @ gui({e.guiPos.x},{e.guiPos.y}) -> world({world.x:F2},{world.y:F2}), scale={e.scale}, intensity={e.intensity}");
        }
        Debug.Log($"[BattleUI] SpawnBackgroundFX done: {spawned} instances");
    }

    private static void ApplyScaleAndIntensity(GameObject go, float scale, float intensity)
    {
        go.transform.localScale = Vector3.one * scale;
        var systems = go.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var ps = systems[i];
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTimeMultiplier *= intensity;
            emission.rateOverDistanceMultiplier *= intensity;
        }
    }

    private void DespawnBackgroundFX()
    {
        for (int i = 0; i < _spawnedBgFx.Count; i++)
            if (_spawnedBgFx[i] != null) Destroy(_spawnedBgFx[i]);
        _spawnedBgFx.Clear();
    }

    // 공격 방향 (플레이어 → 타겟 적). 기본은 오른쪽(+x). 적 위치를 world로 변환해 벡터 계산.
    private Vector3 ComputeAttackDir(int preferredEnemyIdx)
    {
        var target = GetAttackTargetWorld(preferredEnemyIdx);
        if (target == Vector3.zero || _playerView == null) return Vector3.right;
        Vector3 dir = target - _playerView.transform.position;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.0001f) return Vector3.right;
        return dir.normalized;
    }

    // 공격 타겟 적의 world 위치 (torso 부근). preferredIdx 유효하면 그 적, 아니면 첫 살아있는 적.
    private Vector3 GetAttackTargetWorld(int preferredEnemyIdx = -1)
    {
        var enemies = _battle?.state?.enemies;
        if (enemies == null || enemies.Count == 0 || Camera.main == null) return Vector3.zero;

        EnemyInstance target = null;
        if (preferredEnemyIdx >= 0 && preferredEnemyIdx < enemies.Count && !enemies[preferredEnemyIdx].IsDead)
            target = enemies[preferredEnemyIdx];
        else
        {
            foreach (var e in enemies)
            {
                if (!e.IsDead) { target = e; break; }
            }
        }
        if (target == null || !_slotPositions.TryGetValue(target, out var slot)) return Vector3.zero;

        // slot은 발 부근 IMGUI 좌표. 몸통 중앙 부근을 타겟으로 잡기 위해 위로 올림.
        return GuiToWorld(new Vector2(slot.x, slot.y - 60f));
    }

    private Vector3 GuiToWorld(Vector2 guiPos)
    {
        var cam = Camera.main;
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        float sx = guiPos.x * scale;
        float sy = Screen.height - guiPos.y * scale;
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(sx, sy, _vfxZDistance));
        world.z = 0f;
        return world;
    }

    private void NotifyBattleEnd()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || _battle == null) return;

        bool won = _battle.state.PlayerWon;
        int hp = _battle.state.player.hp;
        gsm.EndBattle(won, hp);
    }

    private Texture2D LoadBackgroundFor(EnemyData enemy)
    {
        string path = enemy.enemyType switch
        {
            EnemyType.BOSS => "Backgrounds/Boss_Battle",
            EnemyType.ELITE => "Backgrounds/Elite_Battle",
            _ => UnityEngine.Random.value < 0.5f
                ? "Backgrounds/Normal_Battle"
                : "Backgrounds/Normal_Battle2",
        };

        var tex = Resources.Load<Texture2D>(path);
        if (tex == null) Debug.LogWarning($"[BattleUI] Background not found: Resources/{path}");
        return tex;
    }

    // =========================================================
    // Damage detection & floaters
    // =========================================================

    private void DetectDamage()
    {
        var state = _battle.state;
        _seenThisFrame.Clear();

        int newFloatersThisFrame = 0;

        TryCheckHp(state.player, state.player.hp, ref newFloatersThisFrame);
        foreach (var s in state.field) TryCheckHp(s, s.hp, ref newFloatersThisFrame);
        foreach (var e in state.enemies) TryCheckHp(e, e.hp, ref newFloatersThisFrame);

        if (_lastKnownHp.Count > _seenThisFrame.Count)
        {
            var toRemove = new List<object>();
            foreach (var key in _lastKnownHp.Keys)
                if (!_seenThisFrame.Contains(key)) toRemove.Add(key);
            foreach (var key in toRemove) _lastKnownHp.Remove(key);
        }
    }

    private void TryCheckHp(object unit, int currentHp, ref int newFloatersThisFrame)
    {
        _seenThisFrame.Add(unit);

        if (_lastKnownHp.TryGetValue(unit, out int prev))
        {
            int delta = prev - currentHp;
            if (delta > 0)
            {
                float delay = newFloatersThisFrame * 0.30f;
                _floaters.Add(new DamageFloater
                {
                    anchor = unit,
                    amount = delta,
                    delay = delay,
                    age = 0,
                });
                if (_slotPositions.TryGetValue(unit, out var guiPos))
                {
                    StartCoroutine(SpawnDamageVFXDelayed(guiPos, delay));
                }
                if (unit is EnemyInstance ei
                    && _enemyViews.TryGetValue(ei, out var eView)
                    && eView != null)
                {
                    StartCoroutine(PlayHitDelayed(eView, delay));
                }
                else if (unit is Player && _playerView != null)
                {
                    StartCoroutine(PlayHitDelayed(_playerView, delay));
                }
                newFloatersThisFrame++;
            }
        }
        _lastKnownHp[unit] = currentHp;
    }

    private IEnumerator SpawnDamageVFXDelayed(Vector2 guiPos, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        SpawnDamageVFX(guiPos);
    }

    private IEnumerator PlayHitDelayed(BattleEntityView view, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (view != null) view.PlayHit();
    }

    private void SpawnDamageVFX(Vector2 guiPos)
    {
        if (Camera.main == null) return;
        Vector3 world = GuiToWorld(guiPos);

        if (_vfxHitA   != null) Instantiate(_vfxHitA,   world, Quaternion.identity);
        if (_vfxHitD   != null) Instantiate(_vfxHitD,   world, Quaternion.identity);
        if (_vfxSmokeF != null) Instantiate(_vfxSmokeF, world, Quaternion.identity);
    }

    private void AdvanceFloaters()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < _floaters.Count; i++)
        {
            var f = _floaters[i];
            if (f.delay > 0) f.delay = Mathf.Max(0, f.delay - dt);
            else f.age += dt;
        }
        _floaters.RemoveAll(f => f.age >= DamageFloater.LifeTime);
    }

    // =========================================================
    // OnGUI
    // =========================================================

    void OnGUI()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || gsm.State != GameState.Battle) return;
        if (_battle == null || _battle.state == null) return;

        EnsureStyles();

        // 우클릭으로 타겟팅 취소
        if (_targetingCardIndex >= 0
            && Event.current.type == EventType.MouseDown
            && Event.current.button == 1)
        {
            _targetingCardIndex = -1;
            Event.current.Use();
        }

        // 손에 없는 인덱스를 가리키고 있으면 리셋
        if (_targetingCardIndex >= _battle.state.hand.Count)
        {
            _targetingCardIndex = -1;
        }

        // 1) 배경은 스크린 원본 좌표로 꽉 채움
        GUI.matrix = Matrix4x4.identity;
        DrawBackground();

        // 2) 이후 UI는 1280x720 가상 좌표로 그린 뒤 스케일링
        float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

        var state = _battle.state;

        ComputeSlotPositions(state);

        DrawBattleField(state);
        DrawFloaters();
        DrawTopBar(state, gsm);
        DrawTurnInfo(state);
        DrawHand(state);
        DrawEndTurn(state);
        DrawTargetingHint(state);
    }

    private void DrawTargetingHint(BattleState state)
    {
        if (_targetingCardIndex < 0 || _targetingCardIndex >= state.hand.Count) return;
        var c = state.hand[_targetingCardIndex].data;
        string text = $"▶ {c.nameKr} 사용 중 — 적을 클릭하세요  (우클릭: 취소)";
        GUI.Label(new Rect(0, 115, RefW, 30), text, _targetHintStyle);
    }

    private void DrawBackground()
    {
        // World-space SpriteRenderer로 그리므로 OnGUI 경로는 비워둔다.
        // world 경로가 실패해서 텍스처만 있고 sr이 없을 때만 OnGUI 폴백.
        if (_worldBgSr != null || _backgroundTexture == null) return;
        GUI.DrawTexture(
            new Rect(0, 0, Screen.width, Screen.height),
            _backgroundTexture,
            ScaleMode.ScaleAndCrop,
            alphaBlend: true);
    }

    private void UpdateWorldBackground()
    {
        if (_backgroundTexture == null) return;
        var cam = Camera.main;
        if (cam == null) return;

        if (_worldBgSr == null)
        {
            var go = new GameObject("_BattleBackground");
            _worldBgSr = go.AddComponent<SpriteRenderer>();
            _worldBgSr.sortingOrder = -100;
        }

        var tex = _backgroundTexture;
        _worldBgSr.sprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);

        if (cam.orthographic)
        {
            float camH = cam.orthographicSize * 2f;
            float camW = camH * cam.aspect;
            float spriteW = tex.width / 100f;
            float spriteH = tex.height / 100f;
            float s = Mathf.Max(camW / spriteW, camH / spriteH);
            _worldBgSr.transform.localScale = new Vector3(s, s, 1f);
        }

        var camPos = cam.transform.position;
        _worldBgSr.transform.position = new Vector3(camPos.x, camPos.y, 0f);
        _worldBgSr.enabled = true;
    }

    private void DestroyWorldBackground()
    {
        if (_worldBgSr != null)
        {
            Destroy(_worldBgSr.gameObject);
            _worldBgSr = null;
        }
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _boxStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(10, 10, 8, 8),
            wordWrap = true,
        };
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(8, 8, 8, 8),
            wordWrap = true,
        };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _centerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _intentStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.9f, 0.5f) },
        };
        _damageStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        _targetHintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.3f) },
        };
        _cardCostStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.95f, 0.6f) },
        };
        _cardNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.92f, 0.75f) },
        };
        _cardTypeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.4f) },
        };
        _cardDescStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Normal,
            wordWrap = true,
            padding = new RectOffset(6, 6, 4, 4),
            normal = { textColor = new Color(0.95f, 0.88f, 0.72f) },
        };
        _stylesReady = true;
    }

    private static bool CardNeedsTarget(CardData c)
    {
        return c.cardType == CardType.MAGIC
            && c.subType == CardSubType.ATTACK
            && c.target == TargetType.ENEMY;
    }

    // =========================================================
    // Battle field rendering
    // =========================================================

    private void ComputeSlotPositions(BattleState state)
    {
        // 지면 라인(Y=540)에 발끝이 닿도록 각 유닛 스프라이트 높이의 절반만큼 위로 올려 중심을 잡음.
        // 플레이어/적 h=220/200, 필드 소환수 h=110 기준.
        const float GroundY = 540f;

        _slotPositions.Clear();
        _slotPositions[state.player] = new Vector2(180, GroundY - 110);

        for (int i = 0; i < state.field.Count; i++)
        {
            _slotPositions[state.field[i]] = new Vector2(340 + i * 120, GroundY - 55);
        }

        int aliveIdx = 0;
        foreach (var e in state.enemies)
        {
            if (e.IsDead) continue;
            _slotPositions[e] = new Vector2(1070 - aliveIdx * 200, GroundY - 100);
            aliveIdx++;
        }
    }

    private void DrawBattleField(BattleState state)
    {
        DrawPlayerNPC(state.player, _slotPositions[state.player]);

        foreach (var s in state.field)
            if (_slotPositions.TryGetValue(s, out var pos)) DrawSummon(s, pos);

        for (int i = 0; i < state.enemies.Count; i++)
        {
            var e = state.enemies[i];
            if (e.IsDead) continue;
            if (_slotPositions.TryGetValue(e, out var pos)) DrawEnemy(e, i, pos);
        }
    }

    private void DrawPlayerNPC(Player p, Vector2 center)
    {
        // 캐릭터 스프라이트는 world-space BattleEntityView가 그림. IMGUI에서는 HP 바만 처리.
        const float h = 260;
        if (_playerSprite != null)
        {
            float texAspect = _playerSprite.width / (float)_playerSprite.height;
            float w = h * texAspect;
            var rect = new Rect(center.x - w / 2, center.y - h / 2, w, h);

            // PlayerView world 위치/크기 동기화 — IMGUI 좌표(발 위치)를 world로 변환
            if (_playerView != null && Camera.main != null)
            {
                Vector2 feetGui = new Vector2(center.x, rect.yMax);
                Vector3 feetWorld = GuiToWorld(feetGui);
                Vector3 topWorld  = GuiToWorld(new Vector2(center.x, rect.y));
                float worldHeight = Mathf.Abs(feetWorld.y - topWorld.y);
                _playerView.SetBasePosition(feetWorld);
                _playerView.SetWorldHeight(worldHeight);
            }

            DrawPlayerShieldFx(new Vector2(center.x, rect.center.y), Mathf.Max(w, 160f), h);

            // HP 바 — 캐릭터 발 아래에 살짝 더 넓게, 발과 약간 떨어뜨림
            float barW = Mathf.Max(w + 24f, 110f);
            var barRect = new Rect(center.x - barW / 2, rect.yMax + 6, barW, 16);
            DrawHpBar(barRect, p.hp, p.maxHp, new Color(0.85f, 0.2f, 0.2f));

            if (p.block > 0)
            {
                DrawBlockBadge(new Vector2(center.x, rect.y - 24), p.block, 44f);
            }
        }
        else
        {
            const float fbW = 140, fbH = 200;
            var rect = new Rect(center.x - fbW / 2, center.y - fbH / 2, fbW, fbH);

            FillRect(rect, new Color(0.25f, 0.45f, 0.8f, 0.88f));
            DrawBorder(rect, 2, new Color(0.15f, 0.3f, 0.6f, 1f));

            DrawPlayerShieldFx(new Vector2(rect.center.x, rect.center.y), fbW, fbH);

            DrawHpBar(new Rect(rect.x + 6, rect.y + rect.height - 52, rect.width - 12, 18),
                      p.hp, p.maxHp, new Color(0.85f, 0.2f, 0.2f));

            if (p.block > 0)
            {
                DrawBlockBadge(new Vector2(rect.center.x, rect.y - 24), p.block, 44f);
            }
        }
    }

    // 방패 아이콘 + 숫자 뱃지. center를 중심으로 size 크기로 그림.
    private void DrawBlockBadge(Vector2 center, int block, float size = 40f)
    {
        var iconRect = new Rect(center.x - size / 2, center.y - size / 2, size, size);
        if (_iconShield != null)
        {
            GUI.DrawTexture(iconRect, _iconShield, ScaleMode.ScaleToFit, alphaBlend: true);
        }

        int prevFontSize = _centerStyle.fontSize;
        Color prevColor = _centerStyle.normal.textColor;
        _centerStyle.fontSize = Mathf.RoundToInt(size * 0.42f);
        _centerStyle.normal.textColor = Color.white;

        var shadowRect = new Rect(iconRect.x + 1, iconRect.y + 2, iconRect.width, iconRect.height);
        var prevShadow = _centerStyle.normal.textColor;
        _centerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        GUI.Label(shadowRect, block.ToString(), _centerStyle);
        _centerStyle.normal.textColor = prevShadow;

        GUI.Label(iconRect, block.ToString(), _centerStyle);

        _centerStyle.fontSize = prevFontSize;
        _centerStyle.normal.textColor = prevColor;
    }

    // 플레이어 주위에 떠오르는 반투명 방패 버블. block이 증가한 프레임에 트리거되어
    // ShieldFxDuration 동안 페이드 인 → 유지(펄스) → 페이드 아웃.
    private void DrawPlayerShieldFx(Vector2 center, float targetW, float targetH)
    {
        if (_playerShieldFxStartTime < 0f) return;
        if (_manaFrameTexture == null) return;

        float t = Time.time - _playerShieldFxStartTime;
        if (t >= ShieldFxDuration)
        {
            _playerShieldFxStartTime = -1f;
            return;
        }

        float n = t / ShieldFxDuration;

        // 엔벨로프: 0~0.15 fade-in → 0.15~0.65 hold → 0.65~1 fade-out
        float envelope;
        if (n < 0.15f) envelope = n / 0.15f;
        else if (n < 0.65f) envelope = 1f;
        else envelope = 1f - (n - 0.65f) / 0.35f;
        envelope = Mathf.Clamp01(envelope);

        float pulse = 0.92f + 0.08f * Mathf.Sin(Time.time * 6f);

        // 캐릭터 실루엣 대비 살짝 크게 잡은 버블 기준 크기
        float baseSize = Mathf.Max(targetW, targetH) * 1.35f;

        var prevColor = GUI.color;

        // 1) 바깥 soft glow — 옅은 하늘빛 오라
        {
            float size = baseSize * 1.2f * pulse;
            var r = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
            GUI.color = new Color(0.45f, 0.78f, 1f, 0.16f * envelope);
            GUI.DrawTexture(r, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 2) 메인 bubble — 캐릭터를 감싸는 중심 방패
        {
            float size = baseSize * pulse;
            var r = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
            GUI.color = new Color(0.6f, 0.88f, 1f, 0.5f * envelope);
            GUI.DrawTexture(r, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 3) 확산 링 — 트리거 직후 0.5초 동안 밖으로 퍼지며 페이드
        {
            float ringN = Mathf.Clamp01(n / 0.5f);
            float ringAlpha = (1f - ringN) * 0.55f;
            if (ringAlpha > 0f)
            {
                float size = baseSize * (1.05f + ringN * 0.55f);
                var r = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
                GUI.color = new Color(0.75f, 0.95f, 1f, ringAlpha);
                GUI.DrawTexture(r, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }
        }

        GUI.color = prevColor;
    }

    private void DrawSummon(SummonInstance s, Vector2 center)
    {
        // Lunge 오프셋: 공격 중인 소환수는 오른쪽으로 sin 곡선 이동
        if (ReferenceEquals(_attackingUnit, s))
        {
            float lunge = Mathf.Sin(_attackProgress * Mathf.PI) * LungePixels;
            center.x += lunge;
        }

        const float w = 110, h = 110;
        var rect = new Rect(center.x - w / 2, center.y - h / 2, w, h);

        if (_fieldDinoSprites.TryGetValue(s.data.id, out var tex))
        {
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            FillRect(rect, new Color(0.4f, 0.7f, 0.4f, 0.8f));
            GUI.Label(new Rect(rect.x, rect.y + h / 2 - 10, rect.width, 22),
                      s.data.nameKr, _centerStyle);
        }

        // HP 바 — 적과 동일 스타일, 스프라이트 발 아래로 약간 떨어뜨림
        DrawHpBar(new Rect(rect.x + 6f, rect.y + rect.height + 6f, rect.width - 12f, 14f),
                  s.hp, s.data.hp, new Color(0.85f, 0.2f, 0.2f));

        // ATK 뱃지 — 좌상단 작은 빨간 원 + 숫자
        DrawAtkBadge(new Vector2(rect.x + 6f, rect.y + 6f), s.TotalAttack, s.tempAttackBonus > 0);
    }

    // 작은 ATK 뱃지 — _manaFrameTexture(원형)를 빨간 틴트로 재활용 + 글로우 + 숫자
    private void DrawAtkBadge(Vector2 topLeft, int attack, bool boosted)
    {
        const float size = 30f;
        var rect = new Rect(topLeft.x, topLeft.y, size, size);

        if (_manaFrameTexture != null)
        {
            var prev = GUI.color;

            // 작은 빨간 글로우 (저강도)
            Color glowTint = boosted ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.35f, 0.35f);
            const int layers = 4;
            for (int i = 0; i < layers; i++)
            {
                float t = i / (float)(layers - 1);
                float scale = Mathf.Lerp(1.10f, 1.55f, t);
                float alpha = 0.30f * (1f - t) * (1f - t);
                float gs = size * scale;
                var gr = new Rect(rect.center.x - gs * 0.5f, rect.center.y - gs * 0.5f, gs, gs);
                GUI.color = new Color(glowTint.r, glowTint.g, glowTint.b, alpha);
                GUI.DrawTexture(gr, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }

            // 본체 — 어두운 빨강 틴트
            GUI.color = boosted ? new Color(1f, 0.78f, 0.30f, 1f) : new Color(0.82f, 0.18f, 0.18f, 1f);
            GUI.DrawTexture(rect, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.color = prev;
        }
        else
        {
            FillRect(rect, boosted ? new Color(0.85f, 0.65f, 0.15f, 0.95f) : new Color(0.78f, 0.18f, 0.18f, 0.95f));
            DrawBorder(rect, 1f, new Color(0.85f, 0.66f, 0.28f, 0.95f));
        }

        int prevFontSize = _cardCostStyle.fontSize;
        _cardCostStyle.fontSize = Mathf.RoundToInt(size * 0.50f);
        DrawTextWithOutline(rect, attack.ToString(), _cardCostStyle,
                            Color.white, new Color(0f, 0f, 0f, 0.95f), 1.4f);
        _cardCostStyle.fontSize = prevFontSize;
    }

    private void DrawEnemy(EnemyInstance e, int enemyIndex, Vector2 center)
    {
        const float w = 200, h = 200;
        var rect = new Rect(center.x - w / 2, center.y - h / 2, w, h);

        // 적 애니메이션 뷰는 world-space BattleEntityView가 그림. IMGUI는 HP/intent만.
        EnsureEnemyView(e);
        bool hasView = _enemyViews.TryGetValue(e, out var view);
        if (hasView)
        {
            if (Camera.main != null)
            {
                Vector2 feetGui = new Vector2(center.x, rect.yMax);
                Vector3 feetWorld = GuiToWorld(feetGui);
                Vector3 topWorld  = GuiToWorld(new Vector2(center.x, rect.y));
                float worldHeight = Mathf.Abs(feetWorld.y - topWorld.y);
                view.SetBasePosition(feetWorld);
                view.SetWorldHeight(worldHeight);
            }
        }
        else if (_enemySprites.TryGetValue(e.data.id, out var tex))
        {
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            Color col = e.data.enemyType switch
            {
                EnemyType.BOSS => new Color(0.75f, 0.15f, 0.15f, 0.88f),
                EnemyType.ELITE => new Color(0.8f, 0.45f, 0.1f, 0.88f),
                _ => new Color(0.55f, 0.25f, 0.25f, 0.88f),
            };
            FillRect(rect, col);
            DrawBorder(rect, 2, Color.black);
            GUI.Label(new Rect(rect.x, rect.y + h / 2 - 10, rect.width, 22),
                      e.data.nameKr, _centerStyle);
        }

        GUI.Label(new Rect(rect.x - 30, rect.y - 32, rect.width + 60, 24),
                  $"▲ {e.IntentLabel}", _intentStyle);

        DrawHpBar(new Rect(rect.x + 20, rect.y + rect.height - 8, rect.width - 40, 18),
                  e.hp, e.data.hp, new Color(0.85f, 0.2f, 0.2f));

        if (e.block > 0)
        {
            DrawBlockBadge(new Vector2(rect.xMax - 22, rect.y + rect.height - 8), e.block, 40f);
        }

        // 타겟팅 모드: 빨간 펄스 외곽선 + 클릭 처리
        if (_targetingCardIndex >= 0)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            var glowRect = new Rect(rect.x - 6, rect.y - 6, rect.width + 12, rect.height + 12);
            DrawBorder(glowRect, 4, new Color(1f, 0.2f, 0.2f, pulse));

            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
            {
                ev.Use();
                int cardIdx = _targetingCardIndex;
                int eIdx = enemyIndex;
                _targetingCardIndex = -1;
                _pending.Add(() => {
                    _battle.PlayCard(cardIdx, eIdx);
                    _playerView?.PlayAttack(ComputeAttackDir(eIdx));
                });
            }
        }
    }

    private void DrawHpBar(Rect rect, int curr, int max, Color fill)
    {
        float realFrac = max > 0 ? Mathf.Clamp01((float)curr / max) : 0f;

        // 위치 기반 키로 bar의 표시 fraction을 추적 — 데미지 받으면 pale trail이 따라 내려감
        var key = new Vector2(rect.x, rect.y);
        if (!_hpBarDisplayedFrac.TryGetValue(key, out float displayed))
            displayed = realFrac;

        if (Event.current.type == EventType.Repaint)
        {
            if (realFrac < displayed)
                displayed = Mathf.MoveTowards(displayed, realFrac, Time.unscaledDeltaTime * 0.85f);
            else
                displayed = realFrac; // 힐은 즉시
            _hpBarDisplayedFrac[key] = displayed;
        }

        // 1) 배경 인셋 — 어두운 홈 느낌
        FillRect(rect, new Color(0.08f, 0.05f, 0.05f, 0.92f));

        // 2) 딜레이 트레일 — 실제 hp 구간 ~ displayed 구간 사이에만 pale 잔상
        if (displayed > realFrac)
        {
            float trailStartX = rect.x + rect.width * realFrac;
            float trailWidth = rect.width * (displayed - realFrac);
            FillRect(new Rect(trailStartX, rect.y, trailWidth, rect.height),
                     new Color(1f, 0.88f, 0.55f, 0.88f));
        }

        // 3) 본 HP 채움 + 그라디언트 (상단 하이라이트, 하단 섀도)
        if (realFrac > 0f)
        {
            var fillRect = new Rect(rect.x, rect.y, rect.width * realFrac, rect.height);
            FillRect(fillRect, fill);

            float hiH = Mathf.Max(1f, fillRect.height * 0.38f);
            FillRect(new Rect(fillRect.x, fillRect.y, fillRect.width, hiH),
                     new Color(1f, 0.60f, 0.50f, 0.45f));

            float shH = Mathf.Max(1f, fillRect.height * 0.28f);
            FillRect(new Rect(fillRect.x, fillRect.yMax - shH, fillRect.width, shH),
                     new Color(0f, 0f, 0f, 0.32f));
        }

        // 4) 저체력 펄스 — 30% 이하일 때 빨간 발광이 숨쉬듯 박동
        if (realFrac > 0f && realFrac < 0.3f)
        {
            float pulse = (Mathf.Sin(Time.time * 4.5f) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(0.18f, 0.45f, pulse) * (1f - realFrac / 0.3f);
            FillRect(rect, new Color(1f, 0.15f, 0.15f, alpha));
        }

        // 5) 골드 외곽 프레임 + 내부 암색 인셋 라인 — HUD 톤 통일
        DrawBorder(rect, 1f, new Color(0.86f, 0.66f, 0.28f, 0.95f));
        var innerRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        DrawBorder(innerRect, 1f, new Color(0f, 0f, 0f, 0.55f));

        // 6) 외곽선 텍스트 — 흰 글자 + 검정 외곽
        DrawTextWithOutline(rect, $"{curr}/{max}", _centerStyle,
                            Color.white, new Color(0f, 0f, 0f, 0.95f), 1.2f);
    }

    private void DrawFloaters()
    {
        foreach (var f in _floaters)
        {
            if (f.delay > 0) continue;
            if (!_slotPositions.TryGetValue(f.anchor, out var basePos)) continue;

            float progress = Mathf.Clamp01(f.age / DamageFloater.LifeTime);
            float alpha = 1f - progress;
            float yOffset = -70f * progress;

            var rect = new Rect(basePos.x - 60, basePos.y - 110 + yOffset, 120, 46);
            GUI.color = new Color(1f, 0.25f, 0.25f, alpha);
            GUI.Label(rect, $"-{f.amount}", _damageStyle);
            GUI.color = Color.white;
        }
    }

    // =========================================================
    // Overlay panels
    // =========================================================

    // 상단 HUD 아이콘 뒤에 깔리는 다층 글로우 — 마나 오브의 후광과 동일한 결로
    // 부드럽게 호흡하며 가장자리는 자연스럽게 사라진다.
    private void DrawIconGlow(Rect iconRect, Color tint, float intensity = 1f)
    {
        if (_manaFrameTexture == null) return;

        var prevColor = GUI.color;

        float slow = (Mathf.Sin(Time.time * 1.4f) + 1f) * 0.5f;
        float pulse = Mathf.Lerp(0.85f, 1.0f, slow);

        const int glowLayers = 6;
        const float glowMinScale = 1.15f;
        const float glowMaxScale = 2.10f;
        const float glowBaseAlpha = 0.22f;

        float cx = iconRect.center.x;
        float cy = iconRect.center.y;
        float baseSize = Mathf.Max(iconRect.width, iconRect.height);

        for (int i = 0; i < glowLayers; i++)
        {
            float t = i / (float)(glowLayers - 1);
            float scale = Mathf.Lerp(glowMinScale, glowMaxScale, t) + 0.04f * slow * t;
            float alpha = Mathf.Min(1f, glowBaseAlpha * (1f - t) * (1f - t) * pulse * intensity);
            float gs = baseSize * scale;
            var gr = new Rect(cx - gs * 0.5f, cy - gs * 0.5f, gs, gs);
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(gr, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        GUI.color = prevColor;
    }

    private void DrawTopBar(BattleState state, GameStateManager gsm)
    {
        var p = state.player;
        var run = gsm?.CurrentRun;

        const float barX = 10f;
        const float barY = 8f;
        const float barW = RefW - 20f;
        const float barH = 68f;
        var barRect = new Rect(barX, barY, barW, barH);

        // 배경 없음 — 스탯만 화면 위에 떠 있는 미니멀 스타일

        const float iconSize = 50f;
        const float iconLabelGap = 6f;
        const float slotGap = 28f;
        const float padX = 20f;
        float iconY = barY + (barH - iconSize) * 0.5f;
        float cursorX = barX + padX;

        void DrawSlot(Texture2D tex, string label, Color glowTint, float glowIntensity = 1f)
        {
            if (tex != null)
            {
                var iconRect = new Rect(cursorX, iconY, iconSize, iconSize);
                DrawIconGlow(iconRect, glowTint, glowIntensity);
                GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit);
                cursorX += iconSize + iconLabelGap;
            }
            var size = _labelStyle.CalcSize(new GUIContent(label));
            var labelRect = new Rect(cursorX, barY + (barH - size.y) * 0.5f, size.x + 2f, size.y);
            GUI.Label(labelRect, label, _labelStyle);
            cursorX += size.x + slotGap;
        }

        DrawSlot(_iconHP, $"{p.hp}/{p.maxHp}", new Color(1f, 0.55f, 0.50f), 1.6f);

        if (run != null)
        {
            DrawSlot(_iconGold, $"{run.gold}", new Color(1f, 0.82f, 0.35f));
            DrawSlot(_iconPotion, $"{run.potions.Count}/{RunState.MaxPotionSlots}", new Color(0.55f, 1f, 0.65f));
            DrawSlot(_iconRelic, $"{run.relics.Count}", new Color(0.85f, 0.55f, 1f));

            DrawRightSlots(barRect, barY, barH, iconY, iconSize, iconLabelGap,
                $"{run.currentFloor}/5", $"{state.turn}");
        }
        else
        {
            DrawRightSlots(barRect, barY, barH, iconY, iconSize, iconLabelGap,
                null, $"{state.turn}");
        }
    }

    // 우측 정렬 슬롯들 (Floor + Turn). 우→좌 순서로 그려서 cursor 계산을 단순화.
    private void DrawRightSlots(
        Rect barRect, float barY, float barH,
        float iconY, float iconSize, float iconLabelGap,
        string floorLabel, string turnLabel)
    {
        const float rightPad = 28f;       // 화면 우측 가장자리 여백 (padX보다 살짝 크게)
        const float rightSlotGap = 56f;   // Floor ↔ Turn 간격 (좌측 slotGap보다 넓게)

        float right = barRect.xMax - rightPad;

        // Turn 슬롯 (가장 오른쪽) — 모래시계는 아주 미세하게 좌우로 기울음
        // 글로우는 새 따뜻한 모래시계 톤과 어울리도록 골드/앰버 계열로
        right = DrawRightSlot(right, barY, barH, iconY, iconSize, iconLabelGap,
            _iconTurn, turnLabel, new Color(1f, 0.78f, 0.35f), wobblePhase: 0f);

        // Floor 슬롯 — 계단도 동일하게 살짝 기울음 (위상만 다르게)
        if (floorLabel != null)
        {
            right -= rightSlotGap;
            DrawRightSlot(right, barY, barH, iconY, iconSize, iconLabelGap,
                _iconFloor, floorLabel, new Color(1f, 0.82f, 0.35f), wobblePhase: 2.4f);
        }
    }

    // 한 슬롯을 right 기준으로 우→좌로 그리고, 이 슬롯의 left x를 반환
    // wobblePhase가 >=0 이면 미세한 좌우 기울임 적용 (양옆으로 살짝 기우는 느낌)
    private float DrawRightSlot(float right, float barY, float barH,
        float iconY, float iconSize, float iconLabelGap,
        Texture2D icon, string label, Color glowTint, float wobblePhase)
    {
        var labelSize = _labelStyle.CalcSize(new GUIContent(label));
        float labelX = right - labelSize.x;
        var labelRect = new Rect(labelX, barY + (barH - labelSize.y) * 0.5f, labelSize.x + 2f, labelSize.y);
        GUI.Label(labelRect, label, _labelStyle);

        float iconX = labelX - iconLabelGap - iconSize;
        if (icon != null)
        {
            var iconRect = new Rect(iconX, iconY, iconSize, iconSize);
            DrawIconGlow(iconRect, glowTint);

            // 아주 미세한 좌우 기울임 — 더 천천히 부드럽게, 폭은 더 작게
            float angle = Mathf.Sin(Time.time * 0.7f + wobblePhase) * 0.32f;
            var prevMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, iconRect.center);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            GUI.matrix = prevMatrix;
        }
        return iconX;
    }

    private void DrawTurnInfo(BattleState state)
    {
        var p = state.player;

        // 좌하단 마나 오브 — 다층 글로우 + 펄스 + shimmer 코어로 살아있는 느낌.
        // 덱이 좌하단 모서리를 차지하므로 오브는 그 우측에 자리한다.
        const float orbSize = 80f;
        float orbCx = 175f;
        float orbCy = RefH - 70f;
        var orbRect = new Rect(orbCx - orbSize * 0.5f, orbCy - orbSize * 0.5f, orbSize, orbSize);

        if (_manaFrameTexture != null)
        {
            var prevColor = GUI.color;

            // 호흡 펄스 — 느린 사인파.
            float slow = (Mathf.Sin(Time.time * 1.4f) + 1f) * 0.5f;        // 0..1
            float pulse = Mathf.Lerp(0.85f, 1.0f, slow);

            // 빠른 shimmer — 외곽이 살짝 깜빡이며 살아있는 느낌
            float shimmer = (Mathf.Sin(Time.time * 3.6f) + 1f) * 0.5f;     // 0..1

            // 글로우 색상이 천천히 청록 ↔ 라벤더로 흐른다
            Color glowTint = Color.Lerp(
                new Color(0.50f, 0.88f, 1.00f),
                new Color(0.72f, 0.72f, 1.00f),
                (Mathf.Sin(Time.time * 0.55f) + 1f) * 0.5f);

            // 1) 다층 그래디언트 후광
            const int glowLayers = 8;
            const float glowMaxScale = 1.65f;
            const float glowMinScale = 1.05f;
            const float glowBaseAlpha = 0.16f;

            for (int i = 0; i < glowLayers; i++)
            {
                float t = i / (float)(glowLayers - 1);
                float scale = Mathf.Lerp(glowMinScale, glowMaxScale, t) + 0.04f * slow * t;
                float alpha = glowBaseAlpha * (1f - t) * (1f - t) * pulse;
                float gs = orbSize * scale;
                var gr = new Rect(orbCx - gs * 0.5f, orbCy - gs * 0.5f, gs, gs);
                GUI.color = new Color(glowTint.r, glowTint.g, glowTint.b, alpha);
                GUI.DrawTexture(gr, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }

            // 1-b) Shimmer 링 — 빠르게 깜빡이는 밝은 외곽 빛
            {
                float shimmerScale = 1.32f + 0.10f * shimmer;
                float shimmerAlpha = 0.10f + 0.22f * shimmer;
                float shs = orbSize * shimmerScale;
                var shr = new Rect(orbCx - shs * 0.5f, orbCy - shs * 0.5f, shs, shs);
                GUI.color = new Color(0.92f, 0.97f, 1f, shimmerAlpha);
                GUI.DrawTexture(shr, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }

            // 2) 본체 오브 — 숨쉬듯 ±3% 스케일링 + 미세한 수직 떨림
            float bodyScale = 1f + 0.030f * (slow - 0.5f);
            float bodyBob = Mathf.Sin(Time.time * 2.1f) * 1.4f;
            float bodySize = orbSize * bodyScale;
            var bodyRect = new Rect(orbCx - bodySize * 0.5f,
                                    orbCy - bodySize * 0.5f + bodyBob,
                                    bodySize, bodySize);
            GUI.color = Color.white;
            GUI.DrawTexture(bodyRect, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);

            // 3) 안쪽 코어 하이라이트 — 빠른 shimmer에 맞춰 깜빡이는 흰 점
            {
                float coreSize = orbSize * (0.32f + 0.05f * shimmer);
                var coreRect = new Rect(orbCx - coreSize * 0.5f,
                                        orbCy - coreSize * 0.5f + bodyBob,
                                        coreSize, coreSize);
                GUI.color = new Color(1f, 1f, 1f, 0.12f + 0.18f * shimmer);
                GUI.DrawTexture(coreRect, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }

            GUI.color = prevColor;
        }

        int prevFontSize = _cardCostStyle.fontSize;
        _cardCostStyle.fontSize = Mathf.RoundToInt(orbSize * 0.34f);
        DrawTextWithOutline(orbRect, $"{p.mana}/{p.maxMana}", _cardCostStyle,
                            Color.white, new Color(0, 0, 0, 0.95f), 1.5f);
        _cardCostStyle.fontSize = prevFontSize;

        // 좌하단 덱 더미 — 화면 좌측 최하단 모서리에 작게
        DrawCardPile(new Rect(22f, RefH - 88f, 78f, 78f), _iconDeck, state.deck.Count);

        // 우하단 버린 카드 더미 — 좌측 덱과 대칭
        DrawCardPile(new Rect(RefW - 90f, RefH - 88f, 78f, 78f), _iconDiscard, state.discard.Count);
    }

    private void DrawCardPile(Rect rect, Texture2D icon, int count)
    {
        if (icon != null)
        {
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            FillRect(rect, new Color(0.18f, 0.13f, 0.09f, 0.85f));
            DrawBorder(rect, 2f, new Color(0.7f, 0.55f, 0.3f, 1f));
        }

        // 카운트 텍스트 — 아이콘 중앙 위에 외곽선 텍스트.
        int prevFontSize = _centerStyle.fontSize;
        _centerStyle.fontSize = Mathf.RoundToInt(rect.height * 0.26f);
        DrawTextWithOutline(rect, count.ToString(), _centerStyle,
                            Color.white, new Color(0, 0, 0, 0.95f), 1.6f);
        _centerStyle.fontSize = prevFontSize;
    }

    private void DrawHand(BattleState state)
    {
        const float cardW = 150f;
        const float cardH = 225f;

        int n = state.hand.Count;
        if (n == 0) return;

        // 부채꼴 기하: 화면 하단 훨씬 아래 가상의 원 중심에서 반지름만큼 떨어진 호 위에 카드 배치
        // 카드를 화면 아래로 내려서 배틀필드(발끝 Y≈540)를 가리지 않게 함.
        float centerCardY = RefH - cardH * 0.5f + 60f; // 중앙 카드의 y 중심 (상단 ≈ Y 567, 노출 ≈ 160px)
        float fanRadius   = 1100f;
        float fanOriginX  = RefW * 0.5f;
        float fanOriginY  = centerCardY + fanRadius;

        // 카드 간 각도 고정 (좌우 완전 대칭)
        const float anglePerCard = 6f;
        float totalAngle = (n - 1) * anglePerCard;
        float startAngle = -totalAngle * 0.5f;

        // 드로우 순서: 가장자리 카드부터, 중앙 카드가 마지막(최상단)에 오도록
        // 이렇게 해야 좌우 겹침이 대칭이 됨 (왼쪽 카드가 오른쪽 이웃을 덮고, 오른쪽 카드는 왼쪽 이웃을 덮음)
        float midIdx = (n - 1) * 0.5f;
        var drawOrder = new int[n];
        for (int k = 0; k < n; k++) drawOrder[k] = k;
        System.Array.Sort(drawOrder, (a, b) => Mathf.Abs(b - midIdx).CompareTo(Mathf.Abs(a - midIdx)));

        // 1) 호버 인덱스 계산 — 최상단(= drawOrder의 마지막)부터 역순 검사
        Vector2 mouse = Event.current.mousePosition;
        int hoverIdx = -1;
        for (int k = n - 1; k >= 0; k--)
        {
            int i = drawOrder[k];
            float angle = startAngle + i * anglePerCard;
            Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
            center.y += CardIdleBob(i);
            if (PointInRotatedRect(mouse, center, cardW, cardH, angle))
            {
                hoverIdx = i;
                break;
            }
        }

        // 2) 비호버 카드 — drawOrder 순서대로(바깥 → 안쪽) 회전시켜 드로우
        // 주의: GUIUtility.RotateAroundPivot은 pivot을 스크린 픽셀 좌표로 다루므로
        //       (newMat * baseMatrix 순서로 합성), 가상 1280×720 좌표인 center를 그대로
        //       넘기면 baseMatrix 스케일이 1이 아닐 때 좌우 비대칭이 발생한다.
        //       대신 baseMatrix 안쪽에서 가상 좌표 기준으로 회전 행렬을 직접 합성한다.
        Matrix4x4 baseMatrix = GUI.matrix;
        foreach (int i in drawOrder)
        {
            if (i == hoverIdx) continue;

            var c = state.hand[i].data;
            bool canPlay = IsCardPlayable(state, c);

            float angle = startAngle + i * anglePerCard;
            Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
            center.y += CardIdleBob(i);
            var rect = new Rect(center.x - cardW * 0.5f, center.y - cardH * 0.5f, cardW, cardH);

            GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angle, center);

            if (i == _targetingCardIndex)
            {
                var glowRect = new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8);
                DrawBorder(glowRect, 4, new Color(1f, 0.85f, 0.3f, 1f));
            }
            DrawCardFrame(rect, c, canPlay, drawCost: false);
        }
        GUI.matrix = baseMatrix;

        // 2-b) Cost 패스 — 카드 본체가 모두 그려진 뒤 cost 원만 위에 다시 그린다.
        // 이렇게 해야 좌→우 겹침 순서에 상관없이 cost가 항상 보임.
        foreach (int i in drawOrder)
        {
            if (i == hoverIdx) continue;

            var c = state.hand[i].data;
            bool canPlay = IsCardPlayable(state, c);

            float angle = startAngle + i * anglePerCard;
            Vector2 center = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);
            center.y += CardIdleBob(i);
            var rect = new Rect(center.x - cardW * 0.5f, center.y - cardH * 0.5f, cardW, cardH);

            GUI.matrix = baseMatrix * RotateAroundPivotMatrix(angle, center);
            DrawCardCost(rect, c, canPlay);
        }
        GUI.matrix = baseMatrix;

        // 3) 호버 카드 — 회전 없이, 크게, 위로 올라옴 (맨 위에 그려져야 하므로 마지막)
        if (hoverIdx >= 0)
        {
            int i = hoverIdx;
            var c = state.hand[i].data;
            bool canPlay = IsCardPlayable(state, c);

            float angle = startAngle + i * anglePerCard;
            Vector2 fanCenter = FanCardCenter(fanOriginX, fanOriginY, fanRadius, angle);

            const float hoverScale = 1.18f;
            const float hoverBottomPad = 20f;
            float hw = cardW * hoverScale;
            float hh = cardH * hoverScale;

            // 호버 카드는 부채꼴 위치와 무관하게 화면 하단에 고정 앵커해서 전체가 항상 보이게 함.
            // x는 부채꼴 위치 유지(손 위 어느 카드인지 직관적으로 보이게), y만 화면 하단 기준.
            var hoverRect = new Rect(fanCenter.x - hw * 0.5f, RefH - hh - hoverBottomPad, hw, hh);

            if (i == _targetingCardIndex)
            {
                var glowRect = new Rect(hoverRect.x - 4, hoverRect.y - 4, hoverRect.width + 8, hoverRect.height + 8);
                DrawBorder(glowRect, 4, new Color(1f, 0.85f, 0.3f, 1f));
            }
            DrawCardFrame(hoverRect, c, canPlay, drawCost: true);

            // 클릭 처리: 호버된 카드에서만
            if (canPlay)
            {
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && hoverRect.Contains(ev.mousePosition))
                {
                    ev.Use();
                    int captured = i;
                    if (CardNeedsTarget(c))
                    {
                        _targetingCardIndex = captured;
                    }
                    else
                    {
                        _targetingCardIndex = -1;
                        _pending.Add(() => {
                            _battle.PlayCard(captured, -1);
                            _playerView?.PlayAttack(ComputeAttackDir(-1));
                        });
                    }
                }
            }
        }
    }

    private bool IsCardPlayable(BattleState state, CardData c)
    {
        if (state.IsOver || _endTurnAnimating) return false;
        if (state.player.mana < c.cost) return false;
        if (c.cardType == CardType.SUMMON && c.subType == CardSubType.CARNIVORE && state.field.Count == 0) return false;
        return true;
    }

    private static Vector2 FanCardCenter(float originX, float originY, float radius, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(originX + Mathf.Sin(rad) * radius,
                           originY - Mathf.Cos(rad) * radius);
    }

    // 손패 카드의 idle 수직 호흡 — 카드마다 위상이 어긋나 자연스럽게 출렁인다.
    private static float CardIdleBob(int i)
    {
        return Mathf.Sin(Time.time * 1.6f + i * 0.55f) * 3.2f;
    }

    private static Matrix4x4 RotateAroundPivotMatrix(float angleDeg, Vector2 pivot)
    {
        Vector3 p = new Vector3(pivot.x, pivot.y, 0f);
        return Matrix4x4.Translate(p)
             * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angleDeg))
             * Matrix4x4.Translate(-p);
    }

    private static bool PointInRotatedRect(Vector2 p, Vector2 center, float w, float h, float angleDeg)
    {
        float rad = -angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        Vector2 d = p - center;
        Vector2 local = new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
        return Mathf.Abs(local.x) <= w * 0.5f && Mathf.Abs(local.y) <= h * 0.5f;
    }

    /// <summary>
    /// CardFrame 텍스처를 깔고 그 위에 cost/이름/타입/일러스트/설명을 겹쳐 그린다.
    /// 슬롯 위치는 프레임 이미지 비율 기반 (2:3 기준).
    /// </summary>
    private void DrawCardFrame(Rect rect, CardData c, bool canPlay, bool drawCost)
    {
        var prevColor = GUI.color;

        // 비활성화 카드는 전체적으로 어둡게
        if (!canPlay) GUI.color = new Color(0.55f, 0.55f, 0.55f, 0.9f);

        // 1) 배경 (어두운 슬레이트 텍스처) — 카드 전체를 채움
        if (_cardBgTexture != null)
        {
            GUI.DrawTexture(rect, _cardBgTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }
        else
        {
            FillRect(rect, new Color(0.13f, 0.15f, 0.16f, 1f));
        }

        // 2) 아트 — 카드 상단 영역. SUMMON(공룡)은 슬롯 안에서 더 작게 그려서 다른 카드와 시각 비중을 맞춤.
        var artRect = new Rect(
            rect.x + rect.width  * 0.04f,
            rect.y + rect.height * 0.04f,
            rect.width  * 0.92f,
            rect.height * 0.62f);

        if (_cardSprites.TryGetValue(c.id, out var cardTex))
        {
            GUI.DrawTexture(artRect, cardTex, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            FillRect(artRect, GetCardTypeTint(c) * new Color(1f, 1f, 1f, 0.35f));
        }

        // 3) 설명 패널 — 카드 하단 영역. 어두운 톤 + 가는 브론즈 테두리로 끼움 느낌
        var descPanelRect = new Rect(
            rect.x + rect.width  * 0.07f,
            rect.y + rect.height * 0.69f,
            rect.width  * 0.86f,
            rect.height * 0.24f);
        FillRect(descPanelRect, new Color(0.06f, 0.05f, 0.05f, 0.85f));
        DrawBorder(descPanelRect, 1, new Color(0.55f, 0.42f, 0.22f, 0.7f));

        // 4) 테두리 (가운데 투명) — 위에 얹어서 외곽 마무리
        if (_cardBorderTexture != null)
        {
            GUI.DrawTexture(rect, _cardBorderTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }
        else
        {
            DrawBorder(rect, 2, new Color(0.8f, 0.6f, 0.2f));
        }

        GUI.color = prevColor;

        // 3) Cost 원 (좌상단) — 손패에서는 별도 패스로 그려서 가림 방지.
        if (drawCost)
        {
            DrawCardCost(rect, c, canPlay);
        }

        // 4) 이름 배너 — 호버 카드는 폰트 키우고, 손패 카드는 작게 (가려져도 우측만 보이게)
        var nameRect = new Rect(
            rect.x,
            rect.y + rect.height * 0.03f,
            rect.width,
            rect.height * 0.09f);
        int prevNameSize = _cardNameStyle.fontSize;
        _cardNameStyle.fontSize = drawCost ? 16 : 11;
        GUI.Label(nameRect, GetCardCategoryLabel(c), _cardNameStyle);
        _cardNameStyle.fontSize = prevNameSize;

        // 5) 타입 라벨 — 아트 슬롯 아래, 설명 패널 바로 위에 자리잡아 시각적으로 묶임
        var typeRect = new Rect(
            rect.x + rect.width * 0.1f,
            rect.y + rect.height * 0.625f,
            rect.width * 0.8f,
            rect.height * 0.05f);
        GUI.Label(typeRect, GetCardTypeLabel(c), _cardTypeStyle);

        // 6) 설명 — 하단 패널 안에 가운데 정렬되도록 패널 rect 그대로 사용
        GUI.Label(descPanelRect, GetCardBody(c), _cardDescStyle);
    }

    private void DrawCardCost(Rect rect, CardData c, bool canPlay)
    {
        var prevColor = GUI.color;

        // CardFrame 텍스처(848×1256)의 좌상단 코스트 링 중심: 픽셀 약 (108, 108).
        // 정규화 (0.127, 0.086). 오브는 카드폭의 23.5%로 링 외경을 완전히 덮음.
        // (이전 20%는 살짝 작아 가장자리가 비쳐서 +3.5%p 여유 줌)
        const float orbCenterXFrac = 0.127f;
        const float orbCenterYFrac = 0.086f;
        const float orbSizeFrac    = 0.235f;
        float orbSize = rect.width * orbSizeFrac;
        float orbCx = rect.x + rect.width  * orbCenterXFrac;
        float orbCy = rect.y + rect.height * orbCenterYFrac;
        var orbRect = new Rect(orbCx - orbSize * 0.5f, orbCy - orbSize * 0.5f, orbSize, orbSize);

        if (_manaFrameTexture != null)
        {
            // 호흡 펄스 — 마나 HUD 오브와 동일 스타일.
            float slow = (Mathf.Sin(Time.time * 1.4f) + 1f) * 0.5f;
            float pulse = Mathf.Lerp(0.85f, 1.0f, slow);

            // 1) 다층 그래디언트 후광 — 가까운 링은 진하고, 멀어질수록 알파가
            //    부드럽게 0으로 떨어져 가장자리가 자연스럽게 사라짐.
            const int glowLayers = 8;
            const float glowMaxScale = 1.55f;
            const float glowMinScale = 1.05f;
            float glowBaseAlpha = canPlay ? 0.13f : 0.06f;
            Color glowTint = new Color(0.55f, 0.85f, 1f);

            for (int i = 0; i < glowLayers; i++)
            {
                float t = i / (float)(glowLayers - 1);
                float scale = Mathf.Lerp(glowMinScale, glowMaxScale, t) + 0.03f * slow * t;
                float alpha = glowBaseAlpha * (1f - t) * (1f - t) * pulse;
                float gs = orbSize * scale;
                var gr = new Rect(orbCx - gs * 0.5f, orbCy - gs * 0.5f, gs, gs);
                GUI.color = new Color(glowTint.r, glowTint.g, glowTint.b, alpha);
                GUI.DrawTexture(gr, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
            }

            // 2) 본체 오브 — 플레이 가능 시 숨쉬듯 ±1.5% 스케일링.
            float bodyScale = 1f + 0.015f * (slow - 0.5f);
            float bodySize = orbSize * bodyScale;
            var bodyRect = new Rect(orbCx - bodySize * 0.5f, orbCy - bodySize * 0.5f, bodySize, bodySize);
            GUI.color = canPlay ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.95f);
            GUI.DrawTexture(bodyRect, _manaFrameTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }

        // 숫자: 오브 어두운 중심부 위에 흰 글자 + 검은 외곽선.
        Color textCol = canPlay ? Color.white : new Color(0.75f, 0.75f, 0.75f, 0.9f);
        Color outlineCol = new Color(0f, 0f, 0f, canPlay ? 0.95f : 0.7f);
        int prevFontSize = _cardCostStyle.fontSize;
        _cardCostStyle.fontSize = Mathf.RoundToInt(orbSize * 0.62f);
        DrawTextWithOutline(orbRect, c.cost.ToString(), _cardCostStyle, textCol, outlineCol, 1.2f);
        _cardCostStyle.fontSize = prevFontSize;

        GUI.color = prevColor;
    }

    private static void DrawTextWithOutline(Rect rect, string text, GUIStyle style,
                                            Color textColor, Color outlineColor, float thickness)
    {
        var prev = GUI.color;
        var prevTextColor = style.normal.textColor;

        style.normal.textColor = outlineColor;
        GUI.color = outlineColor;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var r = new Rect(rect.x + dx * thickness, rect.y + dy * thickness, rect.width, rect.height);
                GUI.Label(r, text, style);
            }

        style.normal.textColor = textColor;
        GUI.color = textColor;
        GUI.Label(rect, text, style);

        style.normal.textColor = prevTextColor;
        GUI.color = prev;
    }

    private static Color GetCardTypeTint(CardData c)
    {
        return c.cardType switch
        {
            CardType.SUMMON => new Color(0.5f, 0.8f, 0.4f),
            CardType.MAGIC when c.subType == CardSubType.ATTACK => new Color(0.9f, 0.4f, 0.3f),
            CardType.MAGIC => new Color(0.4f, 0.6f, 0.9f),
            CardType.BUFF => new Color(0.95f, 0.8f, 0.3f),
            CardType.UTILITY => new Color(0.7f, 0.7f, 0.7f),
            CardType.RITUAL => new Color(0.7f, 0.3f, 0.7f),
            _ => new Color(0.6f, 0.6f, 0.6f),
        };
    }

    // 카드 상단(이름 슬롯): 카테고리만
    private static string GetCardCategoryLabel(CardData c)
    {
        return c.cardType switch
        {
            CardType.SUMMON => "Summon",
            CardType.MAGIC => "Spell",
            CardType.BUFF => "Buff",
            CardType.UTILITY => "Utility",
            CardType.RITUAL => "Ritual",
            _ => "",
        };
    }

    // 카드 중앙(타입 라벨): 마법은 Attack/Defense, 그 외는 카드 고유 이름
    private static string GetCardTypeLabel(CardData c)
    {
        if (c.cardType == CardType.MAGIC)
            return c.subType == CardSubType.ATTACK ? "Attack" : "Defense";
        return c.nameEn;
    }

    private static string GetCardBody(CardData c)
    {
        if (c.cardType == CardType.SUMMON)
            return $"ATK {c.attack}\nHP {c.hp}";
        return ShortDesc(c);
    }

    private static string ShortDesc(CardData c)
    {
        if (string.IsNullOrEmpty(c.description)) return "";
        return c.description.Length > 60
            ? c.description.Substring(0, 60) + "…"
            : c.description;
    }

    private void DrawEndTurn(BattleState state)
    {
        GUI.enabled = !state.IsOver && !_endTurnAnimating;

        // 베이스 사이즈(살짝 작아짐) + 호버 시 확대
        var baseRect = new Rect(RefW - 280f, RefH - 80f, 150f, 72f);
        bool hovered = GUI.enabled && baseRect.Contains(Event.current.mousePosition);

        // 호버 스케일 — 즉각적인 펌프 느낌을 위해 약간 보간 (Repaint에서만 누적)
        float targetScale = hovered ? 1.12f : 1.0f;
        if (Event.current.type == EventType.Repaint)
            _endTurnHoverScale = Mathf.Lerp(_endTurnHoverScale, targetScale, Time.unscaledDeltaTime * 14f);

        float w = baseRect.width * _endTurnHoverScale;
        float h = baseRect.height * _endTurnHoverScale;
        var rect = new Rect(baseRect.center.x - w * 0.5f, baseRect.center.y - h * 0.5f, w, h);

        if (_endTurnButtonTex != null)
        {
            var prev = GUI.color;

            // 황금빛 외곽 글로우 — 버튼 텍스처를 확대해 깔고 골드 틴트로 펄스, 호버 시 더 강하게
            float slow = (Mathf.Sin(Time.time * 1.6f) + 1f) * 0.5f;
            float pulse = Mathf.Lerp(0.75f, 1.05f, slow);
            Color goldTint = new Color(1.0f, 0.82f, 0.35f);

            const int glowLayers = 6;
            const float glowMinScale = 1.04f;
            float glowMaxScale = hovered ? 1.46f : 1.32f;
            float glowBaseAlpha = hovered ? 0.48f : 0.32f;

            float cx = rect.center.x;
            float cy = rect.center.y;

            for (int i = 0; i < glowLayers; i++)
            {
                float t = i / (float)(glowLayers - 1);
                float scale = Mathf.Lerp(glowMinScale, glowMaxScale, t) + 0.025f * slow * t;
                float alpha = glowBaseAlpha * (1f - t) * (1f - t) * pulse;
                if (!GUI.enabled) alpha *= 0.35f;
                float gw = rect.width * scale;
                float gh = rect.height * scale;
                var gr = new Rect(cx - gw * 0.5f, cy - gh * 0.5f, gw, gh);
                GUI.color = new Color(goldTint.r, goldTint.g, goldTint.b, alpha);
                GUI.DrawTexture(gr, _endTurnButtonTex, ScaleMode.ScaleToFit, alphaBlend: true);
            }

            GUI.color = GUI.enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(rect, _endTurnButtonTex, ScaleMode.ScaleToFit, alphaBlend: true);
            GUI.color = prev;

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                _targetingCardIndex = -1;
                _pending.Add(() => StartCoroutine(EndTurnCoroutine()));
            }
        }
        else if (GUI.Button(rect, "END\nTURN", _buttonStyle))
        {
            _targetingCardIndex = -1;
            _pending.Add(() => StartCoroutine(EndTurnCoroutine()));
        }

        GUI.enabled = true;
    }

    // =========================================================
    // EndTurn 애니메이션 코루틴
    // =========================================================

    private IEnumerator EndTurnCoroutine()
    {
        if (_battle == null || _battle.state == null) yield break;
        _endTurnAnimating = true;
        var state = _battle.state;

        // Phase 1: 소환수가 1, 2, 3 순서대로 lunge → 공격
        var summons = new List<SummonInstance>(state.field);
        foreach (var s in summons)
        {
            if (s.IsDead) continue;
            if (state.AllEnemiesDead) break;

            yield return AnimateLunge(s, isSummon: true);
            _battle.DoSummonAttack(s);
            yield return new WaitForSeconds(BetweenAttacksPause);
        }

        // 적 전부 사망 → 전투 종료 감지에 맡기고 코루틴 종료
        if (state.AllEnemiesDead)
        {
            _endTurnAnimating = false;
            _attackingUnit = null;
            yield break;
        }

        // Phase 2: 적이 차례대로 lunge → 행동
        var enemies = new List<EnemyInstance>(state.enemies);
        foreach (var e in enemies)
        {
            if (e.IsDead) continue;
            if (state.PlayerLost) break;

            yield return AnimateEnemyAttack(e);
            _battle.DoEnemyAction(e);
            yield return new WaitForSeconds(BetweenAttacksPause);
        }

        if (state.PlayerLost)
        {
            _endTurnAnimating = false;
            _attackingUnit = null;
            yield break;
        }

        // Phase 3: 정리 + 다음 턴 시작
        _battle.EndTurnCleanup();
        _battle.StartNextTurnIfAlive();

        _endTurnAnimating = false;
        _attackingUnit = null;
    }

    /// <summary>
    /// 적의 공격 애니메이션 — BattleEntityView가 있으면 world-space PlayAttack,
    /// 없으면 IMGUI lunge 폴백.
    /// </summary>
    private IEnumerator AnimateEnemyAttack(EnemyInstance e)
    {
        if (_enemyViews.TryGetValue(e, out var view) && view != null)
        {
            view.PlayAttack(Vector3.left);
            yield return new WaitForSeconds(0.55f);
        }
        else
        {
            yield return AnimateLunge(e, isSummon: false);
        }
    }

    /// <summary>
    /// 단일 유닛이 lunge 모션을 수행. _attackingUnit / _attackProgress를 갱신해서
    /// DrawSummon/DrawEnemy가 위치 오프셋을 적용하게 함.
    /// </summary>
    private IEnumerator AnimateLunge(object unit, bool isSummon)
    {
        _attackingUnit = unit;
        _attackProgress = 0f;

        float elapsed = 0f;
        while (elapsed < LungeDuration)
        {
            elapsed += Time.deltaTime;
            _attackProgress = Mathf.Clamp01(elapsed / LungeDuration);
            yield return null;
        }

        _attackProgress = 0f;
        _attackingUnit = null;
    }

    // =========================================================
    // 저수준 사각형 그리기 유틸
    // =========================================================

    private static void FillRect(Rect rect, Color color)
    {
        var prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    private static void DrawBorder(Rect rect, float thickness, Color color)
    {
        FillRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        FillRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }
}
