using System.Collections;
using System.Collections.Generic;
using DianoCard.Data;
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

        // PauseMenuUI 설정 슬라이더에서 직접 set/get. SaveSystem(save.json)에 영구 저장.
        private const string PrefBgm = "dianocard.audio.bgm";
        private const string PrefSfx = "dianocard.audio.sfx";

        private const float DefaultBgmVolume = 0.3f;
        private const float DefaultSfxVolume = 0.3f;

        private float _bgmVolume = DefaultBgmVolume;
        private float _sfxVolume = DefaultSfxVolume;

        public float BgmVolume
        {
            get => _bgmVolume;
            set
            {
                _bgmVolume = Mathf.Clamp01(value);
                if (_bgmSource != null) _bgmSource.volume = _bgmVolume;
                SaveSystem.SetFloat(PrefBgm, _bgmVolume);
                SaveSystem.Save();
            }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set
            {
                _sfxVolume = Mathf.Clamp01(value);
                SaveSystem.SetFloat(PrefSfx, _sfxVolume);
                SaveSystem.Save();
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
            { "card_shuffle",  "Audio/SFX/Cards/card_shuffle"  },
            { "hit_block",     "Audio/SFX/Hits/hit_block"      },
            // 물리 타격 통합 SFX (공룡/적 양쪽 swing 모션에 공용). card_attack(마법 카드)과 분리.
            { "attack",        "Audio/SFX/Hits/attack"         },
            { "ui_click",      "Audio/SFX/UI/ui_click"         },
            { "potion_use",    "Audio/SFX/Items/potion_use"    },
            // 쉴드 획득 SFX — 플레이어/공룡/적 block 증가 시 공용 발동. 같은 프레임 중복 방지는 PlaySFXThrottled 사용.
            { "shield_gain",   "Audio/SFX/Combat/shield_gain"  },
        };

        // BGM 키별 Resources 경로. SFX와 동일한 로딩 패턴.
        private static readonly Dictionary<string, string> BgmPaths = new()
        {
            { "cinder_harrow", "Audio/BGM/cinder_harrow" },
        };

        // Awake에서 1회 자동 재생할 기본 BGM. null이면 자동 재생 안 함.
        private const string DefaultBgmKey = "cinder_harrow";
        private string _currentBgmKey;

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

            _bgmVolume = SaveSystem.GetFloat(PrefBgm, DefaultBgmVolume);
            _sfxVolume = SaveSystem.GetFloat(PrefSfx, DefaultSfxVolume);
            _bgmSource.volume = _bgmVolume;

            if (!string.IsNullOrEmpty(DefaultBgmKey)) PlayBGM(DefaultBgmKey);
        }

        /// <summary>
        /// 지정 키 BGM을 무한 루프 재생. 같은 키가 이미 재생 중이면 no-op.
        /// </summary>
        public void PlayBGM(string key)
        {
            if (string.IsNullOrEmpty(key) || _bgmSource == null) return;
            if (_currentBgmKey == key && _bgmSource.isPlaying) return;
            if (!_cache.TryGetValue(key, out var clip))
            {
                if (!BgmPaths.TryGetValue(key, out var path)) return;
                clip = Resources.Load<AudioClip>(path);
                _cache[key] = clip;
            }
            if (clip == null) return;
            _bgmSource.clip = clip;
            _bgmSource.loop = true;
            _bgmSource.volume = _bgmVolume;
            _bgmSource.Play();
            _currentBgmKey = key;
        }

        public void StopBGM()
        {
            if (_bgmSource == null) return;
            _bgmSource.Stop();
            _bgmSource.clip = null;
            _currentBgmKey = null;
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

        // 같은 프레임 안에서 같은 키가 여러 번 호출되어도 1회만 재생.
        // 카드+유물 동시 트리거 같은 케이스(예: block 게인)에서 SFX 중첩 방지.
        private readonly Dictionary<string, int> _lastFrame = new();
        public void PlaySFXThrottled(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int last = _lastFrame.TryGetValue(key, out var f) ? f : -1;
            if (Time.frameCount == last) return;
            _lastFrame[key] = Time.frameCount;
            PlaySFX(key);
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
