using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;

namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// AttackControlBehavior的P6 Web攻击扩展部分（partial class）
    /// </summary>
    internal partial class AttackControlBehavior
    {
        #region P6 Web攻击

        /// <summary>
        /// 修改Rubble Attack?状态，添加P6 Web Attack监听
        /// </summary>
        private void ModifyRubbleAttackForP6Web()
        {
            if (_attackControlFsm == null) return;

            var rubbleAttackState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Rubble Attack?");
            if (rubbleAttackState == null)
            {
                Log.Warn("未找到Rubble Attack?状态，无法添加P6 Web Attack监听");
                return;
            }

            var actions = rubbleAttackState.Actions.ToList();

            // 在第2个Action（检查Do Phase Roar）之后插入P6 Web Attack检查
            // 原版结构：[0] CheckHeroPerformanceRegionV2, [1] BoolTest (Do Phase Roar), [2] BoolTest (Can Rubble Attack)
            // 插入位置：索引2（在Do Phase Roar之后，Can Rubble Attack之前）
            actions.Insert(2, new BoolTest
            {
                boolVariable = _attackControlFsm.FsmVariables.BoolVariables.ToList().FirstOrDefault(v => v.Name == "Do P6 Web Attack"),
                isTrue = _p6WebAttackEvent,
                isFalse = FsmEvent.GetFsmEvent("NULL"),
                everyFrame = false
            });

            rubbleAttackState.Actions = actions.ToArray();

            // 添加跳转
            var transitions = rubbleAttackState.Transitions.ToList();
            transitions.Add(new FsmTransition
            {
                FsmEvent = _p6WebAttackEvent,
                toState = "P6 Web Prepare",
                toFsmState = _p6WebPrepareState
            });
            rubbleAttackState.Transitions = transitions.ToArray();

            Log.Info("Rubble Attack?状态已添加P6 Web Attack监听");
        }

        /// <summary>
        /// 创建P6 Web攻击的所有状态
        /// </summary>
        private void CreateP6WebAttackStates()
        {
            Log.Info("=== 开始创建P6 Web攻击状态链 ===");

            // 创建所有状态
            _p6WebPrepareState = CreateP6WebPrepareState();
            _p6WebCastState = CreateP6WebCastState();
            _p6WebAttack1State = CreateP6WebAttack1State();
            _p6WebAttack2State = CreateP6WebAttack2State();
            _p6WebAttack3State = CreateP6WebAttack3State();
            _p6WebRecoverState = CreateP6WebRecoverState();

            // 添加到FSM
            var states = _attackControlFsm!.FsmStates.ToList();
            states.Add(_p6WebPrepareState);
            states.Add(_p6WebCastState);
            states.Add(_p6WebAttack1State);
            states.Add(_p6WebAttack2State);
            states.Add(_p6WebAttack3State);
            states.Add(_p6WebRecoverState);
            _attackControlFsm.Fsm.States = states.ToArray();

            // 查找Move Restart状态用于链接
            var moveRestartState = _attackControlFsm.FsmStates.FirstOrDefault(s => s.Name == "Move Restart");

            // 设置状态转换
            SetP6WebAttackTransitions(moveRestartState);

            Log.Info("=== P6 Web攻击状态链创建完成 ===");
        }

        /// <summary>
        /// 创建P6 Web Prepare状态
        /// </summary>
        private FsmState CreateP6WebPrepareState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "P6 Web Prepare",
                Description = "P6阶段Web攻击准备"
            };

            var actions = new List<FsmStateAction>();

            // 1. 设置Do P6 Web Attack为false（消耗标记）
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Attack Control") { Value = "Attack Control" },
                variableName = new FsmString("Do P6 Web Attack") { Value = "Do P6 Web Attack" },
                setValue = new FsmBool(false),
                everyFrame = false
            });

            // 2. 复制Web Prepare的准备动作
            var setBoolAction = CloneAction<SetBoolValue>("Web Prepare");
            if (setBoolAction != null) actions.Add(setBoolAction);

            var setFsmBoolAction = CloneAction<SetFsmBool>("Web Prepare");
            if (setFsmBoolAction != null) actions.Add(setFsmBoolAction);

            state.Actions = actions.ToArray();
            Log.Info("创建P6 Web Prepare状态");
            return state;
        }

        /// <summary>
        /// 创建P6 Web Cast状态
        /// </summary>
        private FsmState CreateP6WebCastState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "P6 Web Cast",
                Description = "P6阶段Web施法动画"
            };

            // 直接克隆Web Cast的所有动作
            state.Actions = CloneStateActions("Web Cast");

            Log.Info("创建P6 Web Cast状态");
            return state;
        }

        /// <summary>
        /// 创建P6 Web Attack 1状态（第一根丝网 + 交汇点生成小丝球）
        /// </summary>
        private FsmState CreateP6WebAttack1State()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "P6 Web Attack 1",
                Description = "第一根丝网攻击 + 交汇点生成小丝球"
            };

            var actions = new List<FsmStateAction>();

            // 1. 选择随机Pattern并激活
            var getRandomChildAction = CloneAction<GetRandomChild>("Activate Strands");
            if (getRandomChildAction != null) actions.Add(getRandomChildAction);

            var sendAttackAction = CloneAction<SendEventByName>("Activate Strands", predicate: a =>
                a.sendEvent?.Value == "ATTACK");
            if (sendAttackAction != null) actions.Add(sendAttackAction);

            // 2. 调用生成小丝球的方法
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SpawnSilkBallsAtWebIntersections") { Value = "SpawnSilkBallsAtWebIntersections" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 3. 等待2s（延长间隔）
            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();
            Log.Info("创建P6 Web Attack 1状态");
            return state;
        }

        /// <summary>
        /// 创建P6 Web Attack 2状态（第二根丝网）
        /// </summary>
        private FsmState CreateP6WebAttack2State()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "P6 Web Attack 2",
                Description = "第二根丝网攻击 + 交汇点生成小丝球"
            };

            var actions = new List<FsmStateAction>();

            // 选择随机Pattern并激活
            var getRandomChildAction = CloneAction<GetRandomChild>("Activate Strands");
            if (getRandomChildAction != null) actions.Add(getRandomChildAction);

            var sendAttackAction = CloneAction<SendEventByName>("Activate Strands", predicate: a =>
                a.sendEvent?.Value == "ATTACK");
            if (sendAttackAction != null) actions.Add(sendAttackAction);

            // 调用生成小丝球
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SpawnSilkBallsAtWebIntersections") { Value = "SpawnSilkBallsAtWebIntersections" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 等待2s
            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();
            Log.Info("创建P6 Web Attack 2状态");
            return state;
        }

        /// <summary>
        /// 创建P6 Web Attack 3状态（第三根丝网）
        /// </summary>
        private FsmState CreateP6WebAttack3State()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "P6 Web Attack 3",
                Description = "第三根丝网攻击 + 交汇点生成小丝球"
            };

            var actions = new List<FsmStateAction>();

            // 选择随机Pattern并激活
            var getRandomChildAction = CloneAction<GetRandomChild>("Activate Strands");
            if (getRandomChildAction != null) actions.Add(getRandomChildAction);

            var sendAttackAction = CloneAction<SendEventByName>("Activate Strands", predicate: a =>
                a.sendEvent?.Value == "ATTACK");
            if (sendAttackAction != null) actions.Add(sendAttackAction);

            // 调用生成小丝球
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SpawnSilkBallsAtWebIntersections") { Value = "SpawnSilkBallsAtWebIntersections" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 等待2s
            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished,
                realTime = false
            });

            state.Actions = actions.ToArray();
            Log.Info("创建P6 Web Attack 3状态");
            return state;
        }

        /// <summary>
        /// 创建P6 Web Recover状态
        /// </summary>
        private FsmState CreateP6WebRecoverState()
        {
            var state = new FsmState(_attackControlFsm!.Fsm)
            {
                Name = "P6 Web Recover",
                Description = "P6阶段Web攻击结束恢复"
            };

            // 直接克隆Web Recover的所有动作
            state.Actions = CloneStateActions("Web Recover");

            Log.Info("创建P6 Web Recover状态");
            return state;
        }

        /// <summary>
        /// 设置P6 Web攻击状态转换
        /// </summary>
        private void SetP6WebAttackTransitions(FsmState? moveRestartState)
        {
            if (_p6WebPrepareState == null || _p6WebCastState == null ||
                _p6WebAttack1State == null || _p6WebAttack2State == null ||
                _p6WebAttack3State == null || _p6WebRecoverState == null)
            {
                Log.Error("P6 Web攻击状态未完全创建，无法设置转换");
                return;
            }

            // P6 Web Prepare -> P6 Web Cast (ATTACK PREPARED)
            _p6WebPrepareState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("ATTACK PREPARED"),
                    toState = "P6 Web Cast",
                    toFsmState = _p6WebCastState
                }
            };

            // P6 Web Cast -> P6 Web Attack 1 (FINISHED)
            _p6WebCastState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "P6 Web Attack 1",
                    toFsmState = _p6WebAttack1State
                }
            };

            // P6 Web Attack 1 -> P6 Web Attack 2 (FINISHED)
            _p6WebAttack1State.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "P6 Web Attack 2",
                    toFsmState = _p6WebAttack2State
                }
            };

            // P6 Web Attack 2 -> P6 Web Attack 3 (FINISHED)
            _p6WebAttack2State.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "P6 Web Attack 3",
                    toFsmState = _p6WebAttack3State
                }
            };

            // P6 Web Attack 3 -> P6 Web Recover (FINISHED)
            _p6WebAttack3State.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "P6 Web Recover",
                    toFsmState = _p6WebRecoverState
                }
            };

            // P6 Web Recover -> Move Restart (FINISHED)
            if (moveRestartState != null)
            {
                _p6WebRecoverState.Transitions = new FsmTransition[]
                {
                    new FsmTransition
                    {
                        FsmEvent = FsmEvent.Finished,
                        toState = "Move Restart",
                        toFsmState = moveRestartState
                    }
                };
            }

            Log.Info("P6 Web攻击状态转换设置完成");
        }

        /// <summary>
        /// 在丝网交汇点生成小丝球（延迟0.5s后释放）
        /// </summary>
        public void SpawnSilkBallsAtWebIntersections()
        {
            StartCoroutine(SpawnSilkBallsCoroutine());
        }

        /// <summary>
        /// 生成小丝球的协程
        /// </summary>
        private IEnumerator SpawnSilkBallsCoroutine()
        {
            Log.Info("=== 开始在丝网交汇点生成小丝球 ===");

            // 获取当前激活的Pattern
            var currentPattern = GetCurrentActiveWebPattern();
            if (currentPattern == null)
            {
                Log.Warn("未找到当前激活的丝网Pattern，跳过小丝球生成");
                yield break;
            }

            Log.Info($"找到当前Pattern: {currentPattern.name}");

            // ==========================================
            // 方法3：精确算法（基于web_strand_caught碰撞体的线段相交）
            // ==========================================
            Log.Info("=== 使用精确算法（基于web_strand_caught碰撞体） ===");
            var colliderInfos = GetWebStrandColliderInfo(currentPattern);
            Log.Info($"找到 {colliderInfos.Count} 根丝线碰撞体");

            var intersectionPointsPrecise = CalculateIntersectionPointsPrecise(colliderInfos);

            // ==========================================
            // 备用：简化算法（相邻中点，用于回退）
            // ==========================================
            List<Vector3> intersectionPoints = new List<Vector3>();

            if (intersectionPointsPrecise.Count > 0)
            {
                // 使用精确算法的结果
                intersectionPoints = intersectionPointsPrecise;
                Log.Info($"✓ 使用精确算法，找到 {intersectionPoints.Count} 个交汇点");
            }
            else
            {
                Log.Warn("精确算法未找到");
                yield break;
            }
            // 获取SilkBallManager
            if (_silkBallManager == null)
            {
                Log.Warn("SilkBallManager未找到，无法生成小丝球");
                yield break;
            }

            // 在每个交汇点生成小丝球（等待0.5秒后生成，让丝网先出现）
            yield return new WaitForSeconds(0.5f);

            var silkBalls = new List<SilkBallBehavior>();
            foreach (var point in intersectionPoints)
            {
                Vector3 spawnPos = point;

                // SpawnSilkBall会自动准备并开始追踪，不需要单独Release
                var silkBall = _silkBallManager.SpawnSilkBall(spawnPos, 6f, 20f, 6f, 0.8f);
                if (silkBall != null)
                {
                    silkBalls.Add(silkBall);
                    Log.Info($"在交汇点 {point} 生成并释放小丝球");
                }
            }
            yield return new WaitForSeconds(0.5f);
            foreach (var silkBall in silkBalls)
            {
                var fsm = silkBall.GetComponent<PlayMakerFSM>();
                fsm.SendEvent("SILK BALL RELEASE");
            }
            Log.Info($"已生成并释放 {silkBalls.Count} 个小丝球");
        }

        /// <summary>
        /// 获取当前激活的丝网Pattern
        /// </summary>
        private GameObject? GetCurrentActiveWebPattern()
        {
            if (_strandPatterns == null) return null;

            var WebPattern = _attackControlFsm!.FsmVariables.FindFsmGameObject("Web Pattern");
            Log.Info($"找到{WebPattern.value.name}是父网");
            return WebPattern.value;
        }

        /// <summary>
        /// 获取Pattern中所有WebStrand的碰撞体信息（基于web_strand_caught）
        /// </summary>
        private List<WebStrandColliderInfo> GetWebStrandColliderInfo(GameObject pattern)
        {
            var colliderInfos = new List<WebStrandColliderInfo>();
            const float extent = 12.5f;

            foreach (Transform strandTransform in pattern.transform)
            {
                if (strandTransform.name.EndsWith("(10)"))
                {
                    Log.Info($"排除边界丝线: {strandTransform.name}");
                    continue;
                }
                if (strandTransform.name.Contains("Silk Boss WebStrand"))
                {

                    // === 修改点：直接使用 strandTransform (父物体/视觉物体) ===
                    // 只要父物体是你看到的那个丝线，就用它的 Position 和 Up

                    Vector3 center = strandTransform.position; // 使用父物体中心
                    Vector3 direction3D = strandTransform.right;  // 使用父物体朝向 (Unity自动处理旋转)
                    // Vector2 direction = new Vector2(direction3D.x, direction3D.y);
                    Vector2 direction = new Vector2(direction3D.x, direction3D.y).normalized;
                    // 计算线段端点
                    Vector3 startPoint = center - new Vector3(direction.x * extent, direction.y * extent, 0);
                    Vector3 endPoint = center + new Vector3(direction.x * extent, direction.y * extent, 0);

                    var info = new WebStrandColliderInfo
                    {
                        name = strandTransform.name,
                        centerPosition = center,
                        rotationZ = strandTransform.eulerAngles.z, // 记录父物体角度
                        startPoint = startPoint,
                        endPoint = endPoint
                    };
                    colliderInfos.Add(info);
                }
            }
            return colliderInfos;
        }
        /// <summary>
        /// 获取Pattern中所有WebStrand的详细信息（用于调试和改进交汇点算法）
        /// </summary>
        private List<WebStrandInfo> GetWebStrandDetailedInfo(GameObject pattern)
        {
            var infos = new List<WebStrandInfo>();

            foreach (Transform child in pattern.transform)
            {
                if (child.name.Contains("Silk Boss WebStrand"))
                {
                    var info = new WebStrandInfo
                    {
                        name = child.name,
                        position = child.position,
                        localPosition = child.localPosition,
                        rotation = child.rotation,
                        eulerAngles = child.eulerAngles,
                        localEulerAngles = child.localEulerAngles,
                        localScale = child.localScale,
                        forward = child.forward,      // 前方向（蓝色轴，Z轴）
                        up = child.up,                // 上方向（绿色轴，Y轴）
                        right = child.right           // 右方向（红色轴，X轴）
                    };
                    infos.Add(info);

                    // 输出详细调试信息
                    Log.Info($"=== 丝线: {info.name} ===");
                    Log.Info($"  position: {info.position}");
                    Log.Info($"  localPosition: {info.localPosition}");
                    Log.Info($"  rotation:{info.rotation}");
                    Log.Info($"  eulerAngles: {info.eulerAngles}");
                    Log.Info($"  localEulerAngles: {info.localEulerAngles}");
                    Log.Info($"  localScale: {info.localScale}");
                    Log.Info($"  forward: {info.forward}");
                    Log.Info($"  up: {info.up}");
                    Log.Info($"  right: {info.right}");
                }
            }

            return infos;
        }

        /// <summary>
        /// 丝线详细信息结构
        /// </summary>
        private struct WebStrandInfo
        {
            public string name;
            public Vector3 position;
            public Vector3 localPosition;
            public Quaternion rotation;
            public Vector3 eulerAngles;
            public Vector3 localEulerAngles;
            public Vector3 localScale;
            public Vector3 forward;  // Z轴方向（丝线的"长度"方向）
            public Vector3 up;       // Y轴方向
            public Vector3 right;    // X轴方向
        }

        /// <summary>
        /// 丝线碰撞体信息（基于web_strand_caught）
        /// </summary>
        private struct WebStrandColliderInfo
        {
            public string name;
            public Vector3 centerPosition;  // 碰撞体中心位置(X, Y)
            public float rotationZ;         // Z轴旋转角度（0=横向，90=竖向）
            public Vector3 startPoint;      // 线段起点（中心-10）
            public Vector3 endPoint;        // 线段终点（中心+10）
        }

        /// <summary>
        /// 计算交汇点（精确版：基于web_strand_caught碰撞体的线段相交）
        /// 这是最精确的方法，使用实际碰撞体的位置和旋转来计算线段交点
        /// </summary>
        private List<Vector3> CalculateIntersectionPointsPrecise(List<WebStrandColliderInfo> colliderInfos)
        {
            var intersections = new List<Vector3>();

            if (colliderInfos.Count < 2)
            {
                return intersections;
            }

            Log.Info("=== 开始计算精确交汇点（基于线段相交） ===");

            // 对所有线段对进行两两相交检测
            for (int i = 0; i < colliderInfos.Count - 1; i++)
            {
                for (int j = i + 1; j < colliderInfos.Count; j++)
                {
                    var strand1 = colliderInfos[i];
                    var strand2 = colliderInfos[j];

                    // 计算两条线段的交点（2D）
                    Vector3? intersection = CalculateLineSegmentIntersection(
                        strand1.startPoint, strand1.endPoint,
                        strand2.startPoint, strand2.endPoint
                    );

                    if (intersection.HasValue)
                    {
                        intersections.Add(intersection.Value);
                        Log.Info($"  找到交点: {strand1.name} × {strand2.name}");
                        Log.Info($"    线段1: {strand1.startPoint} → {strand1.endPoint} (旋转{strand1.rotationZ}°)");
                        Log.Info($"    线段2: {strand2.startPoint} → {strand2.endPoint} (旋转{strand2.rotationZ}°)");
                        Log.Info($"    交点位置: {intersection.Value}");
                    }
                }
            }

            Log.Info($"=== 共找到 {intersections.Count} 个精确交汇点 ===");
            return intersections;
        }

        /// <summary>
        /// 计算两条2D线段的交点
        /// 使用参数方程：P = P1 + t * (P2 - P1)
        /// 返回null表示线段不相交或平行
        /// </summary>
        private Vector3? CalculateLineSegmentIntersection(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
        {
            // 线段1: p1 → p2
            // 线段2: p3 → p4

            float x1 = p1.x, y1 = p1.y;
            float x2 = p2.x, y2 = p2.y;
            float x3 = p3.x, y3 = p3.y;
            float x4 = p4.x, y4 = p4.y;

            // 计算分母
            float denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);

            // 如果分母为0，说明两条线段平行或共线
            if (Mathf.Abs(denom) < 0.0001f)
            {
                return null;
            }

            // 计算参数t和u
            float t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
            float u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / denom;

            // 检查交点是否在两条线段内（t和u都在[0, 1]范围内）
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
            {
                // 计算交点
                float intersectX = x1 + t * (x2 - x1);
                float intersectY = y1 + t * (y2 - y1);
                return new Vector3(intersectX, intersectY, p1.z);  // 保持Z坐标
            }

            return null;  // 线段不相交（延长后可能相交，但线段本身不相交）
        }
        #endregion
    }
}
