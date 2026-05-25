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
        [SerializeField, Range(0f, 0.5f), Tooltip("화면 상단에서 텍스트 박스 시작까지 거리 (Screen.height 비율) — 상단 바와 안 겹치도록 0.12 정도가 적당, 캐릭터 머리(0.30~)와도 안 겹침")]
        private float bannerTopRatio = 0.12f;

        [SerializeField, Range(0.30f, 1.00f), Tooltip("텍스트 박스 폭 (Screen.width 비율) — 양옆 좀 더 좁혀 캐릭터 얼굴이 잘 보이게 0.56 정도")]
        private float bannerWidthRatio = 0.56f;

        [SerializeField, Range(0.05f, 0.30f), Tooltip("텍스트 박스 높이 (Screen.height 비율) — 빌드 폰트 메트릭이 에디터보다 살짝 넓어 wrap 3줄까지 가는 경우 대비")]
        private float bannerHeightRatio = 0.20f;

        [Header("Font Size (화면 높이 비율, 0~0.1)")]
        [SerializeField, Range(0.020f, 0.080f), Tooltip("메시지 본문 폰트 사이즈 (Screen.height 비율) — 빌드 측 폭이 더 잡히는 경우 wrap 방지용 추가 마진")]
        private float messageFontRatio = 0.036f;

        [SerializeField, Range(0.012f, 0.040f), Tooltip("진행 힌트 폰트 사이즈 (Screen.height 비율)")]
        private float hintFontRatio = 0.024f;

        [SerializeField, Range(0.012f, 0.025f), Tooltip("스킵 버튼 폰트 사이즈 (Screen.height 비율)")]
        private float skipBtnFontRatio = 0.018f;

        [Header("Text Backplate (가독성 보조)")]
        [SerializeField, Range(0f, 0.95f),
         Tooltip("안내 텍스트 뒤 반투명 회색 — 배경 일러스트 위에서도 글자 가독성을 확보하되, 너무 까맣지 않게 뒷배경이 비치도록.")]
        private float backplateAlpha = 0.38f;

        [SerializeField, Range(0f, 120f),
         Tooltip("백플레이트 가장자리 페이드 폭(px) — 폭이 클수록 박스 가장자리가 더 자연스럽게 사라진다.")]
        private float backplateFadePx = 64f;

        // 백플레이트 색조 — 순흑 대신 다크 워뮤그레이(보랏빛 살짝)로 바꿔서 게임 톤과 어울리게.
        // 알파는 위 backplateAlpha로 조절. 색을 검정에서 회색으로 옮기면 같은 알파에서도 덜 무겁게 보임.
        private static readonly Color BackplateTint = new(0.18f, 0.16f, 0.22f);

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

        [Header("Non-highlight Card Dim (HandCard 액션 단계)")]
        [SerializeField, Range(0f, 0.8f),
         Tooltip("HandCard 강조 단계에서 강조 대상이 아닌 손패 카드 위에 어둡게 깔 알파 — 안내된 카드와 시각 분리.")]
        private float nonHighlightCardDimAlpha = 0.45f;

        [Header("Skip Button Gate")]
        [SerializeField, Range(0, 4),
         Tooltip("이 단계까지는 Skip 버튼을 숨긴다(첫 안내를 못 보고 스킵 누르는 사고 방지).")]
        private int hideSkipUntilStepIndex = 1;

        [Header("Blocked-action Text Pulse")]
        [SerializeField, Range(0.10f, 1.00f),
         Tooltip("잘못된 액션 차단 시 안내 텍스트가 잠깐 커졌다가 돌아오는 펄스 지속 시간(초).")]
        private float blockedPulseDuration = 0.32f;
        [SerializeField, Range(1.00f, 1.50f),
         Tooltip("펄스 최고 시점에서의 텍스트 스케일 배율(1.0 = 변화 없음).")]
        private float blockedPulsePeakScale = 1.18f;

        // 차단 펄스 — TutorialEvents.OnTutorialActionBlocked 받을 때마다 갱신되는 시작 시각.
        // OnGUI에서 (now - start) / duration 으로 0..1 진행도 계산, sin 곡선 한 번 그려 스케일 적용.
        private float _blockedPulseStartTime = -1f;

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

        void OnEnable()
        {
            TutorialEvents.OnTutorialActionBlocked += HandleBlocked;
        }

        void OnDisable()
        {
            TutorialEvents.OnTutorialActionBlocked -= HandleBlocked;
        }

        // 차단 신호 받으면 펄스 타이머 리셋. 짧은 시간 내 연속 차단도 매번 새로 펄스.
        private void HandleBlocked() => _blockedPulseStartTime = Time.unscaledTime;

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
            // ESC 메뉴 열려있으면 튜토리얼 오버레이는 양보. 안 그러면 UserClick 단계의
            // 화면 전체 MouseDown catcher가 Resume/Abandon 클릭을 가로챈다.
            if (PauseMenuUI.IsOpen) return;
            var mgr = TutorialManager.Instance;
            if (mgr == null || !mgr.IsActive) return;
            EnsureStyles();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 스킵 모달은 명시적 결정 다이얼로그라 dim + 박스 유지.
            if (mgr.SkipPromptOpen)
            {
                DrawSkipPrompt(mgr);
                return;
            }
