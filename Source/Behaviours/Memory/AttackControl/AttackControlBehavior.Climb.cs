using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    internal partial class MemoryAttackControlBehavior
    {
        #region ClimbPin 攻击配置
        /// <summary>
        /// ClimbPin 攻击配置
        /// </summary>
        private class ClimbPinAttackConfig
        {
            public int PinCount = 12;                    // Pin 数量
            public float SpawnHeightOffset = 2f;         // BOSS 上方偏移
            public float PinSpacing = 0.5f;              // Pin 间距
            public float AimOffset = 0.3f;               // 瞄准偏移（玩家左/右）
            public float AimWaitTime = 1f;               // 瞄准后等待时间
            public float FireInterval = 0.15f;          // 发射间隔
            public float FlightDuration = 4f;            // 飞行时间
            public float InitialRotation = -90f;         // 初始朝向（向下）
        }

        private ClimbPinAttackConfig _climbPinConfig = new ClimbPinAttackConfig();
        private FWPinManager? _fwPinManager;
        private List<GameObject> _activeClimbPins = new List<GameObject>();
        #endregion

        #region 爬升阶段攻击系统
        private void CreateClimbPhaseAttackStates()
        {
            if (_attackControlFsm == null) return;

            Log.Info("=== 开始创建爬升阶段攻击状态链 ===");

            // 获取 FWPinManager 引用
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _fwPinManager = managerObj.GetComponent<FWPinManager>();
            }

            var climbStates = CreateStates(_attackControlFsm.Fsm,
                ("Climb Attack Choice", "爬升阶段攻击选择"),
                ("Climb Orbit Attack", "爬升阶段环绕攻击"),
                ("Climb Silk Ball Attack", "爬升阶段丝球攻击"),
                ("Climb Pin Attack", "爬升阶段 Pin 攻击"),
                ("Climb Attack Cooldown", "爬升阶段攻击冷却")
            );
            AddStatesToFsm(_attackControlFsm, climbStates);

            var climbAttackChoice = climbStates[0];
            var climbOrbitAttack = climbStates[1];
            var climbSilkBallAttack = climbStates[2];
            var climbPinAttack = climbStates[3];
            var climbAttackCooldown = climbStates[4];

            var idleState = _idleState;

            AddClimbAttackChoiceActions(climbAttackChoice);
            AddClimbNeedleAttackActions(climbOrbitAttack);  // ClimbOrbit 攻击
            AddClimbSilkBallAttackActions(climbSilkBallAttack);
            AddClimbPinAttackActions(climbPinAttack);
            AddClimbAttackCooldownActions(climbAttackCooldown);

            // 更新转换（移除 ClimbWeb）
            AddClimbAttackTransitionsWithPin(climbAttackChoice, climbOrbitAttack,
                climbSilkBallAttack, climbPinAttack, climbAttackCooldown);

            AddClimbPhaseAttackGlobalTransitions(climbAttackChoice, idleState);

            Log.Info("=== 爬升阶段攻击状态链创建完成 ===");
        }

        private void AddClimbAttackChoiceActions(FsmState choiceState)
        {
            var actions = new List<FsmStateAction>();

            // 三种攻击类型：ClimbOrbit、ClimbSilkBall、ClimbPin
            var climbOrbitEvent = FsmEvent.GetFsmEvent("CLIMB ORBIT ATTACK");
            var climbSilkBallEvent = FsmEvent.GetFsmEvent("CLIMB SILK BALL ATTACK");
            var climbPinEvent = FsmEvent.GetFsmEvent("CLIMB PIN ATTACK");

            actions.Add(new SendRandomEventV4
            {
                events = new FsmEvent[] { climbOrbitEvent, climbSilkBallEvent, climbPinEvent },
                weights = new FsmFloat[] { new FsmFloat(1.0f), new FsmFloat(0.6f), new FsmFloat(1.2f) },
                eventMax = new FsmInt[] { new FsmInt(1), new FsmInt(1), new FsmInt(1) },
                missedMax = new FsmInt[] { new FsmInt(2), new FsmInt(3), new FsmInt(2) },
                activeBool = new FsmBool { UseVariable = true, Value = true }
            });

            choiceState.Actions = actions.ToArray();
        }

        private void AddClimbNeedleAttackActions(FsmState needleState)
        {
            // 此状态现在用于 ClimbOrbit 攻击
            var actions = new List<FsmStateAction>();

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbOrbitAttack") { Value = "ExecuteClimbOrbitAttack" },
                parameters = new FsmVar[0]
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(3.5f),  // ClimbOrbit 攻击需要更长时间
                finishEvent = FsmEvent.Finished
            });

            needleState.Actions = actions.ToArray();
        }

        private void AddClimbSilkBallAttackActions(FsmState silkBallState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" },
                fsm = _bossControlFsm,
                variableName = new FsmString("Climb Cast Pending") { Value = "Climb Cast Pending" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            // 使用新版本的 ClimbSilkBall 攻击（单个丝球 + 旋转丝网）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbSilkBallWithWebAttack") { Value = "ExecuteClimbSilkBallWithWebAttack" },
                parameters = new FsmVar[0]
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(4.0f),  // 新版本需要更长时间
                finishEvent = FsmEvent.Finished
            });

            silkBallState.Actions = actions.ToArray();
        }

        private void AddClimbAttackCooldownActions(FsmState cooldownState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new WaitRandom
            {
                timeMin = new FsmFloat(2f),
                timeMax = new FsmFloat(3f),
                finishEvent = FsmEvent.Finished
            });

            cooldownState.Actions = actions.ToArray();
        }

        private void AddClimbPhaseAttackGlobalTransitions(FsmState climbAttackChoice, FsmState? idleState)
        {
            var globalTransitions = _attackControlFsm!.Fsm.GlobalTransitions.ToList();

            globalTransitions.Add(new FsmTransition
            {
                FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE ATTACK"),
                toState = "Climb Attack Choice",
                toFsmState = climbAttackChoice
            });

            if (idleState != null)
            {
                globalTransitions.Add(new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE END"),
                    toState = "Idle",
                    toFsmState = idleState
                });
            }

            _attackControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
            Log.Info("爬升阶段攻击全局转换添加完成");
        }

        public void ExecuteClimbNeedleAttack()
        {
            Log.Info("执行爬升阶段针攻击（随机Hand）");
            StartCoroutine(ClimbNeedleAttackCoroutine());
        }

        private IEnumerator ClimbNeedleAttackCoroutine()
        {
            int handIndex = UnityEngine.Random.Range(0, 2);
            MemoryHandControlBehavior? selectedHand = handIndex == 0 ? handLBehavior : handRBehavior;

            if (selectedHand == null)
            {
                Log.Error($"未找到Hand {handIndex} Behavior");
                yield break;
            }

            Log.Info($"选择Hand {handIndex} ({selectedHand.gameObject.name})进行爬升阶段环绕攻击（削弱版）");

            selectedHand.StartOrbitAttackSequence();

            yield return new WaitForSeconds(2f);

            selectedHand.StartShootSequence();

            Log.Info("爬升阶段环绕攻击完成（单Hand三根针）");
        }

        public void ExecuteClimbSilkBallAttack()
        {
            Log.Info("执行爬升阶段丝球攻击（四角生成）");
            StartCoroutine(ClimbSilkBallAttackCoroutine());
        }

        private IEnumerator ClimbSilkBallAttackCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController未找到，无法执行丝球攻击");
                yield break;
            }

            if (_silkBallManager == null)
            {
                Log.Warn("SilkBallManager未初始化");
                yield break;
            }

            Vector3 playerPos = hero.transform.position;
            float offset = 15f;

            Vector3[] positions = new Vector3[]
            {
                playerPos + new Vector3(-offset, offset, 0f),
                playerPos + new Vector3(-offset, -offset, 0f),
                playerPos + new Vector3(offset, offset, 0f),
                playerPos + new Vector3(offset, -offset, 0f)
            };
            var ballObjects = new List<GameObject>();
            foreach (var pos in positions)
            {
                // 使用统一版本的丝球
                var behavior = _silkBallManager.SpawnSilkBall(
                    pos,
                    acceleration: 15f,
                    maxSpeed: 20f,
                    chaseTime: 5f,
                    scale: 1f,
                    enableRotation: true
                );

                if (behavior != null)
                {
                    ballObjects.Add(behavior.gameObject);
                    behavior.gameObject.LocateMyFSM("Control").SendEvent("PREPARE");
                    behavior.isPrepared = true;
                    Log.Info($"在位置 {pos} 生成追踪丝球");
                }
            }

            yield return new WaitForSeconds(0.6f);

            Log.Info("=== 广播 SILK BALL RELEASE 事件，释放爬升阶段丝球 ===");
            EventRegister.SendEvent("SILK BALL RELEASE");

            yield return new WaitForSeconds(1f);
        }

        #region ClimbPin 攻击实现
        /// <summary>
        /// 添加 ClimbPin 攻击状态动作
        /// </summary>
        private void AddClimbPinAttackActions(FsmState pinState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ExecuteClimbPinAttack") { Value = "ExecuteClimbPinAttack" },
                parameters = new FsmVar[0]
            });

            // ClimbPin 攻击需要较长时间：生成 + 跟随 + 瞄准 + 发射
            actions.Add(new Wait
            {
                time = new FsmFloat(6f),
                finishEvent = FsmEvent.Finished
            });

            pinState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加包含 ClimbPin 的攻击转换（移除 ClimbWeb）
        /// </summary>
        private void AddClimbAttackTransitionsWithPin(FsmState choiceState, FsmState orbitState,
            FsmState silkBallState, FsmState pinState, FsmState cooldownState)
        {
            choiceState.Transitions = new FsmTransition[]
            {
                CreateTransition(FsmEvent.GetFsmEvent("CLIMB ORBIT ATTACK"), orbitState),
                CreateTransition(FsmEvent.GetFsmEvent("CLIMB SILK BALL ATTACK"), silkBallState),
                CreateTransition(FsmEvent.GetFsmEvent("CLIMB PIN ATTACK"), pinState)
            };

            SetFinishedTransition(orbitState, cooldownState);
            SetFinishedTransition(silkBallState, cooldownState);
            SetFinishedTransition(pinState, cooldownState);

            SetFinishedTransition(cooldownState, choiceState);

            Log.Info("爬升阶段攻击转换设置完成（ClimbOrbit、ClimbSilkBall、ClimbPin）");
        }

        /// <summary>
        /// 执行 ClimbPin 攻击
        /// </summary>
        public void ExecuteClimbPinAttack()
        {
            Log.Info("执行爬升阶段 ClimbPin 攻击");
            StartCoroutine(ClimbPinAttackCoroutine());
        }

        /// <summary>
        /// ClimbPin 攻击协程
        /// </summary>
        private IEnumerator ClimbPinAttackCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController 未找到，无法执行 ClimbPin 攻击");
                yield break;
            }

            if (_fwPinManager == null || !_fwPinManager.IsInitialized)
            {
                Log.Warn("FWPinManager 未初始化，无法执行 ClimbPin 攻击");
                yield break;
            }

            // 清理之前的 Pin
            _activeClimbPins.Clear();

            // 1. 生成阶段：从 FWPinManager 获取 12 个 Pin
            // BOSS 对象就是当前组件所在的 gameObject
            Vector3 bossPos = transform.position;
            int pinCount = _climbPinConfig.PinCount;
            float spacing = _climbPinConfig.PinSpacing;
            float heightOffset = _climbPinConfig.SpawnHeightOffset;
            float aimOffset = _climbPinConfig.AimOffset;

            Log.Info($"生成 {pinCount} 个 Pin，BOSS 位置: {bossPos}");

            for (int i = 0; i < pinCount; i++)
            {
                // 计算 Pin 位置：以 BOSS 为中心，左右各 6 个
                float xOffset = (i - (pinCount - 1) / 2f) * spacing;
                Vector3 spawnPos = bossPos + new Vector3(xOffset, heightOffset, 0f);

                var pin = _fwPinManager.SpawnPinProjectile(spawnPos, null);
                if (pin != null)
                {
                    _activeClimbPins.Add(pin);

                    // 设置初始朝向（-90° 向下）
                    pin.transform.rotation = Quaternion.Euler(0, 0, _climbPinConfig.InitialRotation);

                    // 设置瞄准偏移方向：前 6 根瞄准左边（-1），后 6 根瞄准右边（+1）
                    float offsetDirection = i < pinCount / 2 ? -1f : 1f;
                    _fwPinManager.SetClimbPinAimOffsetDirection(pin, offsetDirection, aimOffset);

                    // 发送 CLIMB_PIN_PREPARE 事件
                    var fsm = pin.LocateMyFSM("Control");
                    if (fsm != null)
                    {
                        fsm.SendEvent("CLIMB_PIN_PREPARE");
                    }
                }
            }

            Log.Info($"已生成 {_activeClimbPins.Count} 个 Pin（前 {pinCount / 2} 根瞄准左边，后 {pinCount / 2} 根瞄准右边）");

            // 2. 跟随阶段：持续更新 Pin 位置跟随 BOSS
            float followDuration = 1.5f;
            float followTimer = 0f;

            while (followTimer < followDuration)
            {
                bossPos = transform.position;

                for (int i = 0; i < _activeClimbPins.Count; i++)
                {
                    var pin = _activeClimbPins[i];
                    if (pin != null && pin.activeInHierarchy)
                    {
                        float xOffset = (i - (_activeClimbPins.Count - 1) / 2f) * spacing;
                        Vector3 targetPos = bossPos + new Vector3(xOffset, heightOffset, 0f);
                        pin.transform.position = targetPos;
                    }
                }

                followTimer += Time.deltaTime;
                yield return null;
            }

            // 3. 瞄准阶段：发送 CLIMB_PIN_AIM 事件，让 Pin 开始瞄准
            // 注意：现在每根 Pin 的偏移方向已经在生成时设置好了
            Log.Info("发送 CLIMB_PIN_AIM 事件，Pin 开始瞄准玩家");

            foreach (var pin in _activeClimbPins)
            {
                if (pin != null && pin.activeInHierarchy)
                {
                    var fsm = pin.LocateMyFSM("Control");
                    if (fsm != null)
                    {
                        fsm.SendEvent("CLIMB_PIN_AIM");
                    }
                }
            }

            // 4. 等待瞄准完成和发射前等待
            yield return new WaitForSeconds(_climbPinConfig.AimWaitTime);

            // 5. 发射阶段：随机顺序以 0.15 秒间隔发送 CLIMB_PIN_THREAD 事件
            // 创建随机顺序的索引列表
            var fireOrder = Enumerable.Range(0, _activeClimbPins.Count).ToList();
            ShuffleList(fireOrder);

            Log.Info("开始发射 Pin");

            foreach (int index in fireOrder)
            {
                if (index < _activeClimbPins.Count)
                {
                    var pin = _activeClimbPins[index];
                    if (pin != null && pin.activeInHierarchy)
                    {
                        var fsm = pin.LocateMyFSM("Control");
                        if (fsm != null)
                        {
                            // 发送 CLIMB_PIN_THREAD 事件触发 Thread 动画
                            // Thread Pull 状态的 Wait 动作完成后会自动触发 CLIMB_PIN_FIRE
                            fsm.SendEvent("CLIMB_PIN_THREAD");
                        }
                    }
                }

                yield return new WaitForSeconds(_climbPinConfig.FireInterval);
            }

            Log.Info("ClimbPin 攻击发射完成");

            // 等待所有 Pin 飞行完成（4 秒）
            yield return new WaitForSeconds(_climbPinConfig.FlightDuration);

            // 清理
            _activeClimbPins.Clear();
            Log.Info("ClimbPin 攻击完成");
        }

        /// <summary>
        /// 洗牌算法
        /// </summary>
        private void ShuffleList<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = Random.Range(0, n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
        #endregion

        #region ClimbOrbit 攻击实现
        /// <summary>
        /// 配置 ClimbOrbit 攻击参数
        /// </summary>
        public void ConfigureClimbOrbitAttack()
        {
            // 设置 Hand L 和 Hand R 的攻击间隔为 0.7 秒（比普通环绕攻击慢）
            if (handLBehavior != null)
            {
                handLBehavior.SetOrbitAttackConfig(-1f, 0.7f);
            }
            if (handRBehavior != null)
            {
                handRBehavior.SetOrbitAttackConfig(-1f, 0.7f);
            }
        }

        /// <summary>
        /// 执行 ClimbOrbit 攻击
        /// </summary>
        public void ExecuteClimbOrbitAttack()
        {
            Log.Info("执行爬升阶段 ClimbOrbit 攻击（双 Hand 环绕）");
            StartCoroutine(ClimbOrbitAttackCoroutine());
        }

        /// <summary>
        /// ClimbOrbit 攻击协程
        /// </summary>
        private IEnumerator ClimbOrbitAttackCoroutine()
        {
            // 配置攻击参数
            ConfigureClimbOrbitAttack();

            // 使用 2 个 Hand 共 6 根 Finger 执行环绕攻击
            if (handLBehavior != null)
            {
                handLBehavior.StartOrbitAttackSequence();
            }
            if (handRBehavior != null)
            {
                handRBehavior.StartOrbitAttackSequence();
            }

            // 等待环绕完成
            yield return new WaitForSeconds(2f);

            // 发射
            if (handLBehavior != null)
            {
                handLBehavior.StartShootSequence();
            }

            yield return new WaitForSeconds(0.5f);

            if (handRBehavior != null)
            {
                handRBehavior.StartShootSequence();
            }

            Log.Info("ClimbOrbit 攻击完成（双 Hand 六根针）");
        }
        #endregion

        #region ClimbSilkBall 攻击实现（更新版）
        /// <summary>
        /// 执行 ClimbSilkBall 攻击（新版：单个丝球 + 旋转丝网）
        /// </summary>
        public void ExecuteClimbSilkBallWithWebAttack()
        {
            Log.Info("执行爬升阶段 ClimbSilkBall 攻击（单个丝球 + 旋转丝网）");
            StartCoroutine(ClimbSilkBallWithWebAttackCoroutine());
        }

        /// <summary>
        /// ClimbSilkBall 攻击协程（新版）
        /// </summary>
        private IEnumerator ClimbSilkBallWithWebAttackCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController 未找到，无法执行 ClimbSilkBall 攻击");
                yield break;
            }

            if (_silkBallManager == null)
            {
                Log.Warn("SilkBallManager 未初始化");
                yield break;
            }

            Vector3 playerPos = hero.transform.position;

            // 随机选择玩家上方或下方 8 单位生成单个丝球
            float verticalOffset = Random.Range(0, 2) == 0 ? 12f : -12f;
            Vector3 spawnPos = playerPos + new Vector3(0f, verticalOffset, 0f);

            Log.Info($"在玩家 {(verticalOffset > 0 ? "上方" : "下方")} 8 单位生成丝球");

            // 生成单个丝球（添加 ignoreWall 和 canBeClearedByAttack 参数）
            var behavior = _silkBallManager.SpawnSilkBall(
                spawnPos,
                acceleration: 12f,
                maxSpeed: 16f,
                chaseTime: 6f,
                scale: 1.2f,
                enableRotation: true,
                ignoreWall: true,
                canBeClearedByAttack: false
            );

            if (behavior != null)
            {
                behavior.gameObject.LocateMyFSM("Control").SendEvent("PREPARE");
                behavior.isPrepared = true;

                yield return new WaitForSeconds(0.5f);

                // 释放丝球
                behavior.SendFsmEvent("SILK BALL RELEASE");

                // 生成旋转丝网（跟随丝球并持续旋转）
                yield return StartCoroutine(SpawnCrossWebsForSilkBall(behavior.transform, 4));
            }

            yield return new WaitForSeconds(2f);
        }

        /// <summary>
        /// 为丝球生成旋转丝网（使用 ConfigureFollowTarget 和 ConfigureContinuousRotation）
        /// </summary>
        private IEnumerator SpawnCrossWebsForSilkBall(Transform silkBallTransform, int webCount)
        {
            if (_singleWebManager == null) yield break;

            float rotationSpeed = 45f;
            Vector3 scale = new Vector3(2.4f, 1.1f, 1f);

            for (int i = 0; i < webCount; i++)
            {
                if (silkBallTransform == null || !silkBallTransform.gameObject.activeInHierarchy) break;

                Vector3 ballPos = silkBallTransform.position;
                float baseAngle = Random.Range(0f, 90f);

                // 生成十字丝网（两根垂直的丝线）
                // 第一根丝线
                var w1 = _singleWebManager.SpawnAndAttack(ballPos, new Vector3(0f, 0f, baseAngle), scale, 0f, 0.75f);
                if (w1 != null)
                {
                    w1.ConfigureFollowTarget(silkBallTransform);
                    w1.ConfigureContinuousRotation(true, rotationSpeed);
                }

                // 第二根丝线（垂直于第一根，静音）
                var w2 = _singleWebManager.SpawnAndAttack(ballPos, new Vector3(0f, 0f, baseAngle + 90f), scale, 0f, 0.75f);
                if (w2 != null)
                {
                    w2.SetAudioEnabled(false);
                    w2.ConfigureFollowTarget(silkBallTransform);
                    w2.ConfigureContinuousRotation(true, rotationSpeed);
                }

                yield return new WaitForSeconds(1.0f);
            }
        }
        #endregion
        #endregion
    }
}

