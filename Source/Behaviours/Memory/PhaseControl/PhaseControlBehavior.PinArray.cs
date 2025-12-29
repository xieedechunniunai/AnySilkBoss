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
    internal partial class MemoryPhaseControlBehavior : MonoBehaviour
    {
        private FsmBool? _pinArraySpecialAvailableVar;
        private FsmEvent? _pinArrayCheckHpEvent;
        private FsmEvent? _pinArraySkipEvent;
        private FsmEvent? _startPinArraySpecialEvent;
        private FsmEvent? _pinArrayRoarDoneEvent;

        private FsmState? _pinArrayHpCheckEntryState;
        private FsmState? _pinArrayHpCheck400State;
        private FsmState? _pinArrayRoarState;
        private FsmState? _pinArrayRoarWaitState;
        private FsmState? _pinArrayPrepareState;
        private FsmState? _pinArrayStartState;
        private FsmState? _pinArrayWaitState;
        private FsmState? _pinArrayEndState;

        private Coroutine? _pinArrayBladeAttackCoroutine;
        private Coroutine? _pinArrayMainCoroutine;

        // PinArray 运行时数据
        private List<GameObject> _pinArrayAllPins = new List<GameObject>();
        private List<GameObject> _pinArrayWaveAPins = new List<GameObject>();  // 偶数索引，先砸地
        private List<GameObject> _pinArrayWaveBPins = new List<GameObject>();  // 奇数索引，后发射
        private FWPinManager? _pinManager;

        // PinArray 参数
        private const int PIN_ARRAY_COUNT = 40;
        private const float PIN_ARRAY_INITIAL_RADIUS = 2f;
        private const float PIN_ARRAY_FINAL_RADIUS = 18f;
        private const float PIN_ARRAY_EXPAND_DURATION = 2.5f;
        private const float PIN_ARRAY_EXPAND_ROTATION_SPEED = 180f;
        private const float PIN_ARRAY_Z_SCALE = 0.35f;  // 椭圆轨迹 Z 方向缩放因子
        // 场地中心坐标（固定，不跟随 BOSS）
        private static readonly Vector3 ARENA_CENTER = new Vector3(40f, 139f, 0f);
        private const float PIN_ARRAY_CENTER_Y_OFFSET = 8f;
        private const float PIN_FIRE_INTERVAL_START = 0.5f;
        private const float PIN_FIRE_INTERVAL_MIN = 0.05f;
        private const float PIN_FIRE_INTERVAL_STEP = 0.05f;
        private const int PIN_FIRE_INTERVAL_BATCH = 2;

        private void AddPinArraySpecialStates()
        {
            if (_phaseControl == null)
            {
                return;
            }

            var p4State = FindState(_phaseControl, "P4");
            var hpCheck4State = FindState(_phaseControl, "HP Check 4");
            var setP5State = FindState(_phaseControl, "Set P5");

            if (p4State == null || hpCheck4State == null || setP5State == null)
            {
                return;
            }

            EnsurePinArraySpecialVariables();
            RegisterPinArraySpecialEvents();

            _pinArrayHpCheckEntryState = CreateState(_phaseControl.Fsm, "HP Check 4 (Entry)", "PinArray gate");
            _pinArrayHpCheck400State = CreateState(_phaseControl.Fsm, "HP Check 4 CompareHP400", "PinArray HP<=400");
            _pinArrayRoarState = CreateState(_phaseControl.Fsm, "PinArray Roar", "PinArray roar");
            _pinArrayRoarWaitState = CreateState(_phaseControl.Fsm, "PinArray Roar Wait", "PinArray roar wait");
            _pinArrayPrepareState = CreateState(_phaseControl.Fsm, "PinArray Prepare", "PinArray prepare");
            _pinArrayStartState = CreateState(_phaseControl.Fsm, "PinArray Start", "PinArray start");
            _pinArrayWaitState = CreateState(_phaseControl.Fsm, "PinArray Wait", "PinArray wait");
            _pinArrayEndState = CreateState(_phaseControl.Fsm, "PinArray End", "PinArray end");

            AddStatesToFsm(_phaseControl, _pinArrayHpCheckEntryState, _pinArrayHpCheck400State, _pinArrayRoarState, _pinArrayRoarWaitState, _pinArrayPrepareState, _pinArrayStartState, _pinArrayWaitState, _pinArrayEndState);

            PatchP4Transition(p4State, hpCheck4State, _pinArrayHpCheckEntryState);

            AddPinArrayHpCheckEntryActions(_pinArrayHpCheckEntryState);
            AddPinArrayHpCheck400Actions(_pinArrayHpCheck400State);
            AddPinArrayRoarActions(_pinArrayRoarState);
            AddPinArrayRoarWaitActions(_pinArrayRoarWaitState);
            AddPinArrayPrepareActions(_pinArrayPrepareState);
            AddPinArrayStartActions(_pinArrayStartState);
            AddPinArrayWaitActions(_pinArrayWaitState);
            AddPinArrayEndActions(_pinArrayEndState);

            _pinArrayHpCheckEntryState.Transitions = new FsmTransition[]
            {
                CreateTransition(_pinArrayCheckHpEvent!, _pinArrayHpCheck400State),
                CreateTransition(_pinArraySkipEvent!, hpCheck4State)
            };

            _pinArrayHpCheck400State.Transitions = new FsmTransition[]
            {
                CreateTransition(_startPinArraySpecialEvent!, _pinArrayRoarState),
                CreateTransition(_pinArraySkipEvent!, hpCheck4State)
            };

            SetFinishedTransition(_pinArrayRoarState, _pinArrayRoarWaitState);
            SetFinishedTransition(_pinArrayPrepareState, _pinArrayStartState);
            SetFinishedTransition(_pinArrayStartState, _pinArrayWaitState);
            SetFinishedTransition(_pinArrayWaitState, _pinArrayEndState);
            SetFinishedTransition(_pinArrayEndState, p4State);

            if (_pinArrayRoarDoneEvent != null)
            {
                _pinArrayRoarWaitState!.Transitions = new FsmTransition[]
                {
                    CreateTransition(_pinArrayRoarDoneEvent, _pinArrayPrepareState)
                };
            }
        }

        private void EnsurePinArraySpecialVariables()
        {
            if (_phaseControl == null) return;

            var boolVars = _phaseControl.FsmVariables.BoolVariables.ToList();
            _pinArraySpecialAvailableVar = boolVars.FirstOrDefault(v => v.Name == "PinArraySpecialAvailable");
            if (_pinArraySpecialAvailableVar == null)
            {
                _pinArraySpecialAvailableVar = new FsmBool("PinArraySpecialAvailable") { Value = true };
                boolVars.Add(_pinArraySpecialAvailableVar);
                _phaseControl.FsmVariables.BoolVariables = boolVars.ToArray();
                _phaseControl.FsmVariables.Init();
            }
        }

        private void RegisterPinArraySpecialEvents()
        {
            if (_phaseControl == null) return;

            _pinArrayCheckHpEvent = GetOrCreateEvent(_phaseControl, "PIN_ARRAY_CHECK_HP");
            _pinArraySkipEvent = GetOrCreateEvent(_phaseControl, "PIN_ARRAY_SKIP");
            _startPinArraySpecialEvent = GetOrCreateEvent(_phaseControl, "START PIN ARRAY SPECIAL");
            _pinArrayRoarDoneEvent = GetOrCreateEvent(_phaseControl, "PIN ARRAY ROAR DONE");
        }

        private void PatchP4Transition(FsmState p4State, FsmState hpCheck4State, FsmState entryState)
        {
            var tookDamage = FsmEvent.GetFsmEvent("TOOK DAMAGE");
            var transitions = p4State.Transitions.ToList();
            for (int i = 0; i < transitions.Count; i++)
            {
                var t = transitions[i];
                if (t != null && t.FsmEvent == tookDamage && (t.toState == hpCheck4State.Name || t.toFsmState == hpCheck4State))
                {
                    t.toState = entryState.Name;
                    t.toFsmState = entryState;
                    transitions[i] = t;
                    p4State.Transitions = transitions.ToArray();
                    return;
                }
            }

            transitions.Add(CreateTransition(tookDamage, entryState));
            p4State.Transitions = transitions.ToArray();
        }

        private void AddPinArrayHpCheckEntryActions(FsmState state)
        {
            // 使用 BoolTest FSM Action 直接检测 FsmBool 变量（纯 FSM 驱动）
            state.Actions = new FsmStateAction[]
            {
                new BoolTest
                {
                    boolVariable = _pinArraySpecialAvailableVar,
                    isTrue = _pinArrayCheckHpEvent,
                    isFalse = _pinArraySkipEvent,
                    everyFrame = false
                }
            };
        }
        private void AddPinArrayHpCheck400Actions(FsmState state)
        {
            var selfGameObject = new FsmGameObject("Self") { Value = gameObject };

            state.Actions = new FsmStateAction[]
            {
                new CompareHP
                {
                    enemy = selfGameObject,
                    integer2 = new FsmInt(400),
                    lessThan = _startPinArraySpecialEvent,
                    equal = _startPinArraySpecialEvent,
                    greaterThan = _pinArraySkipEvent,
                    everyFrame = false
                }
            };
        }

        private void AddPinArrayRoarActions(FsmState state)
        {
            var target = new FsmEventTarget
            {
                target = FsmEventTarget.EventTarget.GameObjectFSM,
                excludeSelf = new FsmBool(false),
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" }
            };

            state.Actions = new FsmStateAction[]
            {
                // 1. 设置 PinArraySpecialAvailable 为 false（防止重复触发）
                new SetBoolValue
                {
                    boolVariable = _pinArraySpecialAvailableVar,
                    boolValue = new FsmBool(false)
                },
                // 2. 暂停地刺系统（在 Roar 时就暂停，不等到 Prepare）
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("PauseSpike") { Value = "PauseSpike" },
                    parameters = new FsmVar[]
                {
                    new FsmVar(typeof(int)) { intValue = _currentPhase.Value }
                },
                },
                // 3. 停止 BOSS 其他攻击
                new SendEventByName
                {
                    eventTarget = new FsmEventTarget
                    {
                        target = FsmEventTarget.EventTarget.GameObjectFSM,
                        excludeSelf = new FsmBool(false),
                        gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                        fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
                    },
                    sendEvent = new FsmString("ATTACK STOP") { Value = "ATTACK STOP" },
                    delay = new FsmFloat(0f),
                    everyFrame = false
                },
                // 4. 发送 ATTACK CLEAR 事件
                new SendEventToRegister
                {
                    eventName = new FsmString("ATTACK CLEAR") { Value = "ATTACK CLEAR" }
                },
                // 5. 通知 BossControl 开始 Roar
                new SendEventByName
                {
                    eventTarget = target,
                    sendEvent = new FsmString("PIN ARRAY ROAR START") { Value = "PIN ARRAY ROAR START" },
                    delay = new FsmFloat(0f),
                    everyFrame = false
                },
                new Wait
                {
                    time = new FsmFloat(0.1f),
                    finishEvent = FsmEvent.Finished
                }
            };
        }



        private void AddPinArrayRoarWaitActions(FsmState? state)
        {
            if (state == null)
            {
                return;
            }

            state.Actions = new FsmStateAction[]
            {
                new Wait
                {
                    time = new FsmFloat(999f),
                    finishEvent = null
                }
            };
        }

        private void AddPinArrayPrepareActions(FsmState state)
        {
            // 简化后的 Prepare 状态：只负责设置 FingerBlade 槽位并获取 PinManager
            // 注意：地刺暂停、变量设置、ATTACK CLEAR 已移到 Roar 状态
            state.Actions = new FsmStateAction[]
            {
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("PinArraySpecialPrepare") { Value = "PinArraySpecialPrepare" },
                    parameters = new FsmVar[0]
                },
                new Wait
                {
                    time = new FsmFloat(0.2f),
                    finishEvent = FsmEvent.Finished
                }
            };
        }

        /// <summary>
        /// PinArray 准备阶段（简化后：只负责获取 PinManager 和设置 FingerBlade 槽位）
        /// 注意：地刺暂停、变量设置、ATTACK STOP/CLEAR 已移到 Roar 状态
        /// </summary>
        public void PinArraySpecialPrepare()
        {
            if (_phaseControl == null) return;

            // 获取 FWPinManager
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _pinManager = managerObj.GetComponent<FWPinManager>();
            }

            // 设置 FingerBlade 槽位并进入大招状态
            Vector3 center = ARENA_CENTER;
            SetFingerBladePinArraySlotsAndEnter(center);
        }

        private void AddPinArrayStartActions(FsmState state)
        {
            state.Actions = new FsmStateAction[]
            {
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("StartPinArraySpecialAttack") { Value = "StartPinArraySpecialAttack" },
                    parameters = new FsmVar[0]
                },
                new Wait
                {
                    time = new FsmFloat(0.1f),
                    finishEvent = FsmEvent.Finished
                }
            };
        }

        public void StartPinArraySpecialAttack()
        {
            // 启动主协程管理整个大招流程
            if (_pinArrayMainCoroutine != null)
            {
                StopCoroutine(_pinArrayMainCoroutine);
            }
            _pinArrayMainCoroutine = StartCoroutine(PinArrayMainCoroutine());
        }

        /// <summary>
        /// PinArray 大招主协程 - 全权管理整个流程
        /// </summary>
        private IEnumerator PinArrayMainCoroutine()
        {


            Vector3 axisCenter = ARENA_CENTER;
            Vector3 center = new Vector3(axisCenter.x, axisCenter.y + PIN_ARRAY_CENTER_Y_OFFSET, axisCenter.z);

            // 1. 生成 40 根 Pin
            yield return SpawnAllPins(center);

            // 2. 旋转展开剑阵
            yield return ExpandPinArray(center);

            // 3. WaveA 向下砸地
            yield return FireWaveASlam();

            // 4. 等待 WaveA 落地恢复
            yield return new WaitForSeconds(1.2f);

            // 5. WaveB 进入 Lift 状态
            SendEventToWaveB("PINARRAY_LIFT");

            // 6. 等待所有 Pin 完成 Lift + Scramble
            yield return new WaitForSeconds(0.8f);

            // 7. 启动 FingerBlade 攻击协程（6 等分节奏）
            if (_pinArrayBladeAttackCoroutine != null)
            {
                StopCoroutine(_pinArrayBladeAttackCoroutine);
            }
            _pinArrayBladeAttackCoroutine = StartCoroutine(PinArrayFingerBladeAttackCoroutine());

            // 8. 逐个发射所有 Pin
            yield return FireAllPinsSequentially();

            // 9. 等待所有 Pin 完成攻击
            yield return new WaitForSeconds(2.0f);

            // 10. 恢复地刺系统（根据当前阶段重新启动循环）
            var spikeFloorsParent = _bossScene?.transform.Find("Spike Floors")?.gameObject;
            MemorySpikeFloorBehavior.ResumeSpikeSystem(spikeFloorsParent);

            Log.Info("[PinArray] 大招主流程完成");
        }

        /// <summary>
        /// 生成所有 Pin
        /// </summary>
        private IEnumerator SpawnAllPins(Vector3 center)
        {
            _pinArrayAllPins.Clear();
            _pinArrayWaveAPins.Clear();
            _pinArrayWaveBPins.Clear();

            if (_pinManager == null || !_pinManager.IsInitialized)
            {
                Log.Warn("[PinArray] FWPinManager 未就绪");
                yield break;
            }

            for (int i = 0; i < PIN_ARRAY_COUNT; i++)
            {
                // 计算初始位置（紧密排列在小圆内）
                float angle = (360f / PIN_ARRAY_COUNT) * i;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                Vector3 spawnPos = center + new Vector3(direction.x, 0f, direction.y) * PIN_ARRAY_INITIAL_RADIUS;

                var pin = _pinManager.SpawnPinProjectile(spawnPos);
                if (pin != null)
                {
                    var pinFsm = pin.LocateMyFSM("Control");
                    if (pinFsm == null)
                    {
                        Log.Warn($"[PinArray] 生成 Pin 但未找到 Control FSM: id={pin.GetInstanceID()}");
                    }

                    // 设置针的旋转角度（全部朝下）
                    pin.transform.rotation = Quaternion.Euler(0, 0, -90f);
                    EnablePinRenderers(pin);
                    DisablePinThreadRenderers(pin);
                    _pinArrayAllPins.Add(pin);

                    // 分组：偶数索引 → WaveA（先砸地），奇数索引 → WaveB
                    if (i % 2 == 0)
                    {
                        _pinArrayWaveAPins.Add(pin);
                    }
                    else
                    {
                        _pinArrayWaveBPins.Add(pin);
                    }
                }
            }

            Log.Info($"[PinArray] 生成 {_pinArrayAllPins.Count} 把针（WaveA: {_pinArrayWaveAPins.Count}, WaveB: {_pinArrayWaveBPins.Count}）");
            yield return null;
        }

        /// <summary>
        /// 旋转展开剑阵
        /// </summary>
        private IEnumerator ExpandPinArray(Vector3 center)
        {
            float elapsed = 0f;
            int count = _pinArrayAllPins.Count;
            if (count == 0) yield break;

            var baseAnglesRad = new float[count];
            for (int i = 0; i < count; i++)
            {
                baseAnglesRad[i] = (Mathf.PI * 2f / count) * i;
                EnablePinRenderers(_pinArrayAllPins[i]);
            }

            while (elapsed < PIN_ARRAY_EXPAND_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / PIN_ARRAY_EXPAND_DURATION);

                // 整体旋转
                float currentRotation = PIN_ARRAY_EXPAND_ROTATION_SPEED * elapsed;
                float rotationRad = currentRotation * Mathf.Deg2Rad;

                float currentRadiusX = Mathf.Lerp(PIN_ARRAY_INITIAL_RADIUS, PIN_ARRAY_FINAL_RADIUS, t);
                float currentRadiusZ = Mathf.Lerp(PIN_ARRAY_INITIAL_RADIUS, PIN_ARRAY_FINAL_RADIUS * PIN_ARRAY_Z_SCALE, t);

                for (int i = 0; i < count; i++)
                {
                    if (_pinArrayAllPins[i] == null) continue;

                    float angleRad = baseAnglesRad[i] + rotationRad;
                    float x = Mathf.Cos(angleRad) * currentRadiusX;
                    float z = Mathf.Sin(angleRad) * currentRadiusZ;
                    _pinArrayAllPins[i].transform.position = center + new Vector3(x, 0f, z);
                }

                yield return null;
            }

            yield return AnimatePinsZToZero(_pinArrayAllPins, 0.3f);

            Log.Info("[PinArray] 展开动画完成");
        }

        private static void EnablePinRenderers(GameObject pin)
        {
            if (pin == null) return;
            var threadTransform = pin.transform.Find("Thread");
            var renderers = pin.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    if (threadTransform != null && renderers[i].transform.IsChildOf(threadTransform))
                    {
                        continue;
                    }
                    renderers[i].enabled = true;
                }
            }
        }

        private static void DisablePinThreadRenderers(GameObject pin)
        {
            if (pin == null) return;
            var threadTransform = pin.transform.Find("Thread");
            if (threadTransform == null) return;

            var renderers = threadTransform.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = false;
                }
            }
        }

        private static IEnumerator AnimatePinsZToZero(List<GameObject> pins, float duration)
        {
            if (pins == null || pins.Count == 0) yield break;
            if (duration <= 0f)
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    var pin = pins[i];
                    if (pin == null) continue;
                    var p = pin.transform.position;
                    pin.transform.position = new Vector3(p.x, p.y, 0f);
                }
                yield break;
            }

            var startPositions = new Vector3[pins.Count];
            for (int i = 0; i < pins.Count; i++)
            {
                var pin = pins[i];
                startPositions[i] = pin != null ? pin.transform.position : Vector3.zero;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

                for (int i = 0; i < pins.Count; i++)
                {
                    var pin = pins[i];
                    if (pin == null) continue;

                    var start = startPositions[i];
                    float z = Mathf.Lerp(start.z, 0f, t);
                    pin.transform.position = new Vector3(start.x, start.y, z);
                }

                yield return null;
            }

            for (int i = 0; i < pins.Count; i++)
            {
                var pin = pins[i];
                if (pin == null) continue;
                var p = pin.transform.position;
                pin.transform.position = new Vector3(p.x, p.y, 0f);
            }
        }

        /// <summary>
        /// WaveA 向下砸地
        /// </summary>
        private IEnumerator FireWaveASlam()
        {
            foreach (var pin in _pinArrayWaveAPins)
            {
                if (pin == null) continue;

                // 确保朝下
                pin.transform.rotation = Quaternion.Euler(0, 0, -90f);

                // 发送 PINARRAY_SLAM 事件进入砸地链路
                var fsm = pin.LocateMyFSM("Control");
                if (fsm != null)
                {
                    fsm.SendEvent("PINARRAY_SLAM");
                }
            }

            Log.Info($"[PinArray] WaveA {_pinArrayWaveAPins.Count} 把针开始砸地");
            yield return null;
        }

        /// <summary>
        /// 给 WaveB 发送事件
        /// </summary>
        private void SendEventToWaveB(string eventName)
        {
            foreach (var pin in _pinArrayWaveBPins)
            {
                if (pin == null) continue;
                var fsm = pin.LocateMyFSM("Control");
                if (fsm != null)
                {
                    var before = fsm.ActiveStateName;
                    fsm.SendEvent(eventName);
                }
            }
        }

        /// <summary>
        /// 逐个发射所有 Pin
        /// </summary>
        private IEnumerator FireAllPinsSequentially()
        {
            // 合并并随机打乱发射顺序
            var allPinsToFire = new List<GameObject>(_pinArrayAllPins);
            for (int i = allPinsToFire.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var temp = allPinsToFire[i];
                allPinsToFire[i] = allPinsToFire[j];
                allPinsToFire[j] = temp;
            }

            int expectedTotal = allPinsToFire.Count;
            allPinsToFire.RemoveAll(pin => pin == null);
            if (allPinsToFire.Count != expectedTotal)
            {
                Log.Warn($"[PinArray] FireAllPinsSequentially: 跳过 {expectedTotal - allPinsToFire.Count} 把无效针");
            }

            float currentInterval = PIN_FIRE_INTERVAL_START;
            int intervalsUsed = 0;

            for (int i = 0; i < allPinsToFire.Count; i++)
            {
                var pin = allPinsToFire[i];
                if (pin == null) continue;

                // 发送 ATTACK 事件（Pin 在 PinArray Ready 状态等待此事件）
                var fsm = pin.LocateMyFSM("Control");
                if (fsm != null)
                {
                    fsm.SendEvent("ATTACK");
                }
                else
                {
                    Log.Warn("[PinArray] FireAllPinsSequentially: Pin 缺少 Control FSM");
                }

                bool hasNext = i < allPinsToFire.Count - 1;
                if (!hasNext)
                {
                    continue;
                }

                yield return new WaitForSeconds(currentInterval);
                intervalsUsed++;
                if (intervalsUsed % PIN_FIRE_INTERVAL_BATCH == 0 && currentInterval > PIN_FIRE_INTERVAL_MIN)
                {
                    currentInterval = Mathf.Max(PIN_FIRE_INTERVAL_MIN, currentInterval - PIN_FIRE_INTERVAL_STEP);
                }
            }

            Log.Info($"[PinArray] 所有 {allPinsToFire.Count} 把针发射完成");
        }

        private static float CalculateSequentialFireDuration(int pinCount)
        {
            if (pinCount <= 1)
            {
                return 0f;
            }

            float duration = 0f;
            float currentInterval = PIN_FIRE_INTERVAL_START;
            int batchCounter = 0;
            int intervals = pinCount - 1;

            for (int i = 0; i < intervals; i++)
            {
                duration += currentInterval;
                batchCounter++;
                if (batchCounter == PIN_FIRE_INTERVAL_BATCH)
                {
                    currentInterval = Mathf.Max(PIN_FIRE_INTERVAL_MIN, currentInterval - PIN_FIRE_INTERVAL_STEP);
                    batchCounter = 0;
                }
            }

            return duration;
        }

        /// <summary>
        /// FingerBlade 攻击协程（6 等分节奏）
        /// </summary>
        private IEnumerator PinArrayFingerBladeAttackCoroutine()
        {
            // 等待一小段时间让 Pin 开始发射
            yield return new WaitForSeconds(0.5f);

            // 计算总发射时间，6 等分
            float totalFireTime = CalculateSequentialFireDuration(_pinArrayAllPins.Count);
            float interval = totalFireTime / 6f;  // 每根 blade 间隔

            for (int i = 0; i < _allFingerBlades.Length; i++)
            {
                var blade = _allFingerBlades[i];
                if (blade == null) continue;

                var fsm = FSMUtility.LocateMyFSM(blade, "Control");
                if (fsm != null)
                {
                    fsm.SendEvent("PINARRAY_ATTACK");
                }

                yield return new WaitForSeconds(interval);
            }
        }

        private void AddPinArrayWaitActions(FsmState state)
        {
            // 总时长估算：展开(2s) + WaveA落地(1.2s) + Lift(0.8s) + 发射(~5s) + 结束等待(2s) ≈ 11s
            // 留 3s 余量
            state.Actions = new FsmStateAction[]
            {
                new Wait
                {
                    time = new FsmFloat(14.0f),
                    finishEvent = FsmEvent.Finished
                }
            };
        }

        private void AddPinArrayEndActions(FsmState state)
        {
            state.Actions = new FsmStateAction[]
            {
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("EndPinArraySpecial") { Value = "EndPinArraySpecial" },
                    parameters = new FsmVar[0]
                },
                new Wait
                {
                    time = new FsmFloat(0.2f),
                    finishEvent = FsmEvent.Finished
                }
            };
        }

        public void EndPinArraySpecial()
        {
            // 停止所有协程
            if (_pinArrayBladeAttackCoroutine != null)
            {
                StopCoroutine(_pinArrayBladeAttackCoroutine);
                _pinArrayBladeAttackCoroutine = null;
            }
            if (_pinArrayMainCoroutine != null)
            {
                StopCoroutine(_pinArrayMainCoroutine);
                _pinArrayMainCoroutine = null;
            }

            // 恢复 BOSS 攻击
            if (_attackControl != null)
            {
                _attackControl.SendEvent("ATTACK START");
            }
            // 清理 Pin 列表
            _pinArrayAllPins.Clear();
            _pinArrayWaveAPins.Clear();
            _pinArrayWaveBPins.Clear();


            var spikeFloorsParent = _bossScene?.transform.Find("Spike Floors")?.gameObject;
            MemorySpikeFloorBehavior.ResumeSpikeSystem(spikeFloorsParent);

            Log.Info("[PinArray] 大招结束，已清理");
        }

        private void SetFingerBladePinArraySlotsAndEnter(Vector3 center)
        {
            float dx = 6f;
            float dy = 9f;

            Vector3[] slots = new Vector3[]
            {
                center + new Vector3(-dx, dy, 0f),
                center + new Vector3(-2*dx, dy, 0f),
                center + new Vector3(-3*dx, dy, 0f),
                center + new Vector3(dx, dy, 0f),
                center + new Vector3(2*dx, dy, 0f),
                center + new Vector3(3*dx, dy, 0f)
            };

            for (int i = 0; i < _allFingerBlades.Length && i < slots.Length; i++)
            {
                var blade = _allFingerBlades[i];
                if (blade == null) continue;

                var fsm = FSMUtility.LocateMyFSM(blade, "Control");
                if (fsm == null) continue;

                var vars = fsm.FsmVariables;
                var v3 = vars.GetFsmVector3("PinArray Slot Target");
                if (v3 == null)
                {
                    var list = vars.Vector3Variables.ToList();
                    v3 = new FsmVector3("PinArray Slot Target") { Value = slots[i] };
                    list.Add(v3);
                    vars.Vector3Variables = list.ToArray();
                    vars.Init();
                }
                else
                {
                    v3.Value = slots[i];
                }

                fsm.SendEvent("PINARRAY_ENTER");
            }
        }
    }
}
