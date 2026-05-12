using UnityEngine;
using static DianoCard.Data.LocaleSettings;

namespace DianoCard.Tutorial
{
    /// <summary>
    /// 튜토리얼 단계 메시지 오버레이.
    /// - 화면 dim/박스 없이 상단 빈 하늘 영역에 큰 텍스트만 띄움
    /// - 사용해야 하는 손패 카드 한 장만 회전 펄스 외곽선으로 강조 (BattleUI 좌표 사용)
    /// - 진화체/적/턴종료 단계는 영역 단위 강조
    /// DefaultExecutionOrder를 BattleUI(0)/PauseMenu(1000)보다 늦게 잡아 위에 그려진다.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public class TutorialOverlay : MonoBehaviour
    {
        // ===== Inspector 조절 가능 =====
        // 비율은 화면 폭/높이 대비. 해상도 바뀌어도 같은 비율로 적용.

        [Header("Text Banner Position (화면 비율, 0~1)")]
        [SerializeField, Range(0f, 0.5f), Tooltip("화면 상단에서 텍스트 박스 시작까지 거리 (Screen.height 비율)")]
        private float bannerTopRatio = 0.10f;

        [SerializeField, Range(0.30f, 1.00f), Tooltip("텍스트 박스 폭 (Screen.width 비율)")]
        private float bannerWidthRatio = 0.62f;

        [SerializeField, Range(0.05f, 0.30f), Tooltip("텍스트 박스 높이 (Screen.height 비율)")]
        private float bannerHeightRatio = 0.13f;

        [Header("Font Size (화면 높이 비율, 0~0.1)")]
        [SerializeField, Range(0.020f, 0.060f), Tooltip("메시지 본문 폰트 사이즈 (Screen.height 비율)")]
        private float messageFontRatio = 0.032f;

        [SerializeField, Range(0.012f, 0.030f), Tooltip("진행 힌트 폰트 사이즈 (Screen.height 비율)")]
        private float hintFontRatio = 0.017f;

        [SerializeField, Range(0.012f, 0.025f), Tooltip("스킵 버튼 폰트 사이즈 (Screen.height 비율)")]
        private float skipBtnFontRatio = 0.016f;

        [Header("Highlight Pulse Outline")]
        [SerializeField, Range(1f, 8f), Tooltip("강조 외곽선 두께 (px)")]
        private float highlightThickness = 4f;

        [SerializeField, Range(8f, 40f), Tooltip("강조 글로우 페이드 거리 (px) — Gaussian 흉내")]
        private float highlightGlowPad = 18f;

        [Header("Style Toggles")]
        [SerializeField, Tooltip("메시지 본문에 굵게 적용 (명조체엔 보통 OFF가 더 우아함)")]
        private bool messageBold = false;

        [Header("Background Dim (UserClick 단계만)")]
        [SerializeField, Range(0f, 0.7f),
         Tooltip("UserClick 단계(읽기 시간)에서만 게임 화면을 어둡게 — 펄스/공격취소 안내 등 잔여 효과 가림. CardPlayed 같은 액션 단계는 0(투명) 유지하여 게임 잘 보이게.")]
        private float userClickDimAlpha = 0.40f;

        // ===== 색 톤 (게임 다크판타지 톤에 맞춘 호박/금) =====
        private static readonly Color TextColorWarm = new(1.00f, 0.92f, 0.72f);
        private static readonly Color TextShadow = new(0.02f, 0.02f, 0.03f, 0.95f);
        private static readonly Color HintColor = new(0.78f, 0.68f, 0.48f);
        private static readonly Color BoxColor = new(0.07f, 0.07f, 0.09f, 0.96f);
        private static readonly Color BoxBorder = new(0.42f, 0.34f, 0.22f, 0.85f);

        private Texture2D _whiteTex;
        private BattleUI _battleUI;
        private GUIStyle _messageStyle;
        private GUIStyle _messageShadowStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _skipBtnStyle;
        private GUIStyle _modalTitleStyle;

        // 게임 톤 폰트 — EventUI/CharacterSelectUI와 동일 패턴.
        // KR: Hahmlet 명조 (다크판타지). EN: Cinzel 디스플레이.
        private Font _fontKR;
        private Font _fontEN;

        void Awake()
        {
            _whiteTex = Texture2D.whiteTexture;
            _fontKR = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");
            _fontEN = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
            if (_fontKR == null) Debug.LogWarning("[TutorialOverlay] Missing Fonts/Hahmlet-VariableFont_wght");
            if (_fontEN == null) Debug.LogWarning("[TutorialOverlay] Missing Fonts/Cinzel-VariableFont_wght");
        }

        private void EnsureStyles()
        {
            // 폰트 사이즈 — Inspector ratio × Screen.height. 매 OnGUI마다 갱신해 런타임 조절 즉시 반영.
            int msgSize  = Mathf.Max(12, Mathf.RoundToInt(Screen.height * messageFontRatio));
            int hintSize = Mathf.Max(10, Mathf.RoundToInt(Screen.height * hintFontRatio));
            int btnSize  = Mathf.Max(10, Mathf.RoundToInt(Screen.height * skipBtnFontRatio));

            // 활성 언어 폰트 — KR이면 Hahmlet, EN이면 Cinzel. 폴백은 GUI.skin 기본.
            bool isKR = DianoCard.Data.LocaleSettings.Current == DianoCard.Data.Language.KR;
            Font textFont = isKR ? _fontKR : _fontEN;
            FontStyle msgStyle = messageBold ? FontStyle.Bold : FontStyle.Normal;

            if (_messageStyle == null)
            {
                _messageStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                };
                ForceAllStateColors(_messageStyle, TextColorWarm);

                _messageShadowStyle = new GUIStyle(_messageStyle);
                ForceAllStateColors(_messageShadowStyle, TextShadow);

                _hintStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Italic,
                };
                ForceAllStateColors(_hintStyle, HintColor);

                _skipBtnStyle = new GUIStyle(GUI.skin.button);

                _modalTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                };
                ForceAllStateColors(_modalTitleStyle, TextColorWarm);
            }

            _messageStyle.font = textFont;
            _messageShadowStyle.font = textFont;
            _hintStyle.font = textFont;
            _skipBtnStyle.font = textFont;
            _modalTitleStyle.font = textFont;

            _messageStyle.fontStyle = msgStyle;
            _messageShadowStyle.fontStyle = msgStyle;
            _skipBtnStyle.fontStyle = msgStyle;
            _modalTitleStyle.fontStyle = msgStyle;

            _messageStyle.fontSize = msgSize;
            _messageShadowStyle.fontSize = msgSize;
            _hintStyle.fontSize = hintSize;
            _skipBtnStyle.fontSize = btnSize;
            _modalTitleStyle.fontSize = msgSize;
        }

        void OnGUI()
        {
            var mgr = TutorialManager.Instance;
            if (mgr == null || !mgr.IsActive) return;
            EnsureStyles();

            // 스킵 모달은 명시적 결정 다이얼로그라 dim + 박스 유지.
            if (mgr.SkipPromptOpen)
            {
                DrawSkipPrompt(mgr);
                return;
            }

            var step = mgr.CurrentStep;
            if (step == null) return;

            // BattleUI 캐시 — 같은 GameObject에 AutoAttach됨.
            if (_battleUI == null) _battleUI = GetComponent<BattleUI>();

            // 펄스(0..1) — Time.unscaledTime 기반이라 일시정지에도 박동 유지.
            float t = Mathf.PingPong(Time.unscaledTime * 0.9f, 1f);
            float pulse = Mathf.SmoothStep(0f, 1f, t);

            // 0) 읽기 단계(UserClick) — 게임 펄스/안내 등 잔여 효과를 가리기 위해 부드러운 dim.
            // 액션 단계(카드 사용/소환/턴 종료 등)는 dim 없음 — 게임이 잘 보여야 인터랙션 가능.
            if (step.trigger == TutorialAdvanceTrigger.UserClick && userClickDimAlpha > 0f)
            {
                DrawRect(new Rect(0, 0, Screen.width, Screen.height),
                    new Color(0f, 0f, 0f, userClickDimAlpha));
            }

            // 1) 강조 — 텍스트보다 먼저 그려야 텍스트가 위에 옴.
            DrawHighlight(step, pulse);

            // 2) 텍스트 영역 — Inspector ratio로 위치/크기 결정. 게임 요소는 절대 가리지 않도록 상단 위주.
            int boxW = Mathf.RoundToInt(Screen.width * bannerWidthRatio);
            int boxH = Mathf.RoundToInt(Screen.height * bannerHeightRatio);
            int boxX = (Screen.width - boxW) / 2;
            int boxY = Mathf.RoundToInt(Screen.height * bannerTopRatio);
            var boxRect = new Rect(boxX, boxY, boxW, boxH);

            // 텍스트 — 검은 안개 띠 없이 8방향 그림자만으로 가독성 확보. 색은 고정(펄스 보간 없음).
            string msg = step.message;
            DrawTextShadow(boxRect, msg);
            _messageStyle.normal.textColor = TextColorWarm;
            GUI.Label(boxRect, msg, _messageStyle);

            // 진행 힌트
            var hintRect = new Rect(boxRect.x, boxRect.yMax + 4, boxRect.width, hintRowHeight());
            GUI.Label(hintRect, HintFor(step.trigger), _hintStyle);

            // 3) 우상단 스킵 버튼
            int btnW = Mathf.RoundToInt(Screen.width * 0.085f);
            int btnH = Mathf.RoundToInt(Screen.height * 0.040f);
            if (GUI.Button(new Rect(Screen.width - btnW - 18, 18, btnW, btnH), L("Skip Tutorial", "튜토리얼 스킵"), _skipBtnStyle))
            {
                mgr.OpenSkipPrompt();
            }

            // 4) UserClick 트리거 — 화면 어디든 클릭하면 진행 (스킵 버튼은 위에서 먼저 처리돼 이벤트가 소비됨)
            if (step.trigger == TutorialAdvanceTrigger.UserClick)
            {
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    mgr.NextByUserClick();
                    e.Use();
                }
            }
        }

        private int hintRowHeight() => Mathf.RoundToInt(Screen.height * 0.030f);

        private void DrawHighlight(TutorialStep step, float pulse)
        {
            switch (step.highlight)
            {
                case TutorialHighlight.HandCard:
                    DrawHandCardHighlight(step.highlightCardId, pulse);
                    break;
                case TutorialHighlight.FieldArea:
                    DrawAreaHighlight(new Rect(Screen.width * 0.06f, Screen.height * 0.32f,
                                               Screen.width * 0.42f, Screen.height * 0.42f), pulse);
                    break;
                case TutorialHighlight.EnemyArea:
                    DrawAreaHighlight(new Rect(Screen.width * 0.55f, Screen.height * 0.30f,
                                               Screen.width * 0.40f, Screen.height * 0.42f), pulse);
                    break;
                case TutorialHighlight.EndTurnButton:
                    DrawAreaHighlight(new Rect(Screen.width - 220, Screen.height - 150, 200, 120), pulse);
                    break;
            }
        }

        /// <summary>BattleUI에서 카드 화면 좌표 + 회전각 받아와 회전 펄스 외곽선을 그린다.
        /// 손에 해당 카드 없거나 BattleUI 미존재면 강조 생략(텍스트만으로 안내).</summary>
        private void DrawHandCardHighlight(string cardId, float pulse)
        {
            if (_battleUI == null || string.IsNullOrEmpty(cardId)) return;
            if (!_battleUI.TryFindHandCardIndexById(cardId, out int idx)) return;
            if (!_battleUI.TryGetHandCardScreenRect(idx, out var rect, out float angleDeg)) return;

            Color baseColor = new(1.00f, 0.78f, 0.42f, Mathf.Lerp(0.65f, 1.00f, pulse));

            var pivot = new Vector2(rect.center.x, rect.center.y);
            Matrix4x4 prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDeg, pivot);

            int pad = Mathf.RoundToInt(highlightGlowPad);
            for (int i = pad; i > 0; i -= 3)
            {
                float frac = 1f - (float)i / pad;
                var c = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * frac * 0.32f);
                var glow = new Rect(rect.x - i, rect.y - i, rect.width + i * 2, rect.height + i * 2);
                DrawBorder(glow, c, 2f);
            }
            DrawBorder(rect, baseColor, highlightThickness);

            GUI.matrix = prev;
        }

        private void DrawAreaHighlight(Rect r, float pulse)
        {
            Color baseColor = new(1.00f, 0.78f, 0.42f, Mathf.Lerp(0.55f, 0.95f, pulse));

            int pad = Mathf.RoundToInt(highlightGlowPad);
            for (int i = pad; i > 0; i -= 3)
            {
                float frac = 1f - (float)i / pad;
                var c = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * frac * 0.30f);
                var glow = new Rect(r.x - i, r.y - i, r.width + i * 2, r.height + i * 2);
                DrawBorder(glow, c, 2f);
            }
            DrawBorder(r, baseColor, Mathf.Max(2f, highlightThickness - 1f));
        }

        // 텍스트 뒤에 부드러운 안개 띠를 4단 알파로 깔아 가독성 보강.
        private void DrawSoftGlow(Rect inner, Color baseColor, int padding)
        {
            for (int i = padding; i > 0; i -= 4)
            {
                float frac = 1f - (float)i / padding;
                var c = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * frac * 0.4f);
                var r = new Rect(inner.x - i, inner.y - i / 2, inner.width + i * 2, inner.height + i);
                DrawRect(r, c);
            }
            DrawRect(inner, new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a));
        }

        private void DrawTextShadow(Rect r, string text)
        {
            var offsets = new (int, int)[] { (-2, 0), (2, 0), (0, -2), (0, 2), (-1, -1), (1, 1), (-1, 1), (1, -1) };
            foreach (var (dx, dy) in offsets)
            {
                GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, _messageShadowStyle);
            }
        }

        private static string HintFor(TutorialAdvanceTrigger trigger) => trigger switch
        {
            TutorialAdvanceTrigger.UserClick => L("▶ Click the screen to continue", "▶ 화면을 클릭하여 다음으로"),
            TutorialAdvanceTrigger.CardPlayed => L("(Auto-advances when you play a card)", "(카드를 사용하면 자동으로 진행됩니다)"),
            TutorialAdvanceTrigger.SummonPlaced => L("(Auto-advances when you summon a dinosaur)", "(공룡을 필드에 소환하면 진행됩니다)"),
            TutorialAdvanceTrigger.FusionResolved => L("(Auto-advances when Fusion triggers)", "(융합을 발동하면 진행됩니다)"),
            TutorialAdvanceTrigger.TurnEnded => L("(Auto-advances when you end the turn)", "(턴을 종료하면 진행됩니다)"),
            TutorialAdvanceTrigger.BattleWon => L("(Auto-advances when you win combat)", "(전투를 끝내면 진행됩니다)"),
            _ => "",
        };

        private void DrawSkipPrompt(TutorialManager mgr)
        {
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));

            int mw = Mathf.RoundToInt(Screen.width * 0.35f);
            int mh = Mathf.RoundToInt(Screen.height * 0.28f);
            int mx = (Screen.width - mw) / 2;
            int my = (Screen.height - mh) / 2;
            var rect = new Rect(mx, my, mw, mh);
            DrawRect(rect, BoxColor);
            DrawBorder(rect, BoxBorder, 1.5f);

            GUI.Label(new Rect(mx, my + 24, mw, 40), L("Skip the tutorial?", "튜토리얼을 스킵할까요?"), _modalTitleStyle);
            GUI.Label(new Rect(mx + 24, my + 80, mw - 48, 80),
                L("Once you skip, it won't show again.\nYou can replay it from the main menu.",
                  "한 번 스킵하면 다시 표시되지 않습니다.\n메인 메뉴에서 다시 볼 수 있습니다."),
                _hintStyle);

            int bw = mw / 2 - 30;
            int bh = Mathf.RoundToInt(Screen.height * 0.052f);
            if (GUI.Button(new Rect(mx + 20, my + mh - bh - 20, bw, bh), L("Continue", "계속하기"), _skipBtnStyle))
            {
                mgr.CloseSkipPrompt();
            }
            if (GUI.Button(new Rect(mx + mw - bw - 20, my + mh - bh - 20, bw, bh), L("Skip", "스킵하기"), _skipBtnStyle))
            {
                mgr.ConfirmSkip();
            }
        }

        // GUI.skin.label 기본 hover/active 색이 normal과 다르면 마우스 올릴 때 글씨 색이 바뀐다.
        // 모든 state(normal/hover/active/focused + on* 4종)에 같은 색을 강제로 박아 변색 방지.
        private static void ForceAllStateColors(GUIStyle s, Color c)
        {
            s.normal.textColor    = c;
            s.hover.textColor     = c;
            s.active.textColor    = c;
            s.focused.textColor   = c;
            s.onNormal.textColor  = c;
            s.onHover.textColor   = c;
            s.onActive.textColor  = c;
            s.onFocused.textColor = c;
        }

        private void DrawRect(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _whiteTex);
            GUI.color = prev;
        }

        private void DrawBorder(Rect r, Color c, float thickness)
        {
            DrawRect(new Rect(r.x, r.y, r.width, thickness), c);
            DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), c);
            DrawRect(new Rect(r.x, r.y, thickness, r.height), c);
            DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), c);
        }
    }
}
