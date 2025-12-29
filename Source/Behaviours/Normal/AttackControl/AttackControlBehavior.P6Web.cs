using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;
using AnySilkBoss.Source.Managers;

namespace AnySilkBoss.Source.Behaviours.Normal
{
    /// <summary>
    /// AttackControlBehavior的P6 Web攻击扩展部分（partial class�?
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

            var rubbleAttackState = FindState(_attackControlFsm, "Rubble Attack?");
            if (rubbleAttackState == null)
            {
                Log.Warn("未找到Rubble Attack?状态，无法添加P6 Web Attack监听");
                return;
            }

            var actions = rubbleAttackState.Actions.ToList();

            // 在第2个Action（检查Do Phase Roar）之后插入P6 Web Attack检�?
            actions.Insert(2, new BoolTest
            {
                boolVariable = _attackControlFsm.FsmVariables.BoolVariables.ToList().FirstOrDefault(v => v.Name == "Do P6 Web Attack"),
                isTrue = _p6WebAttackEvent,
                isFalse = FsmEvent.GetFsmEvent("NULL"),
                everyFrame = false
            });

            rubbleAttackState.Actions = actions.ToArray();

            // 使用 AddTransition 添加跳转
            AddTransition(rubbleAttackState, CreateTransition(_p6WebAttackEvent!, _p6WebPrepareState!));

