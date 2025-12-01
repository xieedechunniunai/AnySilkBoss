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

        // BOSS原始状态（用于大招期间调整和恢复）
        private float _originalBossZ = 0f;           // BOSS原始Z轴
        private Vector3 _originalBossScale;          // BOSS原始Scale
        private GameObject? _bossHaze;

        private GameObject? _bossHaze2;
        // Hair原始状态（Hair是BOSS的兄弟物体，需要同步调整）
        private Transform? _hairTransform;           // Hair Transform引用
        private float _originalHairZ = 0f;           // Hair原始Z轴
        private Vector3 _originalHairScale;          // Hair原始Scale

        // 新增缓存对象
        private GameObject? _handLObj;
        private GameObject? _handRObj;
        private GameObject[] _allFingerBlades = new GameObject[6];

        // 攻击控制器引用
        private PlayMakerFSM? _attackControl;
        private AttackControlBehavior? _attackControlBehavior;

        // 爬升阶段相关标志
        private bool _climbCompleteEventSent = false;
        private bool _climbAttackEventSent = false;  // 标记CLIMB PHASE ATTACK是否已发送

        // Boss Control FSM引用
        private PlayMakerFSM? _bossControl;

        // Boss Scene 引用
        private GameObject? _bossScene;

        // Web Strand Catch Effect 替身引用
        private GameObject? _webStrandCatchEffect;
        private FsmGameObject? _fsmWebStrandCatchEffect;

        // 丝线缠绕动画相关
        private GameObject? _silkYankPrefab;  // Silk_yank原始物体引用
        private GameObject[] _silkYankClones = new GameObject[5];  // 五个克隆体（上、左上、右上、左下、右下）
        private bool _silkYankInitialized = false;  // 是否已初始化

        // FSM变量引用（用于丝线动画的FSM Action）
        private FsmGameObject[] _fsmSilkYankClones = new FsmGameObject[5];
        private FsmGameObject? _fsmHero;
        private FsmFloat? _fsmHeroX;
        private FsmFloat? _fsmHeroY;

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
                FsmAnalyzer.WriteFsmReport(_phaseControl, "D:\\tool\\unityTool\\mods\\new\\AnySilkBoss\\bin\\Debug\\temp\\_phaseControlFsm.txt");
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
            GetBossControlFSM();
            ModifyPhaseBehavior();
            AddBigSilkBallStates();
            AddClimbPhaseStates();
            Log.Info("阶段控制器行为初始化完成");
            _phaseControl.Fsm.InitData();
            _phaseControl.Fsm.InitEvents();
            _phaseControl.FsmVariables.Init();
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
            // 从Boss子物品中查找haze对象（不使用全局查找）
            var haze1Transform = gameObject.transform.Find("haze2 (7)");
            if (haze1Transform != null)
            {
                _bossHaze = haze1Transform.gameObject;
            }
            else
            {
                Log.Warn("未找到子物品 haze2 (7)");
            }

            var haze2Transform = gameObject.transform.Find("haze2 (8)");
            if (haze2Transform != null)
            {
                _bossHaze2 = haze2Transform.gameObject;
            }
            else
            {
                Log.Warn("未找到子物品 haze2 (8)");
            }
            _handLObj = GameObject.Find("Hand L");
            _handRObj = GameObject.Find("Hand R");
            if (_handLObj != null)
            {
                int idx = 0;
                foreach (string bladeName in new[] { "Finger Blade L", "Finger Blade M", "Finger Blade R" })
                {
                    var bladeTf = _handLObj.transform.Find(bladeName);
                    if (bladeTf != null && idx < 3) _allFingerBlades[idx++] = bladeTf.gameObject;
                }
            }
            if (_handRObj != null)
            {
                int idx = 3;
                foreach (string bladeName in new[] { "Finger Blade L", "Finger Blade M", "Finger Blade R" })
                {
                    var bladeTf = _handRObj.transform.Find(bladeName);
                    if (bladeTf != null && idx < 6) _allFingerBlades[idx++] = bladeTf.gameObject;
                }
            }
            Log.Info($"PhaseControlBehavior已收集FingerBlades: {_allFingerBlades.Count(o => o != null)}");

            // 获取 AttackControl FSM 引用（延迟获取 AttackControlBehavior，避免初始化顺序问题）
            _attackControl = FSMUtility.LocateMyFSM(gameObject, "Attack Control");
            if (_attackControl == null)
            {
                Log.Error("未找到 AttackControl FSM");
            }
            else
            {
                Log.Info("成功获取 AttackControl FSM");
            }

            // 获取 Boss Scene 引用
            _bossScene = GameObject.Find("Boss Scene");
            if (_bossScene == null)
            {
                Log.Error("未找到 Boss Scene");
            }
            else
            {
                Log.Info("成功获取 Boss Scene");

                // 获取 Web Strand Catch Effect（替身）
                var catchEffectTf = _bossScene.transform.Find("Web Strand Catch Effect");
                if (catchEffectTf != null)
                {
                    _webStrandCatchEffect = catchEffectTf.gameObject;
                    _fsmWebStrandCatchEffect = new FsmGameObject("WebStrandCatchEffect") { Value = _webStrandCatchEffect };
                    Log.Info("成功获取 Web Strand Catch Effect");
                }
                else
                {
                    Log.Warn("未找到 Web Strand Catch Effect");
                }
            }

            // 初始化丝线缠绕克隆体
            InitializeSilkYankInGetComponents();
        }

        /// <summary>
        /// 在GetComponents中初始化丝线缠绕克隆体
        /// 路径: Boss Scene/Spike Floors/Spike Floor 1/Silk_yank
        /// </summary>
        private void InitializeSilkYankInGetComponents()
        {
            if (_bossScene == null)
            {
                Log.Error("Boss Scene未获取，无法初始化丝线缠绕");
                return;
            }

            try
            {
                // 查找路径: Boss Scene/Spike Floors/Spike Floor 1/Silk_yank
                var spikeFloors = _bossScene.transform.Find("Spike Floors");
                if (spikeFloors == null)
                {
                    Log.Error("未找到 Spike Floors");
                    return;
                }

                var spikeFloor1 = spikeFloors.Find("Spike Floor 1");
                if (spikeFloor1 == null)
                {
                    Log.Error("未找到 Spike Floor 1");
                    return;
                }

                var silkYankTransform = spikeFloor1.Find("Silk_yank");
                if (silkYankTransform == null)
                {
                    Log.Error("未找到 Silk_yank");
                    return;
                }

                _silkYankPrefab = silkYankTransform.gameObject;
                Log.Info($"成功找到 Silk_yank: {_silkYankPrefab.name}");

                // 创建5个克隆体
                for (int i = 0; i < 5; i++)
                {
                    var clone = GameObject.Instantiate(_silkYankPrefab);
                    clone.name = $"Silk_yank_Clone_{i}";
                    clone.transform.SetParent(null);  // 不设父物体

                    // 删除除了 thread 开头的子物品
                    var childrenToDestroy = new List<GameObject>();
                    for (int j = 0; j < clone.transform.childCount; j++)
                    {
                        var child = clone.transform.GetChild(j);

                        if (!child.name.Equals("thread"))
                        {
                            childrenToDestroy.Add(child.gameObject);
                        }
                        else
                        {
                            child.transform.localPosition = new Vector3(0, 0, child.transform.localPosition.z);
                        }
                    }
                    foreach (var child in childrenToDestroy)
                    {
                        GameObject.Destroy(child);
                    }

                    clone.SetActive(false);  // 初始禁用
                    _silkYankClones[i] = clone;
                    Log.Info($"创建丝线克隆体 {i}: {clone.name}");
                }

                _silkYankInitialized = true;
                Log.Info("丝线缠绕动画初始化完成（5个克隆体）");

                // 注册FSM变量
                RegisterSilkYankFsmVariables();
            }
            catch (System.Exception ex)
            {
                Log.Error($"初始化丝线缠绕失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 注册丝线缠绕相关的FSM变量
        /// </summary>
        private void RegisterSilkYankFsmVariables()
        {
            if (_phaseControl == null) return;

            var gameObjectVars = _phaseControl.FsmVariables.GameObjectVariables.ToList();
            var floatVars = _phaseControl.FsmVariables.FloatVariables.ToList();

            // 创建5个丝线克隆体的FSM变量
            string[] silkYankNames = { "SilkYank_Top", "SilkYank_TopLeft", "SilkYank_TopRight", "SilkYank_BottomLeft", "SilkYank_BottomRight" };
            for (int i = 0; i < 5; i++)
            {
                _fsmSilkYankClones[i] = new FsmGameObject(silkYankNames[i]);
                _fsmSilkYankClones[i].Value = _silkYankClones[i];
                gameObjectVars.Add(_fsmSilkYankClones[i]);
            }

            // 创建Hero引用变量
            _fsmHero = new FsmGameObject("SilkYank_Hero");
            _fsmHero.Value = HeroController.instance?.gameObject;
            gameObjectVars.Add(_fsmHero);

            // 创建Hero位置变量
            _fsmHeroX = new FsmFloat("SilkYank_HeroX");
            _fsmHeroY = new FsmFloat("SilkYank_HeroY");
            floatVars.Add(_fsmHeroX);
            floatVars.Add(_fsmHeroY);

            _phaseControl.FsmVariables.GameObjectVariables = gameObjectVars.ToArray();
            _phaseControl.FsmVariables.FloatVariables = floatVars.ToArray();

            Log.Info("丝线缠绕FSM变量注册完成");
        }

        /// <summary>
        /// 修改阶段行为
        /// </summary>
        private void ModifyPhaseBehavior()
        {
            if (_phaseControl == null) return;

            // 修改各阶段血量（翻2倍）
            ModifyPhaseHP();

            // ⚠️ 修改Set P4状态，添加Special Attack设置
            ModifySetP4State();

            // ⚠️ 修改Set P6状态，添加P6 Web Attack触发标记
            ModifySetP6State();

            Log.Info("阶段行为修改完成");
        }

        /// <summary>
        /// 修改Set P4状态，在进入P4时启用Special Attack
        /// </summary>
        private void ModifySetP4State()
        {
            var setP4State = _phaseControl.FsmStates.FirstOrDefault(s => s.Name == "Set P4");
            if (setP4State == null)
            {
                Log.Warn("未找到Set P4状态，跳过Special Attack设置");
                return;
            }

            var actions = setP4State.Actions.ToList();

            // 在状态开头添加设置Special Attack = true的action
            actions.Insert(0, new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Phase Control") { Value = "Phase Control" },
                variableName = new FsmString("Special Attack") { Value = "Special Attack" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            // 同时需要设置到Attack Control FSM和Control FSM
            actions.Insert(1, new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Attack Control") { Value = "Attack Control" },
                variableName = new FsmString("Special Attack") { Value = "Special Attack" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            actions.Insert(2, new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Control") { Value = "Control" },
                variableName = new FsmString("Special Attack") { Value = "Special Attack" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            setP4State.Actions = actions.ToArray();
            Log.Info("Set P4状态已修改：添加Special Attack设置（Phase Control、Attack Control、Control FSM）");
        }

        /// <summary>
        /// 修改Set P6状态，在进入P6时启用P6 Web Attack
        /// </summary>
        private void ModifySetP6State()
        {
            var setP6State = _phaseControl.FsmStates.FirstOrDefault(s => s.Name == "Set P6");
            if (setP6State == null)
            {
                Log.Warn("未找到Set P6状态，跳过P6 Web Attack设置");
                return;
            }

            var actions = setP6State.Actions.ToList();

            // 在状态末尾添加设置Do P6 Web Attack = true的action
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Attack Control") { Value = "Attack Control" },
                variableName = new FsmString("Do P6 Web Attack") { Value = "Do P6 Web Attack" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            setP6State.Actions = actions.ToArray();
            Log.Info("Set P6状态已修改：添加P6 Web Attack触发标记（Attack Control FSM）");
        }

        /// <summary>
        /// 修改各阶段血量，全部翻2倍
        /// </summary>
        private void ModifyPhaseHP()
        {
            if (_phaseControl == null || _hpModified) return;

            Log.Info("开始修改各阶段血量...");
            AddBossHealth(100);
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

            healthManager.AddHP(healAmount, 1000);

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
            var bigSilkBallRoarState = CreateBigSilkBallRoarState();
            var bigSilkBallRoarEndState = CreateBigSilkBallRoarEndState();
            var bigSilkBallPrepareState = CreateBigSilkBallPrepareState();
            var bigSilkBallMoveToCenterState = CreateBigSilkBallMoveToCenterState();
            var bigSilkBallSpawnState = CreateBigSilkBallSpawnState();
            var bigSilkBallWaitState = CreateBigSilkBallWaitState();
            var bigSilkBallEndState = CreateBigSilkBallEndState();
            var bigSilkBallReturnState = CreateBigSilkBallReturnState();  // 新增：BOSS返回前景状态

            // 添加状态到FSM
            var states = _phaseControl.Fsm.States.ToList();
            states.Add(p25State);
            states.Add(hpCheck25State);
            states.Add(bigSilkBallPrepareState);
            states.Add(bigSilkBallMoveToCenterState);
            states.Add(bigSilkBallSpawnState);
            states.Add(bigSilkBallWaitState);
            states.Add(bigSilkBallEndState);
            states.Add(bigSilkBallReturnState);  // 新增：BOSS返回前景状态
            states.Add(bigSilkBallRoarState);
            states.Add(bigSilkBallRoarEndState);
            _phaseControl.Fsm.States = states.ToArray();

            // 修改 Set P3 Web Strand 的跳转：改为跳到P2.5
            ModifySetP3WebStrandTransition(setP3WebStrandState, p25State);

            // 添加状态动作
            AddP25Actions(p25State);
            AddHPCheck25Actions(hpCheck25State);
            // AddBigSilkBallRoarActions 延迟执行，等待 AttackControlBehavior 初始化
            AddBigSilkBallRoarEndActions(bigSilkBallRoarEndState);
            AddBigSilkBallPrepareActions(bigSilkBallPrepareState);
            AddBigSilkBallMoveToCenterActions(bigSilkBallMoveToCenterState);
            AddBigSilkBallSpawnActions(bigSilkBallSpawnState);
            AddBigSilkBallWaitActions(bigSilkBallWaitState);
            AddBigSilkBallEndActions(bigSilkBallEndState);
            AddBigSilkBallReturnActions(bigSilkBallReturnState);  // 新增：返回状态的Actions

            // 添加状态转换
            AddP25Transitions(p25State, hpCheck25State);
            AddHPCheck25Transitions(hpCheck25State, p25State, bigSilkBallRoarState); // 修改为跳转到怒吼状态
            AddBigSilkBallTransitions(bigSilkBallPrepareState, bigSilkBallMoveToCenterState,
                bigSilkBallSpawnState, bigSilkBallWaitState, bigSilkBallEndState, bigSilkBallReturnState, p3State,
                bigSilkBallRoarState, bigSilkBallRoarEndState);


            // 延迟添加 Big Silk Ball Roar 状态的动作（等待 AttackControlBehavior 初始化）
            StartCoroutine(DelayedAddBigSilkBallRoarActions(bigSilkBallRoarState));

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
                Description = "大招结束：清理和恢复Layer"
            };
        }

        /// <summary>
        /// 创建BOSS返回前景状态
        /// </summary>
        private FsmState CreateBigSilkBallReturnState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Big Silk Ball Return",
                Description = "BOSS从背景返回前景"
            };
        }

        /// <summary>
        /// 创建大招怒吼状态
        /// </summary>
        private FsmState CreateBigSilkBallRoarState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Big Silk Ball Roar",
                Description = "大招前怒吼"
            };
        }

        /// <summary>
        /// 创建大招怒吼后移动到中心状态
        /// </summary>
        private FsmState CreateBigSilkBallRoarEndState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Big Silk Ball Roar To Center",
                Description = "大招怒吼后移动到中心"
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
        private void AddHPCheck25Transitions(FsmState hpCheck25State, FsmState p25State, FsmState bigSilkBallRoarState)
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
                    toState = "Big Silk Ball Roar",
                    toFsmState = bigSilkBallRoarState
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

            // 0.5. 禁用haze子物品
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("DisableBossHaze") { Value = "DisableBossHaze" },
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


            actions.Insert(0, new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SendSilkStunnedToAllFingerBlades") { Value = "SendSilkStunnedToAllFingerBlades" },
                parameters = new FsmVar[] { }
            });
            prepareState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加移动到中心状态的动作
        /// </summary>
        private void AddBigSilkBallMoveToCenterActions(FsmState moveToCenterState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 保存BOSS原始Z轴和Scale
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SaveBossOriginalState") { Value = "SaveBossOriginalState" },
                parameters = new FsmVar[0]
            });

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

            // 5. 移动到高处Y位置（上移5单位）
            actions.Add(new AnimateYPositionTo
            {
                GameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                ToValue = new FsmFloat(147.5f),    // 高处Y（上移5单位）
                localSpace = false,
                time = new FsmFloat(1.0f),
                speed = new FsmFloat(5f),
                delay = new FsmFloat(0f),
                easeType = EaseFsmAction.EaseType.linear,
                reverse = new FsmBool(false),
                realTime = false,
            });

            // 6. 启动BOSS Z轴和Scale的渐变协程（同步进行）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartBossTransformAnimation") { Value = "StartBossTransformAnimation" },
                parameters = new FsmVar[0]
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
        /// 添加结束状态的动作（只做必要的清理）
        /// </summary>
        private void AddBigSilkBallEndActions(FsmState endState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 恢复Boss的Layer（从Invincible恢复到原始图层）

            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("RestoreBossTransform") { Value = "RestoreBossTransform" },
                parameters = new FsmVar[0]
            });
            // --- 在结尾前加注入 ---
            actions.Insert(actions.Count, new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SendBladesReturnToAllFingerBlades") { Value = "SendBladesReturnToAllFingerBlades" },
                parameters = new FsmVar[] { }
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(2f),
                finishEvent = FsmEvent.Finished
            });

            endState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加返回状态的动作（3秒恢复期：前2秒BOSS返回前景，再等1秒）
        /// </summary>
        private void AddBigSilkBallReturnActions(FsmState returnState)
        {
            var actions = new List<FsmStateAction>();

            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = LayerMask.NameToLayer("Enemies")  // 恢复到敌人层
            });
            // 恢复haze子物品
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("EnableBossHaze") { Value = "EnableBossHaze" },
                parameters = new FsmVar[0]
            });
            // 2. 等待2秒（BOSS返回前景的时间）+ 额外1秒缓冲
            actions.Add(new Wait
            {
                time = new FsmFloat(1f),
                finishEvent = FsmEvent.Finished
            });

            // 3. 解锁Boss（发送BIG SILK BALL UNLOCK到Boss Control FSM）
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

            // 4. 恢复攻击（发送ATTACK START到Attack Control FSM）
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

            returnState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加怒吼状态的动作
        /// </summary>
        private void AddBigSilkBallRoarActions(FsmState roarState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 播放Tk2d动画 "Roar"
            actions.Add(new Tk2dPlayAnimationWithEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                clipName = new FsmString("Roar") { Value = "Roar" },
                animationTriggerEvent = FsmEvent.Finished
            });

            // 2. 直接克隆怒吼音效动作（从Attack Control FSM的Roar状态）
            var playRoarAudio = _attackControlBehavior?.CloneActionFromAttackControlFSM<PlayAudioEventRandom>("Roar");
            if (playRoarAudio != null)
            {
                actions.Add(playRoarAudio);
            }
            else
            {
                Log.Warn("无法克隆PlayAudioEventRandom动作，使用默认配置");
            }

            // 4. 发送事件
            if (_hairTransform != null)
            {
                actions.Add(new SendEventByName
                {
                    eventTarget = new FsmEventTarget
                    {
                        target = FsmEventTarget.EventTarget.GameObject,
                        gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, gameObject = new FsmGameObject { Value = _hairTransform.gameObject } },
                    },
                    sendEvent = new FsmString("ROAR") { Value = "ROAR" },
                    delay = new FsmFloat(0f),
                    everyFrame = false
                });
            }

            roarState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 延迟添加 Big Silk Ball Roar 状态的动作（等待 AttackControlBehavior 初始化）
        /// </summary>
        private IEnumerator DelayedAddBigSilkBallRoarActions(FsmState roarState)
        {
            // 等待1秒，确保 AttackControlBehavior 已初始化
            yield return new WaitForSeconds(1f);

            // 确保 AttackControlBehavior 已获取
            if (_attackControlBehavior == null)
            {
                _attackControlBehavior = gameObject.GetComponent<AttackControlBehavior>();
            }

            // 如果仍然为 null，继续等待
            int retryCount = 0;
            while (_attackControlBehavior == null && retryCount < 10)
            {
                yield return new WaitForSeconds(0.1f);
                _attackControlBehavior = gameObject.GetComponent<AttackControlBehavior>();
                retryCount++;
            }

            if (_attackControlBehavior == null)
            {
                Log.Error("延迟1秒后仍无法获取 AttackControlBehavior，Big Silk Ball Roar 状态可能缺少音效动作");
                yield break;
            }

            // 现在安全地添加动作
            Log.Info("延迟添加 Big Silk Ball Roar 状态的动作");
            AddBigSilkBallRoarActions(roarState);
        }

        /// <summary>
        /// 添加怒吼后移动到中心状态的动作
        /// </summary>
        private void AddBigSilkBallRoarEndActions(FsmState roarEndState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 设置速度为0（防止重力影响）
            actions.Add(new SetVelocity2d
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                vector = new Vector2(0f, 0f),
                x = new FsmFloat { UseVariable = false },
                y = new FsmFloat(0f),
                everyFrame = false
            });

            actions.Add(new SendEventToRegister
            {
                eventName = new FsmString("ATTACK CLEAR") { Value = "ATTACK CLEAR" },
            });

            actions.Add(new Tk2dWatchAnimationEvents
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                animationCompleteEvent = FsmEvent.Finished,
                animationTriggerEvent = FsmEvent.Finished,
            });

            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            });
            roarEndState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加大招状态之间的转换
        /// </summary>
        private void AddBigSilkBallTransitions(FsmState prepareState, FsmState moveToCenterState,
            FsmState spawnState, FsmState waitState, FsmState endState, FsmState returnState, FsmState p3State,
            FsmState bigSilkBallRoarState, FsmState bigSilkBallRoarEndState)
        {
            bigSilkBallRoarState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Big Silk Ball Roar End",
                    toFsmState = bigSilkBallRoarEndState
                }
            };
            bigSilkBallRoarEndState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Big Silk Ball Prepare",
                    toFsmState = prepareState
                }
            };
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

            // End -> Return（大招结束后进入返回状态）
            endState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Big Silk Ball Return",
                    toFsmState = returnState
                }
            };

            // Return -> P3（返回前景完成后继续战斗）
            returnState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "P3",
                    toFsmState = p3State
                }
            };

            Log.Info("已设置 Big Silk Ball End -> Return -> P3");
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
        /// 向所有六根针发送事件，参数为object，提升PlayMaker CallMethod兼容性
        /// </summary>
        public void SendEventToAllFingerBlades(object eventNameObj)
        {
            string eventName = eventNameObj?.ToString() ?? "";
            Log.Info($"接收到参数: {eventNameObj}, 转换为字符串: {eventName}");
            foreach (var bladeObj in _allFingerBlades)
            {
                if (bladeObj != null)
                {
                    var fsm = bladeObj.GetComponent<PlayMakerFSM>();
                    if (fsm != null) { fsm.SendEvent(eventName); Log.Info($"向{bladeObj.name}发送:{eventName}"); }
                }
            }
        }

        /// <summary>
        /// 向所有六根针发送SILK STUNNED事件（供FSM CallMethod无参数调用）
        /// </summary>
        public void SendSilkStunnedToAllFingerBlades()
        {
            SendEventToAllFingerBlades("SILK STUNNED");
        }

        /// <summary>
        /// 向所有六根针发送BLADES RETURN事件（供FSM CallMethod无参数调用）
        /// </summary>
        public void SendBladesReturnToAllFingerBlades()
        {
            SendEventToAllFingerBlades("BLADES RETURN");
        }
        /// <summary>
        /// 向所有六根针发送BLADES RETURN事件（供FSM CallMethod无参数调用）
        /// </summary>
        public void SendBladesReturnDelay()
        {
            StartCoroutine(SendBladesReturnDelayCoroutine());
        }
        private IEnumerator SendBladesReturnDelayCoroutine()
        {
            // ⚠️ 关键修复：发送两次BLADES RETURN，因为指针有两个状态需要此事件
            // 
            // 第一次：延迟3.5秒，确保指针走完Stagger流程到达Stagger Finish状态
            // 指针Stagger流程：Stagger Pause(0-0.3s) → Stagger Anim(0.4-1s) → Stagger Drop(2.5s) → Stagger Finish
            // Stagger Finish状态接收BLADES RETURN → 进入Rerise Follow
            yield return new WaitForSeconds(3.5f);
            SendBladesReturnToAllFingerBlades();
            Log.Info("第一次发送 BLADES RETURN（针对Stagger Finish状态）");

            // 第二次：再延迟1秒，针对可能处于Rise状态的指针
            // Rise状态接收BLADES RETURN → 进入Begin
            // 模拟原版Rerise Up状态的第二次发送
            yield return new WaitForSeconds(1f);
            SendBladesReturnToAllFingerBlades();
            Log.Info("第二次发送 BLADES RETURN（针对Rise状态）");
        }

        /// <summary>
        /// 在爬升阶段完成时重置Finger Blade状态
        /// 这是为了弥补跳过Move Stop导致的事件丢失
        /// </summary>
        public void ResetFingerBladesOnClimbComplete()
        {
            // ⚠️ 注意：不在这里立即发送BLADES RETURN
            // 因为SendBladesReturnDelay已经在3.5秒后发送过了
            // 如果此时指针还在Stagger流程中，立即发送会导致事件丢失
            // 
            // 只重置Finger Blade的状态标志，确保它们处于正确的初始状态
            foreach (var bladeObj in _allFingerBlades)
            {
                if (bladeObj != null)
                {
                    var fsm = bladeObj.GetComponent<PlayMakerFSM>();
                    if (fsm != null)
                    {
                        // 重置Ready标志
                        var readyVar = fsm.FsmVariables.GetFsmBool("Ready");
                        if (readyVar != null)
                        {
                            readyVar.Value = false;
                        }

                        Log.Info($"重置Finger Blade {bladeObj.name} Ready标志");
                    }
                }
            }
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
        /// 保存BOSS原始状态（Z轴和Scale），同时保存Hair的状态
        /// </summary>
        public void SaveBossOriginalState()
        {
            // 保存BOSS状态
            _originalBossZ = transform.position.z;
            _originalBossScale = transform.localScale;
            Log.Info($"保存BOSS原始状态 - Z轴: {_originalBossZ:F4}, Scale: {_originalBossScale}");

            // 查找并保存Hair状态（Hair是BOSS的兄弟物体，在同一父级下）
            if (_hairTransform == null && transform.parent != null)
            {
                _hairTransform = transform.parent.Find("Silk_Hair");
                if (_hairTransform == null)
                {
                    Log.Warn("未找到 Silk_Hair 物体（可能名称不同或不存在）");
                }
            }

            if (_hairTransform != null)
            {
                _originalHairZ = _hairTransform.position.z;
                _originalHairScale = _hairTransform.localScale;
                Log.Info($"保存Hair原始状态 - Z轴: {_originalHairZ:F4}, Scale: {_originalHairScale}");
            }
        }

        /// <summary>
        /// 启动BOSS Transform渐变动画（Z轴和Scale）
        /// </summary>
        public void StartBossTransformAnimation()
        {
            StartCoroutine(AnimateBossTransform());
        }

        /// <summary>
        /// BOSS Transform渐变协程（Z轴移到后面，Scale放大补偿），同时调整Hair
        /// </summary>
        private IEnumerator AnimateBossTransform()
        {
            float duration = 1.0f;  // 渐变时长1秒，与XY位置移动同步
            float targetZ = 60f;  // 目标Z轴（调整到更深的背景）
            float targetScale = 1.8f;   // 目标Scale倍数（补偿Z轴后移）

            Log.Info($"开始BOSS Transform渐变 - 从Z={_originalBossZ:F4}到{targetZ:F4}, Scale从{_originalBossScale}到{_originalBossScale * targetScale}");

            float elapsed = 0f;
            Vector3 bossStartScale = _originalBossScale.Abs();
            transform.localScale = bossStartScale;
            Vector3 bossEndScale = _originalBossScale.Abs() * targetScale;

            // Hair的渐变参数
            Vector3 hairStartScale = _originalHairScale;
            Vector3 hairEndScale = _originalHairScale * targetScale;

            if (_hairTransform != null)
            {
                Log.Info($"开始Hair Transform渐变 - 从Z={_originalHairZ:F4}到{targetZ:F4}, Scale从{_originalHairScale}到{hairEndScale}");
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Lerp BOSS Z轴和Scale
                Vector3 bossPos = transform.position;
                bossPos.z = Mathf.Lerp(_originalBossZ, targetZ, t);
                transform.position = bossPos;
                transform.localScale = Vector3.Lerp(bossStartScale, bossEndScale, t);

                // Lerp Hair Z轴和Scale
                if (_hairTransform != null)
                {
                    Vector3 hairPos = _hairTransform.position;
                    hairPos.z = Mathf.Lerp(_originalHairZ, targetZ, t);
                    _hairTransform.position = hairPos;
                    _hairTransform.localScale = Vector3.Lerp(hairStartScale, hairEndScale, t);
                }

                yield return null;
            }

            // 确保最终值精确
            Vector3 bossFinalPos = transform.position;
            bossFinalPos.z = targetZ;
            transform.position = bossFinalPos;
            transform.localScale = bossEndScale;

            if (_hairTransform != null)
            {
                Vector3 hairFinalPos = _hairTransform.position;
                hairFinalPos.z = targetZ;
                _hairTransform.position = hairFinalPos;
                _hairTransform.localScale = hairEndScale;
                Log.Info($"Hair Transform渐变完成 - Z={_hairTransform.position.z:F4}, Scale={_hairTransform.localScale}");
            }

            Log.Info($"BOSS Transform渐变完成 - Z={transform.position.z:F4}, Scale={transform.localScale}");
        }

        /// <summary>
        /// 恢复BOSS原始Transform（大招结束后调用）
        /// </summary>
        public void RestoreBossTransform()
        {
            StartCoroutine(RestoreBossTransformCoroutine());
        }

        /// <summary>
        /// 禁用Boss的haze子物品（大招期间）
        /// </summary>
        public void DisableBossHaze()
        {
            if (_bossHaze != null)
            {
                _bossHaze.SetActive(false);
                Log.Info("已禁用 haze2 (7)");
            }
            else
            {
                Log.Warn("_bossHaze 为 null，无法禁用");
            }

            if (_bossHaze2 != null)
            {
                _bossHaze2.SetActive(false);
                Log.Info("已禁用 haze2 (8)");
            }
            else
            {
                Log.Warn("_bossHaze2 为 null，无法禁用");
            }
        }

        /// <summary>
        /// 恢复Boss的haze子物品（大招结束后）
        /// </summary>
        public void EnableBossHaze()
        {
            if (_bossHaze != null)
            {
                _bossHaze.SetActive(true);
                Log.Info("已恢复 haze2 (7)");
            }
            else
            {
                Log.Warn("_bossHaze 为 null，无法恢复");
            }

            if (_bossHaze2 != null)
            {
                _bossHaze2.SetActive(true);
                Log.Info("已恢复 haze2 (8)");
            }
            else
            {
                Log.Warn("_bossHaze2 为 null，无法恢复");
            }
        }

        /// <summary>
        /// 恢复BOSS Transform的协程（渐变过程），同时恢复Hair
        /// </summary>
        private IEnumerator RestoreBossTransformCoroutine()
        {
            float duration = 2.0f;  // 恢复时间（2秒，让玩家看清BOSS从背景返回）

            // BOSS当前状态
            Vector3 bossCurrentPos = transform.position;
            Vector3 bossCurrentScale = transform.localScale;
            float bossStartZ = bossCurrentPos.z;

            Log.Info($"开始恢复BOSS Transform - 从Z={bossCurrentPos.z:F4}到{_originalBossZ:F4}, Scale从{bossCurrentScale}到(1,1,1)");

            // Hair当前状态
            Vector3 hairCurrentPos = Vector3.zero;
            Vector3 hairCurrentScale = Vector3.one;
            float hairStartZ = 0f;

            if (_hairTransform != null)
            {
                hairCurrentPos = _hairTransform.position;
                hairCurrentScale = _hairTransform.localScale;
                hairStartZ = hairCurrentPos.z;
                Log.Info($"开始恢复Hair Transform - 从Z={hairCurrentPos.z:F4}到{_originalHairZ:F4}, Scale从{hairCurrentScale}到(1,1,1)");
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Lerp BOSS Z轴和Scale（Scale恢复到(1,1,1)）
                Vector3 bossPos = transform.position;
                bossPos.z = Mathf.Lerp(bossStartZ, _originalBossZ, t);
                transform.position = bossPos;
                transform.localScale = Vector3.Lerp(bossCurrentScale, Vector3.one, t);

                // Lerp Hair Z轴和Scale（Scale恢复到(1,1,1)）
                if (_hairTransform != null)
                {
                    Vector3 hairPos = _hairTransform.position;
                    hairPos.z = Mathf.Lerp(hairStartZ, _originalHairZ, t);
                    _hairTransform.position = hairPos;
                    _hairTransform.localScale = Vector3.Lerp(hairCurrentScale, Vector3.one, t);
                }

                yield return null;
            }

            // 确保最终值精确
            Vector3 bossFinalPos = transform.position;
            bossFinalPos.z = _originalBossZ;
            transform.position = bossFinalPos;
            transform.localScale = Vector3.one;  // 强制恢复为(1,1,1)

            if (_hairTransform != null)
            {
                Vector3 hairFinalPos = _hairTransform.position;
                hairFinalPos.z = _originalHairZ;
                _hairTransform.position = hairFinalPos;
                _hairTransform.localScale = Vector3.one;  // 强制恢复为(1,1,1)
                Log.Info($"Hair Transform恢复完成 - Z={_hairTransform.position.z:F4}, Scale={_hairTransform.localScale}");
            }

            Log.Info($"BOSS Transform恢复完成 - Z={transform.position.z:F4}, Scale={transform.localScale}");
        }
        #endregion

        #region 爬升阶段相关
        /// <summary>
        /// 获取 Boss Control FSM 引用
        /// </summary>
        private void GetBossControlFSM()
        {
            _bossControl = FSMUtility.LocateMyFSM(gameObject, "Control");
            if (_bossControl == null)
            {
                Log.Error("未找到 Boss Control FSM");
            }
            else
            {
                Log.Info("成功获取 Boss Control FSM");
            }
        }

        /// <summary>
        /// 添加爬升阶段状态序列
        /// </summary>
        private void AddClimbPhaseStates()
        {
            if (_phaseControl == null)
            {
                Log.Error("Phase Control FSM 未初始化，无法添加爬升阶段");
                return;
            }

            Log.Info("=== 开始添加爬升阶段状态序列 ===");

            // 找到关键状态
            var staggerPauseState = _phaseControl.FsmStates.FirstOrDefault(s => s.Name == "Stagger Pause");
            var setP4State = _phaseControl.FsmStates.FirstOrDefault(s => s.Name == "Set P4");

            if (staggerPauseState == null)
            {
                Log.Error("未找到 Stagger Pause 状态");
                return;
            }
            if (setP4State == null)
            {
                Log.Error("未找到 Set P4 状态");
                return;
            }

            // 注册新事件
            RegisterClimbPhaseEvents();

            // 创建新变量
            CreateClimbPhaseVariables();

            // 创建爬升阶段状态（新流程）
            var climbInitCatchState = CreateClimbInitCatchState();           // 硬控玩家+移动+发送ROAR
            var climbWaitRoarState = CreateClimbWaitRoarState();             // 等待Roar完成
            var climbSilkActivateState = CreateClimbSilkActivateState();     // 激活丝线
            var climbCatchEffectState = CreateClimbCatchEffectState();       // 音频+隐藏玩家+激活替身
            var climbPlayerPrepareState = CreateClimbPlayerPrepareState();   // 恢复重力/显示
            var climbPlayerControlState = CreateClimbPhasePlayerControlState();  // 穿墙下落
            var climbBossActiveState = CreateClimbPhaseBossActiveState();    // Boss漫游+监控
            var climbCompleteState = CreateClimbPhaseCompleteState();        // 完成

            // 添加状态到FSM
            var states = _phaseControl.Fsm.States.ToList();
            states.Add(climbInitCatchState);
            states.Add(climbWaitRoarState);
            states.Add(climbSilkActivateState);
            states.Add(climbCatchEffectState);
            states.Add(climbPlayerPrepareState);
            states.Add(climbPlayerControlState);
            states.Add(climbBossActiveState);
            states.Add(climbCompleteState);
            _phaseControl.Fsm.States = states.ToArray();

            // 修改 Stagger Pause 的跳转（跳到新的初始状态）
            ModifyStaggerPauseTransition(staggerPauseState, climbInitCatchState);

            // 添加状态动作
            AddClimbInitCatchActions(climbInitCatchState);
            AddClimbWaitRoarActions(climbWaitRoarState);
            AddClimbSilkActivateActions(climbSilkActivateState);
            AddClimbCatchEffectActions(climbCatchEffectState);
            AddClimbPlayerPrepareActions(climbPlayerPrepareState);
            AddClimbPhasePlayerControlActions(climbPlayerControlState);
            AddClimbPhaseBossActiveActions(climbBossActiveState);
            AddClimbPhaseCompleteActions(climbCompleteState);

            // 添加状态转换（新流程）
            AddClimbPhaseTransitionsNew(climbInitCatchState, climbWaitRoarState,
                climbSilkActivateState, climbCatchEffectState, climbPlayerPrepareState, climbPlayerControlState,
                climbBossActiveState, climbCompleteState, setP4State);

            // 重新初始化FSM
            _phaseControl.Fsm.InitData();
            _phaseControl.Fsm.InitEvents();

            Log.Info("=== 爬升阶段状态序列添加完成 ===");
        }

        /// <summary>
        /// 注册爬升阶段事件
        /// </summary>
        private void RegisterClimbPhaseEvents()
        {
            var events = _phaseControl.Fsm.Events.ToList();

            // 爬升阶段事件
            if (!events.Any(e => e.Name == "CLIMB PHASE START"))
                events.Add(new FsmEvent("CLIMB PHASE START"));

            if (!events.Any(e => e.Name == "CLIMB PHASE END"))
                events.Add(new FsmEvent("CLIMB PHASE END"));

            if (!events.Any(e => e.Name == "CLIMB PHASE ATTACK"))
                events.Add(new FsmEvent("CLIMB PHASE ATTACK"));

            if (!events.Any(e => e.Name == "CLIMB COMPLETE"))
                events.Add(new FsmEvent("CLIMB COMPLETE"));

            // 新增：Boss Roar 协同事件
            if (!events.Any(e => e.Name == "CLIMB ROAR START"))
                events.Add(new FsmEvent("CLIMB ROAR START"));

            if (!events.Any(e => e.Name == "CLIMB ROAR DONE"))
                events.Add(new FsmEvent("CLIMB ROAR DONE"));

            _phaseControl.Fsm.Events = events.ToArray();
            Log.Info("爬升阶段事件注册完成（含Roar协同事件）");
        }

        /// <summary>
        /// 创建爬升阶段变量
        /// </summary>
        private void CreateClimbPhaseVariables()
        {
            var boolVars = _phaseControl.FsmVariables.BoolVariables.ToList();

            // 检查是否已存在
            if (!boolVars.Any(v => v.Name == "Climb Phase Active"))
            {
                var climbPhaseActive = new FsmBool("Climb Phase Active") { Value = false };
                boolVars.Add(climbPhaseActive);
                _phaseControl.FsmVariables.BoolVariables = boolVars.ToArray();
                Log.Info("创建 Climb Phase Active 变量");
            }

            // ⚠️ 创建Phase2特殊攻击变量
            if (!boolVars.Any(v => v.Name == "Special Attack"))
            {
                var specialAttack = new FsmBool("Special Attack") { Value = false };
                boolVars.Add(specialAttack);
                _phaseControl.FsmVariables.BoolVariables = boolVars.ToArray();
                Log.Info("创建 Special Attack 变量（用于Phase2特殊攻击）");
            }
        }

        /// <summary>
        /// 创建 Climb Init Catch 状态（硬控玩家+移动+发送ROAR）
        /// </summary>
        private FsmState CreateClimbInitCatchState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Climb Init Catch",
                Description = "硬控玩家并移动到地面"
            };
        }

        /// <summary>
        /// 创建 Climb Wait Roar 状态（等待Boss Roar完成）
        /// </summary>
        private FsmState CreateClimbWaitRoarState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Climb Wait Roar",
                Description = "等待Boss吼叫完成"
            };
        }

        /// <summary>
        /// 创建 Climb Silk Activate 状态（激活丝线）
        /// </summary>
        private FsmState CreateClimbSilkActivateState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Climb Silk Activate",
                Description = "激活丝线缠绕"
            };
        }

        /// <summary>
        /// 创建 Climb Catch Effect 状态（音频+隐藏玩家+激活替身）
        /// </summary>
        private FsmState CreateClimbCatchEffectState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Climb Catch Effect",
                Description = "播放音频、隐藏玩家、激活替身"
            };
        }

        /// <summary>
        /// 创建 Climb Player Prepare 状态（恢复重力/显示）
        /// </summary>
        private FsmState CreateClimbPlayerPrepareState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Climb Player Prepare",
                Description = "恢复玩家重力和显示"
            };
        }

        /// <summary>
        /// 创建 Climb Phase Player Control 状态
        /// </summary>
        private FsmState CreateClimbPhasePlayerControlState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Climb Phase Player Control",
                Description = "玩家动画控制"
            };
        }

        /// <summary>
        /// 创建 Climb Phase Boss Active 状态
        /// </summary>
        private FsmState CreateClimbPhaseBossActiveState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Climb Phase Boss Active",
                Description = "Boss漫游+玩家进度监控"
            };
        }

        /// <summary>
        /// 创建 Climb Phase Complete 状态
        /// </summary>
        private FsmState CreateClimbPhaseCompleteState()
        {
            return new FsmState(_phaseControl.Fsm)
            {
                Name = "Climb Phase Complete",
                Description = "爬升阶段完成"
            };
        }

        /// <summary>
        /// 修改 Stagger Pause 的跳转
        /// </summary>
        private void ModifyStaggerPauseTransition(FsmState staggerPauseState, FsmState climbInitState)
        {
            // 找到所有跳转到 BG Break Sequence 的转换，改为跳转到 Climb Init Catch
            foreach (var transition in staggerPauseState.Transitions)
            {
                if (transition.toState == "BG Break Sequence")
                {
                    transition.toState = "Climb Init Catch";
                    transition.toFsmState = climbInitState;
                    Log.Info("已修改 Stagger Pause -> Climb Init Catch");
                }
            }
        }

        /// <summary>
        /// 添加 Climb Init Catch 动作（硬控玩家+移动+发送ROAR）
        /// </summary>
        private void AddClimbInitCatchActions(FsmState initState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 停止所有攻击
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

            // 2. 发送 CLIMB ROAR START 给 Boss Control（全局转换，会中断Boss当前行为）
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("CLIMB ROAR START") { Value = "CLIMB ROAR START" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 3. 设置Boss无敌
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = 2  // Invincible
            });

            // 4. 设置阶段标志
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Phase Control") { Value = "Phase Control" },
                variableName = new FsmString("Climb Phase Active") { Value = "Climb Phase Active" },
                setValue = new FsmBool(true),
                everyFrame = false
            });

            // 5. 硬控玩家（完整4步）+ 移动玩家到Y=133.57 + 发送ROAR事件
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("CatchPlayerForClimb") { Value = "CatchPlayerForClimb" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 6. 等待0.3秒（玩家平滑移动到地面的时间）
            actions.Add(new Wait
            {
                time = new FsmFloat(0.3f),
                finishEvent = FsmEvent.Finished
            });

            initState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Wait Roar 动作（等待Boss Roar完成）
        /// </summary>
        private void AddClimbWaitRoarActions(FsmState waitState)
        {
            // 此状态播放玩家 Roar Lock 动画，等待 CLIMB ROAR DONE 事件
            var actions = new List<FsmStateAction>();
            if (_fsmHero != null)
            {
                actions.Add(new Tk2dPlayAnimation
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmHero
                    },
                    animLibName = new FsmString("") { Value = "" },
                    clipName = new FsmString("Roar Lock") { Value = "Roar Lock" },
                });
            }
            else
            {
                Log.Warn("AddClimbWaitRoarActions: _fsmHero 为空，跳过玩家动画");
            }
            waitState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Silk Activate 动作（激活丝线）
        /// 使用原生FSM Action: GetPosition + SetPosition + SetRotation + ActivateGameObject
        /// </summary>
        private void AddClimbSilkActivateActions(FsmState silkState)
        {
            var actions = new List<FsmStateAction>();

            if (_fsmHero == null || _fsmHeroX == null || _fsmHeroY == null)
            {
                Log.Error("FSM变量未初始化，无法添加丝线激活动作");
                silkState.Actions = new FsmStateAction[0];
                return;
            }

            float offsetDistance = 12.5f;

            // 五个位置的偏移量：上、左上、右上、左下、右下
            float[] offsetsX = { 0f, -offsetDistance, offsetDistance, -offsetDistance, offsetDistance };
            float[] offsetsY = { offsetDistance, offsetDistance, offsetDistance, -offsetDistance, -offsetDistance };
            // 五个旋转角度（180°是垂直向下，统一朝向玩家中心）
            float[] rotations = { 180f, 45f, -45f, 135f, -135f };
            //float[] rotations = { -90f, -45f, -135f, 45f, 135f };
            // [0] 获取Hero位置
            actions.Add(new GetPosition
            {
                gameObject = new FsmOwnerDefault
                {
                    OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                    GameObject = _fsmHero
                },
                vector = new FsmVector3(),
                x = _fsmHeroX,
                y = _fsmHeroY,
                z = new FsmFloat(),
                space = Space.World,
                everyFrame = false
            });

            // 为每个丝线克隆体添加: SetPosition + SetRotation + ActivateGameObject
            for (int i = 0; i < 5; i++)
            {
                if (_fsmSilkYankClones[i] == null) continue;

                // 创建临时变量存储计算后的位置
                var posX = new FsmFloat($"SilkYank_PosX_{i}");
                var posY = new FsmFloat($"SilkYank_PosY_{i}");

                // 计算X位置: HeroX + offsetX
                actions.Add(new FloatOperator
                {
                    float1 = _fsmHeroX,
                    float2 = new FsmFloat(offsetsX[i]),
                    operation = FloatOperator.Operation.Add,
                    storeResult = posX,
                    everyFrame = false
                });

                // 计算Y位置: HeroY + offsetY
                actions.Add(new FloatOperator
                {
                    float1 = _fsmHeroY,
                    float2 = new FsmFloat(offsetsY[i]),
                    operation = FloatOperator.Operation.Add,
                    storeResult = posY,
                    everyFrame = false
                });

                // SetPosition
                actions.Add(new SetPosition
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmSilkYankClones[i]
                    },
                    vector = new FsmVector3(),
                    x = posX,
                    y = posY,
                    z = new FsmFloat(0f),
                    space = Space.World,
                    everyFrame = false
                });

                // SetRotation
                actions.Add(new SetRotation
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmSilkYankClones[i]
                    },
                    quaternion = new FsmQuaternion(),
                    vector = new FsmVector3(),
                    xAngle = new FsmFloat(0f),
                    yAngle = new FsmFloat(0f),
                    zAngle = new FsmFloat(rotations[i]),
                    space = Space.World,
                    everyFrame = false
                });

                // ActivateGameObject
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmSilkYankClones[i]
                    },
                    activate = new FsmBool(true),
                    recursive = new FsmBool(false),
                    resetOnExit = false,
                    everyFrame = false
                });
            }

            // 等待0.3秒（丝线生成时间）
            actions.Add(new Wait
            {
                time = new FsmFloat(0.3f),
                finishEvent = FsmEvent.Finished
            });

            silkState.Actions = actions.ToArray();
            Log.Info("丝线激活动作已添加（使用FSM原生Action）");
        }

        /// <summary>
        /// 添加 Climb Catch Effect 动作（音频+隐藏玩家+激活替身）
        /// </summary>
        private void AddClimbCatchEffectActions(FsmState catchEffectState)
        {
            var actions = new List<FsmStateAction>();

            // 1. 获取玩家位置并设置替身位置
            if (_fsmHero != null && _fsmWebStrandCatchEffect != null)
            {
                // 设置替身位置到玩家位置
                actions.Add(new SetPosition
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmWebStrandCatchEffect,
                        gameObject = _webStrandCatchEffect
                    },
                    vector = new FsmVector3(),
                    x = _fsmHeroX ?? new FsmFloat { UseVariable = false, Value = 0f },
                    y = _fsmHeroY ?? new FsmFloat { UseVariable = false, Value = 0f },
                    z = new FsmFloat { UseVariable = false, Value = 0.006f },
                    space = Space.World,
                    everyFrame = false
                });
                // 3. 隐藏玩家（使用 SetMeshRendererEnabled）
                actions.Add(new SetMeshRenderer
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmHero
                    },
                    active = false
                });
                actions.Add(new MatchScaleSign
                {
                    Target = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = _fsmWebStrandCatchEffect
                    },
                    MatchTo = _fsmHero,
                    active = false
                });
                // 2. 激活替身
                actions.Add(new ActivateGameObject
                {
                    gameObject = new FsmOwnerDefault
                    {
                        OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        gameObject = _fsmWebStrandCatchEffect,
                        GameObject = _webStrandCatchEffect
                    },
                    activate = new FsmBool(true) { Value = true },
                    recursive = new FsmBool(false) { Value = false },
                    resetOnExit = false,
                    everyFrame = false
                });
            }
            else
            {
                Log.Warn("Climb Catch Effect: _fsmHero 或 _fsmWebStrandCatchEffect 为 null，跳过视觉效果设置");
            }

            // 4. 等待1.5秒（原始Catch到恢复的时间）
            actions.Add(new Wait
            {
                time = new FsmFloat(1.5f),
                finishEvent = FsmEvent.Finished
            });

            catchEffectState.Actions = actions.ToArray();

            // 延迟添加音频动作（等待 AttackControlBehavior 初始化）
            StartCoroutine(DelayedAddClimbCatchAudioActions(catchEffectState));
        }

        /// <summary>
        /// 延迟添加爬升阶段 Catch 音频动作（等待 AttackControlBehavior 初始化）
        /// </summary>
        private IEnumerator DelayedAddClimbCatchAudioActions(FsmState catchEffectState)
        {
            // 获取 AttackControlBehavior
            if (_attackControlBehavior == null)
            {
                _attackControlBehavior = gameObject.GetComponent<AttackControlBehavior>();
            }

            // 等待 AttackControlBehavior 初始化完成
            if (_attackControlBehavior != null)
            {
                int waitCount = 0;
                while (!_attackControlBehavior.IsAttackControlFsmReady && waitCount < 50)
                {
                    yield return null;
                    waitCount++;
                }
            }

            if (_attackControlBehavior == null || !_attackControlBehavior.IsAttackControlFsmReady)
            {
                Log.Warn("无法获取 AttackControlBehavior 或其未初始化，Climb Catch Effect 音频动作未添加");
                yield break;
            }

            // 从 Catch 状态复制音频行为
            var audioActions = new List<FsmStateAction>();
            var audioAction1 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayRandomAudioClipTable>("Catch");
            var audioAction2 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayAudioEvent>("Catch", 0);
            var audioAction3 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayAudioEvent>("Catch", 1);
            var audioAction4 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayAudioEvent>("Catch", 2);
            var audioAction5 = _attackControlBehavior.CloneActionFromAttackControlFSM<PlayAudioEvent>("Catch", 3);

            if (audioAction1 != null) audioActions.Add(audioAction1);
            if (audioAction2 != null) audioActions.Add(audioAction2);
            if (audioAction3 != null) audioActions.Add(audioAction3);
            if (audioAction4 != null) audioActions.Add(audioAction4);
            if (audioAction5 != null) audioActions.Add(audioAction5);

            // 重新构建动作列表：视觉效果动作 + 音频 + Wait
            var newActions = new List<FsmStateAction>();
            // 添加除最后一个 Wait 外的所有原有动作
            for (int i = 0; i < catchEffectState.Actions.Length - 1; i++)
            {
                newActions.Add(catchEffectState.Actions[i]);
            }
            // 插入音频动作
            newActions.AddRange(audioActions);
            // 添加最后的 Wait
            newActions.Add(catchEffectState.Actions[catchEffectState.Actions.Length - 1]);

            catchEffectState.Actions = newActions.ToArray();
            catchEffectState.SaveActions();
            catchEffectState.LoadActions();
            Log.Info($"爬升阶段：Catch 音频动作已延迟添加（{audioActions.Count}个）");
        }

        /// <summary>
        /// 添加 Climb Player Prepare 动作（恢复重力/显示）
        /// </summary>
        private void AddClimbPlayerPrepareActions(FsmState prepareState)
        {
            var actions = new List<FsmStateAction>();

            // 恢复玩家重力和显示
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("PreparePlayerForFall") { Value = "PreparePlayerForFall" },
                parameters = new FsmVar[0],
                everyFrame = false
            });

            // 等待0.2秒
            actions.Add(new Wait
            {
                time = new FsmFloat(0.2f),
                finishEvent = FsmEvent.Finished
            });

            prepareState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Phase Player Control 动作
        /// </summary>
        private void AddClimbPhasePlayerControlActions(FsmState playerControlState)
        {
            var actions = new List<FsmStateAction>();

            // 调用协程控制玩家动画
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("StartClimbPhasePlayerAnimation") { Value = "StartClimbPhasePlayerAnimation" },
                parameters = new FsmVar[0]
            });

            // 延后跳转，等待玩家稳定（避免玩家跳跃导致检测错误）
            actions.Add(new Wait
            {
                time = new FsmFloat(1.5f),
                finishEvent = FsmEvent.Finished
            });

            playerControlState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Phase Boss Active 动作
        /// </summary>
        private void AddClimbPhaseBossActiveActions(FsmState bossActiveState)
        {
            var actions = new List<FsmStateAction>();

            // 通知Boss Control进入漫游模式
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("CLIMB PHASE START") { Value = "CLIMB PHASE START" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // ⚠️ 重要：保持原始FSM的指针复位逻辑
            // 在进入爬升阶段后1秒，发送BLADES RETURN让指针回位
            // 这是必要的，不能删除，否则指针可能永远收不到复位事件
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("SendBladesReturnDelay") { Value = "SendBladesReturnDelay" },
                parameters = new FsmVar[0]
            });

            // 每帧监控玩家Y位置
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("MonitorPlayerClimbProgress") { Value = "MonitorPlayerClimbProgress" },
                parameters = new FsmVar[0],
                everyFrame = true
            });

            bossActiveState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加 Climb Phase Complete 动作
        /// </summary>
        private void AddClimbPhaseCompleteActions(FsmState completeState)
        {
            var actions = new List<FsmStateAction>();

            // 恢复Boss正常图层
            actions.Add(new SetLayer
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                layer = 11  // Enemies
            });

            // ⚠️ 恢复Boss的Z轴到0.01（原始值）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("RestoreBossZPosition") { Value = "RestoreBossZPosition" },
                parameters = new FsmVar[0]
            });

            // 清除阶段标志
            actions.Add(new SetFsmBool
            {
                gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                fsmName = new FsmString("Phase Control") { Value = "Phase Control" },
                variableName = new FsmString("Climb Phase Active") { Value = "Climb Phase Active" },
                setValue = new FsmBool(false),
                everyFrame = false
            });

            // 通知Boss Control结束漫游
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Control") { Value = "Control" }
                },
                sendEvent = new FsmString("CLIMB PHASE END") { Value = "CLIMB PHASE END" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 通知Attack Control结束爬升攻击
            actions.Add(new SendEventByName
            {
                eventTarget = new FsmEventTarget
                {
                    target = FsmEventTarget.EventTarget.GameObjectFSM,
                    excludeSelf = new FsmBool(false),
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Attack Control") { Value = "Attack Control" }
                },
                sendEvent = new FsmString("CLIMB PHASE END") { Value = "CLIMB PHASE END" },
                delay = new FsmFloat(0f),
                everyFrame = false
            });

            // 重置Finger Blade状态（弥补跳过Move Stop导致的BLADES RETURN事件）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ResetFingerBladesOnClimbComplete") { Value = "ResetFingerBladesOnClimbComplete" },
                parameters = new FsmVar[0]
            });

            // ⚠️ 快速移动Boss回到战斗场地（Y=50附近）
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("MoveBossBackToArena") { Value = "MoveBossBackToArena" },
                parameters = new FsmVar[0]
            });

            // 重置C#端标志
            actions.Add(new CallMethod
            {
                behaviour = new FsmObject { Value = this },
                methodName = new FsmString("ResetClimbPhaseFlags") { Value = "ResetClimbPhaseFlags" },
                parameters = new FsmVar[0]
            });

            // 等待0.5秒
            actions.Add(new Wait
            {
                time = new FsmFloat(0.5f),
                finishEvent = FsmEvent.Finished
            });

            completeState.Actions = actions.ToArray();
        }

        /// <summary>
        /// 添加爬升阶段转换
        /// </summary>
        private void AddClimbPhaseTransitions(FsmState initState, FsmState playerControlState,
            FsmState bossActiveState, FsmState completeState, FsmState setP4State)
        {
            // Init -> Player Control
            initState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Phase Player Control",
                    toFsmState = playerControlState
                }
            };

            // Player Control -> Boss Active (延迟跳转，等待玩家稳定)
            playerControlState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Phase Boss Active",
                    toFsmState = bossActiveState
                }
            };

            // Boss Active -> Complete (通过 CLIMB COMPLETE 事件)
            bossActiveState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB COMPLETE"),
                    toState = "Climb Phase Complete",
                    toFsmState = completeState
                }
            };

            // Complete -> Set P4
            completeState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Set P4",
                    toFsmState = setP4State
                }
            };

            Log.Info("爬升阶段转换设置完成");
        }

        /// <summary>
        /// 添加爬升阶段转换（新流程）
        /// </summary>
        private void AddClimbPhaseTransitionsNew(
            FsmState initCatchState, FsmState waitRoarState,
            FsmState silkActivateState, FsmState catchEffectState, FsmState playerPrepareState,
            FsmState playerControlState, FsmState bossActiveState,
            FsmState completeState, FsmState setP4State)
        {
            // Init Catch -> Wait Roar (FINISHED)
            initCatchState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Wait Roar",
                    toFsmState = waitRoarState
                }
            };

            // Wait Roar -> Silk Activate (CLIMB ROAR DONE)
            waitRoarState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB ROAR DONE"),
                    toState = "Climb Silk Activate",
                    toFsmState = silkActivateState
                }
            };

            // Silk Activate -> Catch Effect (FINISHED)
            silkActivateState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Catch Effect",
                    toFsmState = catchEffectState
                }
            };

            // Catch Effect -> Player Prepare (FINISHED)
            catchEffectState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Player Prepare",
                    toFsmState = playerPrepareState
                }
            };

            // Player Prepare -> Player Control (FINISHED)
            playerPrepareState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Phase Player Control",
                    toFsmState = playerControlState
                }
            };

            // Player Control -> Boss Active (FINISHED)
            playerControlState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Climb Phase Boss Active",
                    toFsmState = bossActiveState
                }
            };

            // Boss Active -> Complete (CLIMB COMPLETE)
            bossActiveState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.GetFsmEvent("CLIMB COMPLETE"),
                    toState = "Climb Phase Complete",
                    toFsmState = completeState
                }
            };

            // Complete -> Set P4 (FINISHED)
            completeState.Transitions = new FsmTransition[]
            {
                new FsmTransition
                {
                    FsmEvent = FsmEvent.Finished,
                    toState = "Set P4",
                    toFsmState = setP4State
                }
            };

            Log.Info("爬升阶段转换设置完成（新流程）");
        }

        #region 爬升阶段C#辅助方法

        /// <summary>
        /// 硬控玩家并移动到地面（完整4步）
        /// </summary>
        public void CatchPlayerForClimb()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Error("硬控玩家失败：HeroController未找到");
                return;
            }

            var rb = hero.GetComponent<Rigidbody2D>();

            // 1. RelinquishControl
            hero.RelinquishControl();

            // 2. StopAnimationControl
            hero.StopAnimationControl();

            // 3. AffectedByGravity(false) - 关键！解决空中BUG
            hero.AffectedByGravity(false);

            // 4. SetVelocity2d(0, 0) - 停止所有速度
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }

            // 5. 设置玩家无敌
            PlayerData.instance.isInvincible = true;

            Log.Info("硬控玩家完成（完整4步），开始平滑移动到地面");

            // 6. 平滑移动玩家到Y=133.57（保持X不变）
            StartCoroutine(AnimatePlayerToGround());

            // 注意：隐藏玩家/激活替身 移到 Climb Catch Effect 状态执行
            // 注意：CLIMB ROAR START 已经在FSM Action中发送，此处不再重复发送
        }

        /// <summary>
        /// 设置捕捉效果（隐藏英雄 + 激活替身）
        /// </summary>
        public void SetupCatchEffect()
        {
            var hero = HeroController.instance;
            if (hero == null) return;

            // 隐藏英雄的MeshRenderer
            var heroRenderer = hero.GetComponent<MeshRenderer>();
            if (heroRenderer != null)
            {
                heroRenderer.enabled = false;
                Log.Info("英雄MeshRenderer已隐藏");
            }

            // 找到并激活Web Strand Catch Effect
            var bossScene = GameObject.Find("Boss Scene");
            if (bossScene != null)
            {
                var catchEffect = bossScene.transform.Find("Web Strand Catch Effect");
                if (catchEffect != null)
                {
                    // 设置替身位置到英雄位置
                    catchEffect.position = hero.transform.position;

                    // 匹配英雄的朝向
                    var heroScale = hero.transform.localScale;
                    var effectScale = catchEffect.localScale;
                    effectScale.x = Mathf.Sign(heroScale.x) * Mathf.Abs(effectScale.x);
                    catchEffect.localScale = effectScale;

                    // 激活替身
                    catchEffect.gameObject.SetActive(true);
                    Log.Info($"Web Strand Catch Effect已激活，位置: {catchEffect.position}");
                }
                else
                {
                    Log.Warn("Web Strand Catch Effect未找到");
                }
            }
        }

        /// <summary>
        /// 平滑移动玩家到地面Y=133.57
        /// </summary>
        private IEnumerator AnimatePlayerToGround()
        {
            var hero = HeroController.instance;
            if (hero == null) yield break;

            float startY = hero.transform.position.y;
            float targetY = 133.57f;
            float duration = 0.3f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // 使用easeOutCubic缓动
                float easeT = 1f - Mathf.Pow(1f - t, 3f);
                float newY = Mathf.Lerp(startY, targetY, easeT);
                hero.transform.position = new Vector3(
                    hero.transform.position.x,
                    newY,
                    hero.transform.position.z
                );
                yield return null;
            }

            // 确保最终位置精确
            hero.transform.position = new Vector3(
                hero.transform.position.x,
                targetY,
                hero.transform.position.z
            );

            Log.Info($"玩家平滑移动到地面完成，最终Y={hero.transform.position.y:F2}");
        }

        /// <summary>
        /// 激活丝线缠绕（爬升阶段专用）
        /// </summary>
        public void ActivateSilkYankForClimb()
        {
            ActivateSilkYankAnimation();
            Log.Info("激活丝线缠绕动画（爬升阶段）");
        }

        /// <summary>
        /// 为下落做准备：恢复重力和显示
        /// </summary>
        public void PreparePlayerForFall()
        {
            var hero = HeroController.instance;
            if (hero == null) return;

            // 恢复重力
            hero.AffectedByGravity(true);

            // 恢复英雄显示
            var meshRenderer = hero.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = true;
                Log.Info("英雄MeshRenderer已恢复显示");
            }

            // 禁用Web Strand Catch Effect替身
            DeactivateCatchEffect();

            // 禁用丝线动画
            DeactivateSilkYankAnimation();

            Log.Info("玩家准备下落：重力已恢复，替身已禁用，丝线已禁用");
        }

        /// <summary>
        /// 禁用捕捉效果（禁用替身）
        /// </summary>
        private void DeactivateCatchEffect()
        {
            var bossScene = GameObject.Find("Boss Scene");
            if (bossScene != null)
            {
                var catchEffect = bossScene.transform.Find("Web Strand Catch Effect");
                if (catchEffect != null)
                {
                    catchEffect.gameObject.SetActive(false);
                    Log.Info("Web Strand Catch Effect已禁用");
                }
            }
        }

        #endregion

        /// <summary>
        /// 禁用玩家输入
        /// </summary>
        public void DisablePlayerInput()
        {
            var hero = HeroController.instance;
            if (hero != null)
            {
                hero.StopAnimationControl();
                hero.RelinquishControl();
                Log.Info("禁用玩家输入和控制");
            }
        }

        /// <summary>
        /// 启动玩家动画控制协程
        /// </summary>
        public void StartClimbPhasePlayerAnimation()
        {
            StartCoroutine(ClimbPhasePlayerAnimationCoroutine());
        }

        /// <summary>
        /// 玩家动画控制协程（穿墙下落阶段）
        /// 注意：玩家已经在前面的状态中被硬控，此处只处理下落逻辑
        /// </summary>
        private IEnumerator ClimbPhasePlayerAnimationCoroutine()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Error("HeroController 未找到");
                yield break;
            }

            var tk2dAnimator = hero.GetComponent<tk2dSpriteAnimator>();
            var rb = hero.GetComponent<Rigidbody2D>();

            Log.Info("开始穿墙下落序列");

            // 1. 保存原始图层
            int originalLayer = hero.gameObject.layer;
            Vector3 currentPos = hero.transform.position;

            // 2. 设置穿墙图层
            hero.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            // 统一确保无敌状态
            PlayerData.instance.isInvincible = true;

            // 4. 计算X轴速度让玩家落到目标位置（X=40）
            if (rb != null)
            {
                float targetX = 40f;
                float currentX = currentPos.x;
                float fallTime = 2.3f; // 实测下落时间

                // 计算所需的X轴速度: vx = deltaX / t
                float deltaX = targetX - currentX;
                float velocityX = deltaX / fallTime;

                // 设置X轴速度，Y轴由重力控制
                rb.linearVelocity = new Vector2(velocityX, rb.linearVelocity.y);

                Log.Info($"玩家下落计算 - 当前位置:({currentX:F2}, {currentPos.y:F2}), 目标X:{targetX}, X轴速度:{velocityX:F2}");
            }

            // 5. 播放 Weak Fall 动画
            if (tk2dAnimator != null)
                tk2dAnimator.Play("Weak Fall");

            // 6. 监控 Y 轴，当 Y < 57 时恢复原始图层和无敌状态（此时还未落地）
            while (hero.transform.position.y >= 57f)
            {
                yield return null;
            }

            // 7. 恢复原始图层和无敌状态（玩家继续下落但不再穿墙）
            hero.gameObject.layer = originalLayer;
            PlayerData.instance.isInvincible = false;
            Log.Info($"玩家 Y < 57，恢复原始图层: {originalLayer}，当前位置: {hero.transform.position.y}");

            // 8. 等待落地（Y <= 53.8）
            while (hero.transform.position.y > 53.8f)
            {
                yield return null;
            }

            Log.Info($"玩家落地，最终位置: {hero.transform.position.y}");
            if (rb != null)
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

            // 9. 播放恢复动画序列
            if (tk2dAnimator != null)
            {
                tk2dAnimator.Play("FallToProstrate");
                yield return new WaitForSeconds(1f);

                tk2dAnimator.Play("ProstrateRiseToKneel");
                yield return new WaitForSeconds(1f);

                tk2dAnimator.Play("GetUpToIdle");
                yield return new WaitForSeconds(0.3f);
            }

            // 10. 恢复玩家控制和动画控制
            hero.RegainControl();
            hero.StartAnimationControl();

            // 11. 玩家恢复可控后再启动爬升阶段攻击，避免落地硬直期间被攻击
            if (_attackControl != null && !_climbAttackEventSent)
            {
                _attackControl.SendEvent("CLIMB PHASE ATTACK");
                _climbAttackEventSent = true;
                Log.Info("玩家恢复可移动，已发送 CLIMB PHASE ATTACK 事件到 Attack Control FSM");
            }

            Log.Info("玩家动画控制完成，恢复控制权和输入");
        }

        /// <summary>
        /// 监控玩家爬升进度
        /// </summary>
        public void MonitorPlayerClimbProgress()
        {
            if (_climbCompleteEventSent) return;

            var hero = HeroController.instance;
            if (hero == null) return;

            // 边界限制：X轴范围 [2, 78]
            Vector3 pos = hero.transform.position;
            if (pos.x < 2f)
            {
                hero.transform.position = new Vector3(2f, pos.y, pos.z);
                var rb = hero.GetComponent<Rigidbody2D>();
                if (rb != null && rb.linearVelocity.x < 0)
                {
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                }
            }
            else if (pos.x > 78f)
            {
                hero.transform.position = new Vector3(78f, pos.y, pos.z);
                var rb = hero.GetComponent<Rigidbody2D>();
                if (rb != null && rb.linearVelocity.x > 0)
                {
                    rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                }
            }

            // 处理collapse_gate：当玩家X > 20时恢复GameObject但禁用Animator
            HandleCollapseGateDuringClimb(pos.x);

            // 检测玩家是否到达目标高度
            if (pos.y >= 133.5f)
            {
                if (!_climbCompleteEventSent)
                {
                    _climbCompleteEventSent = true;
                    _climbPhaseCompleted = true;
                    if (_attackControl != null && !_climbAttackEventSent)
                    {
                        _attackControl.SendEvent("CLIMB PHASE ATTACK");
                        _climbAttackEventSent = true;
                        Log.Info("玩家提前爬到顶，补发 CLIMB PHASE ATTACK 事件");
                    }
                    _phaseControl.SendEvent("CLIMB COMPLETE");
                    Log.Info("玩家到达目标高度，爬升阶段完成，发送 CLIMB COMPLETE 事件");

                    // ⚠️ 爬升完成后，启动X轴监控协程，持续检测玩家X坐标直到X > 20
                    StartCoroutine(MonitorPlayerXForCollapseGate());
                }
            }
        }

        private GameObject? _collapseGate;
        private bool _collapseGateDisabled = false;
        private bool _climbPhaseCompleted = false;

        /// <summary>
        /// 处理collapse_gate的启用/禁用逻辑
        /// </summary>
        private void HandleCollapseGateDuringClimb(float playerX)
        {
            // 首次查找collapse_gate
            if (_collapseGate == null)
            {
                var bossScene = GameObject.Find("Boss Scene");
                if (bossScene != null)
                {
                    var battleGate = bossScene.transform.Find("Battle Gate");
                    if (battleGate != null)
                    {
                        _collapseGate = battleGate.Find("boss_scene_collapse_gate")?.gameObject;
                        Log.Info($"找到collapse_gate: {(_collapseGate != null ? "成功" : "失败")}");
                    }
                }
            }

            if (_collapseGate == null) return;

            // 进入爬升阶段时禁用collapse_gate及其Animator
            if (!_collapseGateDisabled)
            {
                _collapseGate.SetActive(false);
                var animator = _collapseGate.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = false;
                }
                _collapseGateDisabled = true;
                Log.Info("已禁用collapse_gate和其Animator");
            }

            // ⚠️ 爬升完成后，检测X > 20时恢复GameObject但禁用Animator
            // 注意：这个逻辑需要持续监控，不能只在Y >= 133.5f时触发一次
            if (_climbPhaseCompleted && playerX > 20f && !_collapseGate.activeSelf)
            {
                _collapseGate.SetActive(true);
                var animator = _collapseGate.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = false;
                }
                Log.Info($"玩家X > 20，恢复collapse_gate GameObject（X={playerX:F2}），但Animator仍禁用");
            }
        }

        /// <summary>
        /// 重置爬升阶段标志
        /// </summary>
        public void ResetClimbPhaseFlags()
        {
            _climbCompleteEventSent = false;
            _climbAttackEventSent = false;  // 重置攻击事件标志
            _climbPhaseCompleted = false;
            _collapseGateDisabled = false;
            Log.Info("爬升阶段标志已重置");
        }

        /// <summary>
        /// 恢复Boss的Z轴位置到0.01（原始值）
        /// </summary>
        public void RestoreBossZPosition()
        {
            Vector3 currentPos = transform.position;
            currentPos.z = 0.01f;
            transform.position = currentPos;
            Log.Info($"恢复Boss Z轴到0.01，当前位置: {transform.position}");
        }

        /// <summary>
        /// 快速移动Boss回到战斗场地
        /// </summary>
        public void MoveBossBackToArena()
        {
            StartCoroutine(MoveBossBackToArenaCoroutine());
        }

        private IEnumerator MoveBossBackToArenaCoroutine()
        {
            Log.Info("开始快速移动Boss回到战斗场地");

            Vector3 startPos = transform.position;
            Vector3 targetPos = new Vector3(startPos.x, 136f, startPos.z); // 回到Y=136战斗区域
            float duration = 1.5f; // 1.5秒快速移动
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                transform.position = Vector3.Lerp(startPos, targetPos, t);
                yield return null;
            }

            transform.position = targetPos;
            Log.Info($"Boss已回到战斗场地: {transform.position}");
        }

        /// <summary>
        /// 监控玩家X坐标，直到X > 20时恢复collapse_gate
        /// </summary>
        private IEnumerator MonitorPlayerXForCollapseGate()
        {
            var hero = HeroController.instance;
            if (hero == null || _collapseGate == null)
            {
                Log.Warn("无法监控玩家X坐标或collapse_gate为null");
                yield break;
            }

            Log.Info("开始监控玩家X坐标以恢复collapse_gate");

            // 持续监控直到玩家X > 20或collapse_gate已恢复
            while (hero != null && _collapseGate != null)
            {
                float playerX = hero.transform.position.x;

                // 如果玩家X > 20且collapse_gate未激活，则恢复它
                if (playerX > 20f && !_collapseGate.activeSelf)
                {
                    _collapseGate.SetActive(true);
                    var animator = _collapseGate.GetComponent<Animator>();
                    if (animator != null)
                    {
                        animator.enabled = false;
                    }
                    Log.Info($"玩家X坐标 > 20 ({playerX:F2})，已恢复collapse_gate GameObject，Animator保持禁用");
                    yield break; // 恢复后退出监控
                }

                yield return new WaitForSeconds(0.1f); // 每0.1秒检查一次
            }
        }
        #endregion

        #region 丝线缠绕动画
        /// <summary>
        /// 启用丝线缠绕动画，在五个位置显示（上、左上、右上、左下、右下）
        /// </summary>
        private void ActivateSilkYankAnimation()
        {
            if (!_silkYankInitialized || _silkYankClones[0] == null)
            {
                Log.Error("丝线缠绕未初始化，无法启用动画");
                return;
            }

            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Error("HeroController未找到，无法启用丝线缠绕");
                return;
            }

            Vector3 heroPos = hero.transform.position;
            float offsetDistance = 10f;  // 偏移距离

            // 五个位置：上、左上、右上、左下、右下
            Vector3[] positions = new Vector3[]
            {
                heroPos + new Vector3(0f, offsetDistance, 0f),                 // 上
                heroPos + new Vector3(-offsetDistance, offsetDistance, 0f),    // 左上
                heroPos + new Vector3(offsetDistance, offsetDistance, 0f),     // 右上
                heroPos + new Vector3(-offsetDistance, -offsetDistance, 0f),   // 左下
                heroPos + new Vector3(offsetDistance, -offsetDistance, 0f)     // 右下
            };

            // 五个旋转角度（180°是垂直向下，统一朝向玩家中心）
            float[] rotations = new float[]
            {
                180f,   // 上 -> 向下指向玩家
                -45f,   // 左上 -> 向右下指向玩家
                -135f,  // 右上 -> 向左下指向玩家
                45f,    // 左下 -> 向右上指向玩家
                135f    // 右下 -> 向左上指向玩家
            };

            for (int i = 0; i < 5; i++)
            {
                if (_silkYankClones[i] != null)
                {
                    // 设置位置和旋转
                    _silkYankClones[i].transform.position = positions[i];
                    _silkYankClones[i].transform.rotation = Quaternion.Euler(0f, 0f, rotations[i]);

                    // 启用物体，动画会自动播放
                    _silkYankClones[i].SetActive(true);

                    Log.Info($"启用丝线缠绕 {i}: 位置={positions[i]}, 旋转={rotations[i]}°");
                }
            }

            Log.Info("丝线缠绕动画启用完成（5个位置）");
        }

        /// <summary>
        /// 禁用丝线缠绕动画
        /// </summary>
        private void DeactivateSilkYankAnimation()
        {
            for (int i = 0; i < 5; i++)
            {
                if (_silkYankClones[i] != null)
                {
                    _silkYankClones[i].SetActive(false);
                }
            }
            Log.Info("丝线缠绕动画已禁用");
        }

        /// <summary>
        /// 更新丝线位置跟随玩家（协程）
        /// </summary>
        /// <param name="duration">跟随持续时间</param>
        private IEnumerator UpdateSilkYankPositions(float duration)
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Log.Warn("HeroController未找到，无法更新丝线位置");
                yield break;
            }

            float elapsed = 0f;
            float offsetDistance = 10f;  // 偏移距离

            // 五个旋转角度（180°是垂直向下，统一朝向玩家中心）
            float[] rotations = new float[]
            {
                180f,   // 上 -> 向下指向玩家
                -45f,   // 左上 -> 向右下指向玩家
                -135f,  // 右上 -> 向左下指向玩家
                45f,    // 左下 -> 向右上指向玩家
                135f    // 右下 -> 向左上指向玩家
            };

            while (elapsed < duration)
            {
                if (hero != null)
                {
                    Vector3 heroPos = hero.transform.position;

                    // 五个位置：上、左上、右上、左下、右下
                    Vector3[] positions = new Vector3[]
                    {
                        heroPos + new Vector3(0f, offsetDistance, 0f),                 // 上
                        heroPos + new Vector3(-offsetDistance, offsetDistance, 0f),    // 左上
                        heroPos + new Vector3(offsetDistance, offsetDistance, 0f),     // 右上
                        heroPos + new Vector3(-offsetDistance, -offsetDistance, 0f),   // 左下
                        heroPos + new Vector3(offsetDistance, -offsetDistance, 0f)     // 右下
                    };

                    // 更新所有丝线位置
                    for (int i = 0; i < 5; i++)
                    {
                        if (_silkYankClones[i] != null && _silkYankClones[i].activeSelf)
                        {
                            _silkYankClones[i].transform.position = positions[i];
                            _silkYankClones[i].transform.rotation = Quaternion.Euler(0f, 0f, rotations[i]);
                        }
                    }
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Log.Info($"丝线跟随完成，持续时间: {duration}秒");
        }
        #endregion
    }
}
