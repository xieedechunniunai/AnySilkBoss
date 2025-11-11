# Stagger后新阶段"爬升阻挠"整改方案（最终版）

## 一、总览

本方案用于在 Phase Control FSM 的 Stagger Pause 后新增"爬升阻挠"大阶段，包含：
1. 玩家强制动画与位置重置
2. Boss 漫游攻击系统
3. 玩家Y轴进度监控（到达133.5后流转至P4）
4. 严格遵循FSM规则：单事件单跳转、FsmString显式赋Value、InitData/InitEvents

---

## 二、Phase Control FSM 整改

### 2.1 整体结构调整

**核心改动：删除 Stagger Pause → BG Break Sequence 跳转，新增爬升阶段流程**

```
原流程：
HP Check 3 (HP<=0) → Stagger Hit → Stagger Fall → Stagger Pause → BG Break Sequence → ...

新流程：
HP Check 3 (HP<=0) → Stagger Hit → Stagger Fall → Stagger Pause 
    → Climb Phase Init              // 新：初始化
    → Climb Phase Player Control    // 新：玩家动画控制（协程）
    → Climb Phase Boss Active       // 新：Boss漫游+玩家爬升监控（主循环）
    → Climb Phase Complete          // 新：阶段完成清理
    → Set P4                        // 原有：进入P4阶段
```

---

### 2.2 新增状态详细设计

#### 状态1: Climb Phase Init（爬升阶段初始化）

**职责**：初始化阶段，停止所有攻击移动，设置Boss无敌

**动作（Actions）**：
```csharp
[0] SendEventByName
    - eventTarget: Attack Control FSM
    - sendEvent: "ATTACK STOP"
    - 说明：停止所有攻击

[1] SendEventByName
    - eventTarget: Boss Control FSM
    - sendEvent: "MOVE STOP"
    - 说明：停止Boss移动

[2] SetLayer
    - gameObject: Boss (Owner)
    - layer: 2 (Invincible)
    - 说明：设置Boss无敌

[3] SetFsmBool
    - gameObject: Owner
    - fsmName: "Phase Control"
    - variableName: "Climb Phase Active"
    - value: true
    - 说明：设置阶段标志（需新建此Bool变量）

[4] Wait
    - time: 0.5
    - finishEvent: FINISHED
```

**转换（Transitions）**：
```
FINISHED → Climb Phase Player Control
```

---

#### 状态2: Climb Phase Player Control（玩家动画控制）

**职责**：启动玩家动画控制协程（异步执行，不阻塞FSM）

**动作（Actions）**：
```csharp
[0] CallMethod
    - behaviour: PhaseControlBehavior组件
    - methodName: "StartClimbPhasePlayerAnimation"
    - parameters: []
    - 说明：调用C#协程控制玩家动画序列

[1] Wait
    - time: 0.1
    - finishEvent: FINISHED
    - 说明：立即跳转到Boss激活状态（协程在后台运行）
```

**转换（Transitions）**：
```
FINISHED → Climb Phase Boss Active
```

