using System.Collections;
using UnityEngine;

namespace DianoCard.Battle
{
    /// <summary>
    /// World-space animated view for a battle entity (player, enemy, summon).
    /// SpriteRenderer + coroutine tweens. Idle bob, 4-frame attack sequence, hit shake/flash.
    /// Position is driven externally via <see cref="SetBasePosition"/>, animations offset from that base.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class BattleEntityView : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private Vector3 _basePosition;
        private Coroutine _currentAnim;
        private Color _baseColor = Color.white;
        // 월드 단위 고정 대신 캐릭터 높이에 비례해서 둥실둥실 느낌이 카메라 스케일과 무관하게 유지되도록.
        private readonly float _idleBobFraction = 0.028f;
        private readonly float _idleBobFreq = 1.6f;
        private float _idleBobPhase;

        // 공격 시퀀스 프레임들 (모두 선택적 — 없으면 해당 단계 스킵)
        private Sprite _idleSprite;
        private Sprite _windupSprite;
        private Sprite _strikeSprite;
        private Sprite _strikeExtendedSprite;
        private Sprite _summonCastSprite;

        // 스케일 기준이 되는 스프라이트 높이 (바뀌지 않는 고정값). 프레임 스왑 중에도 이 값으로 scale 계산.
        private float _scaleReferenceHeight = -1f;

