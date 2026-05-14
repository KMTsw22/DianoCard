using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DianoCard.UI
{
    /// <summary>
    /// 모든 화면(Lobby/Map/Battle/Reward/Shop/Event 등)에서 표시되는
    /// 다크판타지 톤 마우스 커서.
    ///
    /// Cursor.SetCursor + CursorMode.Auto — OS hardware cursor 경로. 텍스처를 64×64로
    /// 다운샘플해서 Windows OS가 직접 그리도록 함. 게임 메인 스레드가 freeze해도 마우스는
    /// OS가 별도로 그리니까 매끄럽고 클릭 hit-test도 OS 레벨이라 응답 즉시.
    ///
    /// - 우선 Resources/UI/Cursors/cursor_default(.pressed).png를 시도,
    ///   없으면 절차적으로 48×48 에이지드 브라스 화살촉 생성.
    /// - 좌/우 클릭 시 pressed 텍스처로 한 번 SetCursor를 호출해 살짝 톤다운.
    /// - 화면 포커스 복귀 시 다시 적용해 OS 디폴트로 복귀하는 케이스 방지.
    /// GameStateManager.AutoAttachUI에서 컴포넌트 부착.
    /// </summary>
    public class CursorManager : MonoBehaviour
    {
        // 색상 — 흰색 본체 + 검은 외곽선 (Windows 표준 화살촉 톤).
        private static readonly Color32 InkOutline = new Color32(0, 0, 0, 255);          // 검은 외곽선
        private static readonly Color32 WhiteBody  = new Color32(255, 255, 255, 255);    // 흰색 본체
        private static readonly Color32 GreyShade  = new Color32(180, 180, 180, 255);    // 그림자 톤
        private static readonly Color32 BrassDark  = new Color32(70, 70, 70, 255);       // pressed용 회색

        // 텍스처 캔버스 크기 (절차 생성 시). 외부 PNG 사용 시엔 무시됨.
        // 48 ≈ OS 기본의 1.5배. 더 키우려면 64~80, 줄이려면 32~40. polygon도 같이 조정.
        private const int TexSize = 48;

        // 클릭 인식 좌표(텍스처 픽셀 기준). 외부 PNG 사용 시 PNG 디자인에 맞춰 조정.
        // 표준 좌상단 화살촉이면 (0,0) 또는 (1,1). 십자선/링 모양이면 중앙.
        [SerializeField] private Vector2 hotspot = new Vector2(1f, 1f);

        // 화살촉 polygon — Windows 표준 화살촉 비율을 48×48 캔버스로 스케일.
        // 좌상단(1,1) hotspot, 위로 향한 ↖ 모양. 좌측 거의 수직, 우상으로 가는 사선 측면.
        private static readonly Vector2[] ArrowPoly = new Vector2[]
        {
            new Vector2(1f, 1f),     // tip (좌상단 뾰족)
            new Vector2(1f, 36f),    // 좌측 수직 끝
            new Vector2(10f, 29f),   // 안쪽 굴절 (꼬리 시작)
            new Vector2(17f, 43f),   // 꼬리 좌 끝
            new Vector2(22f, 40f),   // 꼬리 우 끝
            new Vector2(16f, 26f),   // 안쪽 굴절 (위로)
            new Vector2(27f, 14f),   // 우측 사선 끝
        };

        private Texture2D _defaultTex;
        private Texture2D _pressedTex;
        private bool _wasDown;

        void Awake()
        {
            // 최우선: OS 커서가 항상 보이게 강제. 어떤 이유로 우리 SetCursor가 실패해도
            // 적어도 OS 디폴트 화살표는 떠야 한다. 이전 세션이 false로 남겼을 가능성 차단.
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

#if UNITY_STANDALONE_OSX
            // Mac은 시스템 기본 화살표가 디스플레이 DPI에 맞춰 정확하게 렌더링됨.
            // 우리 커스텀 텍스처는 Retina 환경에서 logical pixel scaling 때문에 크게 보임.
            // → Mac은 OS 디폴트 사용. 게임 freeze 시에도 OS가 그리므로 안전.
            Debug.Log("[CursorManager] Mac 환경 — OS 기본 커서 사용");
            return;
#endif

            Debug.Log($"[CursorManager] Awake 시작 — Screen={Screen.width}x{Screen.height}, fullscreen={Screen.fullScreen}");

            // OS 하드웨어 커서로 동작시키려면 Windows의 64×64 cap을 넘으면 안 됨 — 넘으면 Unity가
            // 알아서 software로 폴백하고, 그러면 게임 freeze 때 마우스가 같이 멈춰버린다.
            // 64×64로 다운샘플하면 OS가 커서를 그려서 부팅 freeze 동안에도 마우스는 매끄럽게 움직임.
            _defaultTex = Resources.Load<Texture2D>("UI/Cursors/cursor_default");
            float defaultScale = 1f;
            if (_defaultTex == null) _defaultTex = BuildArrow(pressed: false);
            // 모든 경로(외부 PNG/절차 생성) 동일하게 HardwareCursorMaxSize로 cap.
            // Mac은 32, Windows/Linux는 64. Retina 환경에서 logical pixel 기준 시각 크기 통일.
            {
                var src = _defaultTex;
                _defaultTex = DownsampleToHardwareSize(src, out defaultScale);
            }

            _pressedTex = Resources.Load<Texture2D>("UI/Cursors/cursor_pressed");
            if (_pressedTex == null) _pressedTex = BuildArrow(pressed: true);
            _pressedTex = DownsampleToHardwareSize(_pressedTex, out _);

            // hotspot도 같은 비율로 — 픽셀 좌표라 같이 줄여야 tip 위치 유지.
            if (defaultScale < 1f) hotspot = new Vector2(hotspot.x * defaultScale, hotspot.y * defaultScale);

            if (_defaultTex == null)
            {
                Debug.LogError("[CursorManager] 커서 텍스처 빌드 실패 — OS 디폴트 유지.");
                enabled = false;
                return;
            }

            _defaultTex.filterMode = FilterMode.Point;
            _defaultTex.wrapMode = TextureWrapMode.Clamp;
            if (_pressedTex != null)
            {
                _pressedTex.filterMode = FilterMode.Point;
                _pressedTex.wrapMode = TextureWrapMode.Clamp;
            }

            // 텍스처 진단 — 픽셀이 실제로 칠해졌는지 확인.
            int opaque = CountOpaquePixels(_defaultTex);
            Debug.Log($"[CursorManager] 텍스처 OK — {_defaultTex.width}x{_defaultTex.height}, format={_defaultTex.format}, 불투명 픽셀={opaque}/{_defaultTex.width * _defaultTex.height}");

            if (opaque == 0)
            {
                Debug.LogError("[CursorManager] 텍스처가 비어있음 (전부 투명) — OS 디폴트 유지.");
                enabled = false;
                return;
            }

            // Auto 모드 — Windows에서 64×64 이하면 OS 하드웨어 커서로 그려진다. 게임이 freeze해도
            // 마우스는 OS가 별도로 그리니까 매끄럽게 움직이고 클릭도 OS 레벨에서 처리됨.
            ApplyCursor(pressed: false);
            Debug.Log($"[CursorManager] SetCursor 호출 완료 (Auto, {_defaultTex.width}x{_defaultTex.height}).");
        }

        // OS hardware cursor 호환 최대 사이즈. Windows 10/11에서 64×64까지 OS가 그려줌.
        // Mac Retina는 logical pixel 기준이라 64는 device 128px = 시각적으로 2배 큼.
        // → Mac은 32로 cap. Linux/Windows는 64 유지.
#if UNITY_STANDALONE_OSX
        private const int HardwareCursorMaxSize = 24;  // Mac Retina용 — 작게
#else
        private const int HardwareCursorMaxSize = 64;
#endif

        /// <summary>GPU Blit으로 64x64 이하 텍스처 새로 생성. isReadable 설정 무관하게 동작 — Resources의 sprite는 보통 isReadable=0이라 GetPixels32가 안 된다. scale은 원본 대비 축소 비율 (1f 이하).</summary>
        private static Texture2D DownsampleToHardwareSize(Texture2D src, out float scale)
        {
            int maxDim = Mathf.Max(src.width, src.height);
            scale = maxDim > HardwareCursorMaxSize ? (float)HardwareCursorMaxSize / maxDim : 1f;
            int w = Mathf.Max(1, Mathf.RoundToInt(src.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(src.height * scale));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            dst.name = src.name + "_hw";
            return dst;
        }

        private static int CountOpaquePixels(Texture2D t)
        {
            try
            {
                var px = t.GetPixels32();
                int n = 0;
                for (int i = 0; i < px.Length; i++) if (px[i].a > 0) n++;
                return n;
            }
            catch { return -1; }
        }

        void OnDestroy()
        {
            // 컴포넌트가 사라지면 OS 디폴트로 복귀.
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        void Update()
        {
            bool down = IsMouseDown();
            if (down != _wasDown)
            {
                ApplyCursor(down);
                _wasDown = down;
            }
        }

        void OnApplicationFocus(bool focused)
        {
            // 알트탭으로 돌아왔을 때 일부 환경에서 OS 디폴트로 리셋되는 케이스 방어.
            if (focused) ApplyCursor(_wasDown);
        }

        private void ApplyCursor(bool pressed)
        {
            var tex = (pressed && _pressedTex != null) ? _pressedTex : _defaultTex;
            // Auto: 텍스처가 OS hardware cursor 호환 사이즈(Windows 64×64 이하)면 OS가 그림.
            // 게임이 freeze해도 마우스는 OS가 매끄럽게 그리고 클릭 hit-test도 OS 레벨이라 응답 즉시.
            Cursor.SetCursor(tex, hotspot, CursorMode.Auto);
        }

        private static bool IsMouseDown()
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return m.leftButton.isPressed || m.rightButton.isPressed;
#else
            return Input.GetMouseButton(0) || Input.GetMouseButton(1);
#endif
        }

        // ===== 절차적 브라스 화살촉 =====

        /// <summary>표준 흰색 마우스 화살촉을 128×128 캔버스에 그린다. pressed=true면 회색 톤.</summary>
        private static Texture2D BuildArrow(bool pressed)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[TexSize * TexSize];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 0);

            // pressed면 본체를 회색으로.
            Color32 body = pressed ? GreyShade : WhiteBody;

            // 1. polygon 채우기 — 본체.
            FillPolygon(pixels, TexSize, ArrowPoly, body);

            // 2. 외곽선 — 두께 2px 검은 라인. (작아도 잘 보이도록 두 번 그리기)
            DrawPolygonOutline(pixels, TexSize, ArrowPoly, InkOutline);
            DrawPolygonOutlineThick(pixels, TexSize, ArrowPoly, InkOutline);

            // 3. tip 픽셀 일대를 검은색으로 강조 — hotspot 정확도.
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (x + y < 5) pixels[y * TexSize + x] = InkOutline;

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            tex.name = pressed ? "CursorManager.WhiteArrow.Pressed" : "CursorManager.WhiteArrow";
            return tex;
        }

        /// <summary>외곽선을 1px 두껍게 — 각 edge를 1px씩 inset해서 한 번 더 그린다.</summary>
        private static void DrawPolygonOutlineThick(Color32[] pixels, int size, Vector2[] poly, Color32 c)
        {
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Length];
                // 같은 방향으로 1px씩 offset된 라인 두 개 더 그려서 두께 효과.
                DrawLine(pixels, size, (int)a.x + 1, (int)a.y, (int)b.x + 1, (int)b.y, c);
                DrawLine(pixels, size, (int)a.x, (int)a.y + 1, (int)b.x, (int)b.y + 1, c);
            }
        }

        private static void FillPolygon(Color32[] pixels, int size, Vector2[] poly, Color32 fill)
        {
            int n = poly.Length;
            float ymin = poly[0].y, ymax = poly[0].y;
            for (int i = 1; i < n; i++) { if (poly[i].y < ymin) ymin = poly[i].y; if (poly[i].y > ymax) ymax = poly[i].y; }

            int yStart = Mathf.Max(0, Mathf.FloorToInt(ymin));
            int yEnd = Mathf.Min(size - 1, Mathf.CeilToInt(ymax));

            var xs = new System.Collections.Generic.List<float>(n);
            for (int y = yStart; y <= yEnd; y++)
            {
                xs.Clear();
                float yf = y + 0.5f;
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[(i + 1) % n];
                    bool aAbove = a.y > yf;
                    bool bAbove = b.y > yf;
                    if (aAbove == bAbove) continue;
                    float t = (yf - a.y) / (b.y - a.y);
                    float x = a.x + t * (b.x - a.x);
                    xs.Add(x);
                }
                xs.Sort();
                for (int p = 0; p + 1 < xs.Count; p += 2)
                {
                    int xStart = Mathf.Max(0, Mathf.FloorToInt(xs[p]));
                    int xEnd = Mathf.Min(size - 1, Mathf.CeilToInt(xs[p + 1]));
                    for (int x = xStart; x <= xEnd; x++)
                    {
                        pixels[y * size + x] = fill;
                    }
                }
            }
        }

        private static void DrawPolygonOutline(Color32[] pixels, int size, Vector2[] poly, Color32 c)
        {
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Length];
                DrawLine(pixels, size, (int)a.x, (int)a.y, (int)b.x, (int)b.y, c);
            }
        }

        private static void DrawLine(Color32[] pixels, int size, int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (x0 >= 0 && y0 >= 0 && x0 < size && y0 < size)
                    pixels[y0 * size + x0] = c;
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
