using System.Collections;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;

namespace AnySilkBoss.Source.Behaviours.Common
{
    /// <summary>
    /// 统一版单根丝线行为组件
    /// 负责单根丝线的FSM修改、攻击流程控制和生命周期管理
    /// 合并了普通版和梦境版的所有功能
    /// </summary>
    internal class SingleWebBehavior : MonoBehaviour
    {
        #region Fields
        // FSM 引用
        private PlayMakerFSM? _controlFsm;

        // DamageHero 引用
        private DamageHero? _damageHero;
        private Transform? _heroCatcher;

        // 父对象引用（对象池容器）
        private Transform? _poolContainer;

        // 冷却系统
        private bool _isAvailable = true;  // 是否可用（不在冷却中）
        private const float CooldownDuration = 6f;  // 冷却时间6秒

        // 攻击参数
        private bool _isAttacking = false;

        // 高级功能（来自 Memory 版本）
        private Transform? _followTarget;
        private Vector3 _followOffset = Vector3.zero;
        private bool _enableFollowTarget = false;
        private bool _enableContinuousRotation = false;
        private float _continuousRotationSpeed = 0f;

        // 音频资源引用（从 SingleWebManager 获取）
        private AudioClip? _appearAudioClip;
        private AudioClip? _burstAudioClip;
        private GameObject? _audioPlayerPrefab;

        // 音效控制
        private bool _enableAudio = true;  // 是否启用音效
        private FsmStateAction? _appearAudioAction;  // 出现音效 Action 引用
        private FsmStateAction? _burstAudioAction;   // 爆发音效 Action 引用

        // 初始化标志
        private bool _initialized = false;
        #endregion

        #region Properties
        /// <summary>
        /// 是否可用（不在冷却中且未在攻击中）
        /// </summary>
        public bool IsAvailable => _isAvailable && !_isAttacking;

        /// <summary>
        /// 是否正在攻击
        /// </summary>
        public bool IsAttacking => _isAttacking;
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            gameObject.SetActive(true);
        }

        private void OnEnable()
        {
            // 每次激活时重新初始化（如果需要）
            if (!_initialized)
            {
                InitializeBehavior();
            }
        }

        private void OnDisable()
        {
            // 禁用时停止所有协程
            StopAllCoroutines();
            _isAttacking = false;
            ResetDynamicSettings();
        }

        private void LateUpdate()
        {
            if (!_isAttacking)
            {
                return;
            }

            if (_enableFollowTarget && _followTarget != null)
            {
                transform.position = _followTarget.position + _followOffset;
            }

            if (_enableContinuousRotation && Mathf.Abs(_continuousRotationSpeed) > 0.001f)
            {
                transform.Rotate(0f, 0f, _continuousRotationSpeed * Time.deltaTime);
            }
        }
        #endregion


        #region Initialization
        /// <summary>
        /// 初始化行为组件
        /// </summary>
        /// <param name="poolContainer">对象池容器（用于父对象管理）</param>
        public void InitializeBehavior(Transform? poolContainer = null)
        {
            if (_initialized)
            {
                Log.Debug($"SingleWebBehavior 已初始化: {gameObject.name}");
                return;
            }

            // 0. 保存池容器引用
            _poolContainer = poolContainer;

            // 1. 获取 Control FSM
            _controlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
            if (_controlFsm == null)
            {
                Log.Error($"未找到 Control FSM，初始化失败: {gameObject.name}");
                return;
            }

            // 2. 查找 hero_catcher
            _heroCatcher = FindChildRecursive(transform, "hero_catcher");
            if (_heroCatcher == null)
            {
                Log.Error($"未找到 hero_catcher，初始化失败: {gameObject.name}");
                return;
            }

            // 3. 获取 DamageHero 组件并设置事件
            _damageHero = _heroCatcher.GetComponent<DamageHero>();
            if (_damageHero == null)
            {
                Log.Warn("  hero_catcher 没有 DamageHero 组件，跳过");
            }
            else
            {
                // 默认禁用，等待攻击时启用
                _damageHero.enabled = false;
            }

            // 4. 获取音频资源（从 SingleWebManager）
            CacheAudioResources();

            // 5. 修改 Control FSM
            ModifyControlFsm();

            // 6. 禁用 Hornet Catch FSM
            DisableHornetCatchFsm();

            _initialized = true;
            Log.Info($"=== SingleWebBehavior 初始化完成: {gameObject.name} ===");
        }