        // 현재 애니메이션 페이즈의 스케일 배율 (StrikeExtended에서 캐릭터 비율 보정용). 기본 1.0
        private float _activeScaleMultiplier = 1f;
        // StrikeExtended 스프라이트 표시 중 적용할 크기 부스트 (프레임 내 캐릭터가 작게 그려진 경우 보정). 기본 1.0
        private float _strikeExtendedScaleBoost = 1.2f;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _sr.sortingOrder = 50;
            // 개체별 위상 오프셋 — 여러 적/플레이어가 같은 박자로 움직여 기계적으로 보이지 않게.
            _idleBobPhase = Random.value * Mathf.PI * 2f;
        }

        public void SetSprite(Sprite s)
        {
            _sr.sprite = s;
            if (_idleSprite == null) _idleSprite = s;
            if (_scaleReferenceHeight <= 0f && s != null && s.bounds.size.y > 0f)
                _scaleReferenceHeight = s.bounds.size.y;
        }

        /// <summary>
        /// 베이스 틴트 색상 변경. hit flash 코루틴이 종료 시 이 색으로 복구됨.
        /// Reward 화면 등에서 뒷배경 통일 dimming 용도.
        /// </summary>
        public void SetBaseColor(Color c)
        {
            _baseColor = c;
            if (_sr != null) _sr.color = c;
        }

        /// <summary>공격용 4프레임 세트 지정. 일부만 지정해도 됨 (null은 해당 단계 스킵).</summary>
        public void SetAttackFrames(Sprite idle, Sprite windup, Sprite strike, Sprite strikeExtended)
        {
            if (idle           != null) _idleSprite           = idle;
            if (windup         != null) _windupSprite         = windup;
            if (strike         != null) _strikeSprite         = strike;
            if (strikeExtended != null) _strikeExtendedSprite = strikeExtended;
            if (_sr.sprite == null && _idleSprite != null) _sr.sprite = _idleSprite;
        }

        /// <summary>소환 모션용 프레임 지정. null이면 PlaySummon 호출해도 프레임 스왑 없이 위치 트윈만 적용.</summary>
        public void SetSummonFrame(Sprite summonCast)
        {
            if (summonCast != null) _summonCastSprite = summonCast;
        }

        public void SetBasePosition(Vector3 pos)
        {
            _basePosition = pos;
        }

        /// <summary>Rescales transform to target world-space height. 기준 스프라이트 높이 고정으로 프레임 스왑 시 크기 점프 방지.</summary>
        public void SetWorldHeight(float worldHeight)
        {
            // 기준 높이가 아직 없으면 현재 스프라이트로 한 번 잡기
            if (_scaleReferenceHeight <= 0f && _sr.sprite != null && _sr.sprite.bounds.size.y > 0f)
                _scaleReferenceHeight = _sr.sprite.bounds.size.y;
            if (_scaleReferenceHeight <= 0f) return;
            float s = (worldHeight / _scaleReferenceHeight) * _activeScaleMultiplier;
            transform.localScale = new Vector3(s, s, 1f);
        }

        public void SetStrikeExtendedScaleBoost(float boost) => _strikeExtendedScaleBoost = boost;

        public void SetSortingOrder(int order)
        {
            _sr.sortingOrder = order;
        }

        public void PlayAttack(Vector3 dir, float distance = 0.7f, float duration = 0.75f)
        {
            StartAnim(AttackRoutine(dir.normalized, distance, duration));
        }

        public void PlaySummon(Vector3 dir, float distance = 0.18f, float duration = 0.7f)
        {
            StartAnim(SummonRoutine(dir.normalized, distance, duration));
        }

        public void PlayHit(float duration = 0.35f)
        {
            StartAnim(HitRoutine(duration));
        }

        private void StartAnim(IEnumerator routine)
        {
            if (_currentAnim != null) StopCoroutine(_currentAnim);
            _currentAnim = StartCoroutine(routine);
        }

        private void LateUpdate()
        {
            // 애니메이션 진행 중이 아니면 idle bob 적용 — 캐릭터의 실제 월드 높이에 비례
            if (_currentAnim == null && _sr.sprite != null)
            {
                float worldH = _scaleReferenceHeight > 0f
                    ? _scaleReferenceHeight * transform.localScale.y
                    : 1f;
                float bob = Mathf.Sin(Time.time * _idleBobFreq + _idleBobPhase) * worldH * _idleBobFraction;
                transform.position = _basePosition + new Vector3(0f, bob, 0f);
            }
        }

        private IEnumerator AttackRoutine(Vector3 dir, float distance, float duration)
        {
            // 4-프레임 페이즈 분할:
            //   0~30% windup    — 뒤로 빼며 채찍 당김
            //   30~45% strike   — 스냅 순간 (짧게, 크랙 치는 찰나)
            //   45~80% extended — 채찍 최대로 뻗은 상태 유지 (길게, 시각적 임팩트)
            //   80~100% return  — idle 복귀
            float windupEnd   = duration * 0.30f;
            float strikeEnd   = duration * 0.45f;
            float extendedEnd = duration * 0.80f;

            Sprite originalSprite = _sr.sprite;

            float t = 0f;
            int phase = -1; // 0=windup, 1=strike, 2=extended, 3=return
            while (t < duration)
            {
                t += Time.deltaTime;

                int newPhase;
                if      (t < windupEnd)   newPhase = 0;
                else if (t < strikeEnd)   newPhase = 1;
                else if (t < extendedEnd) newPhase = 2;
                else                      newPhase = 3;

                if (newPhase != phase)
                {
                    phase = newPhase;
                    Sprite next = phase switch
                    {
                        0 => _windupSprite,
                        1 => _strikeSprite,
                        2 => _strikeExtendedSprite ?? _strikeSprite, // extended 없으면 strike 유지
                        _ => _idleSprite,
                    };
                    if (next != null) _sr.sprite = next;

                    // StrikeExtended는 프레임 내 캐릭터 비율이 다른 경우가 많아 보정 배율 적용
                    _activeScaleMultiplier = (phase == 2 && _strikeExtendedSprite != null)
                        ? _strikeExtendedScaleBoost
                        : 1f;
                }

                // 위치 트윈
                float offset;
                if (t < windupEnd)
                {
                    float wp = t / windupEnd;
                    offset = Mathf.Lerp(0f, -distance * 0.20f, wp);
                }
                else if (t < strikeEnd)
                {
                    float sp = (t - windupEnd) / (strikeEnd - windupEnd);
                    // strike: 짧고 빠르게 앞으로 튕김
                    offset = Mathf.Lerp(-distance * 0.20f, distance * 0.80f, sp);
                }
                else if (t < extendedEnd)
                {
                    float ep = (t - strikeEnd) / (extendedEnd - strikeEnd);
                    // extended: strike에서 살짝 더 앞으로 밀었다가 유지
                    offset = Mathf.Lerp(distance * 0.80f, distance, Mathf.Pow(ep, 0.4f));
                }
                else
                {
                    float rp = (t - extendedEnd) / (duration - extendedEnd);
                    offset = Mathf.Lerp(distance, 0f, rp);
                }
                transform.position = _basePosition + dir * offset;
                yield return null;
            }
            transform.position = _basePosition;
            if (_idleSprite != null) _sr.sprite = _idleSprite;
            else if (originalSprite != null) _sr.sprite = originalSprite;
            _activeScaleMultiplier = 1f;
            _currentAnim = null;
        }

        private IEnumerator SummonRoutine(Vector3 dir, float distance, float duration)
        {
            // 3-페이즈 분할:
            //   0~20% push    — idle에서 summonCast로 프레임 스왑, 앞으로 밀어냄
            //   20~80% hold   — 뻗은 자세 유지 (소환 순간의 임팩트)
            //   80~100% return — 복귀하며 idle 프레임으로
            float pushEnd = duration * 0.20f;
            float holdEnd = duration * 0.80f;

            Sprite originalSprite = _sr.sprite;
            if (_summonCastSprite != null) _sr.sprite = _summonCastSprite;

            float t = 0f;
            int phase = -1; // 0=push, 1=hold, 2=return
            while (t < duration)
            {
                t += Time.deltaTime;

                int newPhase;
                if      (t < pushEnd) newPhase = 0;
                else if (t < holdEnd) newPhase = 1;
                else                  newPhase = 2;

                if (newPhase != phase)
                {
                    phase = newPhase;
                    if (phase == 2 && _idleSprite != null) _sr.sprite = _idleSprite;
                }

                float offset;
                if (t < pushEnd)
                {
                    float p = t / pushEnd;
                    offset = Mathf.Lerp(0f, distance, Mathf.Pow(p, 0.5f));
                }
                else if (t < holdEnd)
                {
                    offset = distance;
                }
                else
                {
                    float p = (t - holdEnd) / (duration - holdEnd);
                    offset = Mathf.Lerp(distance, 0f, p);
                }
                transform.position = _basePosition + dir * offset;
                yield return null;
            }
            transform.position = _basePosition;
            if (_idleSprite != null) _sr.sprite = _idleSprite;
            else if (originalSprite != null) _sr.sprite = originalSprite;
            _currentAnim = null;
        }

        private IEnumerator HitRoutine(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                float falloff = 1f - p;
                Vector3 shake = new Vector3(
                    Mathf.Sin(t * 90f) * 0.08f * falloff,
                    Mathf.Cos(t * 75f) * 0.03f * falloff,
                    0f);
                transform.position = _basePosition + shake;
                float flash = Mathf.Max(0f, 1f - p * 2.2f);
                _sr.color = Color.Lerp(_baseColor, new Color(1.3f, 0.8f, 0.8f, 1f), flash);
                yield return null;
            }
            transform.position = _basePosition;
            _sr.color = _baseColor;
            _currentAnim = null;
        }
    }
}
