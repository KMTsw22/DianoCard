using System.Collections.Generic;
using UnityEngine;

namespace DianoCard.Game
{
    /// <summary>
    /// 테크트리 노드 카탈로그 — 중앙 루트에서 4방위(공격/방어/경제/운영)로 펼쳐짐.
    /// 좌표는 가상 1280×720 기준 (TechTreeUI에서 그대로 사용).
    ///
    /// 각 방위 = N1(루트 인접) → N2/N3(분기) → N4(캡스톤) 4노드.
    /// N1: 1pt × 3랭크 (소형 누적 버프)
    /// N2/N3: 1pt × 2~3랭크 (중간 분기, 둘 중 한 쪽 끝까지 가도 캡스톤 도달)
    /// N4 캡스톤: 5pt × 1랭크 (브랜치 정수)
    ///
    /// 자세한 효과는 description에 한 줄. 효과 적용 로직은 별도 — 여기는 카탈로그만.
    /// </summary>
    public static class TechTreeCatalog
    {
        // 화면 좌표 — TechTreeUI가 직접 참조
        public const float CenterX = 640f;
        public const float CenterY = 360f;
        public const string RootId = "ROOT";

        /// <summary>루트 노드 — 진짜 노드는 아니지만 시각 중심 + 모든 N1의 prereq 자리. 항상 해금 상태로 취급.</summary>
        public static readonly TechNode Root = new TechNode
        {
            id = RootId,
            name = "기원",
            description = "모든 가지의 시작점",
            direction = TechDirection.Center,
            maxRank = 1,
            perRankCost = 0,
            prereqId = null,
            pos = new Vector2(CenterX, CenterY),
            isCapstone = false,
        };

        public static readonly List<TechNode> Nodes = new()
        {
            // ── 우측: 공격 (Right) ──────────────────────────────
            new TechNode {
                id = "A1", name = "야성의 손톱", description = "시작 덱 공룡 카드 ATK +1",
                direction = TechDirection.Right, maxRank = 3, perRankCost = 1,
                prereqId = RootId, pos = new Vector2(CenterX + 130f, CenterY),
            },
            new TechNode {
                id = "A2", name = "화염각", description = "공격 마법 피해 +1",
                direction = TechDirection.Right, maxRank = 3, perRankCost = 1,
                prereqId = "A1", pos = new Vector2(CenterX + 240f, CenterY - 60f),
            },
            new TechNode {
                id = "A3", name = "살의", description = "단일 공격 5% 확률로 ×1.5 크리티컬",
                direction = TechDirection.Right, maxRank = 2, perRankCost = 1,
                prereqId = "A1", pos = new Vector2(CenterX + 240f, CenterY + 60f),
            },
            new TechNode {
                id = "A4", name = "광폭", description = "[캡스톤] 첫 턴 모든 공격 ×2",
                direction = TechDirection.Right, maxRank = 1, perRankCost = 5,
                prereqId = "A2", pos = new Vector2(CenterX + 360f, CenterY),
                isCapstone = true,
            },

            // ── 좌측: 방어 (Left) ───────────────────────────────
            new TechNode {
                id = "D1", name = "단단한 가죽", description = "최대 HP +5",
                direction = TechDirection.Left, maxRank = 3, perRankCost = 1,
                prereqId = RootId, pos = new Vector2(CenterX - 130f, CenterY),
            },
            new TechNode {
                id = "D2", name = "굳건함", description = "매 턴 시작 시 block +1",
                direction = TechDirection.Left, maxRank = 3, perRankCost = 1,
                prereqId = "D1", pos = new Vector2(CenterX - 240f, CenterY - 60f),
            },
            new TechNode {
                id = "D3", name = "조롱", description = "시작 덱에 도발 카드 +1",
                direction = TechDirection.Left, maxRank = 1, perRankCost = 2,
                prereqId = "D1", pos = new Vector2(CenterX - 240f, CenterY + 60f),
            },
            new TechNode {
                id = "D4", name = "불사", description = "[캡스톤] 패배 시 1회, HP 50%로 부활",
                direction = TechDirection.Left, maxRank = 1, perRankCost = 5,
                prereqId = "D2", pos = new Vector2(CenterX - 360f, CenterY),
                isCapstone = true,
            },

            // ── 상단: 공룡 (Up) ─────────────────────────────────
            new TechNode {
                id = "N1", name = "원시의 각성", description = "Run 시작 시 공룡 카드 1장 강화",
                direction = TechDirection.Up, maxRank = 3, perRankCost = 1,
                prereqId = RootId, pos = new Vector2(CenterX, CenterY - 130f),
            },
            new TechNode {
                id = "N2", name = "진화 가속", description = "진화 비용 1 감소",
                direction = TechDirection.Up, maxRank = 3, perRankCost = 1,
                prereqId = "N1", pos = new Vector2(CenterX - 60f, CenterY - 240f),
            },
            new TechNode {
                id = "N3", name = "야수의 유대", description = "공룡 소환 시 +1 ATK 부여",
                direction = TechDirection.Up, maxRank = 1, perRankCost = 2,
                prereqId = "N1", pos = new Vector2(CenterX + 60f, CenterY - 240f),
            },
            new TechNode {
                id = "N4", name = "정점의 군주", description = "[캡스톤] 공룡 전원에게 매 전투 시작 ATK +2",
                direction = TechDirection.Up, maxRank = 1, perRankCost = 5,
                prereqId = "N2", pos = new Vector2(CenterX, CenterY - 350f),
                isCapstone = true,
            },

            // ── 하단: 운영 (Down) ───────────────────────────────
            new TechNode {
                id = "U1", name = "맑은 정신", description = "시작 손패 +1장 (6장 드로우)",
                direction = TechDirection.Down, maxRank = 1, perRankCost = 2,
                prereqId = RootId, pos = new Vector2(CenterX, CenterY + 130f),
            },
            new TechNode {
                id = "U2", name = "풍요", description = "시작 마나 +1",
                direction = TechDirection.Down, maxRank = 2, perRankCost = 3,
                prereqId = "U1", pos = new Vector2(CenterX - 60f, CenterY + 240f),
            },
            new TechNode {
                id = "U3", name = "상인의 감각", description = "상점 가격 10% 할인",
                direction = TechDirection.Down, maxRank = 1, perRankCost = 2,
                prereqId = "U1", pos = new Vector2(CenterX + 60f, CenterY + 240f),
            },
            new TechNode {
                id = "U4", name = "시작의 축복", description = "[캡스톤] Run 시작 시 T1 유물 1개",
                direction = TechDirection.Down, maxRank = 1, perRankCost = 5,
                prereqId = "U2", pos = new Vector2(CenterX, CenterY + 350f),
                isCapstone = true,
            },
        };

        public static TechNode GetNode(string id)
        {
            if (id == RootId) return Root;
            for (int i = 0; i < Nodes.Count; i++)
                if (Nodes[i].id == id) return Nodes[i];
            return null;
        }
    }
}
