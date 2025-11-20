using System.Collections;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;

namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// 单根丝线行为组件
    /// 负责单根丝线的FSM修改、攻击流程控制和生命周期管理
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

            Log.Info($"=== 初始化 SingleWebBehavior: {gameObject.name} ===");

            // 0. 保存池容器引用
            _poolContainer = poolContainer;

            // 1. 获取 Control FSM
            _controlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
            if (_controlFsm == null)
            {
                Log.Error($"未找到 Control FSM，初始化失败: {gameObject.name}");
                return;
            }
            Log.Info("  找到 Control FSM");

            // 2. 查找 hero_catcher
            _heroCatcher = FindChildRecursive(transform, "hero_catcher");
            if (_heroCatcher == null)
            {
                Log.Error($"未找到 hero_catcher，初始化失败: {gameObject.name}");
                return;
            }
            Log.Info("  找到 hero_catcher");

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
            gameObject.transform.SetScaleX(2f);
            // 4. 修改 Control FSM
            ModifyControlFsm();

            // 5. 禁用 Hornet Catch FSM
            DisableHornetCatchFsm();

            _initialized = true;
            Log.Info($"=== SingleWebBehavior 初始化完成: {gameObject.name} ===");
        }

        /// <summary>
        /// 修改 Control FSM，简化攻击流程
        /// </summary>
        private void ModifyControlFsm()
        {
            if (_controlFsm == null)
            {
                Log.Error("Control FSM 为 null，无法修改");
                return;
            }

            Log.Info("--- 修改 Control FSM ---");

            // 找到 Catch Hero 状态
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
            Log.Info("  已清空 Catch Hero 状态的所有 Actions");

            // 修改跳转：简化为直接回到 Catch Cancel（保持原版流程）
            var inactiveState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Catch Cancel");
            if (inactiveState != null)
            {
                catchHeroState.Transitions = new FsmTransition[]
                {
                    new FsmTransition
                    {
                        FsmEvent = FsmEvent.GetFsmEvent("FINISHED"),
                        ToFsmState = inactiveState,
                        ToState = "Catch Cancel"
                    }
                };
                Log.Info("  已修改 Catch Hero 跳转：FINISHED -> Catch Cancel");
            }

            // 重新初始化 FSM
            _controlFsm.Fsm.InitData();
            _controlFsm.Fsm.InitEvents();
            Log.Info("  Control FSM 修改完成并已重新初始化");
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
                Log.Info("  已禁用 Hornet Catch FSM");
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

            Log.Info($"--- 开始攻击: {gameObject.name} ---");

            // 1. 脱离父对象（从池中取出）
            if (transform.parent == _poolContainer)
            {
                transform.SetParent(null);
                Log.Debug($"  已从池容器脱离");
            }

            // 2. 等待出现延迟
            if (appearDelay > 0)
            {
                yield return new WaitForSeconds(appearDelay);
            }

            // 3. 发送 APPEAR 事件（对象已经是激活状态）
            _controlFsm.SendEvent("APPEAR");
            Log.Info($"  [时间 {appearDelay}s] 已发送 APPEAR 事件");

            // 4. 等待爆发延迟
            yield return new WaitForSeconds(burstDelay);

            // 5. 发送 BURST 事件并启用 DamageHero
            _controlFsm.SendEvent("BURST");
            EnableDamageHero();
            Log.Info($"  [时间 {appearDelay + burstDelay}s] 已发送 BURST 事件并启用伤害");

            // 6. 等待攻击完成（原版 Burst 状态持续 1.75 秒）
            yield return new WaitForSeconds(1.75f);

            // 7. 禁用 DamageHero
            DisableDamageHero();
            Log.Info($"  [时间 {appearDelay + burstDelay + 1.75f}s] 攻击完成，已禁用伤害");

            // 8. 重置状态并回到池中
            ResetToPoolContainer();
            _isAttacking = false;

            Log.Info($"--- 攻击结束: {gameObject.name}，开始 {CooldownDuration}s 冷却 ---");

            // 9. 等待冷却时间后重新可用
            yield return new WaitForSeconds(CooldownDuration);
            _isAvailable = true;
            Log.Info($"丝线 {gameObject.name} 冷却完成，已回到可用池");
        }

        /// <summary>
        /// 强制停止攻击并重置（用于清理或中断）
        /// </summary>
        public void StopAttack()
        {
            StopAllCoroutines();
            _isAttacking = false;
            DisableDamageHero();
            ResetToPoolContainer();
            
            // 注意：强制停止不会重置冷却，需要手动调用 ResetCooldown
        }

        /// <summary>
        /// 重置冷却（立即可用）
        /// </summary>
        public void ResetCooldown()
        {
            _isAvailable = true;
            Log.Debug($"丝线 {gameObject.name} 冷却已重置");
        }

        /// <summary>
        /// 重置并回到池容器
        /// </summary>
        private void ResetToPoolContainer()
        {
            if (_controlFsm != null)
            {
                // 发送 ATTACK CLEAR 全局事件（原版用于清理攻击）
                // 这会触发全局跳转到 Inactive 状态，自动禁用 web_strand_single
                _controlFsm.SendEvent("ATTACK CLEAR");
                Log.Debug($"  已发送 ATTACK CLEAR 事件");
            }

            // 注意：不禁用父对象，FSM 会自动处理 web_strand_single 的禁用
            // gameObject.SetActive(false);  // 移除这行

            // 回到池容器
            if (_poolContainer != null)
            {
                transform.SetParent(_poolContainer);
                Log.Debug($"  已回到池容器");
            }
        }

        /// <summary>
        /// 启用 DamageHero
        /// </summary>
        private void EnableDamageHero()
        {
            if (_damageHero != null)
            {
                _damageHero.enabled = true;
                Log.Debug($"  已启用 DamageHero");
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
                Log.Debug($"  已禁用 DamageHero");
            }
        }

        /// <summary>
        /// 设置 DamageHero 的事件（从外部传入，备用方法，通常不需要手动调用）
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
                Log.Debug($"  已设置 DamageHero 事件（外部传入）: {gameObject.name}");
            }
        }

        /// <summary>
        /// 设置池容器引用（用于父对象管理）
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