#endif

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

            // 1) 비-강조 손패 카드 dim — HandCard 강조 단계에서 다른 카드들 위에 어둡게 깔아 안내 카드와 시각 분리.
            if (step.highlight == TutorialHighlight.HandCard
                && !string.IsNullOrEmpty(step.highlightCardId)
                && nonHighlightCardDimAlpha > 0f
                && _battleUI != null)
            {
                DrawNonHighlightHandDim(step.highlightCardId);
            }

            // 1.5) 강조 — 텍스트보다 먼저 그려야 텍스트가 위에 옴.
            DrawHighlight(step, pulse);

            // 2) 텍스트 영역 — Inspector ratio로 위치/크기 결정. 게임 요소는 절대 가리지 않도록 상단 위주.
            int boxW = Mathf.RoundToInt(Screen.width * bannerWidthRatio);
            int boxH = Mathf.RoundToInt(Screen.height * bannerHeightRatio);
            int boxX = (Screen.width - boxW) / 2;
            int boxY = Mathf.RoundToInt(Screen.height * bannerTopRatio);
            var boxRect = new Rect(boxX, boxY, boxW, boxH);

            // 차단 펄스 스케일 — 잘못된 액션 직후 짧게 텍스트가 커졌다가 돌아온다.
            // 사용자 시선을 안내 문구로 다시 끌어오는 시각 신호.
            float blockedScale = ComputeBlockedPulseScale();
            Matrix4x4 prevMat = GUI.matrix;
            if (blockedScale != 1f)
            {
                GUIUtility.ScaleAroundPivot(new Vector2(blockedScale, blockedScale), boxRect.center);
            }

            // 진행 힌트 영역도 백플레이트가 같이 덮어 텍스트 두 줄을 한 묶음으로 읽히게 함.
            var hintRect = new Rect(boxRect.x, boxRect.yMax + 4, boxRect.width, hintRowHeight());

            // 2a) 백플레이트 — 자주색 하늘 위에서 호박 글자가 묻히는 걸 막는 부드러운 검정 스크림.
            // 가장자리는 fade로 자연스럽게 사라져 직사각형 박스 느낌 안 남.
            if (backplateAlpha > 0f)
            {
                var plateRect = new Rect(boxRect.x - 12f, boxRect.y - 6f,
                                          boxRect.width + 24f,
                                          (hintRect.yMax - boxRect.y) + 12f);
                DrawSoftBackplate(plateRect, new Color(BackplateTint.r, BackplateTint.g, BackplateTint.b, backplateAlpha), backplateFadePx);
            }

            // 2b) 텍스트 — 8방향 그림자 + 본체. 색은 고정(펄스 보간 없음).
            string msg = step.message;
            DrawTextShadow(boxRect, msg);
            _messageStyle.normal.textColor = TextColorWarm;
            GUI.Label(boxRect, msg, _messageStyle);

            // 진행 힌트
            GUI.Label(hintRect, HintFor(step.trigger), _hintStyle);

            if (blockedScale != 1f) GUI.matrix = prevMat;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 3) 우상단 스킵 버튼 — 초반 hideSkipUntilStepIndex 단계까지는 숨김(첫 안내 못 보고 누르는 사고 방지).
            // 출시 빌드에서는 컴파일에서 제거됨 — 튜토리얼은 끝까지 봐야 함.
            if (mgr.StepIndex > hideSkipUntilStepIndex)
            {
                int btnW = Mathf.RoundToInt(Screen.width * 0.085f);
                int btnH = Mathf.RoundToInt(Screen.height * 0.040f);
                if (GUI.Button(new Rect(Screen.width - btnW - 18, 18, btnW, btnH), L("Skip Tutorial", "튜토리얼 스킵"), _skipBtnStyle))
                {
                    mgr.OpenSkipPrompt();
                }
            }
