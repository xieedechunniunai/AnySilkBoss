using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// 阶段控制器行为
    /// 负责管理Boss的各个阶段和血量
    /// </summary>
    internal class PhaseControlBehavior : MonoBehaviour
    {
        // FSM引用
        private PlayMakerFSM _phaseControl = null!;
        
        // 阶段状态变量
        private int _currentPhase = 1;
        private bool[] _phaseFlags = new bool[9]; // P1-P6 + 硬直 + 背景破坏 + 初始状态
        
        // 血量修改标志
        private bool _hpModified = false;
        
        // FSM内部阶段指示变量
        private FsmInt _bossPhaseIndex = null!;

        // 大招相关
        private Managers.BigSilkBallManager? _bigSilkBallManager;
        private GameObject? _currentBigSilkBall;
        private bool _bigSilkBallTriggered = false;  // 确保只触发一次
        private int _originalLayer = 11;             // 保存Boss原始图层（默认Enemies）
        
        // 大招事件标记
        private bool _chargeComplete = false;
        private bool _burstComplete = false;

        private void Awake()
        {
            // 初始化在Start中进行
        }

        private void Start()
        {
            StartCoroutine(DelayedSetup());
        }

        private void Update()
        {
            // 更新阶段状态
            UpdatePhaseState();
            if (Input.GetKeyDown(KeyCode.T))
            {
                LogPhaseControlFSMInfo();
            }
        }

        private void LogPhaseControlFSMInfo()
        {
            if (_phaseControl != null)
            {
                Log.Info($"=== 阶段控制器 FSM 信息 ===");
                Log.Info($"FSM名称: {_phaseControl.FsmName}");
                Log.Info($"当前状态: {_phaseControl.ActiveStateName}");
                FsmAnalyzer.WriteFsmReport(_phaseControl, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\暂存\\_phaseControlFsm.txt");
            }
        }

        /// <summary>
        /// 延迟初始化
        /// </summary>
        private IEnumerator DelayedSetup()
        {
            yield return null; // 等待一帧
            StartCoroutine(SetupPhaseControl());
        }

        /// <summary>
        /// 设置阶段控制器
        /// </summary>
        private IEnumerator SetupPhaseControl()
        {
            GetComponents();
            GetBigSilkBallManager();
            ModifyPhaseBehavior();
            AddBigSilkBallStates();
            Log.Info("阶段控制器行为初始化完成");
            yield return null;
        }

        /// <summary>
        /// 获取必要的组件
        /// </summary>
        private void GetComponents()
        {
            _phaseControl = FSMUtility.LocateMyFSM(gameObject, "Phase Control");
            
            if (_phaseControl == null)
            {
                Log.Error("未找到Phase Control FSM");
                return;
            }
            
            Log.Info("成功获取Phase Control FSM");
            
            // 获取或创建BossPhaseIndex变量
            _bossPhaseIndex = _phaseControl.FsmVariables.GetFsmInt("BossPhaseIndex");
            if (_bossPhaseIndex == null)
            {
                // 创建新的FsmInt变量
                _bossPhaseIndex = new FsmInt("BossPhaseIndex");
                _bossPhaseIndex.Value = 0;
                
                // 添加到FSM变量列表
                var intVars = _phaseControl.FsmVariables.IntVariables.ToList();
                intVars.Add(_bossPhaseIndex);
                _phaseControl.FsmVariables.IntVariables = intVars.ToArray();
                
                Log.Info("创建了新的FSM变量: BossPhaseIndex");
            }
            else
            {
                Log.Info("找到现有FSM变量: BossPhaseIndex");
            }
        }

        /// <summary>
        /// 修改阶段行为
        /// </summary>
        private void ModifyPhaseBehavior()
        {
            if (_phaseControl == null) return;

            // 修改各阶段血量（翻2倍）
            ModifyPhaseHP();
            
            Log.Info("阶段行为修改完成");
        }

        /// <summary>
        /// 修改各阶段血量，全部翻2倍
        /// </summary>
        private void ModifyPhaseHP()
        {
            if (_phaseControl == null || _hpModified) return;

            Log.Info("开始修改各阶段血量...");

            // P1 HP 到 P6 HP 全部翻2倍
            for (int i = 1; i <= 6; i++)
            {
                string hpVarName = $"P{i} HP";
                var hpVar = _phaseControl.FsmVariables.GetFsmInt(hpVarName);
                
                if (hpVar != null)
                {
                    int originalHP = hpVar.Value;
                    hpVar.Value = originalHP * 2;
                    Log.Info($"{hpVarName}: {originalHP} -> {hpVar.Value} (翻2倍)");
                }
                else
                {
                    Log.Warn($"未找到变量: {hpVarName}");
                }
            }

            _hpModified = true;
            Log.Info("所有阶段血量修改完成！");
        }

        /// <summary>
        /// 更新阶段状态
        /// </summary>
        private void UpdatePhaseState()
        {
            if (_phaseControl == null || _bossPhaseIndex == null) return;

            // 获取当前FSM状态名称
            string currentStateName = _phaseControl.ActiveStateName;
            
            // 根据当前状态更新BossPhaseIndex
            int newPhaseIndex = GetPhaseIndexFromState(currentStateName);
            
            if (newPhaseIndex != _bossPhaseIndex.Value)
            {
                _bossPhaseIndex.Value = newPhaseIndex;
                Log.Info($"BossPhaseIndex更新为: {newPhaseIndex} (当前状态: {currentStateName})");
                
                // 触发阶段改变事件
                OnPhaseChanged(_currentPhase, newPhaseIndex);
                _currentPhase = newPhaseIndex;
            }

            // 检查各阶段标志
            CheckPhaseFlags();
        }
        
        /// <summary>
        /// 根据状态名称获取阶段索引
        /// </summary>
        private int GetPhaseIndexFromState(string stateName)
        {
            return stateName switch
            {
                "Init" => 0,
                "Set P1" => 1,
                "P1" => 1,
                "HP Check 1" => 1,
                "Set P2" => 2,
                "P2" => 2,
                "HP Check 2" => 2,
                "Set P3" => 3,
                "Set P3 Web Strand" => 3,
                "P3" => 3,
                "HP Check 3" => 3,
                "MID STAGGER" => 4,
                "Mid Stagger" => 4,
                "Stagger Hit" => 4,
                "Stagger Fall" => 4,
                "Stagger Pause" => 4,
                "BG Break Sequence" => 5,
                "Rubble M" => 5,
                "Rubble Sides" => 5,
                "Set P4" => 6,
                "P4" => 6,
                "HP Check 4" => 6,
                "Set P5" => 7,
                "P5" => 7,
                "HP Check 5" => 7,
                "Set P6" => 8,
                "P6" => 8,
                "HP Check 6" => 8,
                _ => _bossPhaseIndex?.Value ?? 0 // 保持当前值
            };
        }

        /// <summary>
        /// 检查各阶段标志
        /// </summary>
        private void CheckPhaseFlags()
        {
            // 检查P1-P6阶段标志
            for (int i = 1; i <= 6; i++)
            {
                string phaseFlagName = $"P{i}";
                var phaseFlagVar = _phaseControl.FsmVariables.GetFsmBool(phaseFlagName);
                
                if (phaseFlagVar != null && phaseFlagVar.Value && !_phaseFlags[i])
                {
                    OnPhaseFlagSet(i);
                    _phaseFlags[i] = true;
                }
                else if (phaseFlagVar != null && !phaseFlagVar.Value && _phaseFlags[i])
                {
                    OnPhaseFlagCleared(i);
                    _phaseFlags[i] = false;
                }
            }

            // 检查特殊阶段标志
            CheckSpecialPhaseFlags();
        }

        /// <summary>
        /// 检查特殊阶段标志（硬直、背景破坏等）
        /// </summary>
        private void CheckSpecialPhaseFlags()
        {
            // 硬直阶段标志
            var staggerFlagVar = _phaseControl.FsmVariables.GetFsmBool("Stagger");
            if (staggerFlagVar != null && staggerFlagVar.Value && !_phaseFlags[4])
            {
                Log.Info("硬直阶段标志被设置");
                _phaseFlags[4] = true;
            }
            else if (staggerFlagVar != null && !staggerFlagVar.Value && _phaseFlags[4])
            {
                Log.Info("硬直阶段标志被清除");
                _phaseFlags[4] = false;
            }

            // 背景破坏阶段标志
            var bgBreakFlagVar = _phaseControl.FsmVariables.GetFsmBool("BG Break");
            if (bgBreakFlagVar != null && bgBreakFlagVar.Value && !_phaseFlags[5])
            {
                Log.Info("背景破坏阶段标志被设置");
                _phaseFlags[5] = true;
            }
            else if (bgBreakFlagVar != null && !bgBreakFlagVar.Value && _phaseFlags[5])
            {
                Log.Info("背景破坏阶段标志被清除");
                _phaseFlags[5] = false;
            }
        }

        /// <summary>
        /// 阶段改变时的处理
        /// </summary>
        private void OnPhaseChanged(int oldPhase, int newPhase)
        {
            Log.Info($"阶段改变: {GetPhaseName(oldPhase)} -> {GetPhaseName(newPhase)}");
            
            // ========== 在这里添加阶段改变时的逻辑 ==========
            switch (newPhase)
            {
                case 1:
                    OnPhase1Start();
                    break;
                case 2:
                    OnPhase2Start();
                    break;
                case 3:
                    OnPhase3Start();
                    break;
                case 4:
                    OnPhase4Start();
                    break;
                case 5:
                    OnPhase5Start();
                    break;
                case 6:
                    OnPhase6Start();
                    break;
                case 7:
                    OnPhase7Start();
                    break;
                case 8:
                    OnPhase8Start();
                    break;
                default:
                    Log.Info($"未知阶段: {newPhase}");
                    break;
            }
        }

        /// <summary>
        /// 阶段标志设置时的处理
        /// </summary>
        private void OnPhaseFlagSet(int phase)
        {
            Log.Info($"P{phase} 标志已设置");
            
            // ========== 在这里添加阶段标志设置时的逻辑 ==========
        }

        /// <summary>
        /// 阶段标志清除时的处理
        /// </summary>
        private void OnPhaseFlagCleared(int phase)
        {
            Log.Info($"P{phase} 标志已清除");
            
            // ========== 在这里添加阶段标志清除时的逻辑 ==========
        }

        /// <summary>
        /// 获取阶段名称
        /// </summary>
        private string GetPhaseName(int phaseIndex)
        {
            return phaseIndex switch
            {
                0 => "初始化",
                1 => "P1",
                2 => "P2", 
                3 => "P3",
                4 => "硬直阶段",
                5 => "背景破坏",
                6 => "P4",
                7 => "P5",
                8 => "P6",
                _ => $"未知阶段({phaseIndex})"
            };
        }

        #region 各阶段开始处理
        private void OnPhase1Start()
        {
            Log.Info("P1阶段开始");
            // ========== 在这里添加P1特定逻辑 ==========
        }

        private void OnPhase2Start()
        {
            Log.Info("P2阶段开始");
            // ========== 在这里添加P2特定逻辑 ==========
        }

        private void OnPhase3Start()
        {
            Log.Info("P3阶段开始");
            // ========== 在这里添加P3特定逻辑 ==========
        }

        private void OnPhase4Start()
        {
            Log.Info("硬直阶段开始");
            // ========== 在这里添加硬直阶段特定逻辑 ==========
        }

        private void OnPhase5Start()
        {
            Log.Info("背景破坏阶段开始");
            // ========== 在这里添加背景破坏阶段特定逻辑 ==========
        }

        private void OnPhase6Start()
        {
            Log.Info("P4阶段开始");
            // ========== 在这里添加P4特定逻辑 ==========
        }

        private void OnPhase7Start()
        {
            Log.Info("P5阶段开始");
            // ========== 在这里添加P5特定逻辑 ==========
        }

        private void OnPhase8Start()
        {
            Log.Info("P6阶段开始");
            // ========== 在这里添加P6特定逻辑 ==========
        }
        #endregion

        /// <summary>
        /// 获取指定阶段的血量
        /// </summary>
        public int GetPhaseHP(int phase)
        {
            if (_phaseControl == null || phase < 1 || phase > 6) return 0;
            
            string hpVarName = $"P{phase} HP";
            var hpVar = _phaseControl.FsmVariables.GetFsmInt(hpVarName);
            
            return hpVar?.Value ?? 0;
        }

        /// <summary>
        /// 设置指定阶段的血量
        /// </summary>
        public void SetPhaseHP(int phase, int hp)
        {
            if (_phaseControl == null || phase < 1 || phase > 6) return;
            
            string hpVarName = $"P{phase} HP";
            var hpVar = _phaseControl.FsmVariables.GetFsmInt(hpVarName);
            
            if (hpVar != null)
            {
                hpVar.Value = hp;
                Log.Info($"{hpVarName} 设置为: {hp}");
            }
        }

        /// <summary>
        /// 给Boss增加血量（释放大招时回血）
        /// </summary>
        /// <param name="healAmount">回血量（默认200）</param>
        public void AddBossHealth(int healAmount = 200)
        {
            // 获取 HealthManager 组件
            var healthManager = gameObject.GetComponent<HealthManager>();
            if (healthManager == null)
            {
                Log.Error("未找到 HealthManager 组件，无法回血");
                return;
            }

            healthManager.AddHP(healAmount,1000);

            Log.Info($"Boss回血：{healAmount}");
        }

        /// <summary>
        /// 手动触发阶段改变（用于测试）
        /// </summary>
        public void TriggerPhase(int phase)
        {
            if (_phaseControl == null || phase < 1 || phase > 6) return;
            
            Log.Info($"手动触发P{phase}");
            
            var phaseVar = _phaseControl.FsmVariables.GetFsmInt("Phase");
            if (phaseVar != null)
            {
                phaseVar.Value = phase;
            }
        }

        /// <summary>
        /// 重置所有阶段标志
        /// </summary>
        public void ResetPhaseFlags()
        {
            for (int i = 1; i <= 6; i++)
            {
                string phaseFlagName = $"P{i}";
                var phaseFlagVar = _phaseControl.FsmVariables.GetFsmBool(phaseFlagName);
                
                if (phaseFlagVar != null)
                {
                    phaseFlagVar.Value = false;
                }
                
                _phaseFlags[i] = false;
            }
            
            Log.Info("所有阶段标志已重置");
        }
        
        /// <summary>
        /// 获取当前Boss阶段索引（供其他组件使用）
        /// </summary>
        public int GetCurrentPhaseIndex()
        {
            return _bossPhaseIndex?.Value ?? 0;
        }
        
        /// <summary>
        /// 获取当前阶段名称（供其他组件使用）
        /// </summary>
        public string GetCurrentPhaseName()
        {
            if (_phaseControl == null) return "Unknown";
            return _phaseControl.ActiveStateName;
        }
        
        /// <summary>
        /// 检查是否处于指定阶段（供其他组件使用）
        /// </summary>
        public bool IsInPhase(int phaseIndex)
        {
            return GetCurrentPhaseIndex() == phaseIndex;
        }
        
        /// <summary>
        /// 检查是否处于战斗阶段（P1-P3）
        /// </summary>
        public bool IsInCombatPhase()
        {
            int currentPhase = GetCurrentPhaseIndex();
            return currentPhase >= 1 && currentPhase <= 3;
        }

        /// <summary>
        /// 检查是否处于硬直阶段
        /// </summary>
        public bool IsInStaggerPhase()
        {
            return GetCurrentPhaseIndex() == 4;
        }

        /// <summary>
        /// 检查是否处于背景破坏阶段
        /// </summary>
        public bool IsInBackgroundBreakPhase()
        {
            return GetCurrentPhaseIndex() == 5;
        }

        /// <summary>
        /// 检查是否处于P4阶段
        /// </summary>
        public bool IsInPhase4()
        {
            return GetCurrentPhaseIndex() == 6;
        }

        /// <summary>
        /// 检查是否处于P5阶段
        /// </summary>
        public bool IsInPhase5()
        {
            return GetCurrentPhaseIndex() == 7;
        }

        /// <summary>
        /// 检查是否处于P6阶段
        /// </summary>
        public bool IsInPhase6()
        {
            return GetCurrentPhaseIndex() == 8;
        }

        /// <summary>
        /// 检查是否处于后期阶段（P4-P6）
        /// </summary>
        public bool IsInLatePhase()
        {
            int currentPhase = GetCurrentPhaseIndex();
            return currentPhase >= 6 && currentPhase <= 8;
        }

        #region 大丝球大招相关
        /// <summary>
        /// 获取BigSilkBallManager引用
        /// </summary>
        private void GetBigSilkBallManager()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                _bigSilkBallManager = managerObj.GetComponent<Managers.BigSilkBallManager>();
                if (_bigSilkBallManager != null)
                {
                    Log.Info("PhaseControl: 成功获取 BigSilkBallManager");
                }
                else
                {
                    Log.Warn("PhaseControl: 未找到 BigSilkBallManager 组件");
                }
            }
        }

        /// <summary>
        /// 在Phase Control FSM中添加大丝球大招状态序列
        /// 插入在 Set P3 Web Strand 之后，检查血量决定是否触发大招
        /// </summary>
        private void AddBigSilkBallStates()
        {
            if (_phaseControl == null)
            {
                Log.Error("Phase Control FSM 未初始化，无法添加大招状态");
                return;
            }

            Log.Info("=== 开始添加大丝球大招状态序列 ===");

            // 找到关键状态
            var setP3WebStrandState = _phaseControl.FsmStates.FirstOrDefault(s => s.Name == "Set P3 Web Strand");
            var p3State = _phaseControl.FsmStates.FirstOrDefault(s => s.Name == "P3");
            
            if (setP3WebStrandState == null)
            {
                Log.Error("未找到 Set P3 Web Strand 状态");
                return;
            }
            if (p3State == null)
            {
                Log.Error("未找到 P3 状态");
                return;
            }
            // 注册新事件
            RegisterBigSilkBallEvents();
            // 创建P2.5状态（类似P3，监听TOOK DAMAGE事件）
            var p25State = CreateP25State();
            
            // 创建HP Check 2.5状态（检查血量是否触发大招）
            var hpCheck25State = CreateHPCheck25State();

            // 创建大招状态序列
            var bigSilkBallPrepareState = CreateBigSilkBallPrepareState();
            var bigSilkBallMoveToCenterState = CreateBigSilkBallMoveToCenterState();
            var bigSilkBallSpawnState = CreateBigSilkBallSpawnState();
            var bigSilkBallWaitState = CreateBigSilkBallWaitState();
            var bigSilkBallEndState = CreateBigSilkBallEndState();

            // 添加状态到FSM
            var states = _phaseControl.Fsm.States.ToList();
            states.Add(p25State);
            states.Add(hpCheck25State);
            states.Add(bigSilkBallPrepareState);
            states.Add(bigSilkBallMoveToCenterState);
            states.Add(bigSilkBallSpawnState);
            states.Add(bigSilkBallWaitState);
            states.Add(bigSilkBallEndState);
            _phaseControl.Fsm.States = states.ToArray();

            // 修改 Set P3 Web Strand 的跳转：改为跳到P2.5
            ModifySetP3WebStrandTransition(setP3WebStrandState, p25State);

            // 添加状态动作
            AddP25Actions(p25State);
            AddHPCheck25Actions(hpCheck25State);
            AddBigSilkBallPrepareActions(bigSilkBallPrepareState);
            AddBigSilkBallMoveToCenterActions(bigSilkBallMoveToCenterState);
            AddBigSilkBallSpawnActions(bigSilkBallSpawnState);
            AddBigSilkBallWaitActions(bigSilkBallWaitState);
            AddBigSilkBallEndActions(bigSilkBallEndState);

            // 添加状态转换
            AddP25Transitions(p25State, hpCheck25State);
            AddHPCheck25Transitions(hpCheck25State, p25State, bigSilkBallPrepareState);
            AddBigSilkBallTransitions(bigSilkBallPrepareState, bigSilkBallMoveToCenterState, 
                bigSilkBallSpawnState, bigSilkBallWaitState, bigSilkBallEndState, p3State);



            // 重新初始化FSM
            _phaseControl.Fsm.InitData();
            _phaseControl.Fsm.InitEvents();

            Log.Info("=== 大丝球大招状态序列添加完成 ===");
        }

        /// <summary>
        /// 创建P2.5状态（类似P3，监听TOOK DAMAGE事件）
        /// </summary>
        private FsmState CreateP25State()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "P2.5",
                Description = "P2.5阶段：监听TOOK DAMAGE触发血量检查"
            };
        }

        /// <summary>
        /// 创建HP Check 2.5状态（检查血量是否触发大招）
        /// </summary>
        private FsmState CreateHPCheck25State()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "HP Check 2.5",
                Description = "检查血量：<=200触发大招，>200回到P2.5"
            };
        }

        /// <summary>
        /// 创建大招准备状态
        /// </summary>
        private FsmState CreateBigSilkBallPrepareState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Big Silk Ball Prepare",
                Description = "大招准备：停止攻击、设置无敌"
            };
        }

        /// <summary>
        /// 创建移动到中心状态
        /// </summary>
        private FsmState CreateBigSilkBallMoveToCenterState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Big Silk Ball Move To Center",
                Description = "Boss移动到中间高处"
            };
        }

        /// <summary>
        /// 创建生成大丝球状态
        /// </summary>
        private FsmState CreateBigSilkBallSpawnState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Big Silk Ball Spawn",
                Description = "生成大丝球并开始蓄力"
            };
        }

        /// <summary>
        /// 创建等待大招完成状态
        /// </summary>
        private FsmState CreateBigSilkBallWaitState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Big Silk Ball Wait",
                Description = "等待大丝球爆炸和小丝球生成"
            };
        }

        /// <summary>
        /// 创建大招结束状态
        /// </summary>
        private FsmState CreateBigSilkBallEndState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Big Silk Ball End",
                Description = "大招结束：恢复正常、进入下一阶段"
            };
        }

        /// <summary>
        /// 修改Set P3 Web Strand的跳转，指向P2.5状态
        /// </summary>
        private void ModifySetP3WebStrandTransition(FsmState setP3WebStrandState, FsmState p25State)
        {
            // 修改跳转到P2.5
            setP3WebStrandState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "P2.5",
                    toFsmState = p25State
                }
            };
            Log.Info("已修改 Set P3 Web Strand -> P2.5");
        }

        /// <summary>
        /// 添加P2.5状态的动作（无动作，只监听TOOK DAMAGE）
        /// </summary>
        private void AddP25Actions(FsmState p25State)
        {
            // P2.5状态不需要动作，只监听TOOK DAMAGE事件
            p25State.Actions = new FsmStateAction[0];
        }

        /// <summary>
        /// 添加HP Check 2.5状态的动作
        /// </summary>
        private void AddHPCheck25Actions(FsmState hpCheck25State)
        {
            var actions = new List<FsmStateAction>();

            // 使用CompareHP检查血量是否<=200
            var selfGameObject = new FsmGameObject("Self");
            selfGameObject.Value = gameObject;

            var compareHP = new CompareHP
            {
                enemy = selfGameObject,
                integer2 = new FsmInt(200),  // 检查血量是否<=200
                lessThan = FsmEvent.GetFsmEvent("START BIG SILK BALL"),      // <200触发大招
                equal = FsmEvent.GetFsmEvent("START BIG SILK BALL"),         // =200触发大招
                greaterThan = FsmEvent.Finished,               // >200回到P2.5
                everyFrame = false
            };

            actions.Add(compareHP);
            hpCheck25State.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加P2.5状态的转换
        /// </summary>
        private void AddP25Transitions(FsmState p25State, FsmState hpCheck25State)
        {
            // P2.5监听TOOK DAMAGE事件，跳转到HP Check 2.5
            p25State.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("TOOK DAMAGE"),
                    toState = "HP Check 2.5",
                    toFsmState = hpCheck25State
                }
            };
        }

        /// <summary>
        /// 添加HP Check 2.5状态的转换
        /// </summary>
        private void AddHPCheck25Transitions(FsmState hpCheck25State, FsmState p25State, FsmState bigSilkBallPrepareState)
        {
            // FINISHED -> P3 (血量>200)
            // NEXT -> Big Silk Ball Prepare (血量<=200，触发大招)
            hpCheck25State.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "P2.5",
                    toFsmState = p25State
                },
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("START BIG SILK BALL"),
                    toState = "Big Silk Ball Prepare",
                    toFsmState = bigSilkBallPrepareState
                }
            };
        }

        /// <summary>
        /// 添加准备状态的动作
        /// </summary>
        private void AddBigSilkBallPrepareActions(FsmState prepareState)
        {
            var actions = new List<FsmStateAction>();

            // 0. 保存Boss原始图层
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SaveOriginalLayer") { Value = "SaveOriginalLayer" },
                parameters = new FsmVar[0]
            });

            // 1. Boss回血（释放大招时恢复血量）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("AddBossHealth") { Value = "AddBossHealth" },
                parameters = new FsmVar[]
            {
                new FsmVar(typeof(int)) { intValue = 200 }
            },
            });

            // 2. 生成大丝球（在准备阶段生成，这样可以跟随BOSS移动）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SpawnBigSilkBall") { Value = "SpawnBigSilkBall" },
                parameters = new FsmVar[0]
            });

            // 3. 发送 ATTACK STOP 事件到 Attack Control FSM（停止所有攻击）
            actions.Add(new SendEventByName
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
            });

            // 4. 发送 BIG SILK BALL LOCK 事件到 Boss Control FSM（锁定BOSS）
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("BIG SILK BALL LOCK") { Value = "BIG SILK BALL LOCK" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 5. 设置Boss无敌（Layer 2 = Invincible）
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = 2  // 无敌层，防止受到伤害
            });

            // 6. 等待0.5秒后进入移动状态
            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            });

            prepareState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加移动到中心状态的动作
        /// </summary>
        private void AddBigSilkBallMoveToCenterActions(FsmState moveToCenterState)
        {
            var actions = new List<FsmStateAction>();

            // 2. 设置速度为0（防止重力影响）
            actions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new Vector2(0f, 0f),
                x = new FsmFloat { UseVariable = false },
                y = new FsmFloat(0f),
                everyFrame = false
            });

            // 3. 播放动画（可选，使用 Drift F 动画）
            actions.Add(new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Drift F") { Value = "Drift F" },
                animationCompleteEvent = null,
                animationTriggerEvent = null
            });

            // 4. 移动到中心X位置（65.5）
            actions.Add(new AnimateXPositionTo
            {
                GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                ToValue = new FsmFloat(39.5f),  // 房间中心X
                localSpace = false,
                time = new FsmFloat(1.0f),      
                speed = new FsmFloat(8f),       
                delay = new FsmFloat(0f),
                easeType = EaseFsmAction.EaseType.linear,
                reverse = new FsmBool(false),
                realTime = false
            });

            // 5. 移动到高处Y位置（24）
            actions.Add(new AnimateYPositionTo
            {
                GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                ToValue = new FsmFloat(142.5f),    // 高处Y
                localSpace = false,
                time = new FsmFloat(1.0f), 
                speed = new FsmFloat(5f),      
                delay = new FsmFloat(0f),
                easeType = EaseFsmAction.EaseType.linear,
                reverse = new FsmBool(false),
                realTime = false,
            });
            actions.Add(new Wait
            {
                time = new FsmFloat(1.2f),
                finishEvent = FsmEvent.Finished
            });
            moveToCenterState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加生成大丝球状态的动作（实际上是启动蓄力，因为大丝球已在Prepare状态生成）
        /// </summary>
        private void AddBigSilkBallSpawnActions(FsmState spawnState)
        {
            var actions = new List<FsmStateAction>();

            // 启动大丝球蓄力（大丝球已在Prepare状态生成）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartBigSilkBallCharge") { Value = "StartBigSilkBallCharge" },
                parameters = new FsmVar[0]
            });

            // 启动等待大丝球完成的协程（异步）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("WaitForBigSilkBallComplete") { Value = "WaitForBigSilkBallComplete" },
                parameters = new FsmVar[0]
            });

            // 等待0.5秒后进入等待状态
            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            });

            spawnState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加等待状态的动作
        /// </summary>
        private void AddBigSilkBallWaitActions(FsmState waitState)
        {
            // Wait状态不需要任何Action，纯粹等待协程发送"BIG SILK BALL COMPLETE"事件
            // 协程已在Spawn状态启动，会在大丝球完成后发送自定义事件
            // 空状态不会触发FINISHED，只会等待Transition中定义的自定义事件
            waitState.Actions = new FsmStateAction[0];
        }

        /// <summary>
        /// 添加结束状态的动作
        /// </summary>
        private void AddBigSilkBallEndActions(FsmState endState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 恢复Boss的Layer（从Invincible恢复到原始图层）
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = LayerMask.NameToLayer("Enemies")  // 恢复到敌人层
            });

            // 2. 解锁Boss（发送BIG SILK BALL UNLOCK到Boss Control FSM）
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("BIG SILK BALL UNLOCK") { Value = "BIG SILK BALL UNLOCK" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 3. 恢复攻击（发送ATTACK START到Attack Control FSM）
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
                },
                sendEvent = new FsmString("ATTACK START") { Value = "ATTACK START" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 4. 日志记录
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("LogBigSilkBallEnd") { Value = "LogBigSilkBallEnd" },
                parameters = new FsmVar[0]
            });

            // 5. 等待1.5秒后跳转到P3（让BOSS在大招完成后多停留一会）
            actions.Add(new Wait
            {
                time = new FsmFloat(1.5f),
                finishEvent = FsmEvent.Finished
            });

            endState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加大招状态之间的转换
        /// </summary>
        private void AddBigSilkBallTransitions(FsmState prepareState, FsmState moveToCenterState,
            FsmState spawnState, FsmState waitState, FsmState endState, FsmState p3State)
        {
            // Prepare -> Move To Center
            prepareState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Big Silk Ball Move To Center",
                    toFsmState = moveToCenterState
                }
            };

            // Move To Center -> Spawn
            moveToCenterState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Big Silk Ball Spawn",
                    toFsmState = spawnState
                }
            };

            // Spawn -> Wait
            spawnState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Big Silk Ball Wait",
                    toFsmState = waitState
                }
            };

            // Wait -> End (监听自定义事件BIG SILK BALL COMPLETE)
            waitState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("BIG SILK BALL COMPLETE"),
                    toState = "Big Silk Ball End",
                    toFsmState = endState
                }
            };

            // End -> P3（大招完成后返回P3继续战斗）
            endState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "P3",
                    toFsmState = p3State
                }
            };
            
            Log.Info("已设置 Big Silk Ball End -> P3");
        }

        /// <summary>
        /// 注册大招相关的新事件
        /// </summary>
        private void RegisterBigSilkBallEvents()
        {
            // 确保TOOK DAMAGE事件存在（原版事件）
            var tookDamageEvent = FsmEvent.GetFsmEvent("TOOK DAMAGE");
            
            // 注册新的自定义事件
            var startBigSilkBallEvent = new FsmEvent("START BIG SILK BALL");
            var bigSilkBallCompleteEvent = new FsmEvent("BIG SILK BALL COMPLETE");
            var bigSilkBallLockEvent = new FsmEvent("BIG SILK BALL LOCK");
            var bigSilkBallUnlockEvent = new FsmEvent("BIG SILK BALL UNLOCK");
            
            // 将新事件添加到FSM的全局事件列表
            var events = _phaseControl.Fsm.Events.ToList();
            
            if (!events.Any(e => e.Name == "START BIG SILK BALL"))
            {
                events.Add(startBigSilkBallEvent);
            }
            
            if (!events.Any(e => e.Name == "BIG SILK BALL COMPLETE"))
            {
                events.Add(bigSilkBallCompleteEvent);
            }
            
            if (!events.Any(e => e.Name == "BIG SILK BALL LOCK"))
            {
                events.Add(bigSilkBallLockEvent);
            }
            
            if (!events.Any(e => e.Name == "BIG SILK BALL UNLOCK"))
            {
                events.Add(bigSilkBallUnlockEvent);
            }
            
            _phaseControl.Fsm.Events = events.ToArray();
            
            Log.Info("大招事件注册完成（START, COMPLETE, LOCK, UNLOCK）");
        }


        /// <summary>
        /// 保存Boss原始图层
        /// </summary>
        public void SaveOriginalLayer()
        {
            _originalLayer = gameObject.layer;
            Log.Info($"保存Boss原始图层: {_originalLayer}");
        }

        /// <summary>
        /// 恢复Boss原始图层
        /// </summary>
        public void RestoreOriginalLayer()
        {
            gameObject.layer = _originalLayer;
            Log.Info($"恢复Boss图层: {_originalLayer}");
        }

        /// <summary>
        /// 生成大丝球（不启动蓄力）
        /// </summary>
        public void SpawnBigSilkBall()
        {
            if (_bigSilkBallManager == null)
            {
                Log.Error("BigSilkBallManager 未初始化，无法生成大丝球");
                return;
            }

            if (_bigSilkBallTriggered)
            {
                Log.Warn("大丝球已经触发过，跳过生成");
                return;
            }

            _bigSilkBallTriggered = true;

            // 在Boss位置生成大丝球
            Vector3 bossPosition = gameObject.transform.position;
            _currentBigSilkBall = _bigSilkBallManager.SpawnBigSilkBall(bossPosition, gameObject);

            if (_currentBigSilkBall != null)
            {
                Log.Info($"大丝球生成成功，位置: {bossPosition}");
            }
            else
            {
                Log.Error("大丝球生成失败");
            }
        }

        /// <summary>
        /// 启动大丝球蓄力
        /// </summary>
        public void StartBigSilkBallCharge()
        {
            if (_currentBigSilkBall == null)
            {
                Log.Error("大丝球不存在，无法启动蓄力");
                return;
            }

            var behavior = _currentBigSilkBall.GetComponent<BigSilkBallBehavior>();
            if (behavior != null)
            {
                Log.Info("启动大丝球蓄力");
                behavior.StartCharge();
            }
            else
            {
                Log.Error("大丝球缺少 BigSilkBallBehavior 组件");
            }
        }

        /// <summary>
        /// 延迟启动蓄力
        /// </summary>
        private IEnumerator DelayedStartCharge()
        {
            yield return new WaitForSeconds(0.5f);

            if (_currentBigSilkBall != null)
            {
                var behavior = _currentBigSilkBall.GetComponent<BigSilkBallBehavior>();
                if (behavior != null)
                {
                    behavior.StartCharge();
                    Log.Info("大丝球开始蓄力");
                }
            }
        }

        /// <summary>
        /// 接收来自BigSilkBallBehavior的事件通知
        /// </summary>
        public void OnBigSilkBallEvent(string eventName)
        {
            Log.Info($"收到大丝球事件: {eventName}");

            switch (eventName)
            {
                case "ChargeComplete":
                    _chargeComplete = true;
                    Log.Info("大丝球蓄力完成");
                    break;
                    
                case "BurstComplete":
                    _burstComplete = true;
                    Log.Info("大丝球爆炸完成");
                    break;
                    
                default:
                    Log.Warn($"未知的大丝球事件: {eventName}");
                    break;
            }
        }

        /// <summary>
        /// 等待大丝球完成（协程）
        /// </summary>
        public void WaitForBigSilkBallComplete()
        {
            StartCoroutine(WaitForBigSilkBallCompleteCoroutine());
        }

        /// <summary>
        /// 等待大丝球完成的协程
        /// </summary>
        private IEnumerator WaitForBigSilkBallCompleteCoroutine()
        {
            Log.Info("开始等待大丝球完成...");

            // 重置事件标记
            _chargeComplete = false;
            _burstComplete = false;

            // 等待蓄力完成
            float waitTime = 0f;
            while (!_chargeComplete)
            {
                waitTime += Time.deltaTime;
                if (waitTime > 5f)  // 超时保护：5秒
                {
                    Log.Warn("等待蓄力完成超时（5秒），强制继续");
                    break;
                }
                yield return null;
            }
            Log.Info($"确认蓄力完成（等待时间: {waitTime:F2}s），等待爆炸...");

            // 等待爆炸完成
            waitTime = 0f;
            while (!_burstComplete)
            {
                waitTime += Time.deltaTime;
                if (waitTime > 15f)  // 超时保护：15秒（动画11.53秒+缓冲）
                {
                    Log.Warn("等待爆炸完成超时（15秒），强制继续");
                    break;
                }
                yield return null;
            }
            Log.Info($"确认爆炸完成（等待时间: {waitTime:F2}s），大招结束");

            // 发送自定义完成事件到FSM
            if (_phaseControl != null)
            {
                _phaseControl.SendEvent("BIG SILK BALL COMPLETE");
                Log.Info("已发送 BIG SILK BALL COMPLETE 事件到 Phase Control FSM");
            }
        }

        /// <summary>
        /// 大招结束日志
        /// </summary>
        public void LogBigSilkBallEnd()
        {
            Log.Info("=== 大招序列结束 ===");
            Log.Info($"Boss位置: {transform.position}");
            Log.Info($"Boss Layer: {gameObject.layer}");
            Log.Info($"即将进入硬直阶段（Stagger Hit）然后进入P4");
        }
        #endregion
    }
}
