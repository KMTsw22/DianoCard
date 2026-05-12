using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DianoCard.Audio
{
    /// <summary>
    /// SFX/BGM 재생 싱글톤. Resources/Audio/ 아래 클립을 키로 로드해 재생.
    /// 1키 = 1클립 (DianoCard 룰 — 변형 다중 등록 X). PlaySFX(key)로 단순 호출.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private AudioSource _sfxSource;
        private AudioSource _bgmSource;
        private readonly Dictionary<string, AudioClip> _cache = new();

        // PauseMenuUI 설정 슬라이더에서 직접 set/get. PlayerPrefs에 영구 저장.
        private const string PrefBgm = "dianocard.audio.bgm";
        private const string PrefSfx = "dianocard.audio.sfx";

        private float _bgmVolume = 1f;
        private float _sfxVolume = 1f;

        public float BgmVolume
        {
            get => _bgmVolume;
            set
            {
                _bgmVolume = Mathf.Clamp01(value);
                if (_bgmSource != null) _bgmSource.volume = _bgmVolume;
                PlayerPrefs.SetFloat(PrefBgm, _bgmVolume);
            }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set
            {
                _sfxVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(PrefSfx, _sfxVolume);
            }
        }

        // SFX 키별 Resources 경로 매핑. 새 키 추가 시 여기 한 줄 박으면 됨.
        // 파일 없으면 PlaySFX는 no-op (Resources.Load가 null 반환 → 캐시되고 무음).
        private static readonly Dictionary<string, string> SfxPaths = new()
        {
            { "card_attack",   "Audio/SFX/Cards/card_attack"   },
            { "card_buff",     "Audio/SFX/Cards/card_buff"     },
            { "card_debuff",   "Audio/SFX/Cards/card_debuff"   },
            { "card_summon",   "Audio/SFX/Cards/card_summon"   },
            { "card_draw",     "Audio/SFX/Cards/card_draw"     },
            { "card_discard",  "Audio/SFX/Cards/card_discard"  },
            { "card_exhaust",  "Audio/SFX/Cards/card_exhaust"  },
            { "hit_block",     "Audio/SFX/Hits/hit_block"      },
            // 물리 타격 통합 SFX (공룡/적 양쪽 swing 모션에 공용). card_attack(마법 카드)과 분리.
            { "attack",        "Audio/SFX/Hits/attack"         },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("AudioManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AudioManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.spatialBlend = 0f; // 2D

            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.loop = true;
            _bgmSource.spatialBlend = 0f;

            _bgmVolume = PlayerPrefs.GetFloat(PrefBgm, 1f);
            _sfxVolume = PlayerPrefs.GetFloat(PrefSfx, 1f);
            _bgmSource.volume = _bgmVolume;
        }

        public void PlaySFX(string key)
        {
            if (string.IsNullOrEmpty(key) || _sfxSource == null) return;
            if (!_cache.TryGetValue(key, out var clip))
            {
                if (!SfxPaths.TryGetValue(key, out var path)) return;
                clip = Resources.Load<AudioClip>(path);
                _cache[key] = clip; // null도 캐시 (반복 로드 회피)
            }
            if (clip != null) _sfxSource.PlayOneShot(clip, _sfxVolume);
        }

        /// <summary>
        /// 같은 SFX를 N번 짧은 간격으로 재생 (예: 카드 5장 드로우 → 5번 휘리리릭).
        /// 한 번에 다 쳐서 레이어드 카오스가 되는 걸 막기 위해 staggered.
        /// </summary>
        public void PlaySFXBurst(string key, int count, float intervalSec = 0.09f)
        {
            if (count <= 0) return;
            if (count == 1) { PlaySFX(key); return; }
            StartCoroutine(PlaySFXBurstCo(key, count, intervalSec));
        }

        private IEnumerator PlaySFXBurstCo(string key, int count, float intervalSec)
        {
            for (int i = 0; i < count; i++)
            {
                PlaySFX(key);
                if (i < count - 1) yield return new WaitForSeconds(intervalSec);
            }
        }
    }
}
