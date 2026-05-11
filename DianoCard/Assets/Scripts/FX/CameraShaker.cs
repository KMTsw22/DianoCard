using UnityEngine;

namespace DianoCard.FX
{
    /// <summary>
    /// 메인 카메라에 부착되는 셰이크 컴포넌트. Perlin noise 기반의 organic한 흔들림을
    /// 화면 비율(OrthographicSize) 기준 정규화 진폭으로 받아 해상도/카메라 설정에 독립적.
    ///
    /// 사용:
    ///   CameraShaker.Instance?.Shake(amplitude: 0.02f, duration: 0.18f);
    ///   - amplitude: 0.02 = 화면 높이의 2%. 권장 0.005 ~ 0.030.
    ///   - duration: 셰이크 지속 시간(초). 권장 0.10 ~ 0.30.
    ///
    /// 스택 안전:
    ///   연타로 셰이크가 누적돼 어지러워지지 않도록, 새 요청이 기존의 "남은 효과 강도"보다
    ///   약하면 무시한다. 더 강한 요청이 들어오면 기존을 교체.
    /// </summary>
    public class CameraShaker : MonoBehaviour
    {
        public static CameraShaker Instance { get; private set; }

        private Camera _cam;
        private Vector3 _origin;
        private bool _originCaptured;

        // 현재 진행 중인 셰이크 — world units 진폭.
        private float _worldAmplitude;
        private float _duration;
        private float _elapsed;
        private float _seedX;
        private float _seedY;

        // Perlin 샘플링 주파수. 너무 낮으면 천천히 출렁이는 멀미감, 너무 높으면 노이즈가 됨.
        // 20~26 정도가 "탕탕" 임팩트 흔들림.
        private const float NoiseFrequency = 24f;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // 흔들리던 카메라 원위치 복귀 — 컴포넌트가 제거돼도 화면이 어긋난 채 남지 않도록.
            if (_originCaptured && _cam != null) _cam.transform.localPosition = _origin;
        }

        private void EnsureCamera()
        {
            if (_cam != null) return;
            _cam = GetComponent<Camera>() ?? Camera.main;
            if (_cam != null && !_originCaptured)
            {
                _origin = _cam.transform.localPosition;
                _originCaptured = true;
            }
        }

        /// <summary>
        /// 정규화 진폭(amplitude)와 지속시간으로 셰이크 시작. 더 약한 요청은 무시(스택 방지).
        /// </summary>
        public void Shake(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f) return;
            EnsureCamera();
            if (_cam == null) return;

            // 정규화 진폭 → world units. OrthographicSize는 카메라 viewport 절반 높이(world units).
            // 원근 카메라일 경우 fallback으로 5를 가정.
            float halfHeight = _cam.orthographic ? _cam.orthographicSize : 5f;
            float requestedWorldAmp = amplitude * halfHeight * 2f;

            // 이미 진행 중인 셰이크가 더 강하다면 무시 — 연타 누적으로 어지러워지지 않도록.
            float remaining = Mathf.Max(0f, _duration - _elapsed);
            float currentEffectiveAmp = _duration > 0f
                ? _worldAmplitude * (remaining / _duration)
                : 0f;
            if (requestedWorldAmp < currentEffectiveAmp) return;

            _worldAmplitude = requestedWorldAmp;
            _duration = duration;
            _elapsed = 0f;
            _seedX = Random.value * 100f;
            _seedY = Random.value * 100f;
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            if (_duration <= 0f) return;

            if (_elapsed >= _duration)
            {
                _cam.transform.localPosition = _origin;
                _duration = 0f;
                _worldAmplitude = 0f;
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            // 끝부분에서 부드럽게 잦아들도록 easeOut(quad).
            float falloff = (1f - t) * (1f - t);

            // Perlin은 [0,1] → [-1,1]로 변환. X/Y 각각 다른 seed 사용해 직교적인 흔들림.
            float nx = (Mathf.PerlinNoise(_seedX + _elapsed * NoiseFrequency, 0f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(0f, _seedY + _elapsed * NoiseFrequency) - 0.5f) * 2f;

            Vector3 offset = new Vector3(nx, ny, 0f) * (_worldAmplitude * falloff);
            _cam.transform.localPosition = _origin + offset;
        }
    }
}
