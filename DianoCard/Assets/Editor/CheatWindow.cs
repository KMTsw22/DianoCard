using System;
using System.Collections.Generic;
using System.Linq;
using DianoCard.Battle;
using DianoCard.Data;
using DianoCard.Game;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 에디터 상단 메뉴 → Tools/Cheat Panel.
/// "훈련장으로 이동" 버튼 → 챕터1 맵 진입(IsTrainingMode=true).
/// 모든 치트(맵·카드·유물·물약·캐릭·진행)는 이 창의 탭에서 직접 조작 — 게임 화면은 깨끗하게 유지.
/// 디자인 튜닝(외부 BG 프리뷰 / 카드 슬롯 프리뷰)은 보조 영역으로 유지.
/// </summary>
public class CheatWindow : EditorWindow
{
    [MenuItem("Tools/Cheat Panel %#F12")]
    static void Open() => GetWindow<CheatWindow>("Cheat Panel");

    private enum Tab { Enemy, Cards, Relics, Potions, Player, Progress }
    private Tab _tab = Tab.Enemy;

    private Vector2 _tabScroll;
    private Vector2 _designScroll;
    private string _cardSearch = "";

    // 데이터 캐시 — 플레이 모드 진입 시 한 번 로드.
    private List<CardData> _allCards;
    private List<RelicData> _allRelics;
    private List<PotionData> _allPotions;
    private List<EnemyData> _enemiesNormal, _enemiesElite, _enemiesBoss;

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드에서만 사용 가능합니다.", MessageType.Info);
            return;
        }

        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            EditorGUILayout.HelpBox("GameStateManager를 찾을 수 없습니다.", MessageType.Warning);
            return;
        }

        EnsureCaches();

        // ===== 헤더 =====
        EditorGUILayout.LabelField($"상태: {gsm.State}   훈련 모드: {(gsm.IsTrainingMode ? "ON" : "OFF")}",
            EditorStyles.boldLabel);

        var bigBtn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("훈련장으로 이동", bigBtn, GUILayout.Height(36)))
            gsm.EnterTraining();
        if (GUILayout.Button("Lobby로 복귀", GUILayout.Height(36), GUILayout.Width(110)))
        {
            if (gsm.IsTrainingMode) gsm.ExitTraining();
            else gsm.ReturnToLobby();
        }
        EditorGUILayout.EndHorizontal();

        DrawRunSummary(gsm);

        EditorGUILayout.Space(6);

        // ===== 탭 바 =====
        var tabs = new[] { "적", "카드", "유물", "물약", "캐릭", "진행" };
        _tab = (Tab)GUILayout.Toolbar((int)_tab, tabs, GUILayout.Height(26));

        EditorGUILayout.Space(4);

        // ===== 탭 내용 (스크롤) =====
        _tabScroll = EditorGUILayout.BeginScrollView(_tabScroll, GUILayout.ExpandHeight(true));
        switch (_tab)
        {
            case Tab.Enemy:    DrawEnemyTab(gsm); break;
            case Tab.Cards:    DrawCardTab(gsm); break;
            case Tab.Relics:   DrawRelicTab(gsm); break;
            case Tab.Potions:  DrawPotionTab(gsm); break;
            case Tab.Player:   DrawPlayerTab(gsm); break;
            case Tab.Progress: DrawProgressTab(gsm); break;
        }
        EditorGUILayout.EndScrollView();

        // ===== 디자인 도구 (접이식) =====
        EditorGUILayout.Space(8);
        DrawDesignToolsSection();

        // 라이브 업데이트 — 게임 상태 변화가 패널에 즉시 반영되도록.
        Repaint();
    }

    private void DrawRunSummary(GameStateManager gsm)
    {
        var run = gsm.CurrentRun;
        if (run == null)
        {
            EditorGUILayout.LabelField("Run 없음 — 훈련장 진입 또는 새 런 시작 필요");
            return;
        }
        EditorGUILayout.LabelField(
            $"HP {run.playerCurrentHp}/{run.playerMaxHp}  ·  골드 {run.gold}  ·  덱 {run.deck.Count}장  " +
            $"·  유물 {run.relics.Count}  ·  물약 {run.potions.Count}/{run.MaxPotionSlots}");
    }

    // =========================================================
    // 적 탭
    // =========================================================
    private void DrawEnemyTab(GameStateManager gsm)
    {
        bool canFight = gsm.IsTrainingMode;
        if (!canFight)
            EditorGUILayout.HelpBox("훈련 모드가 아닙니다. 클릭은 비활성. '훈련장으로 이동' 후 사용하세요.",
                MessageType.Info);
        else
            EditorGUILayout.HelpBox("클릭 시 그 적과 즉시 전투. HP/덱은 매 전투마다 풀 리셋.", MessageType.None);

        DrawEnemyGroup("일반 (NORMAL)", _enemiesNormal, gsm, canFight);
        DrawEnemyGroup("엘리트 (ELITE)", _enemiesElite, gsm, canFight);
        DrawEnemyGroup("보스 (BOSS)",   _enemiesBoss, gsm, canFight);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("복합 전투 프리셋", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!canFight))
        {
            if (GUILayout.Button("일반 2마리 (E001 + E003)", GUILayout.Height(26)))
                gsm.TrainingStartBattle("E001", "E003");
            if (GUILayout.Button("쌍둥이 (E103 × 2)", GUILayout.Height(26)))
                gsm.TrainingStartBattle("E103", "E103");
        }
    }

    private void DrawEnemyGroup(string header, List<EnemyData> list, GameStateManager gsm, bool canFight)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"{header}  ({list?.Count ?? 0})", EditorStyles.boldLabel);
        if (list == null || list.Count == 0)
        {
            EditorGUILayout.LabelField("(없음)", EditorStyles.miniLabel);
            return;
        }
        const int cols = 2;
        for (int i = 0; i < list.Count; i += cols)
        {
            EditorGUILayout.BeginHorizontal();
            for (int j = 0; j < cols; j++)
            {
                int idx = i + j;
                if (idx >= list.Count) { GUILayout.FlexibleSpace(); continue; }
                var e = list[idx];
                string label = $"{e.id}  {e.nameKr}\nHP {e.hp} / ATK {e.attack}";
                using (new EditorGUI.DisabledScope(!canFight))
                {
                    if (GUILayout.Button(label, GUILayout.Height(36)))
                        gsm.TrainingStartBattle(e.id);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // =========================================================
    // 카드 탭
    // =========================================================
    private void DrawCardTab(GameStateManager gsm)
    {
        var run = gsm.CurrentRun;
        bool hasRun = run != null;

        if (!hasRun)
            EditorGUILayout.HelpBox("Run 없음 — 풀은 보이지만 덱+/손패+는 비활성. '훈련장으로 이동' 또는 '새 런 시작'.",
                MessageType.Info);

        // 검색창
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("검색", GUILayout.Width(36));
        _cardSearch = EditorGUILayout.TextField(_cardSearch ?? "");
        if (GUILayout.Button("X", GUILayout.Width(28))) _cardSearch = "";
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // 현재 덱 — Run 있을 때만
        if (hasRun)
        {
            EditorGUILayout.LabelField($"현재 덱 ({run.deck.Count}장) — 클릭=1장 제거", EditorStyles.boldLabel);
            var deckGroups = run.deck.GroupBy(c => c.id).OrderBy(g => g.Key).ToList();
            if (deckGroups.Count == 0)
            {
                EditorGUILayout.LabelField("(빈 덱)", EditorStyles.miniLabel);
            }
            else
            {
                const int cols = 2;
                for (int i = 0; i < deckGroups.Count; i += cols)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = 0; j < cols; j++)
                    {
                        int idx = i + j;
                        if (idx >= deckGroups.Count) { GUILayout.FlexibleSpace(); continue; }
                        var g = deckGroups[idx];
                        var first = g.First();
                        string lbl = g.Count() > 1 ? $"{first.nameKr} ×{g.Count()}" : first.nameKr;
                        if (GUILayout.Button(lbl, GUILayout.Height(22)))
                        {
                            var toRemove = run.deck.FirstOrDefault(c => c.id == g.Key);
                            if (toRemove != null) run.deck.Remove(toRemove);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.Space(6);
        }

        var battle = GetActiveBattle();
        bool inBattle = hasRun && battle != null && battle.state != null && gsm.State == GameState.Battle;

        var search = (_cardSearch ?? "").Trim();
        var filtered = string.IsNullOrEmpty(search)
            ? _allCards
            : _allCards.Where(c =>
                c.nameKr.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        EditorGUILayout.LabelField(
            $"카드 풀 ({filtered?.Count ?? 0} / 전체 {_allCards?.Count ?? 0})  " +
            (inBattle ? "— 덱+ / 손패+ (전투 중)" : "— 덱+ 클릭으로 덱에 추가"),
            EditorStyles.boldLabel);

        if (filtered == null || filtered.Count == 0)
        {
            EditorGUILayout.LabelField("(데이터 없음 — DataManager.Load 실패 가능. 콘솔 확인)",
                EditorStyles.miniLabel);
            return;
        }

        foreach (var c in filtered)
        {
            EditorGUILayout.BeginHorizontal();
            string sub = c.cardType == CardType.SUMMON ? $"·{c.subType}" : "";
            EditorGUILayout.LabelField($"{c.id} {c.nameKr} [{c.rarity}{sub}]", GUILayout.MinWidth(180));
            using (new EditorGUI.DisabledScope(!hasRun))
            {
                if (GUILayout.Button("덱+", GUILayout.Width(48)))
                    run.deck.Add(c);
            }
            using (new EditorGUI.DisabledScope(!inBattle))
            {
                if (GUILayout.Button("손패+", GUILayout.Width(56)))
                    battle.Cheat_AddCardToHand(c.id);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // =========================================================
    // 유물 탭
    // =========================================================
    private void DrawRelicTab(GameStateManager gsm)
    {
        var run = gsm.CurrentRun;
        bool hasRun = run != null;
        if (!hasRun)
            EditorGUILayout.HelpBox("Run 없음 — 풀은 보이지만 획득 비활성. '훈련장으로 이동' 또는 '새 런 시작'.",
                MessageType.Info);

        if (hasRun)
        {
            EditorGUILayout.LabelField($"보유 유물 ({run.relics.Count}) — 클릭=제거", EditorStyles.boldLabel);
            if (run.relics.Count == 0)
            {
                EditorGUILayout.LabelField("(보유 없음)", EditorStyles.miniLabel);
            }
            else
            {
                const int cols = 2;
                var owned = run.relics.ToList();
                for (int i = 0; i < owned.Count; i += cols)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = 0; j < cols; j++)
                    {
                        int idx = i + j;
                        if (idx >= owned.Count) { GUILayout.FlexibleSpace(); continue; }
                        var r = owned[idx];
                        if (GUILayout.Button(r.nameKr, GUILayout.Height(24)))
                            run.relics.Remove(r);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.Space(6);
        }

        EditorGUILayout.LabelField($"전체 유물 풀 ({_allRelics?.Count ?? 0}) — 획득",
            EditorStyles.boldLabel);
        if (_allRelics == null || _allRelics.Count == 0)
        {
            EditorGUILayout.LabelField("(데이터 없음 — DataManager.Load 실패 가능. 콘솔 확인)",
                EditorStyles.miniLabel);
            return;
        }
        foreach (var r in _allRelics)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{r.id}  {r.nameKr}", GUILayout.MinWidth(180));
            using (new EditorGUI.DisabledScope(!hasRun))
            {
                if (GUILayout.Button("획득", GUILayout.Width(60)))
                    gsm.Cheat_AcquireRelic(r.id);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // =========================================================
    // 물약 탭
    // =========================================================
    private void DrawPotionTab(GameStateManager gsm)
    {
        var run = gsm.CurrentRun;
        bool hasRun = run != null;
        if (!hasRun)
            EditorGUILayout.HelpBox("Run 없음 — 풀은 보이지만 획득 비활성. '훈련장으로 이동' 또는 '새 런 시작'.",
                MessageType.Info);

        if (hasRun)
        {
            EditorGUILayout.LabelField($"보유 물약 ({run.potions.Count}/{run.MaxPotionSlots}) — 클릭=제거",
                EditorStyles.boldLabel);
            if (run.potions.Count == 0)
            {
                EditorGUILayout.LabelField("(보유 없음)", EditorStyles.miniLabel);
            }
            else
            {
                const int cols = 2;
                var owned = run.potions.ToList();
                for (int i = 0; i < owned.Count; i += cols)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = 0; j < cols; j++)
                    {
                        int idx = i + j;
                        if (idx >= owned.Count) { GUILayout.FlexibleSpace(); continue; }
                        var p = owned[idx];
                        if (GUILayout.Button(p.nameKr, GUILayout.Height(24)))
                            run.potions.Remove(p);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.Space(6);
        }

        EditorGUILayout.LabelField($"전체 물약 풀 ({_allPotions?.Count ?? 0}) — 획득 (슬롯 꽉 차면 비활성)",
            EditorStyles.boldLabel);
        if (_allPotions == null || _allPotions.Count == 0)
        {
            EditorGUILayout.LabelField("(데이터 없음 — DataManager.Load 실패 가능. 콘솔 확인)",
                EditorStyles.miniLabel);
            return;
        }
        bool full = hasRun && run.PotionSlotFull;
        foreach (var p in _allPotions)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{p.id}  {p.nameKr}", GUILayout.MinWidth(180));
            using (new EditorGUI.DisabledScope(!hasRun || full))
            {
                if (GUILayout.Button("획득", GUILayout.Width(60)))
                    gsm.Cheat_AcquirePotion(p.id);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    // =========================================================
    // 캐릭터 탭 — HP / 골드
    // =========================================================
    private void DrawPlayerTab(GameStateManager gsm)
    {
        var run = gsm.CurrentRun;
        if (run == null) { EditorGUILayout.HelpBox("Run 없음.", MessageType.Info); return; }

        // 전투 중에만 의미 있는 컨트롤(마나/무적/풀회복) — 우선 노출.
        var battle = GetActiveBattle();
        bool inBattle = battle != null && battle.state != null && battle.state.player != null
                        && gsm.State == GameState.Battle;
        EditorGUILayout.LabelField("전투 중 (마나/무적/풀회복)", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!inBattle))
        {
            string manaLabel = inBattle
                ? $"마나 {battle.state.player.mana}/{battle.state.player.maxMana} → 풀 충전"
                : "마나 풀 충전 (전투 중에만)";
            if (GUILayout.Button(manaLabel, GUILayout.Height(26)))
                battle.state.player.mana = battle.state.player.maxMana;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("HP+마나 풀회복", GUILayout.Height(24)))
                battle.Cheat_FullHeal();
            string invLabel = (inBattle && battle.state.player.cheatInvincible) ? "무적 OFF" : "무적 ON";
            if (GUILayout.Button(invLabel, GUILayout.Height(24)))
                battle.Cheat_ToggleInvincible();
            EditorGUILayout.EndHorizontal();
        }
        if (!inBattle)
            EditorGUILayout.HelpBox("전투에 들어가면 위 버튼 활성화.", MessageType.None);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("HP", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"현재 {run.playerCurrentHp} / 최대 {run.playerMaxHp}");
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("풀 회복")) run.playerCurrentHp = run.playerMaxHp;
        if (GUILayout.Button("HP -10")) run.playerCurrentHp = Mathf.Max(1, run.playerCurrentHp - 10);
        if (GUILayout.Button("HP +10")) run.playerCurrentHp = Mathf.Min(run.playerMaxHp, run.playerCurrentHp + 10);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("최대HP -10"))
        {
            run.playerMaxHp = Mathf.Max(10, run.playerMaxHp - 10);
            run.playerCurrentHp = Mathf.Min(run.playerCurrentHp, run.playerMaxHp);
        }
        if (GUILayout.Button("최대HP +10")) run.playerMaxHp += 10;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("골드", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"보유 {run.gold}G");
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+100"))  run.gold += 100;
        if (GUILayout.Button("+500"))  run.gold += 500;
        if (GUILayout.Button("+1000")) run.gold += 1000;
        if (GUILayout.Button("0G"))    run.gold = 0;
        EditorGUILayout.EndHorizontal();
    }

    // =========================================================
    // 진행 탭 — 비전투 화면 직행 + 일반 런 시작
    // =========================================================
    private void DrawProgressTab(GameStateManager gsm)
    {
        EditorGUILayout.HelpBox("아래 버튼은 훈련 모드를 종료하고 일반 흐름으로 분기합니다.", MessageType.None);

        if (GUILayout.Button("새 런 시작 (캐릭터 선택)", GUILayout.Height(28)))
        {
            if (gsm.IsTrainingMode) gsm.ExitTraining();
            gsm.StartNewRun();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("강제 진입 (현재 Run 그대로)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("보상")) gsm.Cheat_TriggerReward();
        if (GUILayout.Button("상점")) gsm.Cheat_EnterShop();
        if (GUILayout.Button("마을")) gsm.Cheat_EnterVillage();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("미지(?) 노드 시뮬", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("? 랜덤"))   gsm.Cheat_TriggerUnknown(null);
        if (GUILayout.Button("→ 전투"))   gsm.Cheat_TriggerUnknown(GameStateManager.UnknownOutcome.Combat);
        if (GUILayout.Button("→ 보물"))   gsm.Cheat_TriggerUnknown(GameStateManager.UnknownOutcome.Treasure);
        if (GUILayout.Button("→ 상점"))   gsm.Cheat_TriggerUnknown(GameStateManager.UnknownOutcome.Shop);
        if (GUILayout.Button("→ 이벤트")) gsm.Cheat_TriggerUnknown(GameStateManager.UnknownOutcome.Event);
        EditorGUILayout.EndHorizontal();
    }

    // =========================================================
    // 디자인 도구 — 게임 흐름과 무관, 디자인 검토용
    // =========================================================
    private bool _designOpen;
    private void DrawDesignToolsSection()
    {
        _designOpen = EditorGUILayout.Foldout(_designOpen, "디자인 도구 (BG/카드 프리뷰)", true);
        if (!_designOpen) return;

        var cheatUi = GetOrCreateCheatUI();
        bool hasBattleUi = UnityEngine.Object.FindFirstObjectByType<BattleUI>() != null;

        EditorGUILayout.LabelField("BG 프리뷰 (외부 파일)", EditorStyles.miniBoldLabel);
        if (GUILayout.Button("이미지 파일 선택…", GUILayout.Height(22)))
            cheatUi.PickPreviewBgFile();

        if (cheatUi.HasPreviewBg)
        {
            EditorGUILayout.LabelField("로드됨", cheatUi.PreviewBgFileName ?? "(이름 없음)");
            using (new EditorGUI.DisabledScope(!hasBattleUi))
            {
                string isoLabel = cheatUi.IsPreviewIsolateOn
                    ? "● 격리 모드 ON (BG + 네비만)"
                    : "○ 격리 모드 OFF";
                if (GUILayout.Button(isoLabel, GUILayout.Height(22)))
                    cheatUi.SetPreviewIsolateMode(!cheatUi.IsPreviewIsolateOn);
            }
            if (!hasBattleUi)
                EditorGUILayout.HelpBox("BattleUI가 씬에 없음. 전투를 한 번 띄운 뒤 격리 모드 사용.",
                    MessageType.Warning);

            if (cheatUi.IsPreviewIsolateOn)
            {
                EditorGUILayout.LabelField("네비바 컨텍스트", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                DrawCtxBtn(cheatUi, BattleUI.HudContext.Battle,  "전투");
                DrawCtxBtn(cheatUi, BattleUI.HudContext.Map,     "맵");
                DrawCtxBtn(cheatUi, BattleUI.HudContext.Village, "마을");
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("BG 프리뷰 끄기", GUILayout.Height(20)))
                cheatUi.ClearPreviewBackground();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("카드 슬롯 프리뷰 (Inspector 튜닝용)", EditorStyles.miniBoldLabel);
        using (new EditorGUI.DisabledScope(!hasBattleUi))
        {
            if (GUILayout.Button("카드 슬롯 프리뷰 (빈 프레임)", GUILayout.Height(22)))
                cheatUi.OpenCardPreview(slotOnly: true);
            if (GUILayout.Button("일반 카드 프리뷰", GUILayout.Height(22)))
                cheatUi.OpenCardPreview(slotOnly: false);
        }
        if (!hasBattleUi)
            EditorGUILayout.HelpBox("BattleUI가 씬에 없음. 전투에 한 번 들어갔다 나오면 OK.",
                MessageType.Warning);

        using (new EditorGUI.DisabledScope(!cheatUi.IsCardPreviewOpen))
        {
            if (GUILayout.Button("프리뷰 닫기", GUILayout.Height(20)))
                cheatUi.CloseCardPreview();
        }
    }

    // =========================================================
    // 헬퍼
    // =========================================================
    private void EnsureCaches()
    {
        var dm = DataManager.Instance;
        if (dm == null) return;
        // 데이터 미로드면 강제 로드. EditorWindow는 플레이 시작 직후 데이터가 아직 비어있는 시점에
        // OnGUI가 호출될 수 있어, 캐시가 빈 리스트로 굳지 않도록 카운트 기반으로 무효화한다.
        if (!dm.IsLoaded || dm.Cards.Count == 0) dm.Load(force: true);

        // T1/T2 진화 결과체는 융합으로만 획득해야 하는 카드 — 치트에서도 보상/상점과 동일하게 노출 차단.
        // 1차 소스: DataManager.EvolutionResultIds (dino_evolution.csv의 result_card_id),
        // 2차 안전망: ID suffix `_T1`/`_T2` (CSV 행 누락 시에도 차단).
        var evoIds = dm.EvolutionResultIds;
        if (_allCards == null || _allCards.Count != dm.Cards.Count - CountEvolutionCards(dm))
            _allCards = dm.Cards.Values
                .Where(c => !IsEvolutionResult(c, evoIds))
                .OrderBy(c => c.id)
                .ToList();
        if (_allRelics == null || _allRelics.Count != dm.Relics.Count)
            _allRelics = dm.Relics.Values.OrderBy(r => r.id).ToList();
        if (_allPotions == null || _allPotions.Count != dm.Potions.Count)
            _allPotions = dm.Potions.Values.OrderBy(p => p.id).ToList();

        // 적은 _TBD 필터로 캐시 카운트가 dm.Enemies.Count와 다를 수 있음 → 캐시 비어있을 때만 재빌드.
        int cachedTotal = (_enemiesNormal?.Count ?? 0) + (_enemiesElite?.Count ?? 0) + (_enemiesBoss?.Count ?? 0);
        if (_enemiesNormal == null || cachedTotal == 0)
        {
            _enemiesNormal = new List<EnemyData>();
            _enemiesElite  = new List<EnemyData>();
            _enemiesBoss   = new List<EnemyData>();
            foreach (var kv in dm.Enemies)
            {
                var e = kv.Value;
                if (e == null) continue;
                if (!string.IsNullOrEmpty(e.patternSetId) && e.patternSetId.EndsWith("_TBD")) continue;
                switch (e.enemyType)
                {
                    case EnemyType.NORMAL: _enemiesNormal.Add(e); break;
                    case EnemyType.ELITE:  _enemiesElite.Add(e);  break;
                    case EnemyType.BOSS:   _enemiesBoss.Add(e);   break;
                }
            }
            Comparison<EnemyData> byId = (a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal);
            _enemiesNormal.Sort(byId); _enemiesElite.Sort(byId); _enemiesBoss.Sort(byId);
        }
    }

    private static BattleManager GetActiveBattle()
    {
        var ui = UnityEngine.Object.FindFirstObjectByType<BattleUI>();
        return ui != null ? ui.Battle : null;
    }

    // T1/T2 진화 결과체 판정. CSV 누락 시에도 ID suffix로 안전망.
    private static bool IsEvolutionResult(CardData c, HashSet<string> evoIds)
    {
        if (c == null || string.IsNullOrEmpty(c.id)) return false;
        if (evoIds != null && evoIds.Contains(c.id)) return true;
        return c.id.EndsWith("_T1") || c.id.EndsWith("_T2");
    }

    // 캐시 무효화용 — 차단된 진화 카드 수. _allCards.Count + 이 값 == dm.Cards.Count 일 때 캐시 유효.
    private static int CountEvolutionCards(DataManager dm)
    {
        var evoIds = dm.EvolutionResultIds;
        int n = 0;
        foreach (var c in dm.Cards.Values)
            if (IsEvolutionResult(c, evoIds)) n++;
        return n;
    }

    // 격리 모드 컨텍스트 라디오 — 현재 선택된 ctx에는 ▶ 표시.
    private static void DrawCtxBtn(CheatUI cheatUi, BattleUI.HudContext ctx, string label)
    {
        bool selected = cheatUi.PreviewHudCtx == ctx;
        var content = new GUIContent(selected ? "▶ " + label : label);
        if (GUILayout.Button(content, GUILayout.Height(20)))
            cheatUi.PreviewHudCtx = ctx;
    }

    // 플레이 모드에서 CheatUI MonoBehaviour가 씬에 없으면 지속 GameObject로 자동 스폰한다.
    // 디자인 도구(BG/카드 프리뷰) 호스트 역할만 함.
    private static CheatUI GetOrCreateCheatUI()
    {
        var existing = UnityEngine.Object.FindFirstObjectByType<CheatUI>();
        if (existing != null) return existing;

        var go = new GameObject("[CheatUI]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        return go.AddComponent<CheatUI>();
    }
}
