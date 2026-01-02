using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours.Common;

namespace AnySilkBoss.Source.Managers
{
    /// <summary>
    /// 单根丝线管理器 - 统一版本
    /// 负责管理和控制单根丝线攻击（使用统一对象池）
    /// </summary>
    internal class SingleWebManager : MonoBehaviour
    {
        #region Fields
        // 原版丝线资源
        private GameObject? _strandPatterns;
        private GameObject? _pattern1Template;

        // 音频资源缓存（从 Pattern 1 的 silk_boss_pattern_control FSM 获取）
        private AudioClip? _appearAudioClip;           // silk_boss_web_attack_buildup
        private AudioClip? _burstAudioClip;            // silk_boss_web_attack_burst
        private GameObject? _audioPlayerPrefab;        // Audio Player Actor 2D
        
        // 音频资源公开属性
        public AudioClip? AppearAudioClip => _appearAudioClip;
        public AudioClip? BurstAudioClip => _burstAudioClip;
        public GameObject? AudioPlayerPrefab => _audioPlayerPrefab;

        // 单根丝线预制体（模板，用于后续生成实例）
        private GameObject? _singleWebStrandPrefab;
        public GameObject? SingleWebStrandPrefab => _singleWebStrandPrefab;

        // 统一对象池
        private readonly List<SingleWebBehavior> _webPool = new();
        private GameObject? _poolContainer;

        // 初始化标志
        private bool _initialized = false;

        // BOSS 场景名称
        private const string BossSceneName = "Cradle_03";

        // 自动补充池机制
        private bool _enableAutoPooling = false;
        private const int MIN_POOL_SIZE = 70;
        private const float POOL_GENERATION_INTERVAL = 0.2f;
        #endregion

        #region Unity Lifecycle
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }
        #endregion

