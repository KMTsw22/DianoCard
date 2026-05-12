using UnityEngine;
using static DianoCard.Data.LocaleSettings;

namespace DianoCard.Tutorial
{
    /// <summary>
    /// STS 스타일 일회성 팁 모달. 화면 중앙 작은 패널 + "알겠어요" 버튼.
    /// 한 번 본 키는 PlayerPrefs에 마크되어 다시 표시되지 않는다.
    /// 게임 다른 곳에서 TutorialTipModal.Instance.Show(...)로 호출.
    ///
    /// 표시 중에도 게임 인풋은 OnGUI 위 레이어로 차단 (입력은 모달이 흡수).
    /// PauseMenu/RelicPicker/RewardUI(DefaultExecutionOrder 1000)보다 늦게 그려 위에 깔린다.
    /// </summary>
    [DefaultExecutionOrder(1050)]
    public class TutorialTipModal : MonoBehaviour
    {
        public static TutorialTipModal Instance { get; private set; }

        // ===== Inspector 톤 조정 =====
        [Header("Modal Layout (화면 비율)")]
        [SerializeField, Range(0.20f, 0.60f)] private float modalWidthRatio = 0.36f;
        [SerializeField, Range(0.15f, 0.45f)] private float modalHeightRatio = 0.28f;

        [Header("Font Size (화면 높이 비율)")]
        [SerializeField, Range(0.020f, 0.045f)] private float titleFontRatio = 0.028f;
        [SerializeField, Range(0.014f, 0.030f)] private float bodyFontRatio = 0.020f;
        [SerializeField, Range(0.015f, 0.028f)] private float btnFontRatio = 0.020f;

        // ===== 색 톤 =====
        private static readonly Color DimColor = new(0f, 0f, 0f, 0.55f);
        private static readonly Color BoxColor = new(0.07f, 0.07f, 0.09f, 0.97f);
        private static readonly Color BoxBorder = new(0.42f, 0.34f, 0.22f, 0.85f);
        private static readonly Color TitleColor = new(1.00f, 0.78f, 0.42f);
        private static readonly Color BodyColor = new(0.93f, 0.88f, 0.78f);
        private static readonly Color TextShadow = new(0.02f, 0.02f, 0.03f, 0.90f);

        private Texture2D _whiteTex;
        private Font _fontKR;
        private Font _fontEN;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _btnStyle;

        // 현재 표시 중인 팁.
        private string _title;
        private string _body;
        private string _key;
        private bool _open;

        public bool IsOpen => _open;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _whiteTex = Texture2D.whiteTexture;
            _fontKR = Resources.Load<Font>("Fonts/Hahmlet-VariableFont_wght");
            _fontEN = Resources.Load<Font>("Fonts/Cinzel-VariableFont_wght");
        }

        /// <summary>팁 모달 표시. 이미 본 키면 무시. 다른 팁이 표시 중이면 무시.</summary>
        public void Show(string title, string body, string prefsKey)
        {
            if (_open) return;
            if (PlayerPrefs.GetInt(prefsKey, 0) == 1) return;
            _title = title;
            _body = body;
            _key = prefsKey;
            _open = true;
        }

        private void Close()
        {
            if (!string.IsNullOrEmpty(_key))
            {
                PlayerPrefs.SetInt(_key, 1);
                PlayerPrefs.Save();
            }
            _open = false;
            _key = null;
        }

        private void EnsureStyles()
        {
            int titleSize = Mathf.Max(16, Mathf.RoundToInt(Screen.height * titleFontRatio));
            int bodySize  = Mathf.Max(12, Mathf.RoundToInt(Screen.height * bodyFontRatio));
            int btnSize   = Mathf.Max(12, Mathf.RoundToInt(Screen.height * btnFontRatio));

            bool isKR = DianoCard.Data.LocaleSettings.Current == DianoCard.Data.Language.KR;
            Font textFont = isKR ? _fontKR : _fontEN;

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                };
                _titleStyle.normal.textColor = TitleColor;

                _bodyStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                    richText = true,
                };
                _bodyStyle.normal.textColor = BodyColor;

                _btnStyle = new GUIStyle(GUI.skin.button);
            }

            _titleStyle.font = textFont;
            _bodyStyle.font = textFont;
            _btnStyle.font = textFont;
            _titleStyle.fontSize = titleSize;
            _bodyStyle.fontSize = bodySize;
            _btnStyle.fontSize = btnSize;
        }

        void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();

            // 화면 dim
            DrawRect(new Rect(0, 0, Screen.width, Screen.height), DimColor);

            int mw = Mathf.RoundToInt(Screen.width * modalWidthRatio);
            int mh = Mathf.RoundToInt(Screen.height * modalHeightRatio);
            int mx = (Screen.width - mw) / 2;
            int my = (Screen.height - mh) / 2;
            var rect = new Rect(mx, my, mw, mh);

            DrawRect(rect, BoxColor);
            DrawBorder(rect, BoxBorder, 1.5f);

            int pad = Mathf.RoundToInt(mw * 0.05f);

            // 타이틀
            int titleH = Mathf.RoundToInt(_titleStyle.fontSize * 1.6f);
            var titleRect = new Rect(mx + pad, my + pad, mw - pad * 2, titleH);
            GUI.Label(titleRect, "💡 " + _title, _titleStyle);

            // 본문 — 그림자 + 본 텍스트
            int bodyY = my + pad + titleH + Mathf.RoundToInt(mh * 0.03f);
            int btnH = Mathf.RoundToInt(Screen.height * 0.06f);
            int bodyH = my + mh - pad - btnH - bodyY - 8;
            var bodyRect = new Rect(mx + pad, bodyY, mw - pad * 2, bodyH);

            var prevColor = _bodyStyle.normal.textColor;
            _bodyStyle.normal.textColor = TextShadow;
            GUI.Label(new Rect(bodyRect.x + 1, bodyRect.y + 1, bodyRect.width, bodyRect.height), _body, _bodyStyle);
            _bodyStyle.normal.textColor = prevColor;
            GUI.Label(bodyRect, _body, _bodyStyle);

            // 확인 버튼 — 화면 하단 중앙
            int btnW = Mathf.RoundToInt(mw * 0.35f);
            var btnRect = new Rect(mx + (mw - btnW) / 2, my + mh - pad - btnH, btnW, btnH);
            if (GUI.Button(btnRect, L("Got it", "알겠어요"), _btnStyle))
            {
                Close();
            }
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