#endif

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

            // 5) 마지막 — BattleUI의 hover 툴팁을 한 번 더 그려 메시지 박스 위로 올림.
            // (BattleUI도 자기 OnGUI에서 한 번 그렸지만 우리 OnGUI(execOrder 1100)가 그 위에 덮어 가린다.)
            if (_battleUI != null) _battleUI.DrawHoverTooltipTopmost();
        }

        private int hintRowHeight() => Mathf.RoundToInt(Screen.height * 0.030f);

        // 차단 펄스 스케일 — sin 곡선 한 번 (0 → peak → 0). 진행도 t∈[0,1] 동안만 활성, 끝나면 1.0 반환.
        // OutBack/easing 대신 단순 sin이 "통통 튀는" 느낌 없이 깔끔한 호흡.
        private float ComputeBlockedPulseScale()
        {
            if (_blockedPulseStartTime < 0f || blockedPulseDuration <= 0f) return 1f;
            float t = (Time.unscaledTime - _blockedPulseStartTime) / blockedPulseDuration;
            if (t < 0f || t >= 1f) return 1f;
            float bump = Mathf.Sin(t * Mathf.PI); // 0 → 1 → 0
            return 1f + (blockedPulsePeakScale - 1f) * bump;
        }

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
                case TutorialHighlight.PotionIcon:
                    {
                        // 드로어가 열려있으면 (TryGetPotionDrawerItemRect가 채워졌으면) 글로우를 첫 마실 포션으로 이전.
                        // 닫혀있으면 상단 바 아이콘 강조.
                        Rect target;
                        if (_battleUI != null && _battleUI.TryGetPotionDrawerItemRect(out var dr))
                        {
                            target = VirtualToScreenRect(dr, 6f);
                        }
                        else if (_battleUI != null && _battleUI.TryGetTopBarPotionRect(out var pr))
                        {
                            target = VirtualToScreenRect(pr, 6f);
                        }
                        else
                        {
                            target = new Rect(Screen.width * 0.18f, 4f, 110f, 56f);
                        }
                        DrawWarmHalo(target, pulse, 60f, 0.50f);
                        DrawAreaHighlight(target, pulse);
                    }
                    break;
                case TutorialHighlight.RelicIcon:
                    {
                        Rect target = (_battleUI != null && _battleUI.TryGetTopBarRelicRect(out var rr))
                            ? VirtualToScreenRect(rr, 6f)
                            : new Rect(Screen.width * 0.26f, 4f, 100f, 56f);
                        DrawWarmHalo(target, pulse, 60f, 0.50f);
                        DrawAreaHighlight(target, pulse);
                    }
                    break;
                case TutorialHighlight.ManaOrb:
                    {
                        Rect target = (_battleUI != null && _battleUI.TryGetManaOrbRect(out var mr))
                            ? VirtualToScreenRect(mr, 10f)
                            : new Rect(Screen.width * 0.08f, Screen.height - 180f, 130f, 130f);
                        DrawWarmHalo(target, pulse, 70f, 0.50f);
                        DrawAreaHighlight(target, pulse);
                    }
                    break;
                case TutorialHighlight.SwordBadge:
                    {
                        // 공룡 머리 위 검 뱃지 — 가상 좌표라 변환. 폴백은 화면 중하단 임의 위치(에러 케이스).
                        if (_battleUI != null && _battleUI.TryGetSwordBadgeRect(out var sb))
                        {
                            var target = VirtualToScreenRect(sb, 4f);
                            DrawWarmHalo(target, pulse, 40f, 0.55f);
                            DrawAreaHighlight(target, pulse);
                        }
                    }
                    break;
                case TutorialHighlight.SkillBadge:
                    {
                        if (_battleUI != null && _battleUI.TryGetSkillBadgeRect(out var sk))
                        {
                            var target = VirtualToScreenRect(sk, 4f);
                            DrawWarmHalo(target, pulse, 40f, 0.55f);
                            DrawAreaHighlight(target, pulse);
                        }
                    }
                    break;
            }
        }

        // BattleUI는 가상 1280×720 좌표계로 상단 바 rect를 저장한다 (AspectScaler가 uniform 스케일 + 좌상단 anchored).
        // 오버레이 OnGUI는 identity matrix라 스크린 픽셀 좌표로 변환 필요.
        private Rect VirtualToScreenRect(Rect virtRect, float padPx)
        {
            float scale = DianoCard.UI.AspectScaler.Scale;
            return new Rect(virtRect.x * scale - padPx,
                            virtRect.y * scale - padPx,
                            virtRect.width  * scale + padPx * 2f,
                            virtRect.height * scale + padPx * 2f);
        }

        /// <summary>강조되지 않은 손패 카드들 위에 반투명 검정을 깔아 시각적으로 비활성처럼 보이게.
        /// 강조 카드는 펄스 외곽선으로 분리되어 자연스럽게 부각된다.</summary>
        private void DrawNonHighlightHandDim(string highlightCardId)
        {
            // BattleUI는 손패 카드 수만큼 인덱스를 갖는다. 강조 카드 인덱스 외 모두 dim.
            if (!_battleUI.TryFindHandCardIndexById(highlightCardId, out int hlIdx)) return;
            for (int i = 0; i < 12; i++) // 손패 최대치 여유 — 없으면 TryGetHandCardScreenRect가 false
            {
                if (i == hlIdx) continue;
                if (!_battleUI.TryGetHandCardScreenRect(i, out var rect, out float angleDeg)) continue;

                var pivot = new Vector2(rect.center.x, rect.center.y);
                Matrix4x4 prev = GUI.matrix;
                GUIUtility.RotateAroundPivot(angleDeg, pivot);
                DrawRect(rect, new Color(0f, 0f, 0f, nonHighlightCardDimAlpha));
                GUI.matrix = prev;
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

        /// <summary>작은 상단 아이콘(포션/유물/마나 오브)용 추가 halo — 외곽선 글로우만으론
        /// 작은 아이콘이 배경에 묻혀 잘 안 보이는 문제 해결용. 채워진 따뜻한 광원을 펄스로 깐 뒤
        /// 위에 DrawAreaHighlight의 외곽선이 겹쳐져 시선이 강하게 끌린다.
        /// **아이콘 본체 영역(r)은 절대 가리지 않는다** — r의 가장자리부터 바깥쪽으로만 도넛 모양으로 그린다.</summary>
        private void DrawWarmHalo(Rect r, float pulse, float padPx, float maxAlpha)
        {
            Color baseColor = new(1.00f, 0.78f, 0.42f, Mathf.Lerp(0.18f, maxAlpha, pulse));
            int pad = Mathf.RoundToInt(padPx);
            for (int i = pad; i >= 3; i -= 3)
            {
                float frac = 1f - (float)i / pad;   // 바깥(0) → 안쪽(1) — 가장자리에서 진해진다
                var c = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * frac);
                Rect outer = new Rect(r.x - i, r.y - i, r.width + i * 2, r.height + i * 2);
                // 도넛 4분할 — top / bottom / left / right 띠. inner(r) 영역은 비운다.
                DrawRect(new Rect(outer.x, outer.y, outer.width, r.y - outer.y), c);                            // top
                DrawRect(new Rect(outer.x, r.yMax, outer.width, outer.yMax - r.yMax), c);                       // bottom
                DrawRect(new Rect(outer.x, r.y, r.x - outer.x, r.height), c);                                   // left
                DrawRect(new Rect(r.xMax, r.y, outer.xMax - r.xMax, r.height), c);                              // right
            }
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

        /// <summary>안내 텍스트 뒤 부드러운 다크 스크림. 가장자리에서 안쪽으로 갈수록 알파가 누적돼
        /// 가운데는 baseColor 그대로, 바깥은 fade로 사라진다(직사각형 박스 느낌 제거).
        /// DrawSoftGlow와 같은 패턴: 큰 rect(낮은 알파) → 작은 rect(점점 높은 알파) → 본체 단색.</summary>
        private void DrawSoftBackplate(Rect inner, Color baseColor, float fadePx)
        {
            int pad = Mathf.RoundToInt(fadePx);
            for (int i = pad; i > 0; i -= 4)
            {
                float frac = 1f - (float)i / pad; // 바깥 0 → 안쪽 1
                var c = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * frac * 0.45f);
                var r = new Rect(inner.x - i, inner.y - i, inner.width + i * 2, inner.height + i * 2);
                DrawRect(r, c);
            }
            DrawRect(inner, baseColor);
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
            // 메시지 폰트 크기에 비례한 그림자 오프셋. 큰 폰트일수록 더 멀리 펴 가독성 확보.
            int s = Mathf.Max(2, Mathf.RoundToInt(_messageStyle.fontSize * 0.10f));
            var offsets = new (int, int)[]
            {
                (-s, 0), (s, 0), (0, -s), (0, s),
                (-s, -s), (s, s), (-s, s), (s, -s),
            };
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
            TutorialAdvanceTrigger.PotionUsed => L("(Auto-advances when you drink a potion)", "(포션을 마시면 진행됩니다)"),
            TutorialAdvanceTrigger.SummonAttacked => L("(Auto-advances when your dino attacks)", "(공룡으로 공격하면 진행됩니다)"),
            TutorialAdvanceTrigger.SkillUsed => L("(Auto-advances when the skill fires)", "(스킬을 발동하면 진행됩니다)"),
            TutorialAdvanceTrigger.RelicHovered => L("(Hover the relic icon to continue)", "(유물 아이콘 위에 마우스를 올리면 진행됩니다)"),
            TutorialAdvanceTrigger.ManaHovered => L("(Hover the mana orb to continue)", "(마나 오브 위에 마우스를 올리면 진행됩니다)"),
            _ => "",
        };

#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD

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
