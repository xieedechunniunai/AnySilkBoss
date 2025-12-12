using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
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
    }
}