**协程实现（PhaseControlBehavior.cs）**：
```csharp
public void StartClimbPhasePlayerAnimation()
{
    StartCoroutine(ClimbPhasePlayerAnimationCoroutine());
}

private IEnumerator ClimbPhasePlayerAnimationCoroutine()
{
    var hero = HeroController.instance;
    var heroAnimController = hero.GetComponent<HeroAnimationController>();
    var tk2dAnimator = hero.GetComponent<tk2dSpriteAnimator>();
    
    // 1. 禁用玩家控制和动画控制器
    hero.RelinquishControl();
    heroAnimController.enabled = false;
    
    // 2. 保存原始图层
    int originalLayer = hero.gameObject.layer;
    
    // 3. 强制移动到起始位置并设置图层
    hero.transform.position = new Vector3(40f, 54f, 0f);
    hero.gameObject.layer = 2; // Ignore Raycast
    
    // 4. 限制最大速度（可选）
    var rb = hero.GetComponent<Rigidbody2D>();
    if (rb != null)
    {
        rb.velocity = Vector2.zero;
    }
    
    // 5. 播放 Weak Fall 动画
    tk2dAnimator.Play("Weak Fall");
    
    // 6. 等待落地（Y <= 57）
    while (hero.transform.position.y > 57f)
    {
        yield return null;
    }
    
    // 7. 恢复原始图层
    hero.gameObject.layer = originalLayer;
    
    // 8. 播放恢复动画序列
    tk2dAnimator.Play("Fall To Prostrate");
    yield return new WaitForSeconds(0.5f);
    
    tk2dAnimator.Play("Prostrate Rise To Kneel");
    yield return new WaitForSeconds(0.5f);
    
    tk2dAnimator.Play("Get Up To Idle");
    yield return new WaitForSeconds(0.5f);
    
    // 9. 恢复玩家控制
    hero.RegainControl();
    heroAnimController.enabled = true;
    
    Log.Info("玩家动画控制完成，恢复控制权");
}
```

---

#### 状态3: Climb Phase Boss Active（Boss漫游+玩家进度监控主循环）

**职责**：
1. 发送事件启动Boss漫游和攻击
2. 每帧监控玩家Y轴位置
3. 玩家到达133.5后自动结束阶段

**动作（Actions）**：
```csharp
[0] SendEventByName
    - eventTarget: Boss Control FSM
    - sendEvent: "CLIMB PHASE START"
    - 说明：通知Boss Control进入漫游模式

[1] SendEventByName
    - eventTarget: Attack Control FSM
    - sendEvent: "CLIMB PHASE ATTACK"
    - 说明：通知Attack Control开始爬升阶段攻击

[2] CallMethod
    - behaviour: PhaseControlBehavior组件
    - methodName: "MonitorPlayerClimbProgress"
    - parameters: []
    - everyFrame: true
    - 说明：每帧检测玩家Y位置，到达目标后发送事件
```

**转换（Transitions）**：
```
CLIMB COMPLETE → Climb Phase Complete
```

**监控实现（PhaseControlBehavior.cs）**：
```csharp
private bool _climbCompleteEventSent = false;

public void MonitorPlayerClimbProgress()
{
    if (_climbCompleteEventSent) return;
    
    var hero = HeroController.instance;
    if (hero != null && hero.transform.position.y >= 133.5f)
    {
        _climbCompleteEventSent = true;
        _phaseControl.SendEvent("CLIMB COMPLETE");
        Log.Info("玩家到达目标高度，发送 CLIMB COMPLETE 事件");
    }
}
```

---

#### 状态4: Climb Phase Complete（爬升阶段完成）

**职责**：清理阶段状态，恢复Boss正常状态

**动作（Actions）**：
```csharp
[0] SetLayer
    - gameObject: Boss (Owner)
    - layer: 11 (Enemies)
    - 说明：恢复Boss正常图层

[1] SetFsmBool
    - gameObject: Owner
    - fsmName: "Phase Control"
    - variableName: "Climb Phase Active"
    - value: false
    - 说明：清除阶段标志

[2] SendEventByName
    - eventTarget: Boss Control FSM
    - sendEvent: "CLIMB PHASE END"
    - 说明：通知Boss Control结束漫游

[3] SendEventByName
    - eventTarget: Attack Control FSM
    - sendEvent: "CLIMB PHASE END"
    - 说明：通知Attack Control结束爬升攻击

[4] CallMethod
    - behaviour: PhaseControlBehavior组件
    - methodName: "ResetClimbPhaseFlags"
    - parameters: []
    - 说明：重置C#端标志

[5] Wait
    - time: 0.5
    - finishEvent: FINISHED
```

**转换（Transitions）**：
```
FINISHED → Set P4
```

---

### 2.3 新增事件注册

在 `RegisterClimbPhaseEvents()` 方法中注册以下事件：

