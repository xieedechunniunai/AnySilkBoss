using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Actions;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Behaviours.Common;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// Bomb Blast 行为组件 - 管理爆炸的 FSM 补丁、大小调整和丝球环生成
    /// 
    /// 功能：
    /// - FSM 补丁：移除 RecycleSelf，添加 CallMethod 回收
    /// - 大小调整：根据 customSize 设置爆炸尺寸（0=随机，>0直接使用）
    /// - 丝球环生成：在爆炸位置生成反向加速度丝球环
    /// </summary>
    public class BombBlastBehavior : MonoBehaviour
    {
        #region 配置参数
        /// <summary>爆炸大小（默认随机0.6-0.8，>0时直接使用该值）</summary>
        public float customSize = 0f;

        /// <summary>是否生成丝球环</summary>
        public bool spawnSilkBallRing = false;

        /// <summary>丝球数量</summary>
        public int silkBallCount = 8;

        /// <summary>丝球初始向外速度</summary>
        public float initialOutwardSpeed = 12f;

        /// <summary>反向加速度值</summary>
        public float reverseAcceleration = 25f;

        /// <summary>最大向内速度</summary>
        public float maxInwardSpeed = 50f;

        /// <summary>反向加速度持续时间</summary>
        public float reverseAccelDuration = 5f;

        /// <summary>丝球环模式：true=反向加速度，false=径向爆发</summary>
        public bool useReverseAccelMode = true;

        /// <summary>径向爆发速度</summary>
        public float radialBurstSpeed = 18f;

        /// <summary>释放前的等待时间</summary>
        public float releaseDelay = 0.3f;

        // ===== 移动模式配置（用于 BlastBurst3 汇聚爆炸） =====
        /// <summary>是否启用移动模式</summary>
        public bool enableMoveMode = false;

        /// <summary>移动目标 Transform</summary>
        public Transform? moveTarget;

        /// <summary>移动加速度</summary>
        public float moveAcceleration = 30f;

        /// <summary>移动最大速度</summary>
        public float moveMaxSpeed = 8f;

        /// <summary>到达目标的距离阈值</summary>
        public float reachDistance = 2f;

        /// <summary>移动超时时间（秒）</summary>
        public float moveTimeout = 5f;

        /// <summary>移动过程中是否显示爆炸特效（小型预览爆炸）</summary>
        public bool showTrailEffect = false;
        #endregion

        #region 内部引用
        private PlayMakerFSM? _controlFsm;
        private FWBlastManager? _blastManager;
        private SilkBallManager? _silkBallManager;
        private FsmFloat? _sizeVar;
        private bool _fsmPatched = false;
        private bool _isActive = false;
        #endregion

        #region 生命周期
        private void Awake()
        {
            _controlFsm = GetComponent<PlayMakerFSM>();
            GetManagerReferences();
        }

        private void Start()
        {
            // 如果还没补丁过（正常激活流程），在这里补丁
            // 注意：ConfigureMoveMode 可能已经提前调用了 EnsureFsmPatched
            if (_controlFsm != null && !_fsmPatched)
            {
                PatchFsm();
                _fsmPatched = true;
            }
        }

        private void OnEnable()
        {
            _isActive = true;
            // 移动模式的参数更新在 ConfigureMoveMode 中完成
            // FSM 结构已经在 PatchForMoveMode 中一次性创建好，不需要每次激活都修改
        }

        private void OnDisable()
        {
            _isActive = false;
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 配置爆炸参数（由 FWBlastManager.SpawnBombBlast 调用）
        /// </summary>
        public void Configure(bool spawnRing, int ballCount = 8,
            float outSpeed = 12f, float reverseAccel = 25f, float maxInSpeed = 30f, float duration = 5f,
            bool useReverseAccel = true, float radialSpeed = 18f, float delay = 0.3f)
        {
            spawnSilkBallRing = spawnRing;
            silkBallCount = ballCount;
            initialOutwardSpeed = outSpeed;
            reverseAcceleration = reverseAccel;
            maxInwardSpeed = maxInSpeed;
            reverseAccelDuration = duration;
            useReverseAccelMode = useReverseAccel;
            radialBurstSpeed = radialSpeed;
            releaseDelay = delay;
            // 注意：大小设置已迁移到 SetBlastSize()，在 FSM Blast 状态时调用
        }

        /// <summary>
        /// 配置移动模式参数（由 FWBlastManager.SpawnMovingBombBlast 调用）
        /// 注意：必须在 SetActive(true) 之前调用
        /// </summary>
        public void ConfigureMoveMode(Transform target, float accel = 30f, float maxSpeed = 8f,
            float reach = 2f, float timeout = 5f, bool trailEffect = false)
        {
            enableMoveMode = true;
            moveTarget = target;
            moveAcceleration = accel;
            moveMaxSpeed = maxSpeed;
            reachDistance = reach;
            moveTimeout = timeout;
            showTrailEffect = trailEffect;

            // 移动模式下添加 Rigidbody2D（如果没有的话）
            var rb = GetComponent<Rigidbody2D>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0f;
                rb.linearDamping = 0f;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            else
            {
                rb.gravityScale = 0f;
                rb.linearDamping = 0f;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            // 确保 FSM 已被补丁（对象可能是新创建的，Start 还没调用）
            EnsureFsmPatched();

            if (_controlFsm != null)
            {
                _controlFsm.Fsm.HandleFixedUpdate = true;
                _controlFsm.AddEventHandlerComponents();
            }

            // 更新 ChaseTargetAction 参数（Move To Target 和 Moving 状态）
            UpdateMoveTargetParams();
        }

        public void StopMove()
        {
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
        }

        /// <summary>
        /// 确保 FSM 已被补丁（供外部调用，如 ConfigureMoveMode）
        /// </summary>
        public void EnsureFsmPatched()
        {
            if (_fsmPatched) return;
            if (_controlFsm == null)
            {
                _controlFsm = GetComponent<PlayMakerFSM>();
            }
            if (_controlFsm != null)
            {
                PatchFsm();
                _fsmPatched = true;
                Log.Debug("[BombBlastBehavior] FSM 已在 ConfigureMoveMode 中提前补丁");
            }
        }

        /// <summary>
        /// 设置爆炸大小（由 FSM 的 Blast/Moving 状态调用，替代原 RandomFloat）
        /// customSize > 0 时直接使用该值，否则使用默认随机范围
        /// </summary>
        public void SetBlastSize()
        {
            if (_sizeVar == null) return;

            // 如果设置了自定义大小，直接使用（传入什么就是什么）
            if (customSize > 0f)
            {
                _sizeVar.Value = customSize;
                return;
            }

            // 否则使用默认随机大小 (0.6 - 0.8)
            _sizeVar.Value = Random.Range(0.6f, 0.8f);
        }

        /// <summary>
        /// 触发爆炸效果（由 FSM 的 Blast 状态调用）
        /// </summary>
        public void OnBlastTriggered()
        {
            if (spawnSilkBallRing && _silkBallManager != null)
            {
                StartCoroutine(SpawnSilkBallRingCoroutine());
            }
        }

        /// <summary>
        /// 回收爆炸（由 FSM 调用，替代原 RecycleSelf）
        /// </summary>
        public void RecycleBlast()
        {
            if (_blastManager != null)
            {
                _blastManager.RecycleBombBlast(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 重置配置（从池中取出时调用）
        /// </summary>
        public void ResetConfig()
        {
            customSize = 0f;
            spawnSilkBallRing = false;
            silkBallCount = 8;
            initialOutwardSpeed = 12f;
            reverseAcceleration = 25f;
            maxInwardSpeed = 30f;
            reverseAccelDuration = 5f;
            useReverseAccelMode = true;
            radialBurstSpeed = 18f;
            releaseDelay = 0.3f;

            // 重置移动模式配置
            enableMoveMode = false;
            moveTarget = null;
            moveAcceleration = 30f;
            moveMaxSpeed = 8f;
            reachDistance = 2f;
            moveTimeout = 5f;
            showTrailEffect = false;

            // 重置刚体速度
            var rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }

            // 清理 FSM 中 ChaseTargetAction 的目标引用（防止引用已销毁的对象）
            ClearChaseTargetReferences();

            // 重置 FSM Start 状态的跳转
            ResetStartTransition();
        }

        /// <summary>
        /// 清理 FSM 中所有 ChaseTargetAction 的目标引用
        /// </summary>
        private void ClearChaseTargetReferences()
        {
            if (_controlFsm == null) return;

            foreach (var state in _controlFsm.FsmStates)
            {
                if (state.Actions == null) continue;
                foreach (var action in state.Actions)
                {
                    if (action is ChaseTargetAction chaseAction)
                    {
                        chaseAction.targetTransform = null;
                    }
                }
            }
        }
        #endregion

        #region FSM 补丁
        /// <summary>
        /// 补丁 FSM：移除 RecycleSelf，添加 CallMethod 回收，支持移动模式
        /// </summary>
        private void PatchFsm()
        {
            if (_controlFsm == null) return;

            // 缓存 Size 变量引用
            _sizeVar = _controlFsm.FsmVariables.GetFsmFloat("Size");

            // 修改 Blast 状态：替换 RandomFloat 为自定义大小设置
            PatchBlastState();

            // 修改 End 状态：移除 RecycleSelf，添加 CallMethod
            PatchEndState();

            // 修改 Wait 状态：添加 OnBlastTriggered 调用
            PatchWaitState();

            // 添加移动模式支持
            PatchForMoveMode();
        }

        /// <summary>
        /// 补丁 Blast 状态：替换 RandomFloat 为 CallMethod(SetBlastSize)
        /// 这样可以根据 customSize 设置自定义大小
        /// </summary>
        private void PatchBlastState()
        {
            var blastState = _controlFsm!.FsmStates.FirstOrDefault(s => s.Name == "Blast");
            if (blastState == null) return;

            var actions = blastState.Actions.ToList();

            // 检查是否已经补丁过
            bool alreadyPatched = actions.Any(a => a is CallMethod cm &&
                cm.methodName?.Value == "SetBlastSize");
            if (alreadyPatched) return;

            // 找到并移除 RandomFloat action（索引0）
            int randomFloatIndex = -1;
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i] is RandomFloat)
                {
                    randomFloatIndex = i;
                    break;
                }
            }

            if (randomFloatIndex >= 0)
            {
                actions.RemoveAt(randomFloatIndex);

                // 在同一位置插入 CallMethod(SetBlastSize)
                var callMethod = new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("SetBlastSize") { Value = "SetBlastSize" },
                    parameters = new FsmVar[0],
                    storeResult = new FsmVar()
                };
                actions.Insert(randomFloatIndex, callMethod);

                blastState.Actions = actions.ToArray();
                Log.Debug("[BombBlastBehavior] FSM Blast 状态已补丁：RandomFloat -> CallMethod(SetBlastSize)");
            }
        }

        /// <summary>
        /// 补丁 End 状态：移除 RecycleSelf，添加 CallMethod 回收
        /// </summary>
        private void PatchEndState()
        {
            var endState = _controlFsm!.FsmStates.FirstOrDefault(s => s.Name == "End");
            if (endState == null) return;

            var actions = endState.Actions.ToList();

            // 移除 RecycleSelf
            actions.RemoveAll(a => a is RecycleSelf);

            // 添加 CallMethod 调用 RecycleBlast
            var callMethod = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("RecycleBlast") { Value = "RecycleBlast" },
                parameters = new FsmVar[0],
                storeResult = new FsmVar()
            };
            actions.Add(callMethod);

            endState.Actions = actions.ToArray();
            Log.Debug("[BombBlastBehavior] FSM End 状态已补丁：RecycleSelf -> CallMethod(RecycleBlast)");
        }

        /// <summary>
        /// 补丁 Wait 状态：添加 OnBlastTriggered 调用
        /// </summary>
        private void PatchWaitState()
        {
            var waitState = _controlFsm!.FsmStates.FirstOrDefault(s => s.Name == "Wait");
            if (waitState == null) return;

            var actions = waitState.Actions.ToList();

            // 检查是否已经补丁过
            bool alreadyPatched = actions.Any(a => a is CallMethod cm &&
                cm.methodName?.Value == "OnBlastTriggered");
            if (alreadyPatched) return;

            // 在开头添加 CallMethod 调用 OnBlastTriggered
            var callMethod = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("OnBlastTriggered") { Value = "OnBlastTriggered" },
                parameters = new FsmVar[0],
                storeResult = new FsmVar()
            };
            actions.Insert(0, callMethod);

            waitState.Actions = actions.ToArray();
            Log.Debug("[BombBlastBehavior] FSM Wait 状态已补丁：添加 CallMethod(OnBlastTriggered)");
        }

        /// <summary>
        /// 补丁 FSM 以支持移动模式（一次性补丁，事件驱动分支）
        /// 
        /// FSM 结构：
        /// Start -> FINISHED -> Mode Select
        /// Mode Select:
        ///   - START_NORMAL -> Appear Pause (正常流程)
        ///   - START_MOVE -> Move To Target
        /// Move To Target:
        ///   - ChaseTargetAction + WaitRandom(0.2-0.4s)
        ///   - FINISHED -> Moving
        /// Moving:
        ///   - ChaseTargetAction + SetBlastSize + ActivateGameObject
        ///   - Wait 2s -> FINISHED -> Wait
        /// </summary>
        private void PatchForMoveMode()
        {
            // 创建事件
            var startNormalEvent = FsmEvent.GetFsmEvent("START_NORMAL");
            var startMoveEvent = FsmEvent.GetFsmEvent("START_MOVE");
            var reachedBossEvent = FsmStateBuilder.GetOrCreateEvent(_controlFsm!, "REACHED_BOSS");

            // ===== 2. 获取现有状态 =====
            var startState = FsmStateBuilder.FindState(_controlFsm!, "Start");
            var appearPauseState = FsmStateBuilder.FindState(_controlFsm!, "Appear Pause");
            var waitState = FsmStateBuilder.FindState(_controlFsm!, "Wait");
            if (startState == null || appearPauseState == null || waitState == null)
            {
                Log.Warn("[BombBlastBehavior] 找不到必要的 FSM 状态");
                return;
            }

            // ===== 3. 创建 Moving 状态（先创建，因为 Move To Target 需要引用它） =====
            var movingState = FsmStateBuilder.GetOrCreateState(_controlFsm!, "Moving", "继续移动 + 爆炸效果");
            if (movingState.Actions == null || movingState.Actions.Length == 0)
            {
                movingState.Actions = new FsmStateAction[]
                {
                    new ChaseTargetAction
                    {
                        targetTransform = null,
                        acceleration = new FsmFloat { Value = 30f },
                        maxSpeed = new FsmFloat { Value = 8f },
                        useRigidbody = new FsmBool { Value = true },
                        chaseTime = new FsmFloat { Value = 10f },
                        reachDistance = new FsmFloat { Value = 0.5f },
                        onReachTarget = reachedBossEvent,
                        onTimeout = null
                    },
                    new CallMethod
                    {
                        behaviour = new FsmObject { Value = this },
                        methodName = new FsmString("SetBlastSize") { Value = "SetBlastSize" },
                        parameters = new FsmVar[0],
                        storeResult = new FsmVar()
                    },
                    new SetScale
                    {
                        gameObject = new FsmOwnerDefault
                        {
                            OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            GameObject = _controlFsm!.Fsm.GetFsmGameObject("Blast")
                        },
                        vector = new FsmVector3(),
                        x = _sizeVar,
                        y = _sizeVar,
                        z = new FsmFloat { Value = 0f },
                        everyFrame = false
                    },
                    new ActivateGameObject
                    {
                        gameObject = new FsmOwnerDefault
                        {
                            OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            GameObject = _controlFsm!.Fsm.GetFsmGameObject("Blast")
                        },
                        activate = new FsmBool { Value = true },
                        recursive = new FsmBool { Value = false },
                        resetOnExit = false,
                        everyFrame = false
                    },
                    new Wait
                    {
                        time = new FsmFloat { Value = 2f },
                        finishEvent = FsmEvent.Finished,
                        realTime = false
                    }
                };
            }

            var stopMoveState = FsmStateBuilder.GetOrCreateState(_controlFsm!, "Stop Move", "停止移动");
            stopMoveState.Actions = new FsmStateAction[]
            {
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("StopMove") { Value = "StopMove" },
                    parameters = new FsmVar[0],
                    storeResult = new FsmVar()
                },
                new Wait
                {
                    time = new FsmFloat { Value = 2f },
                    finishEvent = FsmEvent.Finished,
                    realTime = false
                }
            };
            FsmStateBuilder.SetFinishedTransition(stopMoveState, waitState);

            movingState.Transitions = new[]
            {
                FsmStateBuilder.CreateTransition(reachedBossEvent, stopMoveState)
            };

            // ===== 4. 创建 Move To Target 状态 =====
            var moveToTargetState = FsmStateBuilder.GetOrCreateState(_controlFsm!, "Move To Target", "追踪目标");
            if (moveToTargetState.Actions == null || moveToTargetState.Actions.Length == 0)
            {
                moveToTargetState.Actions = new FsmStateAction[]
                {
                    new ChaseTargetAction
                    {
                        targetTransform = null,
                        acceleration = new FsmFloat { Value = 30f },
                        maxSpeed = new FsmFloat { Value = 8f },
                        useRigidbody = new FsmBool { Value = true },
                        chaseTime = new FsmFloat { Value = 5f },
                        reachDistance = new FsmFloat { Value = 2f },
                        onReachTarget = null,
                        onTimeout = null
                    },
                    new WaitRandom
                    {
                        timeMin = new FsmFloat { Value = 0.2f },
                        timeMax = new FsmFloat { Value = 0.4f },
                        finishEvent = FsmEvent.Finished,
                        realTime = false
                    }
                };
                FsmStateBuilder.SetFinishedTransition(moveToTargetState, movingState);
            }

            // ===== 5. 创建 Mode Select 状态 =====
            var modeSelectState = FsmStateBuilder.GetOrCreateState(_controlFsm!, "Mode Select", "模式选择");
            if (modeSelectState.Actions == null || modeSelectState.Actions.Length == 0)
            {
                modeSelectState.Actions = new FsmStateAction[0];  // 无动作，纯等待事件
                modeSelectState.Transitions = new[]
                {
                    FsmStateBuilder.CreateTransition(startNormalEvent, appearPauseState),
                    FsmStateBuilder.CreateTransition(startMoveEvent, moveToTargetState)
                };
            }

            // ===== 6. 修改 Start 状态的跳转：FINISHED -> Mode Select =====
            for (int i = 0; i < startState.Transitions.Length; i++)
            {
                if (startState.Transitions[i].EventName == "FINISHED")
                {
                    startState.Transitions[i].ToFsmState = modeSelectState;
                    startState.Transitions[i].ToState = "Mode Select";
                    break;
                }
            }

            Log.Debug("[BombBlastBehavior] FSM 已补丁：Mode Select + Move To Target + Moving 状态已创建");
        }

        /// <summary>
        /// 更新移动目标参数（由 ConfigureMoveMode 调用）
        /// </summary>
        private void UpdateMoveTargetParams()
        {
            if (moveTarget == null || _controlFsm == null) return;

            // 更新 Move To Target 状态的 ChaseTargetAction
            var moveToTargetState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Move To Target");
            if (moveToTargetState != null)
            {
                var chaseAction = moveToTargetState.Actions?.OfType<ChaseTargetAction>().FirstOrDefault();
                if (chaseAction != null)
                {
                    chaseAction.targetTransform = moveTarget;
                    chaseAction.acceleration.Value = moveAcceleration;
                    chaseAction.maxSpeed.Value = moveMaxSpeed;
                    chaseAction.reachDistance.Value = reachDistance;
                    chaseAction.chaseTime.Value = moveTimeout;
                }
            }

            // 更新 Moving 状态的 ChaseTargetAction
            var movingState = _controlFsm.FsmStates.FirstOrDefault(s => s.Name == "Moving");
            if (movingState != null)
            {
                var chaseAction = movingState.Actions?.OfType<ChaseTargetAction>().FirstOrDefault();
                if (chaseAction != null)
                {
                    chaseAction.targetTransform = moveTarget;
                    chaseAction.acceleration.Value = moveAcceleration;
                    chaseAction.maxSpeed.Value = moveMaxSpeed;
                    chaseAction.reachDistance.Value = reachDistance;
                    chaseAction.chaseTime.Value = moveTimeout;
                    chaseAction.onReachTarget = FsmEvent.GetFsmEvent("REACHED_BOSS");
                    chaseAction.onTimeout = null;
                }

                // 更新 SetScale 和 ActivateGameObject 的目标对象（Blast 子物体）
                var blastChild = transform.Find("Blast");
                if (blastChild != null)
                {
                    var setScaleAction = movingState.Actions?.OfType<SetScale>().FirstOrDefault();
                    if (setScaleAction != null)
                    {
                        setScaleAction.gameObject.GameObject.Value = blastChild.gameObject;
                    }

                    var activateAction = movingState.Actions?.OfType<ActivateGameObject>().FirstOrDefault();
                    if (activateAction != null)
                    {
                        activateAction.gameObject.GameObject.Value = blastChild.gameObject;
                    }
                }
            }

            Log.Debug($"[BombBlastBehavior] 移动参数已更新：目标={moveTarget.name}, 加速度={moveAcceleration}, 最大速度={moveMaxSpeed}");
        }

        /// <summary>
        /// 重置 FSM 状态（回收时调用）
        /// </summary>
        private void ResetStartTransition()
        {
            // 新设计不需要修改 FSM 跳转，只需要重置参数
            // FSM 结构是固定的，通过事件选择分支
        }
        #endregion

        #region 丝球环生成
        /// <summary>
        /// 生成丝球环协程
        /// 支持两种模式：
        /// 1. 反向加速度模式：丝球向外飞出后受到指向圆心的加速度，最终返回并撞墙消散
        /// 2. 径向爆发模式：丝球以固定速度径向向外飞出，撞墙消散
        /// </summary>
        private IEnumerator SpawnSilkBallRingCoroutine()
        {
            // 等待一小段时间让爆炸动画开始
            yield return new WaitForSeconds(0.1f);

            if (_silkBallManager == null) yield break;

            Vector3 center = transform.position;
            float angleStep = 360f / silkBallCount;
            float startAngle = Random.Range(0f, 360f);
            float spawnRadius = 1f;  // 丝球生成时距离圆心的距离

            // 保存生成的丝球和它们的方向
            var silkBalls = new List<(SilkBallBehavior ball, Vector2 direction)>();

            // 第一阶段：生成所有丝球（处于 Prepare 状态）
            for (int i = 0; i < silkBallCount; i++)
            {
                float angle = startAngle + i * angleStep;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                // 在圆周上生成丝球（距离中心 spawnRadius）
                Vector3 spawnPos = center + new Vector3(direction.x, direction.y, 0) * spawnRadius;
                var silkBall = _silkBallManager.SpawnSilkBall(
                    spawnPos,
                    acceleration: 0f,
                    maxSpeed: useReverseAccelMode ? maxInwardSpeed : radialBurstSpeed,
                    chaseTime: reverseAccelDuration,
                    scale: 0.8f,
                    enableRotation: true,
                    customTarget: null,
                    ignoreWall: false  // 不忽略墙壁碰撞，撞墙后消散
                );

                if (silkBall != null)
                {
                    silkBalls.Add((silkBall, direction));

                    // 初始化丝球的 Rigidbody
                    var rb = silkBall.GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        rb.gravityScale = 0f;
                        rb.linearVelocity = Vector2.zero;  // 初始静止
                    }

                    // 如果是反向加速度模式，配置参数
                    if (useReverseAccelMode)
                    {
                        silkBall.ConfigureReverseAcceleration(
                            center,
                            reverseAcceleration,
                            initialOutwardSpeed,
                            maxInwardSpeed,
                            reverseAccelDuration
                        );
                    }
                }
            }

            Log.Debug($"[BombBlastBehavior] 已生成 {silkBalls.Count} 个丝球，等待 {releaseDelay} 秒后释放");

            // 第二阶段：等待一段时间
            yield return new WaitForSeconds(releaseDelay);

            // 第三阶段：释放所有丝球
            foreach (var (silkBall, direction) in silkBalls)
            {
                if (silkBall == null || !silkBall.isActive) continue;

                var fsm = silkBall.GetComponent<PlayMakerFSM>();
                var rb = silkBall.GetComponent<Rigidbody2D>();

                if (useReverseAccelMode)
                {
                    // 反向加速度模式：设置初始向外速度，然后启动反向加速度
                    if (rb != null)
                    {
                        rb.linearVelocity = direction * initialOutwardSpeed;
                    }
                    // 通过事件启动反向加速度模式
                    silkBall.StartReverseAccelerationMode();
                }
                else
                {
                    // 径向爆发模式：设置径向速度，使用 Chase Hero 模式但 acceleration=0
                    if (rb != null)
                    {
                        rb.gravityScale = 0f;  // 无重力，纯径向运动
                        rb.linearVelocity = direction * radialBurstSpeed;
                    }
                    // 配置丝球为直线飞行模式：acceleration=0，chaseTime=6s
                    silkBall.acceleration = 0f;
                    silkBall.chaseTime = 6f;
                    // 通过 SILK BALL RELEASE 进入 Chase Hero 状态
                    if (fsm != null)
                    {
                        fsm.SendEvent("SILK BALL RELEASE");
                    }
                }
            }

            Log.Debug($"[BombBlastBehavior] 已释放 {silkBalls.Count} 个丝球，模式: {(useReverseAccelMode ? "反向加速度" : "径向爆发")}");
        }
        #endregion

        #region 辅助方法
        private void GetManagerReferences()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _blastManager = managerObj.GetComponent<FWBlastManager>();
                _silkBallManager = managerObj.GetComponent<SilkBallManager>();
            }
        }
        #endregion
    }
}