        /// <summary>
        /// 从 SingleWebManager 缓存音频资源
        /// </summary>
        private void CacheAudioResources()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj == null)
            {
                Log.Warn("未找到 AnySilkBossManager，无法获取音频资源");
                return;
            }

            var singleWebManager = managerObj.GetComponent<SingleWebManager>();
            if (singleWebManager == null)
            {
                Log.Warn("未找到 SingleWebManager 组件，无法获取音频资源");
                return;
            }

            _appearAudioClip = singleWebManager.AppearAudioClip;
            _burstAudioClip = singleWebManager.BurstAudioClip;
            _audioPlayerPrefab = singleWebManager.AudioPlayerPrefab;

            if (_appearAudioClip != null && _burstAudioClip != null)
            {
                Log.Debug($"音频资源已缓存: appear={_appearAudioClip.name}, burst={_burstAudioClip.name}");
            }
        }

        /// <summary>
        /// 修改 Control FSM，简化攻击流程并添加音效
        /// </summary>
        private void ModifyControlFsm()
        {
            if (_controlFsm == null)
            {
                Log.Error("Control FSM 为 null，无法修改");
                return;
            }

            // 1. 在 Appear 状态添加出现音效
            AddAppearAudio();

            // 2. 在 Burst Start 状态添加爆发音效
            AddBurstAudio();

            // 3. 修改 Catch Hero 状态（移除投技相关逻辑）
            var catchHeroState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Catch Hero");
            if (catchHeroState == null)
            {
                Log.Warn("未找到 Catch Hero 状态，跳过修改");
                return;
            }

            // 清空原有 Actions（移除投技相关逻辑）
            var actions = catchHeroState.Actions.ToList();
            actions.Clear();
            catchHeroState.Actions = actions.ToArray();

            // 修改跳转：简化为直接回到 Catch Cancel（保持原版流程）
            var inactiveState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Catch Cancel");
            if (inactiveState != null)
            {
                catchHeroState.Transitions =
                [
                    new()
                    {
                        FsmEvent = FsmEvent.GetFsmEvent("FINISHED"),
                        ToFsmState = inactiveState,
                        ToState = "Catch Cancel"
                    }
                ];
            }

            // 重新初始化 FSM
            _controlFsm.Fsm.InitData();
        }

        /// <summary>
        /// 在 Appear 状态开头添加出现音效（PlayAudioEvent）
        /// </summary>
        private void AddAppearAudio()
        {
            if (_controlFsm == null || _appearAudioClip == null || _audioPlayerPrefab == null)
            {
                return;
            }

            var appearState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Appear");
            if (appearState == null)
            {
                Log.Warn("未找到 Appear 状态，无法添加出现音效");
                return;
            }

            // 创建 PlayAudioEvent Action
            var playAudioAction = new PlayAudioEvent
            {
                Fsm = _controlFsm.Fsm,
                audioClip = new FsmObject { Value = _appearAudioClip },
                audioPlayerPrefab = new FsmObject { Value = _audioPlayerPrefab.GetComponent<AudioSource>() },
                pitchMin = new FsmFloat { Value = 1f },
                pitchMax = new FsmFloat { Value = 1f },
                volume = new FsmFloat { Value = 1f },
                spawnPoint = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                spawnPosition = new FsmVector3 { Value = Vector3.zero },
                SpawnedPlayerRef = new FsmGameObject { Value = null },
                Enabled = _enableAudio  // 根据初始设置决定是否启用
            };

            // 保存引用以便后续控制
            _appearAudioAction = playAudioAction;

            // 在状态开头插入音效 Action
            var actions = appearState.Actions.ToList();
            actions.Insert(0, playAudioAction);
            appearState.Actions = actions.ToArray();

            Log.Debug($"已在 Appear 状态添加出现音效: {_appearAudioClip.name}, Enabled={_enableAudio}");
        }

        /// <summary>
        /// 在 Burst Start 状态开头添加爆发音效（AudioPlayerOneShotSingleV2）
        /// </summary>
        private void AddBurstAudio()
        {
            if (_controlFsm == null || _burstAudioClip == null || _audioPlayerPrefab == null)
            {
                return;
            }

            var burstStartState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Burst Start");
            if (burstStartState == null)
            {
                Log.Warn("未找到 Burst Start 状态，无法添加爆发音效");
                return;
            }

            // 创建 AudioPlayerOneShotSingleV2 Action
            // 必须初始化所有字段，否则 DoPlayRandomClip 会空引用
            var audioAction = new AudioPlayerOneShotSingleV2
            {
                Fsm = _controlFsm.Fsm,
                audioPlayer = new FsmGameObject { Value = _audioPlayerPrefab },
                spawnPoint = new FsmGameObject { Value = gameObject },
                audioClip = new FsmObject { Value = _burstAudioClip },
                pitchMin = new FsmFloat { Value = 1f },
                pitchMax = new FsmFloat { Value = 1f },
                volume = new FsmFloat { Value = 1f },
                delay = new FsmFloat { Value = 0.5f },
                playVibration = new FsmBool { Value = false },
                vibrationDataAsset = new FsmObject { Value = null },
                storePlayer = new FsmGameObject { Value = null },
                Enabled = _enableAudio  // 根据初始设置决定是否启用
            };

            // 保存引用以便后续控制
            _burstAudioAction = audioAction;

            // 在状态开头插入音效 Action
            var actions = burstStartState.Actions.ToList();
            actions.Insert(0, audioAction);
            burstStartState.Actions = actions.ToArray();

            Log.Debug($"已在 Burst Start 状态添加爆发音效: {_burstAudioClip.name}, Enabled={_enableAudio}");
        }

        /// <summary>
        /// 禁用 Hornet Catch FSM（不再需要投技）
        /// </summary>
        private void DisableHornetCatchFsm()
        {
            var hornetCatchFsm = FSMUtility.LocateMyFSM(gameObject, "Hornet Catch");
            if (hornetCatchFsm != null)
            {
                hornetCatchFsm.enabled = false;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 触发完整攻击流程（出现 → 爆发攻击 → 回收）
        /// </summary>
        /// <param name="appearDelay">出现延迟（秒）</param>
        /// <param name="burstDelay">爆发延迟（秒）</param>
        public void TriggerAttack(float appearDelay = 0f, float burstDelay = 0.75f)
        {
            if (!_isAvailable)
            {
                Log.Warn($"丝线 {gameObject.name} 正在冷却中，无法触发攻击");
                return;
            }

            if (_isAttacking)
            {
                Log.Warn($"丝线 {gameObject.name} 正在攻击中，无法重复触发");
                return;
            }

            StartCoroutine(AttackCoroutine(appearDelay, burstDelay));
        }

        /// <summary>
        /// 配置跟随目标（Memory 版本功能）
        /// </summary>
        public void ConfigureFollowTarget(Transform? target, Vector3? offset = null)
        {
            _followTarget = target;
            _followOffset = offset ?? Vector3.zero;
            _enableFollowTarget = target != null;
        }

        /// <summary>
        /// 配置持续旋转（Memory 版本功能）
        /// </summary>
        public void ConfigureContinuousRotation(bool enable, float rotationSpeed)
        {
            _enableContinuousRotation = enable;
            _continuousRotationSpeed = enable ? rotationSpeed : 0f;
        }

        /// <summary>
        /// 设置是否启用音效（用于避免多根丝线同时播放导致音量过大）
        /// 通过控制 FSM Action 的 Enabled 属性实现
        /// </summary>
        public void SetAudioEnabled(bool enabled)
        {
            _enableAudio = enabled;
            
            // 动态更新已添加的音效 Action 的 Enabled 状态
            if (_appearAudioAction != null)
            {
                _appearAudioAction.Enabled = enabled;
            }
            if (_burstAudioAction != null)
            {
                _burstAudioAction.Enabled = enabled;
            }
        }


        /// <summary>
        /// 攻击协程
        /// </summary>
        private IEnumerator AttackCoroutine(float appearDelay, float burstDelay)
        {
            if (_controlFsm == null)
            {
                Log.Error($"Control FSM 为 null，无法执行攻击: {gameObject.name}");
                yield break;
            }

            _isAttacking = true;
            _isAvailable = false;  // 开始冷却

            Log.Debug($"--- 开始攻击: {gameObject.name} ---");

            // 1. 脱离父对象（从池中取出）
            if (transform.parent == _poolContainer)
            {
                transform.SetParent(null);
            }

            // 2. 等待出现延迟
            if (appearDelay > 0)
            {
                yield return new WaitForSeconds(appearDelay);
            }

            // 3. 发送 APPEAR 事件（对象已经是激活状态）
            _controlFsm.SendEvent("APPEAR");

            // 4. 等待爆发延迟
            yield return new WaitForSeconds(burstDelay);

            // 5. 发送 BURST 事件并启用 DamageHero
            _controlFsm.SendEvent("BURST");
            EnableDamageHero();
 
            // 6. 等待攻击完成（原版 Burst 状态持续 1.75 秒）
            yield return new WaitForSeconds(1.75f);

            // 7. 禁用 DamageHero
            DisableDamageHero();

            // 8. 重置状态并回到池中
            ResetToPoolContainer();
            _isAttacking = false;

            Log.Debug($"--- 攻击结束: {gameObject.name}，开始 {CooldownDuration}s 冷却 ---");

            // 9. 等待冷却时间后重新可用
            yield return new WaitForSeconds(CooldownDuration);
            _isAvailable = true;
        }

        /// <summary>
        /// 强制停止攻击并重置（用于清理或中断）
        /// </summary>
        public void StopAttack()
        {
            StopAllCoroutines();
            _isAttacking = false;
            DisableDamageHero();
            ResetDynamicSettings();
            ResetToPoolContainer();
        }

        /// <summary>
        /// 重置冷却（立即可用）
        /// </summary>
        public void ResetCooldown()
        {
            _isAvailable = true;
        }

        /// <summary>
        /// 重置并回到池容器
        /// </summary>
        private void ResetToPoolContainer()
        {
            ResetDynamicSettings();
            if (_controlFsm != null)
            {
                _controlFsm.SendEvent("ATTACK CLEAR");
            }

            if (_poolContainer != null)
            {
                transform.SetParent(_poolContainer);
            }
        }

        private void ResetDynamicSettings()
        {
            _followTarget = null;
            _followOffset = Vector3.zero;
            _enableFollowTarget = false;
            _enableContinuousRotation = false;
            _continuousRotationSpeed = 0f;
        }

        /// <summary>
        /// 启用 DamageHero
        /// </summary>
        private void EnableDamageHero()
        {
            if (_damageHero != null)
            {
                _damageHero.enabled = true;
            }
        }

        /// <summary>
        /// 禁用 DamageHero
        /// </summary>
        private void DisableDamageHero()
        {
            if (_damageHero != null)
            {
                _damageHero.enabled = false;
            }
        }

        /// <summary>
        /// 设置 DamageHero 的事件
        /// </summary>
        public void SetDamageHeroEvent(DamageHero originalDamageHero)
        {
            if (_damageHero == null)
            {
                Log.Warn($"DamageHero 组件为 null，无法设置事件: {gameObject.name}");
                return;
            }

            if (originalDamageHero != null && originalDamageHero.OnDamagedHero != null)
            {
                _damageHero.OnDamagedHero = originalDamageHero.OnDamagedHero;
            }
        }

        /// <summary>
        /// 设置池容器引用
        /// </summary>
        public void SetPoolContainer(Transform poolContainer)
        {
            _poolContainer = poolContainer;
        }
        #endregion

        #region Utility
        /// <summary>
        /// 递归查找子物体
        /// </summary>
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