```csharp
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
    
    _phaseControl.Fsm.Events = events.ToArray();
    Log.Info("爬升阶段事件注册完成");
}
```

---

### 2.4 新增FSM变量

```csharp
// 在 Phase Control FSM 中新增Bool变量
private void CreateClimbPhaseVariables()
{
    var boolVars = _phaseControl.FsmVariables.BoolVariables.ToList();
    
    var climbPhaseActive = new FsmBool("Climb Phase Active") { Value = false };
    boolVars.Add(climbPhaseActive);
    
    _phaseControl.FsmVariables.BoolVariables = boolVars.ToArray();
}
```

---

## 三、Boss Control FSM 整改

### 3.1 新增漫游系统状态链

#### 整体流程
```
收到 CLIMB PHASE START 事件（全局转换）
    ↓
Climb Roam Init（初始化漫游参数）
    ↓
Climb Roam Select Target（选择漫游目标点）
    ↓
Climb Roam Move（移动到目标）
    ↓
Climb Roam Idle（短暂停留）
    ↓ (循环回 Select Target，直到收到 CLIMB PHASE END)
Idle（收到 CLIMB PHASE END 后恢复正常）
```

---

#### 状态1: Climb Roam Init（漫游初始化）

**动作（Actions）**：
```csharp
[0] SetVelocity2d
    - gameObject: Owner
    - vector: (0, 0)
    - 说明：停止当前速度

[1] CallMethod
    - behaviour: BossBehavior组件
    - methodName: "InitClimbRoamParameters"
    - parameters: []
    - 说明：初始化漫游边界和速度

[2] Wait
    - time: 0.2
    - finishEvent: FINISHED
```

**转换（Transitions）**：
```
FINISHED → Climb Roam Select Target
```

---

#### 状态2: Climb Roam Select Target（选择漫游目标）

**动作（Actions）**：
```csharp
[0] CallMethod
    - behaviour: BossBehavior组件
    - methodName: "CalculateClimbRoamTarget"
    - parameters: []
    - 说明：根据玩家位置计算下一个漫游点

[1] Wait
    - time: 0.1
    - finishEvent: FINISHED
```

**转换（Transitions）**：
```
FINISHED → Climb Roam Move
```

**计算逻辑（BossBehavior.cs）**：
```csharp
private Vector3 _currentRoamTarget;

public void CalculateClimbRoamTarget()
{
    var hero = HeroController.instance;
    if (hero == null) return;
    
    Vector3 playerPos = hero.transform.position;
    
    // 漫游边界
    float minX = 25f;
    float maxX = 55f;
    float minY = playerPos.y + 20f;  // 玩家上方20单位
    float maxY = 145f;                // 房间顶部
    
    // 随机选择目标（玩家上方附近）
    float targetX = Mathf.Clamp(
        playerPos.x + Random.Range(-8f, 8f), 
        minX, 
        maxX
    );
    
    float targetY = Mathf.Clamp(
        playerPos.y + 25f + Random.Range(-5f, 5f), 
        minY, 
        maxY
    );
    
    _currentRoamTarget = new Vector3(targetX, targetY, 0f);
    Log.Info($"选择漫游目标: {_currentRoamTarget}");
}
```

---

#### 状态3: Climb Roam Move（移动到目标）

**动作（Actions）**：
```csharp
[0] CallMethod
    - behaviour: BossBehavior组件
    - methodName: "SelectRoamAnimation"
    - parameters: []
    - 说明：根据移动方向选择动画（Drift F/Drift B）

[1] CallMethod
    - behaviour: BossBehavior组件
    - methodName: "MoveToRoamTarget"
    - parameters: []
    - everyFrame: true
    - 说明：每帧向目标移动，到达后发送FINISHED

[2] CallMethod
    - behaviour: BossBehavior组件
    - methodName: "CheckRoamMoveTimeout"
    - parameters: []
    - everyFrame: true
    - 说明：超时保护（3秒强制完成）
```

