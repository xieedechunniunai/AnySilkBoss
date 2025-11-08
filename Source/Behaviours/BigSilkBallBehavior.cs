using System.Collections;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using System.Linq;

namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// 大丝球Behavior - 管理单个大丝球的行为和FSM
    /// 用于天女散花大招
    /// </summary>
    internal class BigSilkBallBehavior : MonoBehaviour
    {
        #region 参数配置
        [Header("Boss引用")]
        public GameObject? bossObject;
        private PhaseControlBehavior? phaseControlBehavior;  // PhaseControl引用

        [Header("位置参数")]
        public Vector3 chestOffset = new Vector3(0f, 2f, 0f);  // 胸前偏移量

        [Header("蓄力参数")]
        public float chargeDuration = 2.0f;      // 蓄力时长
        public float initialScale = 0.1f;        // 初始缩放（原版的0.1倍）
        public float maxScale = 0.25f;           // 最大缩放（原版的0.25倍）

        [Header("爆炸参数")]
        public string burstAnimationName = "Silk_Cocoon_Intro_Burst";  // 爆炸动画名称
        public float burstDuration = 11.53f;     // 爆炸持续时间（根据动画实际时长）
        public int burstWaveCount = 8;           // 爆炸引导期间的波数（增加波数以匹配更长的动画）
        public int ballsPerWave = 3;             // 每波生成的小丝球数量

        [Header("最终爆发参数")]
        public int finalBurstCount = 55;         // 最终爆发的小丝球数量（增加到50-60个）
        public float finalBurstMinSpeed = 5f;    // 最终爆发最小速度
        public float finalBurstMaxSpeed = 12f;   // 最终爆发最大速度（增加）
        public float horizontalSpeedMultiplier = 0.45f;  // 横向速度倍数（增加50%）
        public float verticalSpeedMultiplier = 2f;  // 向上速度倍数（翻3倍）
        public int ballsPerBatch = 6;            // 每批生成的丝球数量
        public int framesPerBatch = 2;           // 每批之间间隔的帧数
        public float maxSpawnRadius = 3f;        // 最大生成半径（小丝球随机分布在大丝球内部）
        public float innerSpeedMultiplier = 2.0f;  // 内圈速度倍数（距离中心近的速度更快）
        public float outerSpeedMultiplier = 0.5f;  // 外圈速度倍数（距离中心远的速度更慢）

        [Header("引导期间小丝球参数")]
        public float waveSpeed = 8f;             // 引导期间小丝球速度（降低）
        public float spawnRadius = 2f;           // 生成半径
        
        [Header("重力参数")]
        public float[] gravityScales = new float[] { 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.4f };  // 离散重力值（一波波落下，细化为7档）
        #endregion

        #region 组件引用
        private PlayMakerFSM? controlFSM;
        private Animator? animator;
        private Managers.SilkBallManager? silkBallManager;
        #endregion

        #region FSM 变量引用
        private FsmGameObject? bossTransformVar;
        private FsmVector3? chestOffsetVar;
        private FsmFloat? chargeDurationVar;
        private FsmFloat? maxScaleVar;
        private FsmInt? smallBallCountVar;
        private FsmFloat? burstSpeedVar;
        #endregion

        #region 事件引用
        private FsmEvent? startChargeEvent;
        private FsmEvent? animationCompleteEvent;
        #endregion

        #region 状态标记
        private bool isInitialized = false;
        private bool isCharging = false;
        private float chargeElapsed = 0f;
        #endregion

        /// <summary>
        /// 初始化大丝球（从管理器调用）
        /// </summary>
        public void Initialize(GameObject boss)
        {
            if (isInitialized)
            {
                Log.Warn("大丝球已初始化");
                return;
            }

            bossObject = boss;
            
            // 获取PhaseControlBehavior引用
            if (boss != null)
            {
                phaseControlBehavior = boss.GetComponent<PhaseControlBehavior>();
                if (phaseControlBehavior == null)
                {
                    Log.Warn("未找到 PhaseControlBehavior 组件");
                }
            }
            
            // 获取组件
            GetComponentReferences();

            // 创建 FSM
            CreateControlFSM();

            isInitialized = true;
            Log.Info("大丝球初始化完成");
        }

        private void Update()
        {
            // 蓄力过程的缩放动画在协程中处理，这里不需要
            if (Input.GetKeyDown(KeyCode.T))
            {
                LogBigSilkBallFSMInfo();
            }

            if (Input.GetKeyDown(KeyCode.Y))
            {
                AnalyzeAnimator();
            }
        }

        private void LogBigSilkBallFSMInfo()
        {
            if (controlFSM != null)
            {
                Log.Info($"=== 大丝球 Control FSM 信息 ===");
                Log.Info($"FSM名称: {controlFSM.FsmName}");
                Log.Info($"当前状态: {controlFSM.ActiveStateName}");
                FsmAnalyzer.WriteFsmReport(controlFSM, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\暂存\\_bigSilkBallControlFsm.txt");
            }
        }

        /// <summary>
        /// 获取组件引用
        /// </summary>
        private void GetComponentReferences()
        {
            // 获取 Animator
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                Log.Warn("未找到 Animator 组件");
            }
            else
            {
                Log.Info($"找到 Animator 组件，RuntimeAnimatorController: {animator.runtimeAnimatorController?.name ?? "null"}");
                
                // 验证是否有可用的动画片段
                if (animator.runtimeAnimatorController != null)
                {
                    var clips = animator.runtimeAnimatorController.animationClips;
                    if (clips != null && clips.Length > 0)
                    {
                        Log.Info($"Animator 包含 {clips.Length} 个动画片段");
                        foreach (var clip in clips)
                        {
                            Log.Info($"  - {clip.name} ({clip.length:F2}s)");
                        }
                    }
                    else
                    {
                        Log.Warn("Animator 没有动画片段");
                    }
                }
            }

            // 获取 SilkBallManager
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                silkBallManager = managerObj.GetComponent<Managers.SilkBallManager>();
                if (silkBallManager == null)
                {
                    Log.Warn("未找到 SilkBallManager 组件");
                }
            }
            else
            {
                Log.Warn("未找到 AnySilkBossManager 对象");
            }
        }

        /// <summary>
        /// 创建 Control FSM
        /// </summary>
        private void CreateControlFSM()
        {
            if (controlFSM != null)
            {
                Log.Warn("Control FSM 已存在");
                return;
            }

            // 创建 FSM 组件
            controlFSM = gameObject.AddComponent<PlayMakerFSM>();
            controlFSM.FsmName = "Big Silk Ball Control";

            // 创建所有状态
            var initState = CreateInitState();
            var followBossState = CreateFollowBossState();
            var chargeState = CreateChargeState();
            var burstState = CreateBurstState();
            var spawnSmallBallsState = CreateSpawnSmallBallsState();
            var destroyState = CreateDestroyState();

            // 设置状态到 FSM
            controlFSM.Fsm.States = new FsmState[]
            {
                initState,
                followBossState,
                chargeState,
                burstState,
                spawnSmallBallsState,
                destroyState
            };

            // 注册所有事件
            RegisterFSMEvents();

            // 创建 FSM 变量
            CreateFSMVariables();

            // 添加状态动作
            AddInitActions(initState);
            AddFollowBossActions(followBossState);
            AddChargeActions(chargeState);
            AddBurstActions(burstState);
            AddSpawnSmallBallsActions(spawnSmallBallsState);
            AddDestroyActions(destroyState);

            // 添加状态转换
            AddInitTransitions(initState, followBossState);
            AddFollowBossTransitions(followBossState, chargeState);
            AddChargeTransitions(chargeState, burstState);
            AddBurstTransitions(burstState, spawnSmallBallsState);
            AddSpawnSmallBallsTransitions(spawnSmallBallsState, destroyState);

            // 初始化 FSM 数据和事件
            controlFSM.Fsm.InitData();
            controlFSM.Fsm.InitEvents();

            // 设置初始状态
            var fsmType = controlFSM.Fsm.GetType();
            var startedField = fsmType.GetField("started", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (startedField != null)
            {
                startedField.SetValue(controlFSM.Fsm, true);
            }

            controlFSM.Fsm.SetState(initState.Name);
            Log.Info($"=== 大丝球 Control FSM 创建完成，当前状态: {controlFSM.Fsm.ActiveStateName} ===");
        }

        #region FSM 事件和变量注册
        /// <summary>
        /// 注册 FSM 事件
        /// </summary>
        private void RegisterFSMEvents()
        {
            startChargeEvent = FsmEvent.GetFsmEvent("START CHARGE");
            animationCompleteEvent = FsmEvent.GetFsmEvent("ANIMATION COMPLETE");

            var events = controlFSM!.FsmEvents.ToList();
            if (!events.Contains(startChargeEvent)) events.Add(startChargeEvent);
            if (!events.Contains(animationCompleteEvent)) events.Add(animationCompleteEvent);

            // 使用反射设置事件
            var fsmType = controlFSM.Fsm.GetType();
            var eventsField = fsmType.GetField("events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (eventsField != null)
            {
                eventsField.SetValue(controlFSM.Fsm, events.ToArray());
                Log.Info("FSM 事件注册完成");
            }
        }

        /// <summary>
        /// 创建 FSM 变量
        /// </summary>
        private void CreateFSMVariables()
        {
            // GameObject 变量
            bossTransformVar = new FsmGameObject("Boss Transform") { Value = bossObject };
            controlFSM!.FsmVariables.GameObjectVariables = new FsmGameObject[] { bossTransformVar };

            // Vector3 变量
            chestOffsetVar = new FsmVector3("Chest Offset") { Value = chestOffset };
            controlFSM.FsmVariables.Vector3Variables = new FsmVector3[] { chestOffsetVar };

            // Float 变量
            chargeDurationVar = new FsmFloat("Charge Duration") { Value = chargeDuration };
            maxScaleVar = new FsmFloat("Max Scale") { Value = maxScale };
            burstSpeedVar = new FsmFloat("Wave Speed") { Value = waveSpeed };
            controlFSM.FsmVariables.FloatVariables = new FsmFloat[] { chargeDurationVar, maxScaleVar, burstSpeedVar };

            // Int 变量
            smallBallCountVar = new FsmInt("Final Burst Count") { Value = finalBurstCount };
            controlFSM.FsmVariables.IntVariables = new FsmInt[] { smallBallCountVar };
        }
        #endregion

        #region 创建状态
        private FsmState CreateInitState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Init",
                Description = "初始化状态"
            };
        }

        private FsmState CreateFollowBossState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Follow Boss",
                Description = "跟随Boss胸前"
            };
        }

        private FsmState CreateChargeState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Charge",
                Description = "蓄力变大"
            };
        }

        private FsmState CreateBurstState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Burst",
                Description = "爆炸"
            };
        }

        private FsmState CreateSpawnSmallBallsState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Spawn Small Balls",
                Description = "分裂小丝球"
            };
        }

        private FsmState CreateDestroyState()
        {
            return new FsmState(controlFSM!.Fsm)
            {
                Name = "Destroy",
                Description = "销毁自身"
            };
        }
        #endregion

        #region 添加状态动作
        private void AddInitActions(FsmState initState)
        {
            // 使用 CallMethod 来设置初始缩放，避免 SetScale 动作的空引用问题
            var setInitialScaleAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SetInitialScale") { Value = "SetInitialScale" },
                parameters = new FsmVar[0]
            };

            // 等待一帧
            var waitAction = new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished
            };

            initState.Actions = new FsmStateAction[] { setInitialScaleAction, waitAction };
        }

        private void AddFollowBossActions(FsmState followBossState)
        {
            // 自定义跟随动作
            var followAction = new BigSilkBallFollowAction
            {
                bigSilkBallBehavior = this
            };

            followBossState.Actions = new FsmStateAction[] { followAction };
        }

        private void AddChargeActions(FsmState chargeState)
        {
            // 1. 蓄力缩放动作（协程）
            var chargeAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartChargeCoroutine") { Value = "StartChargeCoroutine" },
                parameters = new FsmVar[0]
            };

            // 2. 跟随BOSS动作（持续性，每帧更新）
            var followAction = new BigSilkBallFollowAction
            {
                bigSilkBallBehavior = this
            };

            // 同时执行：蓄力放大 + 跟随BOSS
            chargeState.Actions = new FsmStateAction[] { chargeAction, followAction };
        }

        private void AddBurstActions(FsmState burstState)
        {
            // 固定位置（停止跟随）
            var fixPositionAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("FixPosition") { Value = "FixPosition" },
                parameters = new FsmVar[0]
            };

            // 播放爆炸动画
            var burstAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PlayBurstAnimation") { Value = "PlayBurstAnimation" },
                parameters = new FsmVar[0]
            };

            burstState.Actions = new FsmStateAction[] { fixPositionAction, burstAction };
        }

        private void AddSpawnSmallBallsActions(FsmState spawnSmallBallsState)
        {
            // 生成小丝球
            var spawnAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SpawnSmallBalls") { Value = "SpawnSmallBalls" },
                parameters = new FsmVar[0]
            };

            // 等待一帧后销毁
            var waitAction = new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished
            };

            spawnSmallBallsState.Actions = new FsmStateAction[] { spawnAction, waitAction };
        }

        private void AddDestroyActions(FsmState destroyState)
        {
            var destroyAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("DestroySelf") { Value = "DestroySelf" },
                parameters = new FsmVar[0]
            };

            destroyState.Actions = new FsmStateAction[] { destroyAction };
        }
        #endregion

        #region 添加状态转换
        private void AddInitTransitions(FsmState initState, FsmState followBossState)
        {
            initState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Follow Boss",
                    toFsmState = followBossState
                }
            };
        }

        private void AddFollowBossTransitions(FsmState followBossState, FsmState chargeState)
        {
            followBossState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = startChargeEvent,
                    toState = "Charge",
                    toFsmState = chargeState
                }
            };
        }

        private void AddChargeTransitions(FsmState chargeState, FsmState burstState)
        {
            chargeState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Burst",
                    toFsmState = burstState
                }
            };
        }

        private void AddBurstTransitions(FsmState burstState, FsmState spawnSmallBallsState)
        {
            burstState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = animationCompleteEvent,
                    toState = "Spawn Small Balls",
                    toFsmState = spawnSmallBallsState
                }
            };
        }

        private void AddSpawnSmallBallsTransitions(FsmState spawnSmallBallsState, FsmState destroyState)
        {
            spawnSmallBallsState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Destroy",
                    toFsmState = destroyState
                }
            };
        }
        #endregion

        #region 辅助方法（供FSM调用）
        /// <summary>
        /// 设置初始缩放
        /// </summary>
        public void SetInitialScale()
        {
            transform.localScale = Vector3.one * initialScale;
            Log.Info($"设置初始缩放: {initialScale}");
        }

        /// <summary>
        /// 固定位置（停止跟随Boss）
        /// </summary>
        public void FixPosition()
        {
            // 什么都不做，仅用于标记进入固定位置状态
            // 实际位置已经在上一个状态（Charge或Follow Boss）中确定
            Log.Info($"固定位置: {transform.position}");
        }

        /// <summary>
        /// 开始蓄力协程
        /// </summary>
        public void StartChargeCoroutine()
        {
            Log.Info("StartChargeCoroutine 被调用");
            StartCoroutine(ChargeCoroutine());
        }

        /// <summary>
        /// 蓄力协程
        /// </summary>
        private IEnumerator ChargeCoroutine()
        {
            isCharging = true;
            chargeElapsed = 0f;
            float startScale = initialScale;
            float targetScale = maxScale;

            Log.Info($"开始蓄力：从 {startScale} 到 {targetScale}，持续 {chargeDuration} 秒");

            while (chargeElapsed < chargeDuration)
            {
                chargeElapsed += Time.deltaTime;
                float t = chargeElapsed / chargeDuration;
                float currentScale = Mathf.Lerp(startScale, targetScale, t);
                transform.localScale = Vector3.one * currentScale;
                yield return null;
            }

            transform.localScale = Vector3.one * targetScale;
            isCharging = false;

            Log.Info("蓄力完成");

            // 通知PhaseControl蓄力完成
            NotifyPhaseControl("ChargeComplete");

            // 发送完成事件到FSM
            if (controlFSM != null)
            {
                controlFSM.SendEvent("FINISHED");
            }
        }

        /// <summary>
        /// 生成小丝球（FSM调用）
        /// </summary>
        public void SpawnSmallBalls()
        {
            Log.Info("开始生成小丝球（最终爆发）");
            SpawnFinalBurst();
        }

        /// <summary>
        /// 播放爆炸动画
        /// </summary>
        public void PlayBurstAnimation()
        {
            Log.Info($"播放爆炸动画: {burstAnimationName}");

            if (animator != null)
            {
                // 尝试播放爆炸动画
                animator.Play(burstAnimationName);
                
                // 验证动画是否成功播放
                var clips = animator.runtimeAnimatorController?.animationClips;
                if (clips != null)
                {
                    var targetClip = clips.FirstOrDefault(c => c.name == burstAnimationName);
                    if (targetClip != null)
                    {
                        // 使用实际动画长度更新 burstDuration
                        burstDuration = targetClip.length;
                        Log.Info($"动画长度: {burstDuration:F2}s，将根据此时长进行分波生成");
                    }
                    else
                    {
                        Log.Warn($"未找到动画片段: {burstAnimationName}，使用默认时长");
                    }
                }
            }
            else
            {
                Log.Warn("Animator 为 null，无法播放动画");
            }

            // 启动分波生成协程
            StartCoroutine(BurstSequenceCoroutine());
        }

        /// <summary>
        /// 爆炸序列协程：分波生成小丝球 + 最终爆发
        /// </summary>
        private IEnumerator BurstSequenceCoroutine()
        {
            Log.Info($"开始爆炸序列，总时长: {burstDuration:F2}s，波数: {burstWaveCount}");

            // 前 70% 的时间用于分波生成
            float wavePhaseTime = burstDuration * 0.7f;
            float waveInterval = wavePhaseTime / burstWaveCount;

            // 分波生成小丝球（引导期间）
            for (int wave = 0; wave < burstWaveCount; wave++)
            {
                yield return new WaitForSeconds(waveInterval);
                SpawnWaveBalls(wave);
                Log.Info($"生成第 {wave + 1}/{burstWaveCount} 波小丝球（间隔: {waveInterval:F2}s）");
            }

            // 等待一小段时间后触发最终爆发（提前0.5秒）
            float waitBeforeFinalBurst = burstDuration * 0.05f;  // 等待到75%位置
            yield return new WaitForSeconds(waitBeforeFinalBurst);

            // 最终爆发：上半部分大量小丝球（在75%位置触发，比原来的85%提前）
            SpawnFinalBurst();
            Log.Info($"最终爆发：生成 {finalBurstCount} 个小丝球（在动画{(0.7f + waitBeforeFinalBurst / burstDuration) * 100:F0}%位置）");

            // 等待剩余时间确保动画播放完成
            float remainingTime = burstDuration * 0.25f - waitBeforeFinalBurst;
            yield return new WaitForSeconds(remainingTime);

            // 通知PhaseControl爆炸完成
            NotifyPhaseControl("BurstComplete");

            // 发送动画完成事件到FSM
            if (controlFSM != null)
            {
                controlFSM.SendEvent("ANIMATION COMPLETE");
            }

            Log.Info("爆炸序列完成");
        }

        /// <summary>
        /// 生成每波小丝球（引导期间）
        /// </summary>
        private void SpawnWaveBalls(int waveIndex)
        {
            if (silkBallManager == null)
            {
                Log.Error("SilkBallManager 为 null，无法生成小丝球");
                return;
            }

            Vector3 centerPosition = transform.position;

            for (int i = 0; i < ballsPerWave; i++)
            {
                // 随机角度，稍微偏向上半部分（-30度到210度）
                float angle = Random.Range(-30f, 210f);
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                // 生成位置（稍微偏离中心）
                Vector3 spawnPosition = centerPosition + new Vector3(direction.x, direction.y, 0f) * spawnRadius;

                // 计算初速度（径向）
                Vector2 initialVelocity = direction * waveSpeed;

                // 生成并初始化小丝球
                SpawnSingleBall(spawnPosition, initialVelocity);
            }
        }

        /// <summary>
        /// 最终爆发：生成大量上半部分小丝球
        /// </summary>
        private void SpawnFinalBurst()
        {
            StartCoroutine(SpawnFinalBurstCoroutine());
        }

        /// <summary>
        /// 最终爆发协程：分批生成小丝球
        /// </summary>
        private IEnumerator SpawnFinalBurstCoroutine()
        {
            if (silkBallManager == null)
            {
                Log.Error("SilkBallManager 为 null，无法生成小丝球");
                yield break;
            }

            Vector3 centerPosition = transform.position;
            int spawned = 0;
            int batchIndex = 0;

            Log.Info($"开始分批生成{finalBurstCount}个小丝球，每批{ballsPerBatch}个，间隔{framesPerBatch}帧");

            while (spawned < finalBurstCount)
            {
                int batchSize = Mathf.Min(ballsPerBatch, finalBurstCount - spawned);
                
                for (int i = 0; i < batchSize; i++)
                {
                    // 上半部分角度分布：30度到150度（向上为主）
                    float angle = Random.Range(30f, 150f);
                    float angleRad = angle * Mathf.Deg2Rad;
                    
                    // 径向方向
                    Vector2 radialDirection = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                    
                    // 随机生成距离（0到maxSpawnRadius之间）- 小丝球随机分布在大丝球内部
                    float distanceFromCenter = Random.Range(0f, maxSpawnRadius);
                    
                    // 根据距离计算速度倍数（距离近=速度快，距离远=速度慢）
                    // 使用Lerp在innerSpeedMultiplier和outerSpeedMultiplier之间插值
                    float distanceRatio = distanceFromCenter / maxSpawnRadius;  // 0到1
                    float speedMultiplierByDistance = Mathf.Lerp(innerSpeedMultiplier, outerSpeedMultiplier, distanceRatio);
                    
                    // 随机基础速度
                    float baseSpeed = Random.Range(finalBurstMinSpeed, finalBurstMaxSpeed);
                    
                    // 应用距离倍数得到最终径向速度
                    float radialSpeed = baseSpeed * speedMultiplierByDistance;
                    
                    // 随机横向速度
                    float horizontalSpeed = Random.Range(-radialSpeed * horizontalSpeedMultiplier, 
                                                         radialSpeed * horizontalSpeedMultiplier);
                    
                    // 向上速度增强
                    float verticalComponent = radialDirection.y * radialSpeed * verticalSpeedMultiplier;
                    float horizontalComponent = radialDirection.x * radialSpeed;
                    
                    // 合成速度向量
                    Vector2 initialVelocity = new Vector2(horizontalComponent + horizontalSpeed, verticalComponent);

                    // 生成位置（根据距离随机分布在大丝球内部）
                    Vector3 spawnPosition = centerPosition + new Vector3(radialDirection.x, radialDirection.y, 0f) * distanceFromCenter;

                    // 生成并初始化小丝球
                    SpawnSingleBall(spawnPosition, initialVelocity);
                    
                    spawned++;
                }

                batchIndex++;
                Log.Info($"第{batchIndex}批生成完成，已生成{spawned}/{finalBurstCount}个小丝球");

                // 等待指定帧数
                for (int f = 0; f < framesPerBatch; f++)
                {
                    yield return null;
                }
            }

            Log.Info($"所有小丝球生成完成，共{spawned}个");
        }

        /// <summary>
        /// 生成单个小丝球（重力模式）
        /// </summary>
        private void SpawnSingleBall(Vector3 spawnPosition, Vector2 initialVelocity)
        {
            if (silkBallManager == null) return;

            var silkBall = silkBallManager.SpawnSilkBall(spawnPosition);
            if (silkBall != null)
            {
                var behavior = silkBall.GetComponent<SilkBallBehavior>();
                var rb = silkBall.GetComponent<Rigidbody2D>();
                
                if (behavior != null && rb != null)
                {
                    // 随机选择离散的重力值（实现一波波落下的效果）
                    float randomGravity = gravityScales[Random.Range(0, gravityScales.Length)];
                    
                    // 初始化为重力模式，不追踪玩家
                    behavior.Initialize(
                        spawnPosition,
                        acceleration: 0f,     // 不追踪玩家
                        maxSpeed: initialVelocity.magnitude,
                        chaseTime: 15f,       // 存活时间（增加）
                        scale: 1f,
                        enableRotation: true
                    );

                    // 启用重力并设置重力缩放
                    rb.gravityScale = randomGravity;
                    rb.bodyType = RigidbodyType2D.Dynamic;  // 确保是动态刚体
                    
                    // 计算距离中心的距离（用于日志）
                    float distanceFromCenter = Vector3.Distance(spawnPosition, transform.position);
                    Log.Info($"小丝球生成：距离中心{distanceFromCenter:F2}，速度{initialVelocity.magnitude:F2}，重力{randomGravity:F2}");
                    
                    // 切换到重力状态
                    var fsm = silkBall.GetComponent<PlayMakerFSM>();
                    if (fsm != null)
                    {
                        fsm.SendEvent("SILK BALL RELEASE");  // 先释放
                        // 延迟切换到重力状态，并在切换后设置速度
                        StartCoroutine(SwitchToGravityStateAndSetVelocity(fsm, rb, initialVelocity));
                    }
                }
                else
                {
                    if (behavior == null) Log.Warn("小丝球缺少 SilkBallBehavior 组件");
                    if (rb == null) Log.Warn("小丝球缺少 Rigidbody2D 组件");
                }
            }
        }

        /// <summary>
        /// 切换小丝球到重力状态并设置速度
        /// </summary>
        private IEnumerator SwitchToGravityStateAndSetVelocity(PlayMakerFSM fsm, Rigidbody2D rb, Vector2 velocity)
        {
            yield return new WaitForSeconds(0.1f);
            
            // 切换到Has Gravity状态
            fsm.Fsm.SetState("Has Gravity");
            
            // 等待一帧确保状态完全切换
            yield return null;
            
            // 设置初始速度
            if (rb != null)
            {
                rb.linearVelocity = velocity;
                Log.Info($"小丝球速度已设置: {velocity}，当前速度: {rb.linearVelocity}");
            }
        }

        /// <summary>
        /// 切换小丝球到重力状态（旧版本，保留用于引导期间）
        /// </summary>
        private IEnumerator SwitchToGravityState(PlayMakerFSM fsm)
        {
            yield return new WaitForSeconds(0.1f);
            // 直接设置到 Has Gravity 状态
            fsm.Fsm.SetState("Has Gravity");
        }

        /// <summary>
        /// 通知PhaseControl事件
        /// </summary>
        private void NotifyPhaseControl(string eventName)
        {
            if (phaseControlBehavior != null)
            {
                Log.Info($"通知 PhaseControl: {eventName}");
                phaseControlBehavior.OnBigSilkBallEvent(eventName);
            }
            else
            {
                Log.Warn("PhaseControlBehavior 为 null，无法发送通知");
            }
        }

        /// <summary>
        /// 销毁自身
        /// </summary>
        public void DestroySelf()
        {
            Log.Info("销毁大丝球");
            Destroy(gameObject);
        }

        /// <summary>
        /// 发送开始蓄力事件（从外部调用）
        /// </summary>
        public void StartCharge()
        {
            if (controlFSM != null)
            {
                Log.Info($"发送 START CHARGE 事件，当前状态: {controlFSM.ActiveStateName}");
                controlFSM.SendEvent("START CHARGE");
                Log.Info($"事件发送后状态: {controlFSM.ActiveStateName}");
            }
            else
            {
                Log.Error("controlFSM 为 null，无法发送 START CHARGE 事件");
            }
        }

        /// <summary>
        /// 分析 Animator 组件的所有信息
        /// </summary>
        private void AnalyzeAnimator()
        {
            if (animator == null)
            {
                Log.Warn("Animator 组件为 null，无法分析");
                return;
            }

            Log.Info("=== Animator 组件分析 ===");
            Log.Info($"GameObject: {gameObject.name}");
            Log.Info($"Enabled: {animator.enabled}");
            Log.Info($"RuntimeAnimatorController: {animator.runtimeAnimatorController?.name ?? "null"}");
            
            // 分析参数
            Log.Info("--- Animator 参数 ---");
            if (animator.parameterCount > 0)
            {
                for (int i = 0; i < animator.parameterCount; i++)
                {
                    var param = animator.GetParameter(i);
                    string valueStr = "";
                    
                    switch (param.type)
                    {
                        case UnityEngine.AnimatorControllerParameterType.Float:
                            valueStr = $"Float = {animator.GetFloat(param.nameHash)}";
                            break;
                        case UnityEngine.AnimatorControllerParameterType.Int:
                            valueStr = $"Int = {animator.GetInteger(param.nameHash)}";
                            break;
                        case UnityEngine.AnimatorControllerParameterType.Bool:
                            valueStr = $"Bool = {animator.GetBool(param.nameHash)}";
                            break;
                        case UnityEngine.AnimatorControllerParameterType.Trigger:
                            valueStr = "Trigger";
                            break;
                    }
                    
                    Log.Info($"  [{i}] {param.name} ({param.type}) - {valueStr}");
                }
            }
            else
            {
                Log.Info("  无参数");
            }

            // 分析层
            Log.Info("--- Animator 层 ---");
            Log.Info($"LayerCount: {animator.layerCount}");
            for (int i = 0; i < animator.layerCount; i++)
            {
                string layerName = animator.GetLayerName(i);
                float layerWeight = animator.GetLayerWeight(i);
                Log.Info($"  [{i}] {layerName} - Weight: {layerWeight}");
                
                // 当前状态信息
                var currentState = animator.GetCurrentAnimatorStateInfo(i);
                Log.Info($"      当前状态: Hash={currentState.shortNameHash}, NormalizedTime={currentState.normalizedTime:F2}, Length={currentState.length:F2}s");
            }

            // 动画片段信息
            Log.Info("--- 可用动画片段 ---");
            if (animator.runtimeAnimatorController != null)
            {
                var clips = animator.runtimeAnimatorController.animationClips;
                if (clips != null && clips.Length > 0)
                {
                    Log.Info($"总动画片段数: {clips.Length}");
                    foreach (var clip in clips)
                    {
                        Log.Info($"  - {clip.name} (Length: {clip.length:F2}s, FPS: {clip.frameRate})");
                        
                        // 检查是否是爆炸动画
                        if (clip.name.Contains("Burst") || clip.name.Contains("burst") || 
                            clip.name.Contains("Intro") || clip.name == burstAnimationName)
                        {
                            Log.Info($"    ★ 可能的爆炸动画！");
                        }
                    }
                }
                else
                {
                    Log.Info("  无动画片段");
                }
            }
            
            // 当前播放信息
            Log.Info("--- 当前播放状态 ---");
            Log.Info($"Speed: {animator.speed}");
            Log.Info($"指定的爆炸动画名: {burstAnimationName}");
            
            Log.Info("=== Animator 分析完成 ===");
        }
        #endregion
    }

    #region 自定义 FSM Action
    /// <summary>
    /// 大丝球跟随Boss的自定义Action
    /// </summary>
    internal class BigSilkBallFollowAction : FsmStateAction
    {
        public BigSilkBallBehavior? bigSilkBallBehavior;

        public override void Reset()
        {
            bigSilkBallBehavior = null;
        }

        public override void OnEnter()
        {
            if (bigSilkBallBehavior == null)
            {
                Debug.LogError("BigSilkBallFollowAction: bigSilkBallBehavior 为 null");
                Finish();
                return;
            }
        }

        public override void OnUpdate()
        {
            if (bigSilkBallBehavior == null || bigSilkBallBehavior.bossObject == null)
            {
                return;
            }

            // 更新位置到Boss胸前
            Vector3 bossPosition = bigSilkBallBehavior.bossObject.transform.position;
            Vector3 targetPosition = bossPosition + bigSilkBallBehavior.chestOffset;
            bigSilkBallBehavior.transform.position = targetPosition;
        }
    }
    #endregion
}

