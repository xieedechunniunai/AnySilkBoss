using System.Collections;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using System.Linq;
using System;
using AnySilkBoss.Source.Tools;

namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// Finger Blade控制Behavior - 管理单根Finger Blade的行为
    /// </summary>
    internal class FingerBladeBehavior : MonoBehaviour
    {
        [Header("Finger Blade配置")]
        public int bladeIndex = -1;              // 在手中的索引
        public string parentHandName = "";       // 父手部名称
        public HandControlBehavior? parentHand; // 父手部Behavior引用

        [Header("Finger Blade状态")]
        public bool isActive = false;            // 是否激活
        public bool isAttacking = false;         // 是否正在攻击
        public bool isOrbiting = false;           // 是否正在环绕

        [Header("环绕参数")]
        public float orbitRadius = 7f;             // 环绕半径（增大到6f）
        public float orbitSpeed = 200f;           // 环绕速度（稍微减慢）
        public float orbitOffset = 0f;            // 环绕角度偏移

        // 私有变量
        private PlayMakerFSM? controlFSM;        // Control FSM
        private PlayMakerFSM? tinkFSM;           // Tink FSM
        private Transform? playerTransform;      // 玩家Transform
		// 运行时缓存：新增的FSM变量引用，确保动作字段绑定到同一实例
		private FsmFloat? _orbitRadiusVar;
		private FsmFloat? _orbitSpeedVar;
		private FsmFloat? _orbitOffsetVar;

		// 事件引用缓存
		private FsmEvent? _orbitStartEvent;
		private FsmEvent? _shootEvent;

        // 事件
        public System.Action? OnBladeActivated;
        public System.Action? OnBladeDeactivated;
        public System.Action? OnBladeAttack;

        /// <summary>
        /// 初始化Finger Blade
        /// </summary>
        public void Initialize(int index, string handName, HandControlBehavior hand)
        {
            // 修改bladeIndex为全局唯一：Hand L(0-2), Hand R(3-5)
            int globalIndex = handName == "Hand L" ? index : index + 3;
            bladeIndex = globalIndex;
            parentHandName = handName;
            parentHand = hand;

            Log.Info($"初始化Finger Blade {bladeIndex} (局部索引{index}) ({gameObject.name}) 在 {parentHandName} 下");
            // 查找玩家
            var heroController = FindFirstObjectByType<HeroController>();
            if (heroController != null)
            {
                playerTransform = heroController.transform;
            }
            // 获取FSM组件
            InitializeFSMs();
        }

        /// <summary>
        /// 初始化FSM组件
        /// </summary>
        private void InitializeFSMs()
        {
            PlayMakerFSM[] allFSMs = GetComponents<PlayMakerFSM>();

            foreach (PlayMakerFSM fsm in allFSMs)
            {
                if (fsm.FsmName == "Control")
                {
                    controlFSM = fsm;
                }
                else if (fsm.FsmName == "Tink")
                {
                    tinkFSM = fsm;
                }
            }

            if (controlFSM == null)
            {
                Log.Warn($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到Control FSM");
            }
            AddDynamicVariables();
        }



        private void Update()
        {

        }
        /// <summary>
        /// 新增动态变量
        /// </summary>
        public void AddDynamicVariables()
        {
            if (controlFSM == null) return;
            
			// 复用已存在的变量，否则创建并添加
			var floatVars = controlFSM.FsmVariables.FloatVariables.ToList();
			_orbitRadiusVar = floatVars.FirstOrDefault(v => v.Name == "OrbitRadius");
			_orbitSpeedVar = floatVars.FirstOrDefault(v => v.Name == "OrbitSpeed");
			_orbitOffsetVar = floatVars.FirstOrDefault(v => v.Name == "OrbitOffset");

			bool added = false;
			if (_orbitRadiusVar == null)
			{
				_orbitRadiusVar = new FsmFloat("OrbitRadius") { Value = orbitRadius };
				floatVars.Add(_orbitRadiusVar);
				added = true;
			}
			else
			{
				_orbitRadiusVar.Value = orbitRadius;
			}

			if (_orbitSpeedVar == null)
			{
				_orbitSpeedVar = new FsmFloat("OrbitSpeed") { Value = orbitSpeed };
				floatVars.Add(_orbitSpeedVar);
				added = true;
			}
			else
			{
				_orbitSpeedVar.Value = orbitSpeed;
			}

			if (_orbitOffsetVar == null)
			{
				_orbitOffsetVar = new FsmFloat("OrbitOffset") { Value = orbitOffset };
				floatVars.Add(_orbitOffsetVar);
				added = true;
			}
			else
			{
				_orbitOffsetVar.Value = orbitOffset;
			}

			if (added)
			{
				controlFSM.FsmVariables.FloatVariables = floatVars.ToArray();
			}
        }


        /// <summary>
        /// 设置环绕参数
        /// </summary>
        public void SetOrbitParameters(float radius, float speed, float offset)
        {
            orbitRadius = radius;
            orbitSpeed = speed;
            orbitOffset = offset;

            // 同时更新FSM中的变量，供FingerBladeOrbitAction使用
            if (controlFSM != null)
            {
                // 更新FSM变量（变量已在AddDynamicVariables中创建）
				var radiusVar = _orbitRadiusVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitRadius");
                if (radiusVar != null)
                {
                    radiusVar.Value = radius;
                }
                else
                {
                    Log.Warn($"Finger Blade {bladeIndex} 未找到OrbitRadius变量");
                }

				var speedVar = _orbitSpeedVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitSpeed");
                if (speedVar != null)
                {
                    speedVar.Value = speed;
                }
                else
                {
                    Log.Warn($"Finger Blade {bladeIndex} 未找到OrbitSpeed变量");
                }

				var offsetVar = _orbitOffsetVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitOffset");
                if (offsetVar != null)
                {
                    offsetVar.Value = offset;
                }
                else
                {
                    Log.Warn($"Finger Blade {bladeIndex} 未找到OrbitOffset变量");
                }

				// 验证变量是否正确设置（使用缓存的引用优先）
				var verifyRadius = (_orbitRadiusVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitRadius"))?.Value ?? -1f;
				var verifySpeed = (_orbitSpeedVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitSpeed"))?.Value ?? -1f;
				var verifyOffset = (_orbitOffsetVar ?? controlFSM.FsmVariables.GetFsmFloat("OrbitOffset"))?.Value ?? -1f;

            }
            else
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) Control FSM 为 null，无法设置FSM变量");
            }

        }

        /// <summary>
        /// 注册Finger Blade FSM的所有事件
        /// </summary>
        private void RegisterFingerBladeEvents()
        {
            if (controlFSM == null) return;

            // 创建或获取事件
            _orbitStartEvent = FsmEvent.GetFsmEvent($"ORBIT START {parentHandName} Blade {bladeIndex}");
            _shootEvent = FsmEvent.GetFsmEvent($"SHOOT {parentHandName} Blade {bladeIndex}");

            // 将事件添加到FSM的事件列表中
            var existingEvents = controlFSM.FsmEvents.ToList();
            
            if (!existingEvents.Contains(_orbitStartEvent))
            {
                existingEvents.Add(_orbitStartEvent);
            }
            if (!existingEvents.Contains(_shootEvent))
            {
                existingEvents.Add(_shootEvent);
            }

            // 使用反射设置FsmEvents
            var fsmType = controlFSM.Fsm.GetType();
            var eventsField = fsmType.GetField("events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (eventsField != null)
            {
                eventsField.SetValue(controlFSM.Fsm, existingEvents.ToArray());
            }
            else
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到events字段");
            }
        }

        /// <summary>
        /// 重新链接Finger Blade FSM事件引用
        /// </summary>
        private void RelinkFingerBladeEventReferences()
        {
            if (controlFSM == null) return;

            // 重新初始化FSM数据，确保所有事件引用正确
            controlFSM.Fsm.InitData();
            controlFSM.Fsm.InitEvents();

        }

        /// <summary>
        /// 添加环绕攻击全局转换 - 新版本：两个独立状态
        /// </summary>
        public void AddOrbitGlobalTransition()
        {
            if (controlFSM == null) return;

            // 首先注册所有需要的事件
            RegisterFingerBladeEvents();

            // 创建第一个状态：Orbit Start（接收环绕指令，持续环绕）
            var orbitStartState = CreateOrbitStartState();
            if (orbitStartState == null)
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 创建Orbit Start状态失败");
                return;
            }

            // 创建第二个状态：Orbit Prepare（准备发射，激活antic子物品）
            var orbitPrepareState = CreateOrbitPrepareState();
            if (orbitPrepareState == null)
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 创建Orbit Prepare状态失败");
                return;
            }

            // 创建第三个状态：Orbit Shoot（接收发射指令，设置攻击参数）
            var orbitShootState = CreateOrbitShootState();
            if (orbitShootState == null)
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 创建Orbit Shoot状态失败");
                return;
            }

            // 添加状态动作
            AddOrbitStartActions(orbitStartState);
            AddOrbitPrepareActions(orbitPrepareState);
            AddOrbitShootActions(orbitShootState);

            // 添加状态转换
            AddOrbitStartTransitions(orbitStartState, orbitPrepareState);
            AddOrbitPrepareTransitions(orbitPrepareState, orbitShootState);
            AddOrbitShootTransitions(orbitShootState);

            // 创建全局转换（从Idle到Orbit Start）
            var globalTransition = new FsmTransition
            {
                FsmEvent = _orbitStartEvent,
                toState = "Orbit Start",
                toFsmState = orbitStartState
            };

            // 添加全局转换（使用反射修改只读属性）
            var existingTransitions = controlFSM.FsmGlobalTransitions.ToList();
            existingTransitions.Add(globalTransition);

            // 使用反射设置FsmGlobalTransitions
            var fsmType = controlFSM.Fsm.GetType();
            var globalTransitionsField = fsmType.GetField("globalTransitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (globalTransitionsField != null)
            {
                globalTransitionsField.SetValue(controlFSM.Fsm, existingTransitions.ToArray());
            }
            else
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到globalTransitions字段");
            }

            // 重新链接所有事件引用
            RelinkFingerBladeEventReferences();
        }
        /// <summary>
        /// 创建Orbit Start状态（接收环绕指令，持续环绕）
        /// </summary>
        private FsmState? CreateOrbitStartState()
        {
            if (controlFSM == null) return null;

            // 创建Orbit Start状态
            var orbitStartState = new FsmState(controlFSM.Fsm)
            {
                Name = "Orbit Start",
                Description = $"Finger Blade {bladeIndex} 环绕开始状态"
            };

            // 添加状态到FSM
            var existingStates = controlFSM.FsmStates.ToList();
            existingStates.Add(orbitStartState);
            controlFSM.Fsm.States = existingStates.ToArray();

            return orbitStartState;
        }

        /// <summary>
        /// 创建Orbit Prepare状态（准备发射，激活antic子物品）
        /// </summary>
        private FsmState? CreateOrbitPrepareState()
        {
            if (controlFSM == null) return null;

            // 创建Orbit Prepare状态
            var orbitPrepareState = new FsmState(controlFSM.Fsm)
            {
                Name = "Orbit Prepare",
                Description = $"Finger Blade {bladeIndex} 环绕准备状态"
            };

            // 添加状态到FSM
            var existingStates = controlFSM.FsmStates.ToList();
            existingStates.Add(orbitPrepareState);
            controlFSM.Fsm.States = existingStates.ToArray();

            return orbitPrepareState;
        }

        /// <summary>
        /// 创建Orbit Shoot状态（接收发射指令，设置攻击参数）
        /// </summary>
        private FsmState? CreateOrbitShootState()
        {
            if (controlFSM == null) return null;

            // 创建Orbit Shoot状态
            var orbitShootState = new FsmState(controlFSM.Fsm)
            {
                Name = "Orbit Shoot",
                Description = $"Finger Blade {bladeIndex} 环绕发射状态"
            };

            // 添加状态到FSM
            var existingStates = controlFSM.FsmStates.ToList();
            existingStates.Add(orbitShootState);
            controlFSM.Fsm.States = existingStates.ToArray();

            return orbitShootState;
        }

        /// <summary>
        /// 添加Orbit Start状态的动作
        /// </summary>
        private void AddOrbitStartActions(FsmState orbitStartState)
        {
            // 动作1：开始环绕（持续环绕，直到收到SHOOT事件）
            // 使用变量引用，这样可以在运行时动态获取最新值
			var orbitAction = new FingerBladeOrbitAction
			{
				orbitRadius = _orbitRadiusVar ?? controlFSM!.FsmVariables.GetFsmFloat("OrbitRadius"),
				orbitSpeed = _orbitSpeedVar ?? controlFSM!.FsmVariables.GetFsmFloat("OrbitSpeed"),
				orbitOffset = _orbitOffsetVar ?? controlFSM!.FsmVariables.GetFsmFloat("OrbitOffset")
			};

            // 动作2：等待SHOOT事件（不设置超时，持续环绕）
            var waitForShootAction = new Wait
            {
                time = new FsmFloat(19f), // 设置很长的超时时间
                finishEvent = _shootEvent // 等待SHOOT事件
            };

            orbitStartState.Actions = new FsmStateAction[] { orbitAction, waitForShootAction };
        }

        /// <summary>
        /// 添加Orbit Prepare状态的动作
        /// </summary>
        private void AddOrbitPrepareActions(FsmState orbitPrepareState)
        {
            // 动作1：立即停止环绕动作
            var stopOrbitAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StopOrbitImmediately") { Value = "StopOrbitImmediately" },
                parameters = new FsmVar[0],
                everyFrame = false
            };

            // 动作2：完全清除当前所有速度
            var clearVelocityAction = new SetVelocity2d
            {
                x = new FsmFloat(0f),
                y = new FsmFloat(0f)
            };

            // 动作3：设置攻击参数（根据当前位置和角度）
            var setAttackParamsAction = new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SetAttackParameters") { Value = "SetAttackParameters" },
                parameters = new FsmVar[0],
                everyFrame = false
            };

            // 动作4：激活silk_boss_finger_antic子物品
            var activateAnticAction = new ActivateGameObject
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = new FsmGameObject { Value = gameObject.transform.Find("silk_boss_finger_antic")?.gameObject }
                },
                activate = new FsmBool(true),
                recursive = new FsmBool(false),
                resetOnExit = true
            };

            // 动作5：等待0.2秒
            var waitAction = new Wait
            {
                time = new FsmFloat(0.2f),
                finishEvent = FsmEvent.Finished
            };

            orbitPrepareState.Actions = new FsmStateAction[] { stopOrbitAction, clearVelocityAction, setAttackParamsAction, activateAnticAction, waitAction };
        }

        /// <summary>
        /// 添加Orbit Shoot状态的动作
        /// </summary>
        private void AddOrbitShootActions(FsmState orbitShootState)
        {


            // 动作1：确保物理不下落与无残余速度
            var zeroGravityAction = new SetGravity2dScale
            {
                gravityScale = new FsmFloat(0f)
            };

            var zeroVelocityAction = new SetVelocity2d
            {
                x = new FsmFloat(0f),
                y = new FsmFloat(0f)
            };

            // 动作2：预激活伤害/物理碰撞体，防止直接跳到Shoot时未及时生效
            var damagerVar = controlFSM!.FsmVariables.GetFsmGameObject("Damager");
            var physColliderVar = controlFSM.FsmVariables.GetFsmGameObject("Phys Collider");

            var activateDamager = new ActivateGameObjectDelay
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = new FsmGameObject { Value = damagerVar != null ? damagerVar.Value : null }
                },
                activate = new FsmBool(true),
                // recursive = new FsmBool(false),
                delay = new FsmFloat(0.1f),
                resetOnExit = true
            };

            var activatePhysCollider = new ActivateGameObject
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    gameObject = new FsmGameObject { Value = physColliderVar != null ? physColliderVar.Value : null }
                },
                activate = new FsmBool(true),
                recursive = new FsmBool(false),
                resetOnExit = true
            };

            // 动作3：等待短暂时间让参数生效
            var waitAction = new Wait
            {
                time = new FsmFloat(0.1f),
                finishEvent = FsmEvent.Finished
            };

            orbitShootState.Actions = new FsmStateAction[] {  zeroGravityAction, zeroVelocityAction, activateDamager, activatePhysCollider, waitAction };
        }

        /// <summary>
        /// 添加Orbit Start状态的转换
        /// </summary>
        private void AddOrbitStartTransitions(FsmState orbitStartState, FsmState orbitPrepareState)
        {
            // 从Orbit Start到Orbit Shoot的转换（通过SHOOT事件）
            if (_shootEvent == null)
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到SHOOT事件: SHOOT {parentHandName} Blade {bladeIndex}");
                return;
            }

            var shootTransition = new FsmTransition
            {
                FsmEvent = _shootEvent,
                toState = "Orbit Prepare",
                toFsmState = orbitPrepareState
            };

            orbitStartState.Transitions = new FsmTransition[] { shootTransition };
        }

         private void AddOrbitPrepareTransitions(FsmState orbitPrepareState, FsmState orbitShootState)
        {
            var finishedTransition = new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Orbit Shoot",
                toFsmState = orbitShootState
            };

            orbitPrepareState.Transitions = new FsmTransition[] { finishedTransition };
        }
        /// <summary>
        /// 添加Orbit Shoot状态的转换
        /// </summary>
        private void AddOrbitShootTransitions(FsmState orbitShootState)
        {
            // 直接进入Shoot（从当前环绕位置立即发射）
            var shootState = controlFSM!.FsmStates.FirstOrDefault(state => state.Name == "Shoot");
            if (shootState == null)
            {
                Log.Error($"Finger Blade {bladeIndex} ({gameObject.name}) 未找到Shoot状态");
                return;
            }

            // 发射准备完成后直接进入Shoot
            var finishedTransition = new FsmTransition
            {
                FsmEvent = FsmEvent.Finished,
                toState = "Shoot",
                toFsmState = shootState
            };

            orbitShootState.Transitions = new FsmTransition[] { finishedTransition };
        }

        /// <summary>
        /// 设置攻击参数（根据当前位置和角度）
        /// </summary>
        public void SetAttackParameters()
        {
            if (controlFSM == null) return;

            // 获取当前位置和角度
            Vector3 currentPosition = transform.position;
            Vector3 playerPosition = playerTransform != null ? playerTransform.position : Vector3.zero;

            // 计算攻击方向（从当前位置指向玩家）
            Vector3 attackDirection = (playerPosition - currentPosition).normalized;
            float attackAngle = Mathf.Atan2(attackDirection.y, attackDirection.x) * Mathf.Rad2Deg - 5f;

            // 设置Attack Rotation（攻击旋转角度）
            var attackRotationVar = controlFSM.FsmVariables.GetFsmFloat("Attack Rotation");
            if (attackRotationVar != null)
            {
                attackRotationVar.Value = attackAngle;
            }
            else
            {
                Log.Warn("未找到Attack Rotation变量");
            }

            // 设置Attack Angle（直接供Shoot的SetVelocityAsAngle使用）
            var attackAngleVar = controlFSM.FsmVariables.GetFsmFloat("Attack Angle");
            if (attackAngleVar != null)
            {
                attackAngleVar.Value = attackAngle;
            }
            else
            {
                Log.Warn("未找到Attack Angle变量");
            }

            // 设置Attack Y Scale（攻击Y轴缩放）
            var attackYScaleVar = controlFSM.FsmVariables.GetFsmFloat("Attack Y Scale");
            if (attackYScaleVar != null)
            {
                attackYScaleVar.Value = 1f;
            }
            else
            {
                Log.Warn("未找到Attack Y Scale变量");
            }

            // 设置Travel Time Multiplier（影响Return/移动插值节奏）
            var travelTimeMulVar = controlFSM.FsmVariables.GetFsmFloat("Travel Time Multiplier");
            if (travelTimeMulVar != null)
            {
                // 非快速版本，使用原版默认0.02；如有需要 Hand 侧会改为0.015
                travelTimeMulVar.Value = 0.02f;
            }
            else
            {
                Log.Warn("未找到Travel Time Multiplier变量");
            }

            // 设置Wait Min和Wait Max（等待时间）
            var waitMinVar = controlFSM.FsmVariables.GetFsmFloat("Wait Min");
            if (waitMinVar != null)
            {
                waitMinVar.Value = 0f;
            }
            else
            {
                Log.Warn("未找到Wait Min变量");
            }

            var waitMaxVar = controlFSM.FsmVariables.GetFsmFloat("Wait Max");
            if (waitMaxVar != null)
            {
                waitMaxVar.Value = 0f;
            }
            else
            {
                Log.Warn("未找到Wait Max变量");
            }

            // 设置Quick Recover（快速恢复）
            var quickRecoverVar = controlFSM.FsmVariables.GetFsmBool("Quick Recover");
            if (quickRecoverVar != null)
            {
                quickRecoverVar.Value = false;
            }
            else
            {
                Log.Warn("未找到Quick Recover变量");
            }

            // 设置Thunk Time（命中后停顿时长），避免后续状态立刻结束
            var thunkTimeVar = controlFSM.FsmVariables.GetFsmFloat("Thunk Time");
            if (thunkTimeVar != null)
            {
                // 模拟Attack Start Pause的随机区间[0.75, 0.9]
                thunkTimeVar.Value = UnityEngine.Random.Range(0.75f, 0.9f);
            }
            else
            {
                Log.Warn("未找到Thunk Time变量");
            }

            // 设置Ready（原链路在Attack Start Pause里会置为false）
            var readyVar2 = controlFSM.FsmVariables.GetFsmBool("Ready");
            if (readyVar2 != null)
            {
                readyVar2.Value = false;
            }
            else
            {
                Log.Warn("未找到Ready变量");
            }

        }

        /// <summary>
        /// 立即停止环绕（供FSM调用）
        /// </summary>
        public void StopOrbitImmediately()
        {
            // 立即停止移动，确保没有惯性
            if (gameObject != null)
            {
                // 获取Rigidbody2D组件并立即停止移动
                var rb2d = gameObject.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    rb2d.linearVelocity = Vector2.zero;
                    rb2d.angularVelocity = 0f;
                }
            }
        }
    }

    /// <summary>
    /// Finger Blade 环绕攻击自定义Action
    /// </summary>
    public class FingerBladeOrbitAction : FsmStateAction
    {
        [Header("环绕参数")]
        public FsmFloat orbitRadius = 7f;
        public FsmFloat orbitSpeed = 200;
        public FsmFloat orbitOffset = 0f;

        private GameObject? _hero;
        private Transform? _ownerTransform;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;
        private float _orbitTimer = 0f;
        private bool _isOrbiting = false;

        public override void Reset()
        {
            orbitRadius = 7f;
            orbitSpeed = 200;
            orbitOffset = 0f;
            _hero = null;
            _ownerTransform = null;
            _orbitTimer = 0f;
            _isOrbiting = false;
        }

        public override void OnEnter()
        {
            _hero = HeroController.instance?.gameObject;
            if (_hero == null)
            {
                Debug.LogWarning("FingerBladeOrbitAction: 未找到HeroController");
                Finish();
                return;
            }

            _ownerTransform = Owner.transform;
            _originalPosition = _ownerTransform.position;
            _originalRotation = _ownerTransform.rotation;

            // 获取当前环绕参数值（直接使用传入的变量引用）
            float currentOffset = orbitOffset != null ? orbitOffset.Value : 0f;
            float currentSpeed = orbitSpeed != null ? orbitSpeed.Value : 0f;
            // 根据orbitOffset设置初始时间偏移，确保每个Finger Blade有不同的起始位置
            _orbitTimer = currentOffset / currentSpeed;
            _isOrbiting = true;

            Debug.Log($"FingerBladeOrbitAction: 开始环绕攻击 - {Owner.name}，初始时间偏移: {_orbitTimer}，偏移角度: {currentOffset}°");
        }

        public override void OnUpdate()
        {
            if (_hero == null || _ownerTransform == null)
            {
                Finish();
                return;
            }

            // 如果环绕已停止，立即停止移动并完成动作
            if (!_isOrbiting)
            {
                Finish();
                return;
            }

            try
            {
                // 获取当前环绕参数值（直接使用传入的变量引用）
                float currentOffset = orbitOffset != null ? orbitOffset.Value : 0f;
                float currentSpeed = orbitSpeed != null ? orbitSpeed.Value : 0f;
                float currentRadius = orbitRadius != null ? orbitRadius.Value : 0f;
                // 计算环绕角度
                float orbitAngle = _orbitTimer * currentSpeed;
                float radians = orbitAngle * Mathf.Deg2Rad;

                // 计算环绕位置（在XY平面上做圆周运动）
                Vector3 offset = new Vector3(
                    Mathf.Cos(radians) * currentRadius,
                    Mathf.Sin(radians) * currentRadius,
                    0f
                );

                Vector3 targetPosition = _hero.transform.position + offset;

                // 移动Finger Blade到目标位置（只调整X和Y坐标）
                Vector3 currentPos = _ownerTransform.position;
                Vector3 newPos = Vector3.Lerp(currentPos, targetPosition, Time.deltaTime * 8f);
                _ownerTransform.position = new Vector3(newPos.x, newPos.y, currentPos.z); // 保持Z坐标不变

                // 让Finger Blade朝向玩家（只调整Rotation的Z轴）
                Vector3 direction = (_hero.transform.position - _ownerTransform.position).normalized;
                if (direction != Vector3.zero)
                {
                    // 计算朝向玩家的角度（Z轴旋转），并添加5度顺时针偏移
                    float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 180f + 10f;
                    Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);

                    // 使用更快的插值速度来跟上环绕运动
                    _ownerTransform.rotation = Quaternion.Lerp(_ownerTransform.rotation, targetRotation, Time.deltaTime * 20f);
                }

                // 累积时间在计算和更新之后进行，保持初始相位
                _orbitTimer += Time.deltaTime;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"FingerBladeOrbitAction 更新过程中出错: {e}");
                Finish();
            }
        }

        public override void OnExit()
        {
            _isOrbiting = false;
            
            // 立即停止移动，确保没有惯性
            if (_ownerTransform != null)
            {
                // 获取Rigidbody2D组件并立即停止移动
                var rb2d = _ownerTransform.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    rb2d.linearVelocity = Vector2.zero;
                    rb2d.angularVelocity = 0f;
                }
            }
            
            Debug.Log($"FingerBladeOrbitAction: 环绕攻击结束 - {Owner.name}，立即停止移动");
        }

        /// <summary>
        /// 停止环绕（供外部调用）
        /// </summary>
        public void StopOrbit()
        {
            _isOrbiting = false;
        }

    }
}
