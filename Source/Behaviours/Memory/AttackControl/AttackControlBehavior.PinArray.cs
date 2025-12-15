using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Actions;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// Attack Control 行为 - PinArray 模块（剑阵大招）
    /// 
    /// 剑阵大招流程：
    /// 1. 召唤 40 针在场地中上（全部朝下）
    /// 2. 旋转展开
    /// 3. 展开完毕后，每隔一把向下攻击（20 针同时落下）
    /// 4. 砸地后不返回池，向上移动一点点
    /// 5. 对所有针角度打乱
    /// 6. 逐个发射剩余针
    /// </summary>
    internal partial class MemoryAttackControlBehavior
    {
        #region PinArray 字段
        // FSM 事件
        private FsmEvent? _pinArrayAttackEvent;

        // FSM 状态
        private FsmState? _pinArrayPrepareState;
        private FsmState? _pinArraySpawnState;
        private FsmState? _pinArrayExpandState;
        private FsmState? _pinArrayFirstWaveState;
        private FsmState? _pinArrayRiseState;
        private FsmState? _pinArrayScrambleState;
        private FsmState? _pinArrayFireRemainingState;
        private FsmState? _pinArrayEndState;

        // 管理器引用
        private FWPinManager? _pinManagerRef;

        // 剑阵运行时数据
        private List<GameObject> _pinArrayAllPins = new List<GameObject>();
        private List<GameObject> _pinArrayFirstWavePins = new List<GameObject>();
        private List<GameObject> _pinArraySecondWavePins = new List<GameObject>();

        // 剑阵参数
        private const int PIN_ARRAY_COUNT = 40;
        private const float PIN_ARRAY_SPAWN_CENTER_X = 37.5f;
        private const float PIN_ARRAY_SPAWN_CENTER_Y = 20f;
        private const float PIN_ARRAY_INITIAL_RADIUS = 2f;
        private const float PIN_ARRAY_FINAL_RADIUS = 12f;
        private const float PIN_ARRAY_EXPAND_DURATION = 2f;
        private const float PIN_ARRAY_EXPAND_ROTATION_SPEED = 180f;
        private const float PIN_ARRAY_FIRST_WAVE_SPEED = 30f;
        private const float PIN_ARRAY_RISE_DISTANCE = 2f;
        private const float PIN_ARRAY_ANGLE_SCRAMBLE_RANGE = 60f;
        private const float PIN_ARRAY_REMAINING_FIRE_SPEED = 25f;
        #endregion

        #region PinArray 初始化
        /// <summary>
        /// 初始化 PinArray 攻击
        /// 在 ModifyAttackControlFSM 中调用
        /// </summary>
        private void InitializePinArrayAttack()
        {
            if (_attackControlFsm == null) return;

            // 获取管理器引用
            GetPinArrayManagerReferences();

            // 注册事件
            RegisterPinArrayEvents();

            // 创建状态
            CreatePinArrayStates();

            // 添加到 HandPtnChoice 的跳转
            AddPinArrayTransitionToHandPtnChoice();

            Log.Info("[PinArray] 剑阵大招初始化完成");
        }

        /// <summary>
        /// 获取管理器引用
        /// </summary>
        private void GetPinArrayManagerReferences()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _pinManagerRef = managerObj.GetComponent<FWPinManager>();
            }
        }

        /// <summary>
        /// 注册 PinArray 相关事件
        /// </summary>
        private void RegisterPinArrayEvents()
        {
            if (_attackControlFsm == null) return;

            _pinArrayAttackEvent = GetOrCreateEvent(_attackControlFsm, "PIN ARRAY ATTACK");

            Log.Info("[PinArray] 事件注册完成");
        }
        #endregion

        #region PinArray 状态创建
        /// <summary>
        /// 创建 PinArray 状态链
        /// 流程：Prepare → Spawn → Expand → FirstWave → Rise → Scramble → FireRemaining → End
        /// </summary>
        private void CreatePinArrayStates()
        {
            if (_attackControlFsm == null) return;

            // 创建所有状态
            _pinArrayPrepareState = CreateAndAddState(_attackControlFsm, 
                "Pin Array Prepare", "剑阵准备");
            _pinArraySpawnState = CreateAndAddState(_attackControlFsm, 
                "Pin Array Spawn", "生成飞针");
            _pinArrayExpandState = CreateAndAddState(_attackControlFsm, 
                "Pin Array Expand", "旋转展开");
            _pinArrayFirstWaveState = CreateAndAddState(_attackControlFsm, 
                "Pin Array First Wave", "第一波攻击");
            _pinArrayRiseState = CreateAndAddState(_attackControlFsm, 
                "Pin Array Rise", "剩余针上升");
            _pinArrayScrambleState = CreateAndAddState(_attackControlFsm, 
                "Pin Array Scramble", "角度打乱");
            _pinArrayFireRemainingState = CreateAndAddState(_attackControlFsm, 
                "Pin Array Fire Remaining", "发射剩余针");
            _pinArrayEndState = CreateAndAddState(_attackControlFsm, 
                "Pin Array End", "剑阵结束");

            // 添加状态动作
            AddPinArrayPrepareActions(_pinArrayPrepareState);
            AddPinArraySpawnActions(_pinArraySpawnState);
            AddPinArrayExpandActions(_pinArrayExpandState);
            AddPinArrayFirstWaveActions(_pinArrayFirstWaveState);
            AddPinArrayRiseActions(_pinArrayRiseState);
            AddPinArrayScrambleActions(_pinArrayScrambleState);
            AddPinArrayFireRemainingActions(_pinArrayFireRemainingState);
            AddPinArrayEndActions(_pinArrayEndState);

            // 设置状态转换
            SetFinishedTransition(_pinArrayPrepareState, _pinArraySpawnState);
            SetFinishedTransition(_pinArraySpawnState, _pinArrayExpandState);
            SetFinishedTransition(_pinArrayExpandState, _pinArrayFirstWaveState);
            SetFinishedTransition(_pinArrayFirstWaveState, _pinArrayRiseState);
            SetFinishedTransition(_pinArrayRiseState, _pinArrayScrambleState);
            SetFinishedTransition(_pinArrayScrambleState, _pinArrayFireRemainingState);
            SetFinishedTransition(_pinArrayFireRemainingState, _pinArrayEndState);
            
            // End 状态返回 HandPtnChoice
            if (_handPtnChoiceState != null)
            {
                SetFinishedTransition(_pinArrayEndState, _handPtnChoiceState);
            }

            Log.Info("[PinArray] 状态链创建完成");
        }

        /// <summary>
        /// 添加 PinArray 跳转到 HandPtnChoice
        /// </summary>
        private void AddPinArrayTransitionToHandPtnChoice()
        {
            if (_handPtnChoiceState == null || _pinArrayAttackEvent == null || _pinArrayPrepareState == null)
                return;

            AddTransition(_handPtnChoiceState, CreateTransition(_pinArrayAttackEvent, _pinArrayPrepareState));

            Log.Info("[PinArray] HandPtnChoice 跳转添加完成");
        }
        #endregion

        #region PinArray 状态动作
        private void AddPinArrayPrepareActions(FsmState state)
        {
            // 准备阶段：播放蓄力动画/音效，清理之前的数据
            var prepareAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PinArrayPrepare") { Value = "PinArrayPrepare" },
                parameters = new FsmVar[0]
            };

            var waitAction = new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[] { prepareAction, waitAction };
        }

        private void AddPinArraySpawnActions(FsmState state)
        {
            var spawnAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PinArraySpawn") { Value = "PinArraySpawn" },
                parameters = new FsmVar[0]
            };

            var waitAction = new Wait
            {
                time = new FsmFloat(0.3f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[] { spawnAction, waitAction };
        }

        private void AddPinArrayExpandActions(FsmState state)
        {
            var expandAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PinArrayStartExpand") { Value = "PinArrayStartExpand" },
                parameters = new FsmVar[0]
            };

            var waitAction = new Wait
            {
                time = new FsmFloat(PIN_ARRAY_EXPAND_DURATION + 0.2f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[] { expandAction, waitAction };
        }

        private void AddPinArrayFirstWaveActions(FsmState state)
        {
            var fireAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PinArrayFireFirstWave") { Value = "PinArrayFireFirstWave" },
                parameters = new FsmVar[0]
            };

            // 等待第一波落地
            var waitAction = new Wait
            {
                time = new FsmFloat(1.5f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[] { fireAction, waitAction };
        }

        private void AddPinArrayRiseActions(FsmState state)
        {
            var riseAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PinArrayRiseRemaining") { Value = "PinArrayRiseRemaining" },
                parameters = new FsmVar[0]
            };

            var waitAction = new Wait
            {
                time = new FsmFloat(0.6f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[] { riseAction, waitAction };
        }

        private void AddPinArrayScrambleActions(FsmState state)
        {
            var scrambleAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PinArrayScrambleAngles") { Value = "PinArrayScrambleAngles" },
                parameters = new FsmVar[0]
            };

            var waitAction = new Wait
            {
                time = new FsmFloat(0.3f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[] { scrambleAction, waitAction };
        }

        private void AddPinArrayFireRemainingActions(FsmState state)
        {
            var fireAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PinArrayStartFireRemaining") { Value = "PinArrayStartFireRemaining" },
                parameters = new FsmVar[0]
            };

            // 等待所有针发射完毕（根据针数量和间隔估算）
            var waitAction = new Wait
            {
                time = new FsmFloat(PIN_ARRAY_COUNT / 2 * 0.15f + 1f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[] { fireAction, waitAction };
        }

        private void AddPinArrayEndActions(FsmState state)
        {
            var endAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PinArrayEnd") { Value = "PinArrayEnd" },
                parameters = new FsmVar[0]
            };

            var waitAction = new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            };

            state.Actions = new FsmStateAction[] { endAction, waitAction };
        }
        #endregion

        #region PinArray 执行方法（供 CallMethod 调用）
        /// <summary>
        /// 准备阶段
        /// </summary>
        public void PinArrayPrepare()
        {
            // 清理之前的数据
            _pinArrayAllPins.Clear();
            _pinArrayFirstWavePins.Clear();
            _pinArraySecondWavePins.Clear();

            Log.Info("[PinArray] 准备阶段开始");
        }

        /// <summary>
        /// 生成所有针
        /// </summary>
        public void PinArraySpawn()
        {
            if (_pinManagerRef == null)
            {
                Log.Warn("[PinArray] FWPinManager 未找到");
                return;
            }

            Vector3 center = new Vector3(PIN_ARRAY_SPAWN_CENTER_X, PIN_ARRAY_SPAWN_CENTER_Y, 0f);

            for (int i = 0; i < PIN_ARRAY_COUNT; i++)
            {
                // 计算初始位置（紧密排列在小圆内）
                float angle = (360f / PIN_ARRAY_COUNT) * i;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                Vector3 spawnPos = center + new Vector3(direction.x, direction.y, 0) * PIN_ARRAY_INITIAL_RADIUS;

                // 生成针
                var pin = _pinManagerRef.SpawnPinProjectile(spawnPos);

                if (pin != null)
                {
                    // 设置针的旋转角度（全部朝下，即 rotation.z = -90）
                    pin.transform.rotation = Quaternion.Euler(0, 0, -90f);
                    
                    _pinArrayAllPins.Add(pin);

                    // 分配到两个波次
                    if (i % 2 == 0)
                    {
                        _pinArrayFirstWavePins.Add(pin);  // 偶数索引 -> 第一波
                    }
                    else
                    {
                        _pinArraySecondWavePins.Add(pin); // 奇数索引 -> 第二波
                    }
                }
            }

            Log.Info($"[PinArray] 生成 {_pinArrayAllPins.Count} 把针");
        }

        /// <summary>
        /// 开始展开动画
        /// </summary>
        public void PinArrayStartExpand()
        {
            StartCoroutine(PinArrayExpandCoroutine());
        }

        /// <summary>
        /// 展开动画协程
        /// </summary>
        private IEnumerator PinArrayExpandCoroutine()
        {
            float elapsed = 0f;
            int count = _pinArrayAllPins.Count;
            Vector3 center = new Vector3(PIN_ARRAY_SPAWN_CENTER_X, PIN_ARRAY_SPAWN_CENTER_Y, 0f);

            // 记录每个针的初始和目标位置
            var initialPositions = new Vector3[count];
            var targetPositions = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                if (_pinArrayAllPins[i] == null) continue;

                initialPositions[i] = _pinArrayAllPins[i].transform.position;

                float angle = (360f / count) * i;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                targetPositions[i] = center + new Vector3(direction.x, direction.y, 0) * PIN_ARRAY_FINAL_RADIUS;
            }

            while (elapsed < PIN_ARRAY_EXPAND_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / PIN_ARRAY_EXPAND_DURATION);

                // 整体旋转
                float currentRotation = PIN_ARRAY_EXPAND_ROTATION_SPEED * elapsed;

                for (int i = 0; i < count; i++)
                {
                    if (_pinArrayAllPins[i] == null) continue;

                    // 插值位置
                    Vector3 basePos = Vector3.Lerp(initialPositions[i], targetPositions[i], t);

                    // 应用旋转（绕中心点）
                    Vector3 offset = basePos - center;
                    float rotRad = currentRotation * Mathf.Deg2Rad;
                    Vector3 rotatedOffset = new Vector3(
                        offset.x * Mathf.Cos(rotRad) - offset.y * Mathf.Sin(rotRad),
                        offset.x * Mathf.Sin(rotRad) + offset.y * Mathf.Cos(rotRad),
                        offset.z
                    );

                    _pinArrayAllPins[i].transform.position = center + rotatedOffset;
                }

                yield return null;
            }

            Log.Info("[PinArray] 展开动画完成");
        }

        /// <summary>
        /// 发射第一波
        /// </summary>
        public void PinArrayFireFirstWave()
        {
            foreach (var pin in _pinArrayFirstWavePins)
            {
                if (pin == null) continue;

                // 设置朝下
                pin.transform.rotation = Quaternion.Euler(0, 0, -90);

                // 设置速度
                var rb = pin.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.down * PIN_ARRAY_FIRST_WAVE_SPEED;
                }

                // 触发 FSM 发射状态
                var fsm = pin.GetComponent<PlayMakerFSM>();
                if (fsm != null)
                {
                    fsm.SendEvent("DIRECT_FIRE");
                }
            }

            Log.Info($"[PinArray] 第一波发射 {_pinArrayFirstWavePins.Count} 把针");
        }

        /// <summary>
        /// 剩余针上升
        /// </summary>
        public void PinArrayRiseRemaining()
        {
            StartCoroutine(PinArrayRiseCoroutine());
        }

        /// <summary>
        /// 上升协程
        /// </summary>
        private IEnumerator PinArrayRiseCoroutine()
        {
            float riseDuration = 0.5f;
            float elapsed = 0f;

            var initialPositions = new Dictionary<GameObject, Vector3>();
            foreach (var pin in _pinArraySecondWavePins)
            {
                if (pin != null)
                {
                    initialPositions[pin] = pin.transform.position;
                }
            }

            while (elapsed < riseDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / riseDuration;

                foreach (var pin in _pinArraySecondWavePins)
                {
                    if (pin == null || !initialPositions.ContainsKey(pin)) continue;

                    Vector3 targetPos = initialPositions[pin] + Vector3.up * PIN_ARRAY_RISE_DISTANCE;
                    pin.transform.position = Vector3.Lerp(initialPositions[pin], targetPos, t);
                }

                yield return null;
            }

            Log.Info("[PinArray] 剩余针上升完成");
        }

        /// <summary>
        /// 打乱角度
        /// </summary>
        public void PinArrayScrambleAngles()
        {
            foreach (var pin in _pinArraySecondWavePins)
            {
                if (pin == null) continue;

                float randomAngle = Random.Range(-PIN_ARRAY_ANGLE_SCRAMBLE_RANGE, PIN_ARRAY_ANGLE_SCRAMBLE_RANGE);
                float baseAngle = -90f;  // 基础朝下
                pin.transform.rotation = Quaternion.Euler(0, 0, baseAngle + randomAngle);
            }

            Log.Info("[PinArray] 角度打乱完成");
        }

        /// <summary>
        /// 开始发射剩余针
        /// </summary>
        public void PinArrayStartFireRemaining()
        {
            StartCoroutine(PinArrayFireRemainingCoroutine());
        }

        /// <summary>
        /// 发射剩余针协程
        /// </summary>
        private IEnumerator PinArrayFireRemainingCoroutine()
        {
            // 随机打乱发射顺序
            var shuffledPins = new List<GameObject>(_pinArraySecondWavePins);
            for (int i = shuffledPins.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var temp = shuffledPins[i];
                shuffledPins[i] = shuffledPins[j];
                shuffledPins[j] = temp;
            }

            foreach (var pin in shuffledPins)
            {
                if (pin == null) continue;

                // 获取当前朝向
                float angle = pin.transform.eulerAngles.z;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                // 设置速度
                var rb = pin.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = direction * PIN_ARRAY_REMAINING_FIRE_SPEED;
                }

                // 触发 FSM 发射状态
                var fsm = pin.GetComponent<PlayMakerFSM>();
                if (fsm != null)
                {
                    fsm.SendEvent("DIRECT_FIRE");
                }

                // 随机间隔
                float interval = Random.Range(0.05f, 0.15f);
                yield return new WaitForSeconds(interval);
            }

            Log.Info($"[PinArray] 剩余 {shuffledPins.Count} 把针发射完成");
        }

        /// <summary>
        /// 结束阶段
        /// </summary>
        public void PinArrayEnd()
        {
            // 清理数据
            _pinArrayAllPins.Clear();
            _pinArrayFirstWavePins.Clear();
            _pinArraySecondWavePins.Clear();

            Log.Info("[PinArray] 剑阵大招结束");
        }
        #endregion
    }
}
