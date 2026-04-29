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

        // N프레임 시퀀스(2~8장 등). 설정되면 4페이즈 로직 대신 이것을 우선 사용.
        // 첫 프레임은 idle, 중간~후반은 strike peak, 마지막은 follow-through 정도로 해석.
        private Sprite[] _attackSequence;
        // 피격/소환도 동일한 "균등 시간 N프레임" 시퀀스로 재생 가능.
        // null이면 HitRoutine은 프레임 스왑 없이 shake/flash만, SummonRoutine은 _summonCastSprite 단일 프레임 사용.
        private Sprite[] _hitSequence;
        private Sprite[] _summonSequence;

        // 스케일 기준이 되는 스프라이트 높이 (바뀌지 않는 고정값). 프레임 스왑 중에도 이 값으로 scale 계산.
        private float _scaleReferenceHeight = -1f;

        // BattleUI에서 마지막으로 지정한 "캐릭터가 월드에서 가져야 할 실제 높이".
        // 프레임 스왑으로 스프라이트 bounds가 달라져도 이 값에 맞춰 스케일이 재계산됨.
        private float _intendedWorldHeight = -1f;

        // 현재 애니메이션 페이즈의 스케일 배율 (StrikeExtended에서 캐릭터 비율 보정용). 기본 1.0
        private float _activeScaleMultiplier = 1f;
        // StrikeExtended 스프라이트 표시 중 적용할 크기 부스트 (프레임 내 캐릭터가 작게 그려진 경우 보정). 기본 1.0
        private float _strikeExtendedScaleBoost = 1.2f;
        // SequenceAttackRoutine 전체 구간에 적용되는 스케일 부스트 — 시퀀스 캔버스에 캐릭터가
        // 일부분만 차지(여백이 큰 가로형 GIF 분해본 등)할 때 idle 정적 스프라이트와 시각 크기를 맞추기 위한 보정.
        private float _sequenceScaleBoost = 1f;

        // 발 밑 그림자. 캐릭터가 공격/이동으로 움직여도 지면에 고정되도록 별도 GameObject로 관리 (parent는 캐릭터가 아님).
        private GameObject _shadowGO;
        private SpriteRenderer _shadowSr;
        private float _shadowRelativeHeight = 0.10f; // intendedWorldHeight 대비 그림자 세로 길이
        private float _shadowWidthScale = 1f;        // 가로 폭만 추가 배수 (세로 유지)
        private Vector2 _shadowWorldOffset = Vector2.zero;

        // 팬텀 모드 — 본체 반투명 + 본체 뒤에 같은 스프라이트의 확대 사본을 펄스 글로우로 렌더.
        // 이끼 잡몹(E901 보스 소환물)처럼 "도깨비불" 느낌 줄 때 사용.
        private bool _phantomMode;
        private float _phantomBaseAlpha = 0.5f;
        private Color _phantomGlowTint = new Color(1f, 0.55f, 0.18f, 1f);
        private GameObject _glowGO;
        private SpriteRenderer _glowSr;
        private float _phantomPulsePhase;

        // Idle breathing — CharacterSelectUI의 캐릭터 호흡과 동일 공식(Y만 소폭, smoothstep eased).
        // 공격/피격/소환 코루틴 동안은 자동 off (_currentAnim != null).
        public bool breathingEnabled = false;
        public float breathingAmp = 0.015f;   // 1.5% Y
        public float breathingFreq = 0.15f;   // ~6.7s 주기
        public float breathingPhase = 1.5f;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _sr.sortingOrder = 50;
            // 개체별 위상 오프셋 — 여러 적/플레이어가 같은 박자로 움직여 기계적으로 보이지 않게.
            _idleBobPhase = Random.value * Mathf.PI * 2f;
            _phantomPulsePhase = Random.value * Mathf.PI * 2f;
        }

        public void SetSprite(Sprite s)
        {
            _sr.sprite = s;
            if (_idleSprite == null) _idleSprite = s;
            if (_scaleReferenceHeight <= 0f && s != null && s.bounds.size.y > 0f)
                _scaleReferenceHeight = s.bounds.size.y;
            if (_glowSr != null) _glowSr.sprite = s;
        }

        /// <summary>
        /// 팬텀 모드 토글 — 본체 알파를 baseAlpha로 낮추고 본체 뒤에 같은 스프라이트의 확대 사본을
        /// glowTint로 틴트한 펄스 글로우로 렌더.
        /// </summary>
        public void SetPhantomMode(bool enabled, float baseAlpha = 0.5f, Color? glowTint = null)
        {
            _phantomMode = enabled;
            _phantomBaseAlpha = Mathf.Clamp01(baseAlpha);
            if (glowTint.HasValue) _phantomGlowTint = glowTint.Value;

            if (!enabled)
            {
                _baseColor = Color.white;
                if (_sr != null) _sr.color = _baseColor;
                if (_glowGO != null) { Destroy(_glowGO); _glowGO = null; _glowSr = null; }
                return;
            }

            // 본체 알파 적용 — _baseColor에 반영해야 hit flash 종료 시 알파가 복구됨.
            _baseColor = new Color(1f, 1f, 1f, _phantomBaseAlpha);
            if (_sr != null) _sr.color = _baseColor;

            // 글로우 차일드 생성/재사용. 본체와 같은 부모에 붙여 transform 이동(공격 등)에 끌려가지 않도록.
            if (_glowGO == null)
            {
                _glowGO = new GameObject(gameObject.name + "_Glow");
                _glowGO.transform.SetParent(transform, worldPositionStays: false);
                _glowSr = _glowGO.AddComponent<SpriteRenderer>();
                _glowSr.sortingOrder = _sr.sortingOrder - 1;
            }
            _glowSr.sprite = _sr.sprite;
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

        /// <summary>
        /// N프레임 공격 시퀀스 지정 (move-board로 생성한 CH001_attack_f01~fN.png 같은 결과).
        /// 설정되면 기존 4페이즈 로직 대신 프레임 시퀀스를 균등 시간으로 재생.
        /// 첫 프레임은 idle로도 재사용되어 대기 포즈가 됨.
        /// </summary>
        public void SetAttackSequence(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0) { _attackSequence = null; return; }
            // null 제거한 유효 프레임만 보관
            var clean = new System.Collections.Generic.List<Sprite>(frames.Length);
            foreach (var f in frames) if (f != null) clean.Add(f);
            _attackSequence = clean.Count > 0 ? clean.ToArray() : null;
            if (_attackSequence != null && _idleSprite == null) _idleSprite = _attackSequence[0];
            if (_sr.sprite == null && _idleSprite != null) _sr.sprite = _idleSprite;
        }

        /// <summary>소환 모션용 프레임 지정. null이면 PlaySummon 호출해도 프레임 스왑 없이 위치 트윈만 적용.</summary>
        public void SetSummonFrame(Sprite summonCast)
        {
            if (summonCast != null) _summonCastSprite = summonCast;
        }

        /// <summary>피격 모션용 N프레임 시퀀스. 설정되면 HitRoutine 동안 shake/flash와 함께 균등 시간으로 프레임을 스왑한다.</summary>
        public void SetHitSequence(Sprite[] frames)
        {
            _hitSequence = SanitizeSequence(frames);
        }

        /// <summary>소환 모션용 N프레임 시퀀스. 설정되면 SummonRoutine 동안 push-hold-return 내내 프레임을 균등 시간으로 스왑한다.
        /// _summonCastSprite(단일 프레임)보다 우선 적용되며, 없으면 _summonCastSprite 폴백.</summary>
        public void SetSummonSequence(Sprite[] frames)
        {
            _summonSequence = SanitizeSequence(frames);
        }

        private static Sprite[] SanitizeSequence(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0) return null;
            var clean = new System.Collections.Generic.List<Sprite>(frames.Length);
            foreach (var f in frames) if (f != null) clean.Add(f);
            return clean.Count > 0 ? clean.ToArray() : null;
        }

        public void SetBasePosition(Vector3 pos)
        {
            _basePosition = pos;
            ApplyShadowTransform();
        }

        /// <summary>
        /// 발 밑 그림자 스프라이트 지정. null 전달 시 기존 그림자 제거.
        /// <paramref name="relativeHeight"/>는 캐릭터 intendedWorldHeight 대비 그림자 세로 길이 비율.
        /// </summary>
        /// <summary>
        /// 그림자 스프라이트는 유지한 채 크기/위치/알파만 런타임 갱신.
        /// Inspector에서 실시간으로 튜닝할 때 매 프레임 호출해도 된다.
        /// </summary>
        public void UpdateShadowParams(float relativeHeight, float widthScale, Vector2 worldOffset, float alpha)
        {
            _shadowRelativeHeight = relativeHeight;
            _shadowWidthScale = widthScale;
            _shadowWorldOffset = worldOffset;
            if (_shadowSr != null) _shadowSr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            ApplyShadowTransform();
        }

        public void SetShadowSprite(Sprite shadow, float relativeHeight = 0.10f, Vector2 worldOffset = default, float alpha = 1f)
        {
            _shadowRelativeHeight = relativeHeight;
            _shadowWorldOffset = worldOffset;

            if (shadow == null)
            {
                if (_shadowGO != null) Destroy(_shadowGO);
                _shadowGO = null;
                _shadowSr = null;
                return;
            }

            if (_shadowGO == null)
            {
                _shadowGO = new GameObject(gameObject.name + "_Shadow");
                // 부모와 동일한 상위에 붙여서 이 엔티티의 transform 이동(공격/피격)에 그림자가 끌려가지 않도록 함.
                _shadowGO.transform.SetParent(transform.parent, worldPositionStays: false);
                _shadowSr = _shadowGO.AddComponent<SpriteRenderer>();
                _shadowSr.sortingOrder = _sr.sortingOrder - 1;
            }
            _shadowSr.sprite = shadow;
            _shadowSr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            ApplyShadowTransform();
        }

        private void ApplyShadowTransform()
        {
            if (_shadowGO == null || _shadowSr == null || _shadowSr.sprite == null) return;

            // 캐릭터 스프라이트의 하단 경계 = 실제 발 위치. pivot=Center면 _basePosition은 몸통 중앙이라
            // 그냥 _basePosition에 그림자를 두면 캐릭터 뒤에 가려진다.
            // X는 live transform.position을 따라가서 공격/피격으로 캐릭터가 좌우로 움직이면 그림자도 함께 이동.
            // Y는 _basePosition 기준(+ sprite 하단 offset)으로 지면에 고정 — 점프/바닥 이탈 애니메이션에도 그림자는 바닥에 유지.
            Vector3 feetPos = new Vector3(transform.position.x, _basePosition.y, _basePosition.z);
            if (_sr != null && _sr.sprite != null)
            {
                float scaleY = transform.localScale.y;
                feetPos.y += _sr.sprite.bounds.min.y * scaleY;
            }
            _shadowGO.transform.position = feetPos + new Vector3(_shadowWorldOffset.x, _shadowWorldOffset.y, 0f);

            if (_intendedWorldHeight > 0f)
            {
                float boundsH = _shadowSr.sprite.bounds.size.y;
                if (boundsH > 0.001f)
                {
                    float s = (_intendedWorldHeight * _shadowRelativeHeight) / boundsH;
                    _shadowGO.transform.localScale = new Vector3(s * _shadowWidthScale, s, 1f);
                }
            }
        }

        /// <summary>Rescales transform to target world-space height. 현재 스프라이트의 bounds를 기준으로 매번 재계산 → 프레임 스왑 시 보이는 높이 일관.</summary>
        public void SetWorldHeight(float worldHeight)
        {
            _intendedWorldHeight = worldHeight;
            // 기준 높이 캐시 (디버그·폴백용, 실제 스케일은 아래 ApplyWorldHeight가 현재 sprite 기준으로 재계산)
            if (_scaleReferenceHeight <= 0f && _sr.sprite != null && _sr.sprite.bounds.size.y > 0f)
                _scaleReferenceHeight = _sr.sprite.bounds.size.y;
            ApplyWorldHeight();
        }

        /// <summary>
        /// 저장된 intendedWorldHeight 와 **현재 스프라이트의 bounds 높이**로 스케일 재계산.
        /// 프레임 스왑(공격 시퀀스, idle/strike 교체 등)마다 호출되어 캐릭터가 커보이거나 작아보이는 점프 방지.
        /// </summary>
        private void ApplyWorldHeight()
        {
            if (_intendedWorldHeight <= 0f || _sr.sprite == null) return;
            float boundsH = _sr.sprite.bounds.size.y;
            if (boundsH <= 0.001f) return;
            float s = (_intendedWorldHeight / boundsH) * _activeScaleMultiplier;
            transform.localScale = new Vector3(s, s, 1f);
            ApplyShadowTransform();
        }

        public void SetStrikeExtendedScaleBoost(float boost) => _strikeExtendedScaleBoost = boost;

        /// <summary>SequenceAttackRoutine 전체 구간에 적용될 스케일 부스트 — 시퀀스 캔버스 내 캐릭터가
        /// 작게 그려진 가로형 GIF 분해본 등에서 idle 정적 스프라이트와 시각 크기를 맞추기 위해 사용.</summary>
        public void SetSequenceScaleBoost(float boost) => _sequenceScaleBoost = boost;

        public void SetSortingOrder(int order)
        {
            _sr.sortingOrder = order;
            if (_shadowSr != null) _shadowSr.sortingOrder = order - 1;
        }

        private void OnDestroy()
        {
            if (_shadowGO != null) Destroy(_shadowGO);
            if (_glowGO != null) Destroy(_glowGO);
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
            // idle bob 비활성 — 캐릭터가 지면에 고정돼 있도록
            if (_currentAnim == null && _sr.sprite != null)
            {
                transform.position = _basePosition;

                // Idle breathing — Y만 살짝 늘리고 발 위치는 pivot 보정으로 지면에 고정.
                if (breathingEnabled && _intendedWorldHeight > 0f)
                {
                    float boundsH = _sr.sprite.bounds.size.y;
                    if (boundsH > 0.001f)
                    {
                        float t = Time.time * Mathf.PI * 2f * breathingFreq + breathingPhase;
                        float rawSin = Mathf.Sin(t);
                        float eased = rawSin * rawSin * Mathf.Sign(rawSin);
                        float breathY = 1f + eased * breathingAmp;
                        float s = (_intendedWorldHeight / boundsH) * _activeScaleMultiplier;
                        transform.localScale = new Vector3(s, s * breathY, 1f);
                        // center pivot이면 bounds.min.y가 음수 → Y 스케일 증가분만큼 position을 올려 발 위치를 고정
                        float minY = _sr.sprite.bounds.min.y;
                        transform.position = _basePosition + new Vector3(0f, -minY * s * (breathY - 1f), 0f);
                    }
                }
            }

            // 그림자는 매 프레임 live 위치 따라가도록 — 공격/피격으로 캐릭터가 움직이면 그림자도 따라옴.
            ApplyShadowTransform();

            // 팬텀 글로우 펄스 — 스케일·알파 동시 진동. 차일드라 본체 위치/스케일에 자동 따라옴.
            if (_phantomMode && _glowSr != null && _sr != null && _sr.sprite != null)
            {
                float pt = Time.time * Mathf.PI * 2f * 0.55f + _phantomPulsePhase;
                float pulse = (Mathf.Sin(pt) + 1f) * 0.5f; // 0~1
                // 펄스 진폭/세기 절반 이상 축소 — 글로우 후광이 자기주장 안 하고 본체 뒤에 살짝 빛남.
                float scale = Mathf.Lerp(1.08f, 1.15f, pulse);  // 기존 1.18~1.34 (range 0.16) → 1.08~1.15 (range 0.07)
                float alpha = Mathf.Lerp(0.12f, 0.26f, pulse);  // 기존 0.22~0.55 (peak 0.55) → 0.12~0.26 (peak 0.26)
                _glowGO.transform.localScale = new Vector3(scale, scale, 1f);
                _glowGO.transform.localPosition = Vector3.zero;
                var c = _phantomGlowTint;
                _glowSr.color = new Color(c.r, c.g, c.b, alpha);
                _glowSr.sortingOrder = _sr.sortingOrder - 1;
            }
        }

        private IEnumerator AttackRoutine(Vector3 dir, float distance, float duration)
        {
            // 0) N프레임 시퀀스가 설정돼 있으면 우선 재생 (move-board 결과물 사용 경로).
            if (_attackSequence != null && _attackSequence.Length > 0)
            {
                yield return SequenceAttackRoutine(dir, distance, duration);
                yield break;
            }

            // 4-프레임 페이즈 분할 (레거시):
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
                    ApplyWorldHeight();
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
            ApplyWorldHeight();
            _currentAnim = null;
        }

        /// <summary>
        /// N프레임 공격 시퀀스를 균등 시간으로 재생.
        /// 위치는 idle(처음) → 중간 peak에서 최대 전진 → 마지막에 복귀하는 3단 곡선.
        /// 프레임 개수와 무관하게 동작 (2~8 프레임 모두 OK).
        /// </summary>
        private IEnumerator SequenceAttackRoutine(Vector3 dir, float distance, float duration)
        {
            int n = _attackSequence.Length;
            Sprite originalSprite = _sr.sprite;
            float perFrame = duration / n;
            int lastFrame = -1;

            // 시퀀스 전체 구간에 스케일 부스트 적용 — 가로형 캔버스에 캐릭터가 일부만 차지할 때 idle과 크기 맞춤.
            _activeScaleMultiplier = _sequenceScaleBoost;

            // 위치 커브: 앞 20% 약간 뒤로(windup) → 60% 지점에서 peak(+distance) → 마지막 20% 복귀
            float backEnd = duration * 0.20f;   // 이 시점까지 살짝 뒤로
            float peakT   = duration * 0.60f;   // 이 시점에서 전진 최대
            float returnStart = duration * 0.85f;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;

                // 현재 재생 프레임 = (t / duration) * n
                int frameIdx = Mathf.Clamp(Mathf.FloorToInt(t / perFrame), 0, n - 1);
                if (frameIdx != lastFrame)
                {
                    _sr.sprite = _attackSequence[frameIdx];
                    lastFrame = frameIdx;
                    ApplyWorldHeight(); // 프레임별 bounds 높이 차이로 캐릭터가 커/작아 보이는 것 방지
                }

                // 위치 오프셋 — 모든 프레임에 균일하게 적용되는 앞뒤 모션
                float offset;
                if (t < backEnd)
                {
                    float p = t / backEnd;
                    offset = Mathf.Lerp(0f, -distance * 0.15f, p);
                }
                else if (t < peakT)
                {
                    float p = (t - backEnd) / (peakT - backEnd);
                    offset = Mathf.Lerp(-distance * 0.15f, distance, Mathf.Pow(p, 0.6f));
                }
                else if (t < returnStart)
                {
                    offset = distance;
                }
                else
                {
                    float p = (t - returnStart) / (duration - returnStart);
                    offset = Mathf.Lerp(distance, 0f, p);
                }
                transform.position = _basePosition + dir * offset;
                yield return null;
            }

            transform.position = _basePosition;
            // 공격 종료 후 idle 프레임으로 복귀
            if (_idleSprite != null) _sr.sprite = _idleSprite;
            else if (_attackSequence != null && _attackSequence.Length > 0) _sr.sprite = _attackSequence[0];
            else if (originalSprite != null) _sr.sprite = originalSprite;
            _activeScaleMultiplier = 1f;
            ApplyWorldHeight();
            _currentAnim = null;
        }

        private IEnumerator SummonRoutine(Vector3 dir, float distance, float duration)
        {
            // 3-페이즈 분할:
            //   0~20% push    — idle에서 소환 포즈로 프레임 스왑, 앞으로 밀어냄
            //   20~80% hold   — 뻗은 자세 유지 (소환 순간의 임팩트)
            //   80~100% return — 복귀하며 idle 프레임으로
            // _summonSequence가 설정되어 있으면 push-hold-return 구간 **전체**를 시퀀스의 균등 시간 재생으로 덮음.
            // 없으면 기존 _summonCastSprite 단일 프레임 동작.
            float pushEnd = duration * 0.20f;
            float holdEnd = duration * 0.80f;

            Sprite originalSprite = _sr.sprite;
            bool useSeq = _summonSequence != null && _summonSequence.Length > 0;
            if (!useSeq && _summonCastSprite != null) { _sr.sprite = _summonCastSprite; ApplyWorldHeight(); }

            float t = 0f;
            int phase = -1;
            int lastFrameIdx = -1;
            while (t < duration)
            {
                t += Time.deltaTime;

                if (useSeq)
                {
                    float p = Mathf.Clamp01(t / duration);
                    int n = _summonSequence.Length;
                    int idx = Mathf.Clamp(Mathf.FloorToInt(p * n), 0, n - 1);
                    if (idx != lastFrameIdx)
                    {
                        _sr.sprite = _summonSequence[idx];
                        ApplyWorldHeight();
                        lastFrameIdx = idx;
                    }
                }
                else
                {
                    int newPhase;
                    if      (t < pushEnd) newPhase = 0;
                    else if (t < holdEnd) newPhase = 1;
                    else                  newPhase = 2;

                    if (newPhase != phase)
                    {
                        phase = newPhase;
                        if (phase == 2 && _idleSprite != null) { _sr.sprite = _idleSprite; ApplyWorldHeight(); }
                    }
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
            ApplyWorldHeight();
            _currentAnim = null;
        }

        private IEnumerator HitRoutine(float duration)
        {
            // _hitSequence가 있으면 shake/flash와 동시에 프레임을 균등 시간 재생.
            // 없으면 프레임 스왑 없이 기존 shake/flash만.
            bool useSeq = _hitSequence != null && _hitSequence.Length > 0;
            int lastFrameIdx = -1;

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

                if (useSeq)
                {
                    int n = _hitSequence.Length;
                    int idx = Mathf.Clamp(Mathf.FloorToInt(p * n), 0, n - 1);
                    if (idx != lastFrameIdx)
                    {
                        _sr.sprite = _hitSequence[idx];
                        ApplyWorldHeight();
                        lastFrameIdx = idx;
                    }
                }
                yield return null;
            }
            transform.position = _basePosition;
            _sr.color = _baseColor;
            if (useSeq && _idleSprite != null) { _sr.sprite = _idleSprite; ApplyWorldHeight(); }
            _currentAnim = null;
        }
    }
}