        #region Scene Management
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != BossSceneName) return;
            
            // 如果已经初始化，只需要确保池子可用，不需要重新初始化
            if (_initialized && _poolContainer != null && _singleWebStrandPrefab != null)
            {
                Log.Info($"[SingleWebManager] 检测到 BOSS 场景 {scene.name}，已初始化，跳过重复初始化");
                return;
            }
            
            Log.Info($"检测到 BOSS 场景 {scene.name}，开始初始化 SingleWebManager...");
            StartCoroutine(Initialize());
        }

        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            if (oldScene.name == BossSceneName && newScene.name != BossSceneName)
            {
                Log.Info($"离开 BOSS 场景 {oldScene.name}，清理 SingleWebManager 缓存");
                CleanupPool();
                _initialized = false;
            }
        }
        #endregion

        #region Initialization
        private IEnumerator Initialize()
        {
            Log.Info("=== 开始初始化 SingleWebManager ===");

            if (_singleWebStrandPrefab != null)
            {
                Destroy(_singleWebStrandPrefab);
                _singleWebStrandPrefab = null;
            }

            yield return new WaitForSeconds(0.5f);

            GetStrandPatterns();
            GetPattern1Template();
            CacheAudioResources();  // 缓存音频资源
            yield return CreateSingleWebStrandPrefab();

            // 创建统一池容器 - 检查是否已存在，避免重复创建
            var existingPool = transform.Find("SingleWeb Pool");
            if (existingPool != null)
            {
                _poolContainer = existingPool.gameObject;
                Log.Info("[SingleWebManager] 复用已存在的 SingleWeb Pool");
            }
            else
            {
                _poolContainer = new GameObject("SingleWeb Pool");
                _poolContainer.transform.SetParent(transform);
                Log.Info("[SingleWebManager] 创建新的 SingleWeb Pool");
            }
            _enableAutoPooling = true;

            _initialized = true;
            Log.Info("=== SingleWebManager 初始化完成 ===");

            StartCoroutine(AutoPoolGeneration());
            Log.Info($"[SingleWebManager] 统一池机制已启用，目标大小: {MIN_POOL_SIZE}");
        }

        private void GetStrandPatterns()
        {
            var bossScene = GameObject.Find("Boss Scene");
            if (bossScene != null)
            {
                var strandPatternsTransform = bossScene.transform.Find("Strand Patterns");
                if (strandPatternsTransform != null)
                {
                    _strandPatterns = strandPatternsTransform.gameObject;
                    Log.Info($"从 Boss Scene 找到 Strand Patterns: {_strandPatterns.name}");
                    return;
                }
            }
            Log.Error("未找到 Strand Patterns GameObject！");
        }

        private void GetPattern1Template()
        {
            if (_strandPatterns == null)
            {
                Log.Error("Strand Patterns 为 null，无法获取 Pattern 1");
                return;
            }

            var pattern1Transform = _strandPatterns.transform.Find("Pattern 1");
            if (pattern1Transform != null)
            {
                _pattern1Template = pattern1Transform.gameObject;
                Log.Info($"找到 Pattern 1 模板: {_pattern1Template.name}");
            }
            else
            {
                Log.Error("未找到 Pattern 1 GameObject！");
            }
        }

        /// <summary>
        /// 从 Pattern 1 的 silk_boss_pattern_control FSM 缓存音频资源
        /// </summary>
        private void CacheAudioResources()
        {
            if (_pattern1Template == null)
            {
                Log.Error("Pattern 1 模板为 null，无法缓存音频资源");
                return;
            }

            var patternControlFsm = FSMUtility.LocateMyFSM(_pattern1Template, "silk_boss_pattern_control");
            if (patternControlFsm == null)
            {
                Log.Error("未找到 silk_boss_pattern_control FSM，无法缓存音频资源");
                return;
            }

            // 从 Web Appear 状态获取 PlayAudioEvent
            var webAppearState = patternControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Web Appear");
            if (webAppearState != null)
            {
                var playAudioAction = webAppearState.Actions.FirstOrDefault(a => a is PlayAudioEvent) as PlayAudioEvent;
                if (playAudioAction != null)
                {
                    _appearAudioClip = playAudioAction.audioClip.Value as AudioClip;
                    _audioPlayerPrefab = (playAudioAction.audioPlayerPrefab.Value as AudioSource)?.gameObject;
                    Log.Info($"缓存出现音效: {_appearAudioClip?.name ?? "null"}");
                }
            }

            // 从 Web Burst Start 状态获取 AudioPlayerOneShotSingleV2
            var webBurstStartState = patternControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Web Burst Start");
            if (webBurstStartState != null)
            {
                var audioAction = webBurstStartState.Actions.FirstOrDefault(a => a is AudioPlayerOneShotSingleV2) as AudioPlayerOneShotSingleV2;
                if (audioAction != null)
                {
                    _burstAudioClip = audioAction.audioClip.Value as AudioClip;
                    // 如果之前没获取到 audioPlayerPrefab，从这里获取
                    if (_audioPlayerPrefab == null)
                    {
                        _audioPlayerPrefab = audioAction.audioPlayer.Value;
                    }
                    Log.Info($"缓存爆发音效: {_burstAudioClip?.name ?? "null"}");
                }
            }

            if (_appearAudioClip == null || _burstAudioClip == null)
            {
                Log.Warn("部分音频资源缓存失败，丝线攻击可能没有音效");
            }
            else
            {
                Log.Info("=== 音频资源缓存完成 ===");
            }
        }

        private IEnumerator CreateSingleWebStrandPrefab()
        {
            if (_pattern1Template == null)
            {
                Log.Error("Pattern 1 模板为 null，无法创建单根丝线预制体");
                yield break;
            }

            GameObject? firstWebStrand = null;
            foreach (Transform child in _pattern1Template.transform)
            {
                if (child.name.Contains("Silk Boss WebStrand"))
                {
                    firstWebStrand = child.gameObject;
                    break;
                }
            }

            if (firstWebStrand == null)
            {
                Log.Error("未找到 Silk Boss WebStrand，无法创建单根丝线预制体");
                yield break;
            }

            _singleWebStrandPrefab = Instantiate(firstWebStrand);
            _singleWebStrandPrefab.name = "Single WebStrand Prefab";
            _singleWebStrandPrefab.transform.SetScaleX(5f);
            _singleWebStrandPrefab.transform.SetParent(transform);
            _singleWebStrandPrefab.SetActive(false);

            ConfigureWebStrandPrefab();
            Log.Info($"=== 单根丝线预制体创建完成: {_singleWebStrandPrefab.name} ===");
            yield return null;
        }
        #endregion


        #region Pool Management
        public void EnsurePoolInitialized()
        {
            if (!_initialized)
            {
                Log.Warn("[SingleWebManager] 尚未初始化，请等待场景加载完成");
                return;
            }

            if (_poolContainer == null)
            {
                // 检查是否已存在，避免重复创建
                var existingPool = transform.Find("SingleWeb Pool");
                if (existingPool != null)
                {
                    _poolContainer = existingPool.gameObject;
                    Log.Info("[SingleWebManager] EnsurePoolInitialized: 复用已存在的 SingleWeb Pool");
                }
                else
                {
                    _poolContainer = new GameObject("SingleWeb Pool");
                    _poolContainer.transform.SetParent(transform);
                    Log.Info("[SingleWebManager] EnsurePoolInitialized: 创建新的 SingleWeb Pool");
                }
                _enableAutoPooling = true;
            }
        }

        public bool IsPoolLoaded => _poolContainer != null && _initialized;
        public bool IsInitialized() => _initialized;
        public GameObject? GetStrandPatternsReference() => _strandPatterns;
        public GameObject? GetPattern1TemplateReference() => _pattern1Template;
        #endregion

        #region Object Pool
        private SingleWebBehavior? GetAvailableWeb()
        {
            if (_poolContainer == null)
            {
                Log.Warn("[SingleWebManager] 池容器未初始化，尝试创建...");
                EnsurePoolInitialized();
                if (_poolContainer == null)
                {
                    Log.Error("[SingleWebManager] 无法创建池容器");
                    return null;
                }
            }

            var availableWeb = _webPool.FirstOrDefault(w => w != null && w.IsAvailable);
            if (availableWeb != null)
            {
                return availableWeb;
            }

            return CreateNewWebInstance();
        }

        private SingleWebBehavior? CreateNewWebInstance()
        {
            if (_singleWebStrandPrefab == null)
            {
                Log.Error("单根丝线预制体未初始化，无法创建实例");
                return null;
            }

            if (_poolContainer == null)
            {
                Log.Error("对象池容器未初始化，无法创建实例");
                return null;
            }

            var webInstance = Instantiate(_singleWebStrandPrefab, _poolContainer.transform);
            webInstance.name = $"SingleWeb #{_webPool.Count}";
            webInstance.SetActive(true);

            ConfigureWebInstance(webInstance);

            var behavior = webInstance.AddComponent<SingleWebBehavior>();
            behavior.InitializeBehavior(_poolContainer.transform);

            _webPool.Add(behavior);
            return behavior;
        }

        private IEnumerator AutoPoolGeneration()
        {
            while (true)
            {
                yield return new WaitForSeconds(POOL_GENERATION_INTERVAL);

                if (!_enableAutoPooling || !_initialized || _singleWebStrandPrefab == null || _poolContainer == null)
                {
                    continue;
                }

                int currentPoolSize = _webPool.Count(w => w != null);
                if (currentPoolSize < MIN_POOL_SIZE)
                {
                    var newWeb = CreateNewWebInstance();
                    newWeb?.ResetCooldown();
                }
            }
        }
        #endregion


        #region Public Methods
        public SingleWebBehavior? SpawnAndAttack(
            Vector3 position,
            Vector3? rotation = null,
            Vector3? scale = null,
            float appearDelay = 0f,
            float burstDelay = 0.75f)
        {
            var webBehavior = GetAvailableWeb();
            if (webBehavior == null)
            {
                Log.Error("无法获取可用丝线，生成失败");
                return null;
            }

            webBehavior.transform.position = position;
            webBehavior.transform.eulerAngles = rotation ?? Vector3.zero;
            webBehavior.transform.localScale = scale ?? Vector3.one;

            webBehavior.TriggerAttack(appearDelay, burstDelay);
            return webBehavior;
        }

        public SingleWebBehavior? SpawnAndAttack(Vector3 position)
        {
            return SpawnAndAttack(position, null, null, 0f, 0.75f);
        }

        public SingleWebBehavior? SpawnWithFollowTarget(
            Vector3 position,
            Transform followTarget,
            Vector3? followOffset = null,
            Vector3? rotation = null,
            Vector3? scale = null,
            float appearDelay = 0f,
            float burstDelay = 0.75f)
        {
            var webBehavior = SpawnAndAttack(position, rotation, scale, appearDelay, burstDelay);
            webBehavior?.ConfigureFollowTarget(followTarget, followOffset);
            return webBehavior;
        }

        public SingleWebBehavior? SpawnWithContinuousRotation(
            Vector3 position,
            float rotationSpeed,
            Vector3? rotation = null,
            Vector3? scale = null,
            float appearDelay = 0f,
            float burstDelay = 0.75f)
        {
            var webBehavior = SpawnAndAttack(position, rotation, scale, appearDelay, burstDelay);
            webBehavior?.ConfigureContinuousRotation(true, rotationSpeed);
            return webBehavior;
        }

        public List<SingleWebBehavior> SpawnMultipleAndAttack(
            Vector3[] positions,
            Vector3? rotation = null,
            Vector3? scale = null,
            Vector2? randomAppearDelay = null,
            float burstDelay = 0.75f)
        {
            var behaviors = new List<SingleWebBehavior>();
            Vector2 delayRange = randomAppearDelay ?? new Vector2(0f, 0.3f);

            foreach (var pos in positions)
            {
                float randomDelay = Random.Range(delayRange.x, delayRange.y);
                var behavior = SpawnAndAttack(pos, rotation, scale, randomDelay, burstDelay);
                if (behavior != null)
                {
                    behaviors.Add(behavior);
                }
            }

            Log.Info($"批量生成了 {behaviors.Count} 根丝线并触发攻击");
            return behaviors;
        }

        public void ClearPool()
        {
            foreach (var web in _webPool)
            {
                if (web != null)
                {
                    web.StopAttack();
                    web.ResetCooldown();
                }
            }
            Log.Info($"已清空对象池（共 {_webPool.Count} 个丝线）");
        }

        public void EnsurePoolCapacity(int minCount)
        {
            if (!_initialized || _singleWebStrandPrefab == null || _poolContainer == null)
            {
                Log.Warn("SingleWebManager 未初始化，无法确保池容量");
                return;
            }

            int currentCount = _webPool.Count(w => w != null);
            int needed = minCount - currentCount;

            if (needed > 0)
            {
                Log.Info($"池当前有 {currentCount} 根丝线，需要 {needed} 根，开始预热...");
                for (int i = 0; i < needed; i++)
                {
                    var web = CreateNewWebInstance();
                    web?.ResetCooldown();
                }
                Log.Info($"池预热完成，当前有 {_webPool.Count(w => w != null)} 根丝线");
            }
        }
        #endregion


        #region Configuration
        private void ConfigureWebStrandPrefab()
        {
            if (_singleWebStrandPrefab == null)
            {
                Log.Error("单根丝线预制体为 null，无法配置");
                return;
            }

            Transform? heroCatcherTransform = FindChildRecursive(_singleWebStrandPrefab.transform, "hero_catcher");
            if (heroCatcherTransform == null)
            {
                Log.Error("未找到 hero_catcher，无法添加 DamageHero 组件");
                return;
            }

            GameObject heroCatcher = heroCatcherTransform.gameObject;

            var existingDamageHero = heroCatcher.GetComponent<DamageHero>();
            if (existingDamageHero != null)
            {
                Destroy(existingDamageHero);
            }

            var damageHero = heroCatcher.AddComponent<DamageHero>();
            damageHero.damageDealt = 2;
            damageHero.hazardType = GlobalEnums.HazardType.ENEMY;
            damageHero.resetOnEnable = false;
            damageHero.collisionSide = GlobalEnums.CollisionSide.top;
            damageHero.canClashTink = false;
            damageHero.noClashFreeze = true;
            damageHero.noTerrainThunk = true;
            damageHero.noTerrainRecoil = true;
            damageHero.hasNonBouncer = false;
            damageHero.overrideCollisionSide = false;
            damageHero.enabled = false;

            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                var damageHeroEventManager = managerObj.GetComponent<DamageHeroEventManager>();
                if (damageHeroEventManager != null && damageHeroEventManager.HasDamageHero())
                {
                    var originalDamageHero = damageHeroEventManager.DamageHero;
                    if (originalDamageHero?.OnDamagedHero != null)
                    {
                        damageHero.OnDamagedHero = originalDamageHero.OnDamagedHero;
                    }
                    else
                    {
                        damageHero.OnDamagedHero = new UnityEngine.Events.UnityEvent();
                    }
                }
                else
                {
                    damageHero.OnDamagedHero = new UnityEngine.Events.UnityEvent();
                }
            }
            else
            {
                damageHero.OnDamagedHero = new UnityEngine.Events.UnityEvent();
            }

            Log.Info($"已配置预制体 DamageHero: 伤害={damageHero.damageDealt}");
        }

        private void ConfigureWebInstance(GameObject webInstance)
        {
            var rb2d = webInstance.GetComponent<Rigidbody2D>();
            if (rb2d == null)
            {
                rb2d = webInstance.AddComponent<Rigidbody2D>();
            }

            rb2d.bodyType = RigidbodyType2D.Dynamic;
            rb2d.gravityScale = 0f;
            rb2d.linearDamping = 0f;
            rb2d.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            webInstance.layer = LayerMask.NameToLayer("Enemy Attack");

            var heroCatcher = FindChildRecursive(webInstance.transform, "hero_catcher");
            if (heroCatcher != null)
            {
                heroCatcher.gameObject.layer = LayerMask.NameToLayer("Enemy Attack");
                var collider = heroCatcher.GetComponent<Collider2D>();
                if (collider != null)
                {
                    collider.isTrigger = true;
                }
            }
        }
        #endregion


        #region Cleanup
        public void CleanupPool()
        {
            Log.Info("=== 开始清理 SingleWebManager 缓存 ===");

            StopAllCoroutines();
            _enableAutoPooling = false;

            int destroyedCount = 0;
            foreach (var web in _webPool)
            {
                if (web != null && web.gameObject != null)
                {
                    Destroy(web.gameObject);
                    destroyedCount++;
                }
            }
            _webPool.Clear();
            Log.Info($"已销毁对象池中的 {destroyedCount} 个丝线实例");

            if (_poolContainer != null)
            {
                Destroy(_poolContainer);
                _poolContainer = null;
            }

            if (_singleWebStrandPrefab != null)
            {
                Destroy(_singleWebStrandPrefab);
                _singleWebStrandPrefab = null;
            }

            _strandPatterns = null;
            _pattern1Template = null;
            _initialized = false;

            Log.Info("=== SingleWebManager 清理完成 ===");
        }
        #endregion

        #region Utility
        private Transform? FindChildRecursive(Transform parent, string childName)
        {
            Transform? child = parent.Find(childName);
            if (child != null)
                return child;

            foreach (Transform t in parent)
            {
                child = FindChildRecursive(t, childName);
                if (child != null)
                    return child;
            }

            return null;
        }
        #endregion
    }
}
