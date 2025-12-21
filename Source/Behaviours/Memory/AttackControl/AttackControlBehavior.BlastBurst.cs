using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AnySilkBoss.Source.Actions;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// Attack Control 行为 - BlastBurst 模块
    ///
    /// 包含三种爆炸攻击：
    /// 1. BLAST BURST 1：少量爆炸 + 丝球连段
    /// 2. BLAST BURST 2：黑屏 + 密集爆炸 + 预判攻击
    /// 3. BLAST BURST 3：汇聚爆炸向 Boss 靠拢
    /// </summary>
    internal partial class MemoryAttackControlBehavior
    {
        #region BlastBurst 字段
        // FSM 事件
        private FsmEvent? _blastBurst1Event;
        private FsmEvent? _blastBurst2Event;
        private FsmEvent? _blastBurst3Event;

        // 协程完成事件（用于协程结束时触发状态跳转，替代 Wait + FINISHED）
        private FsmEvent? _blastBurst1DoneEvent;
        private FsmEvent? _blastBurst2DoneEvent;
        private FsmEvent? _blastBurst3DoneEvent;

        // FSM 状态
        private FsmState? _blastBurst1PrepareState;
        private FsmState? _blastBurst1AttackState;
        private FsmState? _blastBurst1EndState;

        private FsmState? _blastBurst2PrepareState;
        private FsmState? _blastBurst2BlackoutState;
        private FsmState? _blastBurst2AttackState;
        private FsmState? _blastBurst2EndState;

        private FsmState? _blastBurst3PrepareState;
        private FsmState? _blastBurst3SpawnState;
        private FsmState? _blastBurst3ConvergeState;
        private FsmState? _blastBurst3FinalState;
        private FsmState? _blastBurst3EndState;

        // 管理器引用
        private FWBlastManager? _blastManagerRef;
        #endregion

        #region BlastBurst 初始化
        /// <summary>
        /// 初始化 BlastBurst 攻击
        /// 在 ModifyAttackControlFSM 中调用
        /// </summary>
        private void InitializeBlastBurstAttacks()
        {
            if (_attackControlFsm == null)
                return;

            // 获取管理器引用
            GetBlastBurstManagerReferences();

            // 注册事件
            RegisterBlastBurstEvents();

            // 创建状态
            CreateBlastBurst1States();
            CreateBlastBurst2States();
            CreateBlastBurst3States();

            // 添加到 HandPtnChoice 的跳转
            AddBlastBurstTransitionsToHandPtnChoice();

            Log.Info("[BlastBurst] 爆炸攻击初始化完成");
        }

        /// <summary>
        /// 获取管理器引用
        /// </summary>
        private void GetBlastBurstManagerReferences()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _blastManagerRef = managerObj.GetComponent<FWBlastManager>();
            }
        }

        /// <summary>
        /// 注册 BlastBurst 相关事件
        /// </summary>
        private void RegisterBlastBurstEvents()
        {
            if (_attackControlFsm == null)
                return;

            _blastBurst1Event = GetOrCreateEvent(_attackControlFsm, "BLAST BURST 1");
            _blastBurst2Event = GetOrCreateEvent(_attackControlFsm, "BLAST BURST 2");
            _blastBurst3Event = GetOrCreateEvent(_attackControlFsm, "BLAST BURST 3");

            // 注册协程完成事件
            _blastBurst1DoneEvent = GetOrCreateEvent(_attackControlFsm, "BLAST BURST 1 DONE");
            _blastBurst2DoneEvent = GetOrCreateEvent(_attackControlFsm, "BLAST BURST 2 DONE");
            _blastBurst3DoneEvent = GetOrCreateEvent(_attackControlFsm, "BLAST BURST 3 DONE");

            Log.Info("[BlastBurst] 事件注册完成");
        }
        #endregion

        #region BlastBurst1 - 少量爆炸 + 丝球连段
        /// <summary>
        /// 创建 BlastBurst1 状态链
        /// 流程：Prepare → Attack → End
        /// </summary>
        private void CreateBlastBurst1States()
        {
            if (_attackControlFsm == null)
                return;

            // 创建状态
            _blastBurst1PrepareState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 1 Prepare",
                "爆炸连段攻击准备"
            );
            _blastBurst1AttackState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 1 Attack",
                "爆炸连段攻击执行"
            );
            _blastBurst1EndState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 1 End",
                "爆炸连段攻击结束"
            );

            // 添加状态动作
            AddBlastBurst1PrepareActions(_blastBurst1PrepareState);
            AddBlastBurst1AttackActions(_blastBurst1AttackState);
            AddBlastBurst1EndActions(_blastBurst1EndState);

            // 设置状态转换
            SetFinishedTransition(_blastBurst1PrepareState, _blastBurst1AttackState);
            // Attack 状态使用协程完成事件触发跳转，而不是 Wait + FINISHED
            if (_blastBurst1DoneEvent != null)
            {
                AddTransition(_blastBurst1AttackState, CreateTransition(_blastBurst1DoneEvent, _blastBurst1EndState));
            }

            // End 状态返回 Idle
            var idleState = _idleState;
            if (idleState != null)
            {
                SetFinishedTransition(_blastBurst1EndState, idleState);
            }

            Log.Info("[BlastBurst1] 状态链创建完成");
        }

        /// <summary>
        /// 执行 BlastBurst1 攻击（协程方式，供 CallMethod 调用）
        /// </summary>
        public void ExecuteBlastBurst1Attack()
        {
            StartCoroutine(BlastBurst1AttackCoroutine());
        }

        /// <summary>
        /// BlastBurst1 攻击协程
        /// 场地范围: X:22-55, Y:135-145
        /// 特点: 更大尺寸 + 丝球环（随机选择反向加速度或径向爆发模式）
        /// </summary>
        private IEnumerator BlastBurst1AttackCoroutine()
        {
            if (_blastManagerRef == null)
            {
                Log.Warn("[BlastBurst1] FWBlastManager 未找到");
                yield break;
            }

            // 生成 6-9 个爆炸，每个带丝球连段
            int blastCount = Random.Range(6, 9);

            for (int i = 0; i < blastCount; i++)
            {
                // 随机位置（场地范围: X:22-55, Y:135-145）
                Vector3 blastPos = GetBlastBurst1Position();

                // 随机选择模式：50% 反向加速度，50% 径向爆发
                bool useReverseAccel = Random.value > 0.5f;

                // 生成爆炸：较大尺寸(1.0) + 丝球环
                _blastManagerRef.SpawnBombBlast(
                    blastPos,
                    parent: null,
                    size: 1f,
                    spawnSilkBallRing: true,
                    silkBallCount: 8,
                    initialOutwardSpeed: 20f,
                    reverseAcceleration: 20f,
                    maxInwardSpeed: 50f,
                    reverseAccelDuration: 5f,
                    useReverseAccelMode: useReverseAccel,
                    radialBurstSpeed: 18f,
                    releaseDelay: 0.3f
                );

                // 间隔延迟
                yield return new WaitForSeconds(Random.Range(0.3f, 0.6f));
            }

            Log.Info($"[BlastBurst1] 攻击完成，共生成 {blastCount} 个爆炸+丝球环");

            // 协程完成后发送事件触发 FSM 状态跳转
            if (_attackControlFsm != null && _blastBurst1DoneEvent != null)
            {
                _attackControlFsm.Fsm.Event(_blastBurst1DoneEvent);
            }
        }

        /// <summary>
        /// 获取 BlastBurst1 的随机位置（X:22-55, Y:135-145）
        /// </summary>
        private Vector3 GetBlastBurst1Position()
        {
            float x = Random.Range(20f, 57f);
            float y = Random.Range(134.5f, 144f);
            return new Vector3(x, y, 0f);
        }
        #endregion

        #region BlastBurst2 - 黑屏 + 密集爆炸 + 预判
        /// <summary>
        /// 创建 BlastBurst2 状态链
        /// 流程：Prepare → Blackout → Attack → End
        /// </summary>
        private void CreateBlastBurst2States()
        {
            if (_attackControlFsm == null)
                return;

            // 创建状态
            _blastBurst2PrepareState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 2 Prepare",
                "黑屏爆炸准备"
            );
            _blastBurst2BlackoutState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 2 Blackout",
                "黑屏效果"
            );
            _blastBurst2AttackState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 2 Attack",
                "密集爆炸攻击"
            );
            _blastBurst2EndState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 2 End",
                "黑屏爆炸结束"
            );

            // 添加状态动作
            AddBlastBurst2PrepareActions(_blastBurst2PrepareState);
            AddBlastBurst2BlackoutActions(_blastBurst2BlackoutState);
            AddBlastBurst2AttackActions(_blastBurst2AttackState);
            AddBlastBurst2EndActions(_blastBurst2EndState);

            // 设置状态转换
            SetFinishedTransition(_blastBurst2PrepareState, _blastBurst2BlackoutState);
            // Blackout 状态使用协程完成事件触发跳转，而不是 Wait + FINISHED
            if (_blastBurst2DoneEvent != null)
            {
                AddTransition(_blastBurst2BlackoutState, CreateTransition(_blastBurst2DoneEvent, _blastBurst2AttackState));
            }
            SetFinishedTransition(_blastBurst2AttackState, _blastBurst2EndState);

            // End 状态返回 Idle
            var idleState2 = _idleState;
            if (idleState2 != null)
            {
                SetFinishedTransition(_blastBurst2EndState, idleState2);
            }

            Log.Info("[BlastBurst2] 状态链创建完成");
        }

        /// <summary>
        /// 执行 BlastBurst2 攻击（协程方式）
        /// </summary>
        public void ExecuteBlastBurst2Attack()
        {
            StartCoroutine(BlastBurst2AttackCoroutine());
        }

        /// <summary>
        /// BlastBurst2 攻击协程
        /// 特点: 黑屏后保持0.3s，全部爆发完成后恢复屏幕和Boss
        /// </summary>
        private IEnumerator BlastBurst2AttackCoroutine()
        {
            if (_blastManagerRef == null)
            {
                Log.Warn("[BlastBurst2] FWBlastManager 未找到");
                yield break;
            }

            // 1. 淡入黑屏
            yield return StartCoroutine(FadeToBlack(0.3f));

            // 2. 黑屏保持 0.3s
            yield return new WaitForSeconds(0.3f);

            // 3. 隐藏 Boss
            SetBossVisible(false);

            // 4. 密集爆炸
            int totalBlasts = Random.Range(10, 15);
            int predictiveBlasts = 4; // 其中 4 个使用预判

            for (int i = 0; i < totalBlasts; i++)
            {
                Vector3 blastPos;

                if (i < predictiveBlasts)
                {
                    // 预判攻击（限制在场地范围 X:20-57, Y:134.5-144）
                    blastPos = PredictiveBlastAction.CalculatePrediction(
                        predictionTime: 0.8f,
                        randomOffset: 2f,
                        minX: 20f, maxX: 57f,
                        minY: 134.5f, maxY: 144f);
                    _blastManagerRef.SpawnBombBlast(blastPos, size: 0.65f);
                }
                else
                {
                    // 随机位置
                    blastPos = GetBlastBurst1Position();
                    _blastManagerRef.SpawnBombBlast(blastPos, size: Random.Range(0.9f, 1.2f));
                }
                yield return new WaitForSeconds(Random.Range(0.08f, 0.2f));
            }

            // 5. 等待最后一个爆炸完成
            yield return new WaitForSeconds(0.5f);

            // 6. 恢复 Boss
            SetBossVisible(true);

            // 7. 淡出黑屏
            yield return StartCoroutine(FadeFromBlack(0.3f));

            Log.Info($"[BlastBurst2] 攻击完成，共 {totalBlasts} 个爆炸（含 {predictiveBlasts} 个预判）");

            // 协程完成后发送事件触发 FSM 状态跳转
            if (_attackControlFsm != null && _blastBurst2DoneEvent != null)
            {
                _attackControlFsm.Fsm.Event(_blastBurst2DoneEvent);
            }
        }

        /// <summary>
        /// 淡入黑屏
        /// </summary>
        private IEnumerator FadeToBlack(float duration)
        {
            var fader = GameObject.Find("Beast Slash Fader");
            if (fader != null)
            {
                var spriteRenderer = fader.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null)
                {
                    float elapsed = 0f;
                    Color startColor = new Color(0, 0, 0, 0);
                    Color endColor = new Color(0, 0, 0, 1);
                    while (elapsed < duration)
                    {
                        elapsed += Time.deltaTime;
                        spriteRenderer.color = Color.Lerp(startColor, endColor, elapsed / duration);
                        yield return null;
                    }
                    spriteRenderer.color = endColor;
                }
            }
            yield return null;
        }

        /// <summary>
        /// 淡出黑屏
        /// </summary>
        private IEnumerator FadeFromBlack(float duration)
        {
            var fader = GameObject.Find("Beast Slash Fader");
            if (fader != null)
            {
                var spriteRenderer = fader.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null)
                {
                    float elapsed = 0f;
                    Color startColor = new Color(0, 0, 0, 1);
                    Color endColor = new Color(0, 0, 0, 0);
                    while (elapsed < duration)
                    {
                        elapsed += Time.deltaTime;
                        spriteRenderer.color = Color.Lerp(startColor, endColor, elapsed / duration);
                        yield return null;
                    }
                    spriteRenderer.color = endColor;
                }
            }
            yield return null;
        }

        /// <summary>
        /// 设置 Boss 可见性
        /// </summary>
        private void SetBossVisible(bool visible)
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = visible;
            }
        }
        #endregion

        #region BlastBurst3 - 汇聚爆炸
        /// <summary>
        /// 创建 BlastBurst3 状态链
        /// 流程：Prepare → Spawn → Converge → Final → End
        /// </summary>
        private void CreateBlastBurst3States()
        {
            if (_attackControlFsm == null)
                return;

            // 创建状态
            _blastBurst3PrepareState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 3 Prepare",
                "汇聚爆炸准备"
            );
            _blastBurst3SpawnState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 3 Spawn",
                "生成外围爆炸"
            );
            _blastBurst3ConvergeState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 3 Converge",
                "爆炸汇聚"
            );
            _blastBurst3FinalState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 3 Final",
                "最终爆炸"
            );
            _blastBurst3EndState = CreateAndAddState(
                _attackControlFsm,
                "Blast Burst 3 End",
                "汇聚爆炸结束"
            );

            // 添加状态动作
            AddBlastBurst3PrepareActions(_blastBurst3PrepareState);
            AddBlastBurst3SpawnActions(_blastBurst3SpawnState);
            AddBlastBurst3ConvergeActions(_blastBurst3ConvergeState);
            AddBlastBurst3FinalActions(_blastBurst3FinalState);
            AddBlastBurst3EndActions(_blastBurst3EndState);

            // 设置状态转换
            SetFinishedTransition(_blastBurst3PrepareState, _blastBurst3SpawnState);
            // Spawn 状态使用协程完成事件触发跳转，而不是 Wait + FINISHED
            // 协程完成后直接跳到 End 状态（省略空的 Converge 和 Final 状态）
            if (_blastBurst3DoneEvent != null)
            {
                AddTransition(_blastBurst3SpawnState, CreateTransition(_blastBurst3DoneEvent, _blastBurst3EndState));
            }

            // End 状态返回 Idle
            var idleState3 = _idleState;
            if (idleState3 != null)
            {
                SetFinishedTransition(_blastBurst3EndState, idleState3);
            }

            Log.Info("[BlastBurst3] 状态链创建完成");
        }

        /// <summary>
        /// 执行 BlastBurst3 攻击（协程方式）
        /// </summary>
        public void ExecuteBlastBurst3Attack()
        {
            StartCoroutine(BlastBurst3AttackCoroutine());
        }

        /// <summary>
        /// BlastBurst3 攻击协程
        /// 特点: 在Boss外围生成爆炸，爆炸会向Boss移动，到达后触发爆炸
        /// 使用 FWBlastManager.SpawnMovingBombBlast 实现移动模式
        /// </summary>
        private IEnumerator BlastBurst3AttackCoroutine()
        {
            if (_blastManagerRef == null)
            {
                Log.Warn("[BlastBurst3] FWBlastManager 未找到");
                yield break;
            }

            // 获取 Boss Transform
            Transform bossTransform = transform;

            // 配置参数
            int blastCount = Random.Range(7, 11);
            float outerRadius = 18f;   // 外围半径
            float innerRadius = 13f;    // 内围半径
            float moveAcceleration = 30f;
            float moveMaxSpeed = 7f;
            float reachDistance = 0.2f;
            float moveTimeout = 0.9f;

            Log.Info($"[BlastBurst3] 开始生成 {blastCount} 个汇聚爆炸");

            // 记录生成开始时间
            float spawnStartTime = Time.time;

            for (int i = 0; i < blastCount; i++)
            {
                // 均匀分布在外围，主要在下半部分（玩家活动区域）
                float angle;
                if (Random.value < 0.7f)
                {
                    // 下半部分: 180° - 360°
                    angle = Random.Range(180f, 360f);
                }
                else
                {
                    // 上半部分: 0° - 180°
                    angle = Random.Range(0f, 180f);
                }

                // 在外围和内围之间随机半径
                float radius = Random.Range(innerRadius, outerRadius);
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                // 获取当前 Boss 位置作为生成中心
                Vector3 currentBossPos = bossTransform.position;
                Vector3 spawnPos = currentBossPos + new Vector3(direction.x, direction.y, 0) * radius;
                float size = Random.Range(0.8f, 1.3f);
                // 使用移动模式生成爆炸
                _blastManagerRef.SpawnMovingBombBlast(
                    spawnPos,
                    bossTransform,
                    moveAcceleration,
                    moveMaxSpeed,
                    reachDistance,
                    moveTimeout,
                    size: size
                );

                // 生成间隔
                yield return new WaitForSeconds(Random.Range(0.10f, 0.16f));
            }

            // 计算生成耗时
            float spawnDuration = Time.time - spawnStartTime;
            // 等待最后一个爆炸完成追踪和爆炸动画
            // moveTimeout(1.8s) + 爆炸动画时间(Appear Pause 0.3s + Blast 0.3s + Wait 0.8s) + 小余量
            float waitAfterSpawn = moveTimeout + 0.05f;
            yield return new WaitForSeconds(waitAfterSpawn);
            _naChargeEffect!.SetActive(false);
            // 最终在 Boss 位置生成一个大型结束爆炸
            _blastManagerRef.SpawnBombBlast(bossTransform.position, size: 3f);

            Log.Info($"[BlastBurst3] 汇聚爆炸攻击完成，最终爆炸大小: 3倍");

            // 协程完成后发送事件触发 FSM 状态跳转
            if (_attackControlFsm != null && _blastBurst3DoneEvent != null)
            {
                _attackControlFsm.Fsm.Event(_blastBurst3DoneEvent);
            }
        }
        #endregion

        #region BlastBurst 辅助方法
        /// <summary>
        /// 添加 BlastBurst 跳转到 HandPtnChoice
        /// </summary>
        private void AddBlastBurstTransitionsToHandPtnChoice()
        {
            if (_handPtnChoiceState == null)
                return;

            // 添加从 HandPtnChoice 到各个 BlastBurst 攻击的跳转
            if (_blastBurst1Event != null && _blastBurst1PrepareState != null)
            {
                AddTransition(
                    _handPtnChoiceState,
                    CreateTransition(_blastBurst1Event, _blastBurst1PrepareState)
                );
            }

            if (_blastBurst2Event != null && _blastBurst2PrepareState != null)
            {
                AddTransition(
                    _handPtnChoiceState,
                    CreateTransition(_blastBurst2Event, _blastBurst2PrepareState)
                );
            }

            if (_blastBurst3Event != null && _blastBurst3PrepareState != null)
            {
                AddTransition(
                    _handPtnChoiceState,
                    CreateTransition(_blastBurst3Event, _blastBurst3PrepareState)
                );
            }

            // 将 BlastBurst 事件添加到 SendRandomEventV4 的随机选择池
            AddBlastBurstToSendRandomEvent();

            Log.Info("[BlastBurst] HandPtnChoice 跳转添加完成");
        }

        /// <summary>
        /// 将 BlastBurst 事件添加到 SendRandomEventV4 动作中
        /// </summary>
        private void AddBlastBurstToSendRandomEvent()
        {
            if (_handPtnChoiceState == null) return;

            var sendRandomEventActions = _handPtnChoiceState.Actions.OfType<SendRandomEventV4>().ToList();
            foreach (var action in sendRandomEventActions)
            {
                // 计算需要添加的事件数量
                int eventsToAdd = 3; // BLAST BURST 1, 2, 3
                int currentLength = action.events.Length;
                int newLength = currentLength + eventsToAdd;

                // 创建新数组
                var newEvents = new FsmEvent[newLength];
                var newWeights = new FsmFloat[newLength];
                var newEventMax = new FsmInt[newLength];
                var newMissedMax = new FsmInt[newLength];

                // 复制原有数据
                for (int i = 0; i < currentLength; i++)
                {
                    newEvents[i] = action.events[i];
                    newWeights[i] = action.weights[i];
                    newEventMax[i] = action.eventMax[i];
                    newMissedMax[i] = action.missedMax[i];
                }

                // 添加 BLAST BURST 1
                if (_blastBurst1Event != null)
                {
                    newEvents[currentLength] = _blastBurst1Event;
                    newWeights[currentLength] = new FsmFloat(1.5f);
                    newEventMax[currentLength] = new FsmInt(2);
                    newMissedMax[currentLength] = new FsmInt(5);
                }

                // 添加 BLAST BURST 2
                if (_blastBurst2Event != null)
                {
                    newEvents[currentLength + 1] = _blastBurst2Event;
                    newWeights[currentLength + 1] = new FsmFloat(1f);
                    newEventMax[currentLength + 1] = new FsmInt(1);
                    newMissedMax[currentLength + 1] = new FsmInt(6);
                }

                // 添加 BLAST BURST 3
                if (_blastBurst3Event != null)
                {
                    newEvents[currentLength + 2] = _blastBurst3Event;
                    newWeights[currentLength + 2] = new FsmFloat(1f);
                    newEventMax[currentLength + 2] = new FsmInt(1);
                    newMissedMax[currentLength + 2] = new FsmInt(6);
                }

                // 更新动作属性
                action.events = newEvents;
                action.weights = newWeights;
                action.eventMax = newEventMax;
                action.missedMax = newMissedMax;

                Log.Info($"[BlastBurst] 已添加 {eventsToAdd} 个爆炸攻击事件到 SendRandomEventV4");
            }
        }

        /// <summary>
        /// 获取随机爆炸位置（场地范围内，默认范围）
        /// </summary>
        private Vector3 GetRandomBlastPosition()
        {
            // 使用 BlastBurst1 的场地范围
            return GetBlastBurst1Position();
        }

        /// <summary>
        /// 生成丝球环（用于爆炸连段）
        /// </summary>
        private void SpawnSilkBallRing(
            Vector3 center,
            int count,
            float radius,
            float speed,
            bool useReverseAccel
        )
        {
            if (_silkBallManager == null)
                return;

            float angleStep = 360f / count;
            float startAngle = Random.Range(0f, 360f);

            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + i * angleStep;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                Vector3 spawnPos = center + new Vector3(direction.x, direction.y, 0) * radius;

                var silkBall = _silkBallManager.SpawnMemorySilkBall(
                    spawnPos,
                    acceleration: 0f,
                    maxSpeed: speed,
                    chaseTime: 10f,
                    scale: 1f,
                    enableRotation: true
                );

                if (silkBall != null)
                {
                    var rb = silkBall.GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        rb.gravityScale = 0f;
                        rb.linearVelocity = direction * speed;
                        silkBall.StartProtectionTime(0.5f);
                    }
                }
            }
        }
        #endregion

        #region BlastBurst 状态动作
        /// <summary>
        /// 添加 BlastBurst1 准备状态动作
        /// </summary>
        private void AddBlastBurst1PrepareActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                // 短暂等待
                new Wait { time = 0.2f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst1 攻击状态动作
        /// </summary>
        private void AddBlastBurst1AttackActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                // 调用协程执行攻击，协程完成后会发送 BLAST BURST 1 DONE 事件
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("ExecuteBlastBurst1Attack") { Value = "ExecuteBlastBurst1Attack" },
                    parameters = new FsmVar[0],
                    storeResult = new FsmVar()
                }
                // 不再使用 Wait，由协程完成后发送事件触发跳转
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst1 结束状态动作
        /// </summary>
        private void AddBlastBurst1EndActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                // 短暂恢复
                new Wait { time = 0.3f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst2 准备状态动作
        /// </summary>
        private void AddBlastBurst2PrepareActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                new Wait { time = 0.1f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst2 黑屏状态动作
        /// </summary>
        private void AddBlastBurst2BlackoutActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                // 调用协程执行攻击（包含黑屏和爆炸），协程完成后会发送 BLAST BURST 2 DONE 事件
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("ExecuteBlastBurst2Attack") { Value = "ExecuteBlastBurst2Attack" },
                    parameters = new FsmVar[0],
                    storeResult = new FsmVar()
                }
                // 不再使用 Wait，由协程完成后发送事件触发跳转
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst2 攻击状态动作（空，逻辑在黑屏状态）
        /// </summary>
        private void AddBlastBurst2AttackActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                new Wait { time = 0.1f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst2 结束状态动作
        /// </summary>
        private void AddBlastBurst2EndActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                new Wait { time = 0.3f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst3 准备状态动作
        /// </summary>
        private void AddBlastBurst3PrepareActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                // 设置 BossControl 的 InBurst3 变量为 true，触发 Boss 动画协作
                new SetFsmBool
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" },
                    variableName = new FsmString("InBurst3") { Value = "InBurst3" },
                    setValue = new FsmBool(true),
                    everyFrame = false
                },
                new ActivateGameObject
                    {
                        gameObject = new FsmOwnerDefault
                        {
                            OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            gameObject = new FsmGameObject { Value = _naChargeEffect }
                        },
                        activate = new FsmBool(true),
                        recursive = new FsmBool(false),
                        resetOnExit = false,
                        everyFrame = false
                    },
                new Wait { time = 0.2f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst3 生成状态动作
        /// </summary>
        private void AddBlastBurst3SpawnActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                // 调用协程执行攻击，协程完成后会发送 BLAST BURST 3 DONE 事件
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("ExecuteBlastBurst3Attack") { Value = "ExecuteBlastBurst3Attack" },
                    parameters = new FsmVar[0],
                    storeResult = new FsmVar()
                }
                // 不再使用 Wait，由协程完成后发送事件触发跳转
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst3 汇聚状态动作（空，逻辑在生成状态）
        /// </summary>
        private void AddBlastBurst3ConvergeActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                new Wait { time = 0.05f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst3 最终爆炸状态动作（空，逻辑在生成状态）
        /// </summary>
        private void AddBlastBurst3FinalActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                new Wait { time = 0.05f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 BlastBurst3 结束状态动作
        /// </summary>
        private void AddBlastBurst3EndActions(FsmState state)
        {
            if (state == null) return;

            var actions = new List<FsmStateAction>
            {
                // 发送 END BURST3 事件给 BossControl，通知 Boss 结束动画
                new SendEventByName
                {
                    eventTarget = new FsmEventTarget
                    {
                        target = FsmEventTarget.EventTarget.GameObjectFSM,
                        excludeSelf = new FsmBool(false),
                        gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                        fsmName = new FsmString("Control") { Value = "Control" }
                    },
                    sendEvent = new FsmString("END BURST3") { Value = "END BURST3" },
                    delay = new FsmFloat(0f),
                    everyFrame = false
                },
                new Wait { time = 0.1f, finishEvent = FsmEvent.Finished }
            };
            state.Actions = actions.ToArray();
        }
        #endregion
    }
}
