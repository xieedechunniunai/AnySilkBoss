using System.Collections;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 地刺行为控制器 - 管理单个地刺(Spike Floor)的行为
    /// 
    /// 核心逻辑：
    /// 1. 地刺通过 "Extra Tag" 标记空闲状态
    /// 2. 攻击时移除 Tag，攻击结束后恢复 Tag
    /// 3. 在 Spikes End 状态触发下一个空闲地刺（形成循环）
    /// 4. P1-P6 阶段分别保持 1-6 个地刺同时攻击
    /// </summary>
    internal class MemorySpikeFloorBehavior : MonoBehaviour
    {
        #region 配置参数
        [Header("地刺配置")]
        public int spikeIndex = 0;                    // 地刺索引 (0-5)
        public string idleTag = "Extra Tag";          // 空闲状态标记
        public float chainTriggerDelay = 0.1f;        // 链式触发延迟（秒）
        
        #endregion

        #region 组件引用
        private PlayMakerFSM? _controlFsm;            // 地刺的 Control FSM
        private Transform? _spikeFloorsParent;        // Spike Floors 父物体
        #endregion

        #region 状态标记
        private bool _isInitialized = false;
        private bool _isAttacking = false;            // 是否正在攻击
        private int _currentPhase = 1;                // 当前阶段
        #endregion

        #region FSM 状态和事件引用
        private FsmState? _idleState;
        private FsmState? _spikesEndState;
        private FsmEvent? _attackEvent;
        #endregion

        private void Start()
        {
            StartCoroutine(DelayedSetup());
        }

        /// <summary>
        /// 延迟初始化
        /// </summary>
        private IEnumerator DelayedSetup()
        {
            yield return null; // 等待一帧

            GetComponents();
            
            if (_controlFsm == null)
            {
                Log.Error($"[SpikeFloor {spikeIndex}] 未找到 Control FSM");
                yield break;
            }

            ModifyControlFSM();
            _isInitialized = true;
            
            Log.Info($"[SpikeFloor {spikeIndex}] 初始化完成");
        }

        /// <summary>
        /// 获取组件引用
        /// </summary>
        private void GetComponents()
        {
            // 获取 Control FSM
            _controlFsm = FSMUtility.LocateMyFSM(gameObject, "Control");
            
            // 获取父物体引用
            _spikeFloorsParent = transform.parent;
            
            // 从名称解析索引（如 "Spike Floor 1" -> 0）
            var nameParts = gameObject.name.Split(' ');
            if (nameParts.Length >= 3 && int.TryParse(nameParts[2], out int index))
            {
                spikeIndex = index - 1; // 转为0索引
            }
        }

        /// <summary>FSM 输出路径</summary>
        private const string FSM_OUTPUT_PATH = "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\";

        /// <summary>
        /// 修改 Control FSM
        /// </summary>
        private void ModifyControlFSM()
        {
            if (_controlFsm == null) return;

            // 输出修改前的 FSM 报告（只对第一个地刺输出，避免重复）
            if (spikeIndex == 0)
            {
                string prePath = FSM_OUTPUT_PATH + "_spikeFloor_preModify.txt";
                FsmAnalyzer.WriteFsmReport(_controlFsm, prePath);
                Log.Debug($"[SpikeFloor {spikeIndex}] 修改前 FSM 报告已输出: {prePath}");
            }

            // 获取状态引用
            _idleState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Idle");
            _spikesEndState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Spikes End");
            
            // 获取事件引用
            _attackEvent = FsmEvent.GetFsmEvent("ATTACK");

            // 修改 Antic 状态 - 将 Hits 从 2 改为 4
            ModifyAnticState();

            // 修改 Tink 状态 - 将 FloatAdd 从 -2.5 改为 -1
            ModifyTinkState();

            // 修改 Spikes End 状态 - 添加链式触发逻辑
            if (_spikesEndState != null)
            {
                ModifySpikesEndState();
            }
            else
            {
                Log.Warn($"[SpikeFloor {spikeIndex}] 未找到 Spikes End 状态");
            }

            // 修改 Idle 状态 - 添加攻击开始回调
            if (_idleState != null)
            {
                ModifyIdleState();
            }

            // 重新初始化 FSM
            _controlFsm.Fsm.InitData();

            // 输出修改后的 FSM 报告（只对第一个地刺输出）
            if (spikeIndex == 0)
            {
                string postPath = FSM_OUTPUT_PATH + "_spikeFloor_postModify.txt";
                FsmAnalyzer.WriteFsmReport(_controlFsm, postPath);
                Log.Debug($"[SpikeFloor {spikeIndex}] 修改后 FSM 报告已输出: {postPath}");
            }
        }

        /// <summary>
        /// 修改 Antic 状态 - 将 Hits 从 2 改为 4（增加地刺耐久）
        /// </summary>
        private void ModifyAnticState()
        {
            if (_controlFsm == null) return;

            var anticState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Antic");
            if (anticState == null)
            {
                Log.Warn($"[SpikeFloor {spikeIndex}] 未找到 Antic 状态");
                return;
            }

            foreach (var action in anticState.Actions)
            {
                // 查找 SetIntValue 动作（设置 Hits）
                if (action is SetIntValue setIntAction)
                {
                    // 将 Hits 从 2 改为 4
                    if (setIntAction.intValue.Value == 2)
                    {
                        setIntAction.intValue.Value = 4;
                        Log.Info($"[SpikeFloor {spikeIndex}] Antic 状态 Hits 修改: 2 -> 4");
                    }
                }
            }
        }

        /// <summary>
        /// 修改 Tink 状态 - 将 FloatAdd 从 -2.5 改为 -1（减少每次受击的时间扣除）
        /// </summary>
        private void ModifyTinkState()
        {
            if (_controlFsm == null) return;

            var tinkState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Tink");
            if (tinkState == null)
            {
                Log.Warn($"[SpikeFloor {spikeIndex}] 未找到 Tink 状态");
                return;
            }

            foreach (var action in tinkState.Actions)
            {
                // 查找 FloatAdd 动作（调整 Spike Time）
                if (action is FloatAdd floatAddAction)
                {
                    // 将 -2.5 改为 -1
                    if (Mathf.Approximately(floatAddAction.add.Value, -2.5f))
                    {
                        floatAddAction.add.Value = -1f;
                        Log.Info($"[SpikeFloor {spikeIndex}] Tink 状态 FloatAdd 修改: -2.5 -> -1");
                    }
                }
            }
        }

        /// <summary>
        /// 修改 Idle 状态 - 移除自动攻击，改为等待外部 ATTACK 事件
        /// </summary>
        private void ModifyIdleState()
        {
            if (_idleState == null || _controlFsm == null) return;

            // 移除 Idle 状态中的 Wait 动作（原版会自动 1s 后触发 ATTACK）
            // 改为只监听 ATTACK 和 SPIKE INTRO 事件
            var actions = _idleState.Actions.ToList();
            actions.RemoveAll(a => a is Wait); // 移除 Wait 动作，阻止自动攻击
            _idleState.Actions = actions.ToArray();
            Log.Info($"[SpikeFloor {spikeIndex}] Idle 状态已移除自动攻击");

            // 在 Attack Delay 状态添加攻击开始回调
            var attackDelayState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Attack Delay");
            if (attackDelayState != null)
            {
                var onAttackStartAction = new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("OnAttackStart") { Value = "OnAttackStart" },
                    parameters = new FsmVar[0]
                };
                var delayActions = attackDelayState.Actions.ToList();
                delayActions.Insert(0, onAttackStartAction);
                attackDelayState.Actions = delayActions.ToArray();
                Log.Info($"[SpikeFloor {spikeIndex}] 已在 Attack Delay 状态添加攻击开始回调");
            }
        }

        /// <summary>
        /// 修改 Spikes End 状态 - 添加链式触发逻辑
        /// </summary>
        private void ModifySpikesEndState()
        {
            if (_spikesEndState == null || _controlFsm == null) return;

            // 获取原有动作
            var originalActions = _spikesEndState.Actions.ToList();

            // 创建攻击结束回调（在状态结束时触发下一个地刺）
            var onAttackEndAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("OnAttackEnd") { Value = "OnAttackEnd" },
                parameters = new FsmVar[0]
            };

            // 将回调添加到动作列表开头
            originalActions.Insert(0, onAttackEndAction);
            _spikesEndState.Actions = originalActions.ToArray();

            Log.Info($"[SpikeFloor {spikeIndex}] 已修改 Spikes End 状态，添加链式触发逻辑");
        }

        #region 公共方法（供 FSM 调用）

        /// <summary>
        /// 攻击开始回调 - 移除空闲标记
        /// </summary>
        public void OnAttackStart()
        {
            _isAttacking = true;
            
            // 移除空闲标记 Tag（原版 FSM 已经做了这个，但确保一下）
            Log.Debug($"[SpikeFloor {spikeIndex}] 攻击开始");
        }

        /// <summary>
        /// 攻击结束回调 - 恢复空闲标记并触发下一个地刺
        /// </summary>
        public void OnAttackEnd()
        {
            _isAttacking = false;
            
            Log.Debug($"[SpikeFloor {spikeIndex}] 攻击结束，触发下一个地刺");
            
            // 延迟触发下一个地刺
            StartCoroutine(TriggerNextSpikeDelayed());
        }

        /// <summary>
        /// 延迟触发下一个地刺
        /// </summary>
        private IEnumerator TriggerNextSpikeDelayed()
        {
            yield return new WaitForSeconds(chainTriggerDelay);
            TriggerNextSpike();
        }

        /// <summary>
        /// 触发下一个空闲地刺
        /// </summary>
        private void TriggerNextSpike()
        {
            if (_spikeFloorsParent == null)
            {
                Log.Warn($"[SpikeFloor {spikeIndex}] 父物体引用为空");
                return;
            }

            // 查找所有带有 "Extra Tag" 的空闲地刺
            GameObject? targetSpike = FindClosestIdleSpike();
            
            if (targetSpike != null)
            {
                // 发送 ATTACK 事件
                var targetFsm = FSMUtility.LocateMyFSM(targetSpike, "Control");
                if (targetFsm != null)
                {
                    targetFsm.SendEvent("ATTACK");
                    Log.Debug($"[SpikeFloor {spikeIndex}] 触发下一个地刺: {targetSpike.name}");
                }
            }
            else
            {
                Log.Debug($"[SpikeFloor {spikeIndex}] 没有找到空闲地刺");
            }
        }

        /// <summary>
        /// 查找最近的空闲地刺（带 Extra Tag）
        /// </summary>
        private GameObject? FindClosestIdleSpike()
        {
            if (_spikeFloorsParent == null) return null;

            // 获取玩家位置
            var hero = HeroController.instance;
            if (hero == null) return null;
            
            Vector3 heroPos = hero.transform.position;
            
            GameObject? closestSpike = null;
            float closestDistance = float.MaxValue;

            // 遍历所有地刺
            foreach (Transform child in _spikeFloorsParent)
            {
                // 跳过自己
                if (child.gameObject == gameObject) continue;
                
                // 检查是否有 Extra Tag（通过检查 FSM 变量或标签）
                var behavior = child.GetComponent<MemorySpikeFloorBehavior>();
                if (behavior != null && behavior._isAttacking) continue; // 跳过正在攻击的

                // 检查 FSM 状态（如果在 Idle 状态则为空闲）
                var fsm = FSMUtility.LocateMyFSM(child.gameObject, "Control");
                if (fsm != null && fsm.ActiveStateName == "Idle")
                {
                    float distance = Vector3.Distance(heroPos, child.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestSpike = child.gameObject;
                    }
                }
            }

            return closestSpike;
        }

        /// <summary>
        /// 设置当前阶段
        /// </summary>
        public void SetPhase(int phase)
        {
            _currentPhase = phase;
            Log.Debug($"[SpikeFloor {spikeIndex}] 阶段设置为 {phase}");
        }

        /// <summary>
        /// 强制触发攻击（供外部调用）
        /// </summary>
        public void ForceAttack()
        {
            if (_controlFsm != null && !_isAttacking)
            {
                _controlFsm.SendEvent("ATTACK");
                Log.Debug($"[SpikeFloor {spikeIndex}] 强制触发攻击");
            }
        }

        /// <summary>
        /// 检查是否空闲
        /// </summary>
        public bool IsIdle()
        {
            return !_isAttacking && _controlFsm != null && _controlFsm.ActiveStateName == "Idle";
        }

        #endregion

        #region 静态方法

        /// <summary>
        /// 初始化所有地刺
        /// </summary>
        public static void InitializeAllSpikeFloors(GameObject spikeFloorsParent)
        {
            if (spikeFloorsParent == null)
            {
                Log.Error("Spike Floors 父物体为空");
                return;
            }

            int count = 0;
            foreach (Transform child in spikeFloorsParent.transform)
            {
                // 添加行为组件（如果还没有）
                var behavior = child.GetComponent<MemorySpikeFloorBehavior>();
                if (behavior == null)
                {
                    behavior = child.gameObject.AddComponent<MemorySpikeFloorBehavior>();
                    count++;
                }
            }

            Log.Info($"初始化了 {count} 个地刺行为组件");
        }

        /// <summary>
        /// 触发指定数量的地刺攻击
        /// </summary>
        public static void TriggerSpikeAttacks(GameObject spikeFloorsParent, int count)
        {
            if (spikeFloorsParent == null) return;

            var behaviors = spikeFloorsParent.GetComponentsInChildren<MemorySpikeFloorBehavior>();
            int triggered = 0;

            foreach (var behavior in behaviors)
            {
                if (triggered >= count) break;
                
                if (behavior.IsIdle())
                {
                    behavior.ForceAttack();
                    triggered++;
                }
            }

            Log.Info($"触发了 {triggered}/{count} 个地刺攻击");
        }

        /// <summary>
        /// 设置所有地刺的阶段
        /// </summary>
        public static void SetAllSpikesPhase(GameObject spikeFloorsParent, int phase)
        {
            if (spikeFloorsParent == null) return;

            var behaviors = spikeFloorsParent.GetComponentsInChildren<MemorySpikeFloorBehavior>();
            foreach (var behavior in behaviors)
            {
                behavior.SetPhase(phase);
            }

            Log.Info($"设置所有地刺阶段为 P{phase}");
        }

        #endregion
    }
}
