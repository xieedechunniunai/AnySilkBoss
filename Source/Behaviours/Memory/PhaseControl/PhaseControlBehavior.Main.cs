using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using TMProOld;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;
namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 阶段控制器行为
    /// 负责管理Boss的各个阶段和血量
    /// </summary>
    internal partial class MemoryPhaseControlBehavior : MonoBehaviour
    {
        // FSM引用
        private PlayMakerFSM _phaseControl = null!;

        // 阶段状态变量
        private FsmInt _currentPhase = 1;
        private bool[] _phaseFlags = new bool[9]; // P1-P6 + 硬直 + 背景破坏 + 初始状态

        // 血量修改标志
        private bool _hpModified = false;
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
        private MemoryAttackControlBehavior? _attackControlBehavior;

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

        // 小骑士MOD适配：全局Hero变量缓存
        private GameObject? _globalHero;

        private void Awake()
        {
            // 初始化在Start中进行
        }

        private void Start()
        {
            StartCoroutine(DelayedSetup());
            
            // 尝试获取全局 Hero 变量（小骑士MOD适配）
            TryGetGlobalHero();
        }

        private void Update()
        {
            // 按T键打印全局Hero信息（调试用）
            if (Input.GetKeyDown(KeyCode.T))
            {
                PrintGlobalHeroInfo();
            }
        }

        /// <summary>
        /// 尝试获取全局 Hero 变量（小骑士MOD适配）
        /// </summary>
        private void TryGetGlobalHero()
        {
            try
            {
                var heroVar = FsmVariables.GlobalVariables.GetFsmGameObject("Hero");
                if (heroVar != null && heroVar.Value != null)
                {
                    _globalHero = heroVar.Value;
                    Log.Info($"[PhaseControl] 获取到全局 Hero: {_globalHero.name}");
                }
                else
                {
                    Log.Warn("[PhaseControl] 全局 Hero 变量为空");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[PhaseControl] 获取全局 Hero 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 打印全局 Hero 信息（按T键触发）
        /// </summary>
        private void PrintGlobalHeroInfo()
        {
            // 每次按T都重新获取，以便检测变化
            TryGetGlobalHero();

            if (_globalHero == null)
            {
                Log.Info("[PhaseControl] 全局 Hero 为空");
                return;
            }

            Log.Info($"========== 全局 Hero 信息 ==========");
            Log.Info($"名称: {_globalHero.name}");
            Log.Info($"位置: {_globalHero.transform.position}");
            Log.Info($"Layer: {_globalHero.layer} ({LayerMask.LayerToName(_globalHero.layer)})");
            Log.Info($"激活状态: {_globalHero.activeSelf}");
            
            var components = _globalHero.GetComponents<Component>();
            Log.Info($"组件数量: {components.Length}");
            Log.Info("组件列表:");
            foreach (var comp in components)
            {
                if (comp != null)
                {
                    Log.Info($"  - {comp.GetType().Name}");
                }
            }
            Log.Info($"=====================================");
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
            AddPinArraySpecialStates();
            Log.Info("阶段控制器行为初始化完成");
            _phaseControl.Fsm.InitData();
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
            _currentPhase = _phaseControl.FsmVariables.GetFsmInt("CurrentPhase");
            if (_currentPhase == null)
            {
                // 创建新的FsmInt变量
                _currentPhase = new FsmInt("CurrentPhase");
                _currentPhase.Value = 0;

                // 添加到FSM变量列表
                var intVars = _phaseControl.FsmVariables.IntVariables.ToList();
                intVars.Add(_currentPhase);
                _phaseControl.FsmVariables.IntVariables = intVars.ToArray();

                Log.Info("创建了新的FSM变量: CurrentPhase");
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
                var BossTitle = _bossScene.transform.Find("Boss Title");
                if (BossTitle != null)
                {
                    var TitleText = BossTitle.transform.Find("Title Text");
                    if (TitleText != null)
                    {
                        var Silk_Title_Image = TitleText.transform.Find("Silk_Title_Image");
                        var Silk_Title_Text = TitleText.transform.Find("Silk_Title_Text");
                        if (Silk_Title_Image != null && Silk_Title_Text != null)
                        {
                            Silk_Title_Image.gameObject.SetActive(false);
                            Silk_Title_Text.gameObject.SetActive(true);

                            // 设置标题颜色为星空蓝
                            var titleSmallSuper = Silk_Title_Text.Find("Title Small Super");
                            if (titleSmallSuper != null)
                            {
                                var tmp = titleSmallSuper.GetComponent<TextMeshPro>();
                                if (tmp != null)
                                {
                                    // 星河蓝：明亮的蓝色带点紫调
                                    tmp.color = new Color(0.45f, 0.45f, 1f, 1f);
                                }
                            }
                            var titleSmallMain = Silk_Title_Text.Find("Title Small Main");
                            if (titleSmallMain != null)
                            {
                                var tmp = titleSmallMain.GetComponent<TextMeshPro>();
                                if (tmp != null)
                                {
                                    // 淡金色
                                    tmp.color = new Color(1f, 0.94f, 0.7f, 1f);
                                }
                            }
                        }
                    }
                }
                else
                {
                    Log.Warn("未找到 Boss Title");
                }
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

            // 修改各阶段血量
            ModifyPhaseHP();

            // ⚠️ 修改Set P4状态，添加Special Attack设置
            ModifySetP4State();

            // ⚠️ 修改Set P6状态，添加P6 Web Attack触发标记
            ModifySetP6State();

            // ⚠️ 修改所有 Set P1-P6 状态，添加地刺触发逻辑,更改阶段变量的值
            ModifyAllSetPStatesForSpike();

            Log.Info("阶段行为修改完成");
        }

        /// <summary>
        /// 修改所有 Set P1-P6 状态，添加地刺触发逻辑并移除原版地刺控制
        /// 使用 FSM 变量传递模式：PhaseControl 设置 AttackControl 的变量
        /// </summary>
        private void ModifyAllSetPStatesForSpike()
        {
            if (_phaseControl == null) return;

            for (int phase = 1; phase <= 6; phase++)
            {
                string stateName = $"Set P{phase}";
                var setState = _phaseControl.FsmStates.FirstOrDefault(s => s.Name == stateName);

                if (setState == null)
                {
                    Log.Warn($"未找到 {stateName} 状态，跳过地刺触发设置");
                    continue;
                }

                // 1. 先移除原版地刺相关 Actions（如 Can Spike Pull 设置）
                var filteredActions = RemoveOriginalSpikeActions(setState.Actions.ToList());

                // 2. 设置 AttackControl FSM 的 CurrentPhase 变量
                filteredActions.Add(new SetFsmInt
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Attack Control") { Value = "Attack Control" },
                    variableName = new FsmString("CurrentPhase") { Value = "CurrentPhase" },
                    setValue = new FsmInt(phase),
                    everyFrame = false
                });

                // 3. 设置 AttackControl FSM 的 SpikeAttackPending = true
                filteredActions.Add(new SetFsmBool
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Attack Control") { Value = "Attack Control" },
                    variableName = new FsmString("SpikeAttackPending") { Value = "SpikeAttackPending" },
                    setValue = new FsmBool(true),
                    everyFrame = false
                });
                filteredActions.Add(new SetFsmInt
                {
                    gameObject = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.UseOwner },
                    fsmName = new FsmString("Phase Control") { Value = "Phase Control" },
                    variableName = new FsmString("CurrentPhase") { Value = "CurrentPhase" },
                    setValue = new FsmInt(phase),
                    everyFrame = false
                });
                setState.Actions = filteredActions.ToArray();
                Log.Info($"{stateName} 状态已添加地刺触发逻辑（SetFsmBool）");
            }
        }

        /// <summary>
        /// 移除原版地刺相关 Actions（如设置 Can Spike Pull 的 SetFsmBool）
        /// 同时移除原版落石攻击触发（Can Rubble Attack）
        /// </summary>
        private List<FsmStateAction> RemoveOriginalSpikeActions(List<FsmStateAction> actions)
        {
            var result = new List<FsmStateAction>();
            int removedCount = 0;

            foreach (var action in actions)
            {
                bool shouldRemove = false;

                // 移除设置 Can Spike Pull 的 SetFsmBool
                // 移除设置 Can Rubble Attack 的 SetFsmBool（原版落石攻击）
                if (action is SetFsmBool setFsmBool)
                {
                    if (setFsmBool.variableName?.Value == "Can Spike Pull")
                    {
                        shouldRemove = true;
                        Log.Debug($"[PhaseControl] 移除 SetFsmBool (Can Spike Pull)");
                    }
                    else if (setFsmBool.variableName?.Value == "Can Rubble Attack")
                    {
                        shouldRemove = true;
                        Log.Debug($"[PhaseControl] 移除 SetFsmBool (Can Rubble Attack)");
                    }
                }

                if (shouldRemove)
                {
                    removedCount++;
                }
                else
                {
                    result.Add(action);
                }
            }

            if (removedCount > 0)
            {
                Log.Info($"[PhaseControl] 移除了 {removedCount} 个原版 Actions");
            }

            return result;
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
            AddBossHealth(180);
            for (int i = 1; i <= 6; i++)
            {
                string hpVarName = $"P{i} HP";
                var hpVar = _phaseControl.FsmVariables.GetFsmInt(hpVarName);

                switch (i)
                {
                    case 1:
                        hpVar.Value = 280;
                        break;
                    case 2:
                        hpVar.Value = 380;//380;
                        break;
                    case 3:
                        hpVar.Value = 500;//500;
                        break;
                    case 4:
                        hpVar.Value = 640;//640;
                        break;
                    case 5:
                        hpVar.Value = 520;
                        break;
                    case 6:
                        hpVar.Value = 630;
                        break;
                }
            }

            _hpModified = true;
            Log.Info("所有阶段血量修改完成！");
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
        /// 暂停地刺系统（供 FSM CallMethod 调用）
        /// </summary>
        public void PauseSpike(int phase)
        {
            MemorySpikeFloorBehavior.PauseSpikeSystem(phase);
        }
    }
}