**转换（Transitions）**：
```
FINISHED → Climb Roam Idle
```

**移动实现（BossBehavior.cs）**：
```csharp
private float _roamMoveStartTime;
private bool _roamMoveComplete = false;

public void SelectRoamAnimation()
{
    var bossPos = transform.position;
    bool movingForward = _currentRoamTarget.x > bossPos.x;
    
    var tk2dAnimator = GetComponent<tk2dSpriteAnimator>();
    tk2dAnimator.Play(movingForward ? "Drift F" : "Drift B");
    
    _roamMoveStartTime = Time.time;
    _roamMoveComplete = false;
}

public void MoveToRoamTarget()
{
    if (_roamMoveComplete) return;
    
    Vector3 currentPos = transform.position;
    float distance = Vector3.Distance(currentPos, _currentRoamTarget);
    
    // 到达检测（容差0.5单位）
    if (distance < 0.5f)
    {
        _roamMoveComplete = true;
        _bossControlFsm.SendEvent("FINISHED");
        return;
    }
    
    // 平滑移动
    float moveSpeed = 5f;
    Vector3 direction = (_currentRoamTarget - currentPos).normalized;
    transform.position += direction * moveSpeed * Time.deltaTime;
}

public void CheckRoamMoveTimeout()
{
    if (_roamMoveComplete) return;
    
    if (Time.time - _roamMoveStartTime > 3f)
    {
        Log.Warn("漫游移动超时，强制完成");
        _roamMoveComplete = true;
        _bossControlFsm.SendEvent("FINISHED");
    }
}
```

---

#### 状态4: Climb Roam Idle（短暂停留）

**动作（Actions）**：
```csharp
[0] Tk2dPlayAnimation
    - gameObject: Owner
    - clipName: "Idle"

[1] Wait
    - time: Random.Range(1f, 2f)  // 1-2秒随机
    - finishEvent: FINISHED
```

**转换（Transitions）**：
```
FINISHED → Climb Roam Select Target  // 循环
```

---

### 3.2 全局转换（Global Transitions）

```csharp
// 新增两个全局转换
private void AddClimbPhaseGlobalTransitions()
{
    var globalTransitions = _bossControlFsm.Fsm.GlobalTransitions.ToList();
    
    // 1. 收到 CLIMB PHASE START → Climb Roam Init
    globalTransitions.Add(new FsmTransition
    {
        FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE START"),
        toState = "Climb Roam Init"
    });
    
    // 2. 收到 CLIMB PHASE END → Idle
    globalTransitions.Add(new FsmTransition
    {
        FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE END"),
        toState = "Idle"
    });
    
    _bossControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
}
```

---

## 四、Attack Control FSM 整改

### 4.1 新增爬升阶段攻击系统

#### 整体流程
```
收到 CLIMB PHASE ATTACK 事件（全局转换）
    ↓
Climb Attack Choice（随机选择攻击）
    ↓ [CLIMB NEEDLE/WEB/SILK BALL]
对应的攻击状态
    ↓
Climb Attack Cooldown（冷却）
    ↓ (循环回 Choice，直到收到 CLIMB PHASE END)
Idle（收到 CLIMB PHASE END 后恢复正常）
```

---

#### 状态1: Climb Attack Choice（攻击选择）

**动作（Actions）**：
```csharp
[0] SendRandomEventV4
    - events: [
        CLIMB NEEDLE ATTACK,
        CLIMB WEB ATTACK,
        CLIMB SILK BALL ATTACK
      ]
    - weights: [1.0, 0.8, 0.6]
    - eventMax: [999, 999, 999]  // 无限制
    - missedMax: [0, 0, 0]
    - 说明：随机选择一种攻击
```

**转换（Transitions）**：
```
CLIMB NEEDLE ATTACK → Climb Needle Attack
CLIMB WEB ATTACK → Climb Web Attack
CLIMB SILK BALL ATTACK → Climb Silk Ball Attack
```

---

#### 状态2-4: 攻击状态（占位，后续细化）

