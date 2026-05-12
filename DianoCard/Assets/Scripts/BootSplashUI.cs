using UnityEngine;

/// <summary>
/// 부팅 스플래시 — Resources/Splash/LastEmber_KeyVisual 이미지를 풀스크린 cover-crop으로 깔고
/// 하단 중앙에 미니멀 진행바. 키비주얼 없으면 검은 배경으로 폴백.
///
/// [RuntimeInitializeOnLoadMethod]로 첫 씬 로드 전에 자동 생성. 씬 파일 편집 불필요.
/// 완료 시 0.25초 hold → 0.6초 페이드아웃 → 자체 파괴.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class BootSplashUI : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoBootstrap()
    {
        var go = new GameObject("BootSplashUI");
        go.AddComponent<BootSplashUI>();
        DontDestroyOnLoad(go);
    }

    private float _alpha = 1f;
    private float _fadeStart = -1f;
    private const float FadeDuration = 0.6f;
    private const float HoldAfterDone = 0.25f;

    private float _shownProgress01;
    private Texture2D _keyVisualTex;

    void Awake()
    {
        // BeforeSceneLoad보다 늦은 시점이라 Resources.Load 가능. OnGUI에서 lazy 로드도 되지만
        // 첫 프레임에 텍스처가 보장되도록 Awake에서 미리 로드.
        _keyVisualTex = Resources.Load<Texture2D>("Splash/LastEmber_KeyVisual");
        if (_keyVisualTex == null)
            Debug.LogWarning("[BootSplashUI] Missing: Resources/Splash/LastEmber_KeyVisual — falling back to black");
    }

    void OnGUI()
    {
        if (_alpha <= 0f) return;
        GUI.depth = -1000;

        _shownProgress01 = Mathf.MoveTowards(_shownProgress01, BattleUI.PreloadProgress01, Time.unscaledDeltaTime * 1.5f);

        var prevColor = GUI.color;
        var prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;

        // 배경 — 키비주얼 cover-crop (있으면) / 검은색 (폴백)
        if (_keyVisualTex != null)
        {
            // 검은 letterbox 먼저 깔아두기 (cover-crop이라 잘려나가지만 안전)
            GUI.color = new Color(0f, 0f, 0f, _alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            // cover-crop — 화면을 가득 채우되 텍스처 비율 유지, 넘치는 부분은 잘림.
            float screenAspect = (float)Screen.width / Screen.height;
            float texAspect = (float)_keyVisualTex.width / _keyVisualTex.height;
            Rect dest;
            if (screenAspect > texAspect)
            {
                float w = Screen.width;
                float h = w / texAspect;
                dest = new Rect(0, (Screen.height - h) * 0.5f, w, h);
            }
            else
            {
                float h = Screen.height;
                float w = h * texAspect;
                dest = new Rect((Screen.width - w) * 0.5f, 0, w, h);
            }
            GUI.color = new Color(1f, 1f, 1f, _alpha);
            GUI.DrawTexture(dest, _keyVisualTex, ScaleMode.StretchToFill);
        }
        else
        {
            GUI.color = new Color(0f, 0f, 0f, _alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        // 화면 맨 아래 풀너비 두꺼운 진행바 — 다크판타지 ember 톤.
        float barH = Mathf.Max(14f, Screen.height * 0.022f);
        float by = Screen.height - barH;
        float barW = Screen.width;

        // 트랙 — 차콜 베이스
        GUI.color = new Color(0.06f, 0.05f, 0.04f, _alpha * 0.92f);
        GUI.DrawTexture(new Rect(0f, by, barW, barH), Texture2D.whiteTexture);
        // 상단 inner shadow — 살짝 들어간 느낌
        GUI.color = new Color(0f, 0f, 0f, _alpha * 0.55f);
        GUI.DrawTexture(new Rect(0f, by, barW, 2f), Texture2D.whiteTexture);
        // 하단 베젤 — 어두운 1px (트랙이 위로 솟은 느낌)
        GUI.color = new Color(0f, 0f, 0f, _alpha * 0.45f);
        GUI.DrawTexture(new Rect(0f, by + barH - 1f, barW, 1f), Texture2D.whiteTexture);

        // 채움 — ember 펄스 (불꽃 깜빡거림)
        float fillW = barW * _shownProgress01;
        if (fillW > 0f)
        {
            float pulse = 0.88f + 0.12f * Mathf.Sin(Time.unscaledTime * 2.4f);

            // 본체 — 진한 ember 오렌지
            GUI.color = new Color(1f * pulse, 0.55f * pulse, 0.22f * pulse, _alpha);
            GUI.DrawTexture(new Rect(0f, by, fillW, barH), Texture2D.whiteTexture);

            // 상단 하이라이트 — 채움 위쪽 절반을 밝게 (3D 글로우)
            GUI.color = new Color(1f, 0.82f, 0.48f, _alpha * 0.55f);
            GUI.DrawTexture(new Rect(0f, by, fillW, barH * 0.45f), Texture2D.whiteTexture);

            // shimmer 스윕 — 채움 안에서 밝은 띠가 좌→우로 흐름 (3.2초 주기). 양 끝 페이드 흉내 위해 3단 박스.
            if (fillW > 60f)
            {
                const float shimmerCycle = 3.2f;
                const float shimmerW = 120f;
                float t = (Time.unscaledTime % shimmerCycle) / shimmerCycle; // 0~1
                float sx = t * (fillW + shimmerW) - shimmerW; // -120 → fillW
                float sLeft  = Mathf.Max(0f, sx);
                float sRight = Mathf.Min(fillW, sx + shimmerW);
                if (sRight > sLeft)
                {
                    float w = sRight - sLeft;
                    // 3등분 — 양쪽 얇게, 가운데 진하게 (그라데이션 흉내)
                    GUI.color = new Color(1f, 1f, 0.88f, _alpha * 0.08f);
                    GUI.DrawTexture(new Rect(sLeft,             by, w * 0.33f, barH), Texture2D.whiteTexture);
                    GUI.color = new Color(1f, 1f, 0.88f, _alpha * 0.20f);
                    GUI.DrawTexture(new Rect(sLeft + w * 0.33f, by, w * 0.34f, barH), Texture2D.whiteTexture);
                    GUI.color = new Color(1f, 1f, 0.88f, _alpha * 0.08f);
                    GUI.DrawTexture(new Rect(sLeft + w * 0.67f, by, w * 0.33f, barH), Texture2D.whiteTexture);
                }
            }

            // 선두 글로우 — 채움 끝지점에서 좌측으로 페이드되는 밝은 띠 (3단 박스로 흉내)
            if (fillW > 8f)
            {
                float glowW = Mathf.Min(40f, fillW);
                float gx = fillW - glowW;
                GUI.color = new Color(1f, 0.92f, 0.62f, _alpha * 0.10f);
                GUI.DrawTexture(new Rect(gx,               by, glowW * 0.5f, barH), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.95f, 0.72f, _alpha * 0.30f * pulse);
                GUI.DrawTexture(new Rect(gx + glowW * 0.5f, by, glowW * 0.5f, barH), Texture2D.whiteTexture);
            }
            // 선두 코어 — 4px 밝은 라인
            if (fillW > 4f)
            {
                GUI.color = new Color(1f, 0.97f, 0.80f, _alpha * pulse);
                GUI.DrawTexture(new Rect(fillW - 3f, by - 1f, 3f, barH + 2f), Texture2D.whiteTexture);
            }
        }

        // 상단 브라스 핀스트라이프 — 2px 골드 라인 (가는 + 약한 페이드)
        GUI.color = new Color(0.92f, 0.76f, 0.42f, _alpha * 0.55f);
        GUI.DrawTexture(new Rect(0f, by - 2f, barW, 1f), Texture2D.whiteTexture);
        GUI.color = new Color(0.92f, 0.76f, 0.42f, _alpha * 0.22f);
        GUI.DrawTexture(new Rect(0f, by - 3f, barW, 1f), Texture2D.whiteTexture);

        // 스플래시 떠있는 동안 뒤 로비 입력 차단
        if (Event.current.type == EventType.MouseDown
            || Event.current.type == EventType.MouseUp
            || Event.current.type == EventType.MouseDrag)
        {
            Event.current.Use();
        }

        GUI.color = prevColor;
        GUI.matrix = prevMatrix;

        // 페이드아웃 트리거
        if (!BattleUI.IsPreloading && _shownProgress01 >= 0.995f && _fadeStart < 0f)
            _fadeStart = Time.unscaledTime + HoldAfterDone;
        if (_fadeStart > 0f && Time.unscaledTime >= _fadeStart)
        {
            float t = (Time.unscaledTime - _fadeStart) / FadeDuration;
            _alpha = Mathf.Clamp01(1f - t);
            if (_alpha <= 0f) Destroy(gameObject);
        }
    }
}
