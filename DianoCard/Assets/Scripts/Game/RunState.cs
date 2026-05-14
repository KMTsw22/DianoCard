using System;
using System.Collections.Generic;
using DianoCard.Data;

namespace DianoCard.Game
{
    /// <summary>
    /// 한 run(전체 게임 한 판) 동안 유지되는 플레이어 상태.
    /// Lobby에서 NEW RUN 시 생성, Defeat/Victory 시 Lobby로 돌아가면서 폐기.
    /// </summary>
    [Serializable]
    public class RunState
    {
        public int playerMaxHp = 70;
        public int playerCurrentHp = 70;
        public int gold = 0;

        public List<CardData> deck = new();
        public List<RelicData> relics = new();
        public List<PotionData> potions = new();

        // 신규 획득 알림 — 유물/포션 패널을 열면 꺼진다. (TechTreeState.hasNewPoints과 동일 패턴)
        public bool hasNewRelic;
        public bool hasNewPotion;

        // 베이스 포션 슬롯 — R004 약초꾼의 가방 같은 유물이 있으면 동적 증가.
        public const int BasePotionSlots = 3;

        public int currentFloor = 1;
        public string chapterId = "CH01";
        public string characterId = "CH002";  // 선택된 캐릭터 id — 1차 출시는 Arkane(CH002) 단일

        // 미지(?) 노드 pity 카운터 — 마지막 해당 결과 이후 쌓인 이벤트 수(0~5 캡).
        // 규칙/표는 GameStateManager.RollUnknownOutcome 주석 참조.
        public int unknownPityCombat = 0;
        public int unknownPityShop = 0;
        public int unknownPityTreasure = 0;

        // 직전 전투 클리어 시 생성된 보상 (RewardUI가 읽음).
        // BattleReward.extraCardChoiceSets가 List<List<>>라 JsonUtility로 직렬화 불가 — Save 대상에서 제외.
        // 결과적으로 Reward 도중 강제 종료 시 보상은 forfeit, 사용자는 해당 노드 재진입(SaveSnapshot이 currentColumn=-1로 정규화).
        [NonSerialized]
        public BattleReward pendingReward;

        /// <summary>
        /// 현재 보유 가능한 포션 슬롯 수. R004 같은 POTION_SLOT 유물이 있으면 베이스 + 보너스.
        /// 매번 relics를 스캔하므로 유물 획득/판매 즉시 반영.
        /// </summary>
        public int MaxPotionSlots
        {
            get
            {
                int bonus = 0;
                foreach (var r in relics)
                {
                    if (r != null && r.effectType == "POTION_SLOT") bonus += r.value;
                }
                return BasePotionSlots + bonus;
            }
        }

        public bool PotionSlotFull => potions.Count >= MaxPotionSlots;
    }
}