**Climb Needle Attack（针攻击）**：
```csharp
动作：
[0] CallMethod
    - behaviour: AttackControlBehavior
    - methodName: "ExecuteClimbNeedleAttack"
    - 说明：从上方向玩家发射2-3根针

[1] Wait
    - time: 0.8
    - finishEvent: FINISHED

转换：
FINISHED → Climb Attack Cooldown
```

**Climb Web Attack（网攻击）**：
```csharp
动作：
[0] CallMethod
    - behaviour: AttackControlBehavior
    - methodName: "ExecuteClimbWebAttack"
    - 说明：在玩家附近生成网障碍

[1] Wait
    - time: 1.0
    - finishEvent: FINISHED

转换：
FINISHED → Climb Attack Cooldown
```

**Climb Silk Ball Attack（丝球攻击）**：
```csharp
动作：
[0] CallMethod
    - behaviour: AttackControlBehavior
    - methodName: "ExecuteClimbSilkBallAttack"
    - 说明：在玩家周围生成3-5个小丝球

[1] Wait
    - time: 1.2
    - finishEvent: FINISHED

转换：
FINISHED → Climb Attack Cooldown
```

---

#### 状态5: Climb Attack Cooldown（攻击冷却）

**动作（Actions）**：
```csharp
[0] Wait
    - time: Random.Range(2f, 3f)  // 2-3秒冷却
    - finishEvent: FINISHED
```

**转换（Transitions）**：
```
FINISHED → Climb Attack Choice  // 循环
```

---

### 4.2 全局转换

```csharp
private void AddClimbPhaseAttackGlobalTransitions()
{
    var globalTransitions = _attackControlFsm.Fsm.GlobalTransitions.ToList();
    
    // 1. 收到 CLIMB PHASE ATTACK → Climb Attack Choice
    globalTransitions.Add(new FsmTransition
    {
        FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE ATTACK"),
        toState = "Climb Attack Choice"
    });
    
    // 2. 收到 CLIMB PHASE END → Idle
    globalTransitions.Add(new FsmTransition
    {
        FsmEvent = FsmEvent.GetFsmEvent("CLIMB PHASE END"),
        toState = "Idle"
    });
    
    _attackControlFsm.Fsm.GlobalTransitions = globalTransitions.ToArray();
}
```

---

## 五、关键技术细节

### 5.1 玩家控制与动画

```csharp
// 禁用控制
HeroController.instance.RelinquishControl();
HeroAnimationController.enabled = false;

// 图层切换（避免碰撞）
hero.gameObject.layer = 2;  // Ignore Raycast（空中）
hero.gameObject.layer = 11; // Enemies（落地后）

// 恢复控制
HeroController.instance.RegainControl();
HeroAnimationController.enabled = true;
```

---

### 5.2 Boss位置算法

```csharp
// 漫游目标计算（保持在玩家上方）
Vector3 CalculateRoamTarget(Vector3 playerPos)
{
    float targetX = Mathf.Clamp(
        playerPos.x + Random.Range(-8f, 8f),
        25f,  // 房间左边界
        55f   // 房间右边界
    );
    
    float targetY = Mathf.Clamp(
        playerPos.y + 25f + Random.Range(-5f, 5f),
        playerPos.y + 20f,  // 玩家上方最少20单位
        145f                // 房间顶部
    );
    
    return new Vector3(targetX, targetY, 0f);
}
```

---

### 5.3 玩家进度监控

```csharp
// 每帧检测（在 Climb Phase Boss Active 状态的 everyFrame Action 中）
public void MonitorPlayerClimbProgress()
{
    if (_climbCompleteEventSent) return;
    
    var hero = HeroController.instance;
    if (hero != null && hero.transform.position.y >= 133.5f)
    {
        _climbCompleteEventSent = true;
        _phaseControl.SendEvent("CLIMB COMPLETE");
    }
}
```

---

## 六、FSM规则自检清单