            Log.Info("Rubble Attack?状态已添加P6 Web Attack监听");
        }

        /// <summary>
        /// 创建P6 Web攻击的所有状�?
        /// </summary>
        private void CreateP6WebAttackStates()
        {
            Log.Info("=== 开始创建P6 Web攻击状态链 ===");

            // 使用 FsmStateBuilder 批量创建P6 Web攻击状�?
            var p6WebStates = CreateStates(_attackControlFsm!.Fsm,
                ("P6 Web Prepare", "P6阶段Web攻击准备"),
                ("P6 Web Cast", "P6阶段Web施法动画"),
                ("P6 Web Attack 1", "第一根丝网攻�?+ 交汇点生成小丝球"),
                ("P6 Web Attack 2", "第二根丝网攻�?+ 交汇点生成小丝球"),
                ("P6 Web Attack 3", "第三根丝网攻�?+ 交汇点生成小丝球"),
                ("P6 Web Recover", "P6阶段Web攻击结束恢复")
            );
            AddStatesToFsm(_attackControlFsm, p6WebStates);

            _p6WebPrepareState = p6WebStates[0];
            _p6WebCastState = p6WebStates[1];
            _p6WebAttack1State = p6WebStates[2];
            _p6WebAttack2State = p6WebStates[3];
            _p6WebAttack3State = p6WebStates[4];
            _p6WebRecoverState = p6WebStates[5];

            // 添加各状态的动作（使用原有方法）
            SetP6WebPrepareActions(_p6WebPrepareState);
            SetP6WebCastActions(_p6WebCastState);
            SetP6WebAttack1Actions(_p6WebAttack1State);
            SetP6WebAttack2Actions(_p6WebAttack2State);
            SetP6WebAttack3Actions(_p6WebAttack3State);
            SetP6WebRecoverActions(_p6WebRecoverState);

            // 查找Move Restart状态用于链�?
            var moveRestartState = FindState(_attackControlFsm, "Move Restart");

            // 设置状态转�?
            SetP6WebAttackTransitions(moveRestartState);

            Log.Info("=== P6 Web攻击状态链创建完成 ===");
        }

        /// <summary>
        /// 设置P6 Web Prepare状态的动作
        /// </summary>
        private void SetP6WebPrepareActions(FsmState state)
        {
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

            // 2. 复制Web Prepare的准备动�?
            var setBoolAction = CloneAction<SetBoolValue>("Web Prepare");
            if (setBoolAction != null) actions.Add(setBoolAction);

            var setFsmBoolAction = CloneAction<SetFsmBool>("Web Prepare");
            if (setFsmBoolAction != null) actions.Add(setFsmBoolAction);

            state.Actions = actions.ToArray();
            Log.Info("设置P6 Web Prepare动作");
        }

        /// <summary>
        /// 设置P6 Web Cast状态的动作
        /// </summary>
        private void SetP6WebCastActions(FsmState state)
        {
            // 直接克隆Web Cast的所有动�?
            state.Actions = CloneStateActions("Web Cast");
            Log.Info("设置P6 Web Cast动作");
        }

        /// <summary>
        /// 设置P6 Web Attack状态的通用动作（丝网攻�?+ 生成小丝球）
        /// </summary>
        private void SetP6WebAttackCommonActions(FsmState state, string logName)
        {
            var actions = new List<FsmStateAction>();

            // 选择随机Pattern并激�?
            var getRandomChildAction = CloneAction<GetRandomChild>("Activate Strands");
            if (getRandomChildAction != null) actions.Add(getRandomChildAction);

            var sendAttackAction = CloneAction<SendEventByName>("Activate Strands", predicate: a =>
                a.sendEvent?.Value == "ATTACK");
            if (sendAttackAction != null) actions.Add(sendAttackAction);

            // 调用生成小丝球的方法
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
            Log.Info($"设置{logName}动作");
        }

        private void SetP6WebAttack1Actions(FsmState state) => SetP6WebAttackCommonActions(state, "P6 Web Attack 1");
        private void SetP6WebAttack2Actions(FsmState state) => SetP6WebAttackCommonActions(state, "P6 Web Attack 2");
        private void SetP6WebAttack3Actions(FsmState state) => SetP6WebAttackCommonActions(state, "P6 Web Attack 3");

        /// <summary>
        /// 设置P6 Web Recover状态的动作
        /// </summary>
        private void SetP6WebRecoverActions(FsmState state)
        {
            // 直接克隆Web Recover的所有动�?
            state.Actions = CloneStateActions("Web Recover");
            Log.Info("设置P6 Web Recover动作");
        }

        /// <summary>
        /// 设置P6 Web攻击状态转�?
        /// </summary>
        private void SetP6WebAttackTransitions(FsmState? moveRestartState)
        {
            if (_p6WebPrepareState == null || _p6WebCastState == null ||
                _p6WebAttack1State == null || _p6WebAttack2State == null ||
                _p6WebAttack3State == null || _p6WebRecoverState == null)
            {
                Log.Error("P6 Web攻击状态未完全创建，无法设置转�?");
                return;
            }

            // P6 Web Prepare -> P6 Web Cast (ATTACK PREPARED)
            _p6WebPrepareState.Transitions = new FsmTransition[]
            {
                CreateTransition(FsmEvent.GetFsmEvent("ATTACK PREPARED"), _p6WebCastState)
            };

            // P6 Web Cast -> Attack 1 -> Attack 2 -> Attack 3 -> Recover (链式FINISHED转换)
            SetFinishedTransition(_p6WebCastState, _p6WebAttack1State);
            SetFinishedTransition(_p6WebAttack1State, _p6WebAttack2State);
            SetFinishedTransition(_p6WebAttack2State, _p6WebAttack3State);
            SetFinishedTransition(_p6WebAttack3State, _p6WebRecoverState);

            // P6 Web Recover -> Move Restart (FINISHED)
            if (moveRestartState != null)
            {
                SetFinishedTransition(_p6WebRecoverState, moveRestartState);
            }

            Log.Info("P6 Web攻击状态转换设置完�?");
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
            // 方法3：精确算法（基于web_strand_caught碰撞体的线段相交�?
            // ==========================================
            Log.Info("=== 使用精确算法（基于web_strand_caught碰撞体） ===");
            var colliderInfos = GetWebStrandColliderInfo(currentPattern);
            Log.Info($"找到 {colliderInfos.Count} 根丝线碰撞体");

            var intersectionPointsPrecise = CalculateIntersectionPointsPrecise(colliderInfos);

            // ==========================================
            // 备用：简化算法（相邻中点，用于回退�?
            // ==========================================
            List<Vector3> intersectionPoints = new List<Vector3>();

            if (intersectionPointsPrecise.Count > 0)
            {
                // 使用精确算法的结�?
                intersectionPoints = intersectionPointsPrecise;
                Log.Info($"�?使用精确算法，找{intersectionPoints.Count} 个交汇点");
            }
            else
            {
                Log.Warn("精确算法未找");
                yield break;
            }
            // 获取SilkBallManager
            if (_silkBallManager == null)
            {
                Log.Warn("SilkBallManager未找到，无法生成小丝?");
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
            yield return new WaitForSeconds(0.66f);
            
            // 使用 EventRegister 全局广播释放事件
            Log.Info($"=== 广播 SILK BALL RELEASE 事件，释P6 交点丝球 ===");
            EventRegister.SendEvent("SILK BALL RELEASE");
            
            Log.Info($"已生成并释放 {silkBalls.Count} 个小丝球");
        }

        /// <summary>
        /// 获取当前激活的丝网Pattern
        /// </summary>
        private GameObject? GetCurrentActiveWebPattern()
        {
            if (_strandPatterns == null) return null;

            var WebPattern = _attackControlFsm!.FsmVariables.FindFsmGameObject("Web Pattern");
            return WebPattern.value;
        }

        /// <summary>
        /// 获取Pattern中所有WebStrand的碰撞体信息（基于web_strand_caught�?
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

                    // === 修改点：直接使用 strandTransform (父物�?视觉物体) ===
                    // 只要父物体是你看到的那个丝线，就用它�?Position �?Up

                    Vector3 center = strandTransform.position; // 使用父物体中�?
                    Vector3 direction3D = strandTransform.right;  // 使用父物体朝�?(Unity自动处理旋转)
                    // Vector2 direction = new Vector2(direction3D.x, direction3D.y);
                    Vector2 direction = new Vector2(direction3D.x, direction3D.y).normalized;
                    // 计算线段端点
                    Vector3 startPoint = center - new Vector3(direction.x * extent, direction.y * extent, 0);
                    Vector3 endPoint = center + new Vector3(direction.x * extent, direction.y * extent, 0);

                    var info = new WebStrandColliderInfo
                    {
                        name = strandTransform.name,
                        centerPosition = center,
                        rotationZ = strandTransform.eulerAngles.z, // 记录父物体角�?
                        startPoint = startPoint,
                        endPoint = endPoint
                    };
                    colliderInfos.Add(info);
                }
            }
            return colliderInfos;
        }
        /// <summary>
        /// 获取Pattern中所有WebStrand的详细信息（用于调试和改进交汇点算法�?
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
            public Vector3 forward;  // Z轴方向（丝线�?长度"方向�?
            public Vector3 up;       // Y轴方�?
            public Vector3 right;    // X轴方�?
        }

        /// <summary>
        /// 丝线碰撞体信息（基于web_strand_caught�?
        /// </summary>
        private struct WebStrandColliderInfo
        {
            public string name;
            public Vector3 centerPosition;  // 碰撞体中心位�?X, Y)
            public float rotationZ;         // Z轴旋转角度（0=横向�?0=竖向�?
            public Vector3 startPoint;      // 线段起点（中�?10�?
            public Vector3 endPoint;        // 线段终点（中�?10�?
        }

        /// <summary>
        /// 计算交汇点（精确版：基于web_strand_caught碰撞体的线段相交�?
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

            // 对所有线段对进行两两相交检�?
            for (int i = 0; i < colliderInfos.Count - 1; i++)
            {
                for (int j = i + 1; j < colliderInfos.Count; j++)
                {
                    var strand1 = colliderInfos[i];
                    var strand2 = colliderInfos[j];

                    // 计算两条线段的交点（2D�?
                    Vector3? intersection = CalculateLineSegmentIntersection(
                        strand1.startPoint, strand1.endPoint,
                        strand2.startPoint, strand2.endPoint
                    );

                    if (intersection.HasValue)
                    {
                        intersections.Add(intersection.Value);
                        Log.Info($"  找到交点: {strand1.name} × {strand2.name}");
                        Log.Info($"    线段1: {strand1.startPoint} �?{strand1.endPoint} (旋转{strand1.rotationZ}°)");
                        Log.Info($"    线段2: {strand2.startPoint} �?{strand2.endPoint} (旋转{strand2.rotationZ}°)");
                        Log.Info($"    交点位置: {intersection.Value}");
                    }
                }
            }

            Log.Info($"=== 共找�?{intersections.Count} 个精确交汇点 ===");
            return intersections;
        }

        /// <summary>
        /// 计算两条2D线段的交�?
        /// 使用参数方程：P = P1 + t * (P2 - P1)
        /// 返回null表示线段不相交或平行
        /// </summary>
        private Vector3? CalculateLineSegmentIntersection(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
        {
            // 线段1: p1 �?p2
            // 线段2: p3 �?p4

            float x1 = p1.x, y1 = p1.y;
            float x2 = p2.x, y2 = p2.y;
            float x3 = p3.x, y3 = p3.y;
            float x4 = p4.x, y4 = p4.y;

            // 计算分母
            float denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);

            // 如果分母�?，说明两条线段平行或共线
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

            return null;  // 线段不相交（延长后可能相交，但线段本身不相交�?
        }
        #endregion
    }
}