### 6.1 命名规范
- [x] 顶部标签中的 FSM 名称与实现一致
- [x] GameObject 名称明确

### 6.2 状态-事件-跳转
- [x] 所有 A→B 跳转由**单一事件**触发
- [x] 无多事件收敛到同一目标状态
- [x] 未使用 FINISHED 作为多路通配

### 6.3 数据结构
- [x] 所有 FsmString 同步设置了 Value
  ```csharp
  new FsmString("ATTACK STOP") { Value = "ATTACK STOP" }
  ```

### 6.4 初始化
- [x] 变更后调用：
  ```csharp
  _phaseControl.Fsm.InitData();
  _phaseControl.Fsm.InitEvents();
  ```

### 6.5 职责清晰
- [x] 右侧 Actions 对应状态职责单一
- [x] 无副作用越界

---

## 七、实施步骤总结

### 步骤1: Phase Control FSM
1. 修改 `Stagger Pause` 转换：删除 `→ BG Break Sequence`，改为 `→ Climb Phase Init`
2. 创建4个新状态（Init, Player Control, Boss Active, Complete）
3. 注册新事件（CLIMB PHASE START/END/ATTACK, CLIMB COMPLETE）
4. 添加 Bool 变量 `Climb Phase Active`
5. 实现 C# 协程：`StartClimbPhasePlayerAnimation()`, `MonitorPlayerClimbProgress()`
6. 调用 `InitData()` 和 `InitEvents()`

### 步骤2: Boss Control FSM
1. 创建4个漫游状态（Init, Select Target, Move, Idle）
2. 添加全局转换（CLIMB PHASE START → Init, CLIMB PHASE END → Idle）
3. 实现 C# 方法：`CalculateClimbRoamTarget()`, `MoveToRoamTarget()`
4. 调用 `InitData()` 和 `InitEvents()`

### 步骤3: Attack Control FSM
1. 创建5个攻击状态（Choice, 3种攻击, Cooldown）
2. 添加全局转换（CLIMB PHASE ATTACK → Choice, CLIMB PHASE END → Idle）
3. 实现 C# 方法：`ExecuteClimbXXXAttack()`（具体逻辑后续细化）
4. 调用 `InitData()` 和 `InitEvents()`

---

## 八、预期效果

完成后，玩家在P3阶段结束（HP<=0）后将经历：
1. **硬直序列**：Stagger Hit → Stagger Fall → Stagger Pause (2秒)
2. **爬升阶段开始**：
   - 玩家被强制传送到 (40, 54)，播放坠落动画
   - Boss 进入无敌状态并开始漫游
3. **爬升过程**：
   - 玩家恢复控制后需向上攀爬
   - Boss 在玩家上方漫游并持续攻击阻挠
4. **阶段结束**：
   - 玩家到达 Y≥133.5 后自动触发完成
   - Boss 恢复正常状态并进入 P4 阶段

---

## 九、注意事项

1. **边界保护**：
   - 玩家死亡时立即结束阶段（需在 Phase Control 添加 HERO DIED 全局转换）
   - 超时保护（Boss移动、玩家动画协程均有超时）

2. **性能优化**：
   - 玩家位置检测每帧执行但计算简单（仅Y轴比较）
   - Boss 移动使用平滑插值，避免瞬移

3. **状态恢复**：
   - 阶段结束后确保：
     - Boss Layer 恢复为 11（Enemies）
     - 玩家控制权恢复
     - 所有 FSM 回到正常循环

4. **后续扩展**：
   - 攻击具体实现可根据游戏平衡性调整
   - 可添加更多攻击类型到 `Climb Attack Choice`
   - 可调整漫游速度、攻击频率等参数

---

**最终检查点**：
- 严格遵循「一事件一跳转」原则 ✓
- 所有 FsmString 显式赋 Value ✓
- 完成后调用 InitData() 和 InitEvents() ✓
- 协程异步执行，不阻塞 FSM 主流程 ✓
- 玩家进度监控实时响应 ✓


