# PinArray 大招（P4，HP<400 一次性触发）设计文档

## 0. 范围与目标

- **修改目标**：记忆版 BOSS（Memory），以 `Phase Control` / `Attack Control` / `Hand Control` / `FingerBlade Control` / `Pin Projectile(Control)` 为核心。
- **触发条件**：爬升阶段结束进入 P4 后，Boss HP 降到 400 以下时触发一次（只触发一次）。
- **表现目标**：
  - 从中心点生成 40 根 Pin，先聚点，再展开并旋转扩散，最后 Z 归 0。
  - WaveA（隔一个 Pin）走“砸地链路”：复制原 `Fire/Thunk` 但 **不回收**，落地后恢复参数、抬起、打乱角度并进入等待；之后收到 `ATTACK` 事件时回到原本发射链路，最终 **自动回收**。
  - WaveB（剩余 Pin）在展开后抬起/打乱角度，然后进入逐个发射流程（`DIRECT_FIRE` + 延迟 `ATTACK`）。
  - 6 根 FingerBlade：通过 **全局事件** 进入“移动到槽位 → 锁头瞄准 → 等待攻击事件 → 进入 `Antic Pull`”链路；在 Pin 发射期间按节奏触发 `ATTACK`（或自定义事件）让单根 FingerBlade 出手。

## 1. 建模约束（必须遵守）

- **一事件一跳转**：从状态 A 到状态 B 的跳转必须由唯一事件完成。
- **禁止多事件收敛**：不要让多个不同事件都指向同一目标状态。
- **FINISHED 禁止当作路由**：FINISHED 只能表达该状态瞬发动作完成。

本文档的状态链全部按上述规则拆分。

---

## 2. 坐标体系解释：世界坐标 vs “相对中心点坐标”

你提到“槽位坐标体系不太懂”，这里用最直接的方式解释：

### 2.1 世界坐标（World）
- 直接给一个绝对位置，例如：`(x=40, y=135, z=0)`。
- 优点：简单。
- 缺点：场地中心、Boss 位置、战斗房间不同会导致槽位不对。

### 2.2 相对中心点坐标（推荐）
- 先定义一个大招的中心点 `center`（世界坐标）。
- 槽位位置用偏移量 `offset` 表示，最后转换为世界坐标：

```text
slotWorldPos = center + offset
```

例如要 6 个槽位：

- 上排（Hand L）：
  - 左：`center + (-dx, +dy, 0)`
  - 中：`center + ( 0, +dy, 0)`
  - 右：`center + (+dx, +dy, 0)`
- 下排（Hand R）：
  - 左：`center + (-dx, -dy, 0)`
  - 中：`center + ( 0, -dy, 0)`
  - 右：`center + (+dx, -dy, 0)`

其中 `dx/dy` 是可调参数（例如 dx=6, dy=4）。

优点：中心点变了（比如你想更靠上），槽位会整体跟着走。

---

## 3. 触发点：Phase Control 的 HP Check 4 改造

### 3.1 新增变量
- `Phase Control` FSM：
  - `Bool PinArraySpecialAvailable`（初始 `True`）

### 3.2 新增事件
- `START PIN ARRAY SPECIAL`

### 3.3 状态拆分（示例命名）

#### State：`HP Check 4 (Entry)`
- **Actions**
  - `BoolTest(PinArraySpecialAvailable)`
    - TrueEvent：`PIN_ARRAY_CHECK_HP`
    - FalseEvent：`PIN_ARRAY_SKIP`
- **Transitions**
  - `PIN_ARRAY_CHECK_HP -> HP Check 4 CompareHP400`
  - `PIN_ARRAY_SKIP -> HP Check 4 CompareHP0`

#### State：`HP Check 4 CompareHP400`
- **Actions**
  - `CompareHP integer2=400`
    - `<= -> START PIN ARRAY SPECIAL`
    - `> -> PIN_ARRAY_SKIP`
- **Transitions**
  - `START PIN ARRAY SPECIAL -> PinArray Roar`
  - `PIN_ARRAY_SKIP -> HP Check 4 CompareHP0`

#### State：`HP Check 4 CompareHP0`（保留原逻辑）
- **Actions**
  - `CompareHP integer2=0`
    - `<=0 -> NEXT`
    - `>0 -> FINISHED`
- **Transitions**
  - `NEXT -> Set P5`
  - `FINISHED -> P4`

---

## 4. PinArray 总状态链（Phase Control 内）

1. `PinArray Roar`
2. `PinArray Prepare`
3. `PinArray Spawn+Layout`
4. `PinArray ExpandRotate`
5. `PinArray WaveA Slam Trigger`
6. `PinArray WaveB Lift+Scramble`
7. `PinArray Fire Sequence`
8. `PinArray Cleanup`
9. `PinArray ReturnToP4`

> 说明：WaveA/WaveB 的“后续等待 ATTACK 回到原发射链”主要发生在 **Pin 自己的 FSM**，Phase Control 只负责“下发事件”。

---

## 5. Pin Projectile（Control FSM）补丁设计：WaveA 砸地不回收 + 二段进入原流程

### 5.1 背景：为什么需要新增链路

当前 Pin 的原流程（概念）：

- `Dormant` 收到 `ATTACK` → `Attack Pause` → `Move Pin` → `Antic` → `Thread Pull` → `Fire` → `Thunk` → `Release Pin` → `Dormant`
- 你们的补丁在 `Release Pin` 末尾会回收：`RecyclePinProjectile(pin)`

所以如果 WaveA 也走原 `Fire/Thunk/Release`，就会回收，无法“砸地后抬起再二段”。

### 5.2 新增事件（Pin Control）

- `PINARRAY_SLAM`：进入砸地专用链路（复制 Fire/Thunk）
- `PINARRAY_SCRAMBLE`：打乱角度/准备二段
- `PINARRAY_READY`：进入等待（等待 `ATTACK` 回到原流程）

> `ATTACK` 保持复用：最终二段发射仍靠原 `ATTACK` 进入原发射链。

### 5.3 新增状态链（Pin Control）

#### State：`PinArray Slam Fire`（复制原 Fire 的核心动作）
- **职责**：向下发射（砸地运动），但不要走到回收。
- **建议动作**（尽量贴近原 Fire）：
  - `SetVelocityAsAngle` 或 `SetVelocity2D`（方向向下，速度可配）
  - `ActivateGameObject(Damager)=True`（砸地阶段需要伤害）
  - `ActivateGameObject(Terrain Detector)=True`
  - `Collision2dEvent(OnCollisionEnter2D -> PINARRAY_LAND)`（不要用 THUNK 事件复用，避免和原链路冲突）
- **Transition**
  - `PINARRAY_LAND -> PinArray Slam Thunk`

#### State：`PinArray Slam Thunk`（复制原 Thunk 的表现）
- **职责**：落地特效/短暂停顿。
- **建议动作**：
  - 播放 thunk effect、震屏、短 wait
  - `ActivateGameObject(Damager)=False`（落地后避免持续触发）
- **Transition**
  - `FINISHED -> PinArray Slam Recover`

#### State：`PinArray Slam Recover`
- **职责**：恢复部分参数，准备抬起。
- **建议动作**：
  - 关闭/复位碰撞探测器（Terrain Detector）
  - 复位刚体速度
- **Transition**
  - `FINISHED -> PinArray Lift`

#### State：`PinArray Lift`
- **职责**：向上抬起一点点（你强调要抬起）。
- **建议动作**：
  - `AnimateRigidBody2DPositionTo` 或 `AnimatePositionToV2`（如果 pin 用 transform 驱动也可）
- **Transition**
  - `FINISHED -> PinArray Scramble`

#### State：`PinArray Scramble`
- **职责**：打乱角度并设置二段目标方向（上下互指）。
- **建议动作**：
  - 自定义 Action：根据 `center` 与 pin 当前方位计算目标角度，然后 random 扰动
  - 将计算结果写入 pin 的 `transform.rotation`
- **Transition**
  - `FINISHED -> PinArray Ready`

#### State：`PinArray Ready`
- **职责**：等待二段发射命令。
- **Transition**
  - `ATTACK -> (原 Dormant 的 ATTACK 入口状态)`

> 关键点：这里不要多事件汇聚。`PinArray Ready` 只监听 `ATTACK`，且只跳一个目标状态。

### 5.4 全局转换
- 增加全局转换：
  - `PINARRAY_SLAM -> PinArray Slam Fire`

---

## 6. FingerBlade（Control FSM）补丁设计：全局事件进入“槽位移动→锁头→等待→Antic Pull”

### 6.1 背景：为什么不能只发 `SHOOT Hand X Blade i`

`SHOOT` 事件本质是进入原攻击链的一部分（你已经在 `Shoot` 开头插入 `SpawnAndFirePin`）。
但你当前需求是：
- 先进大招专用位置
- 持续锁头
- 等待“攻击事件”再进入 `Antic Pull`（从而自然进入 `Shoot`）

所以必须新增一条专用链路，而不是直接 `SHOOT`。

### 6.2 新增变量（FingerBlade Control）

对每根 blade，新增（FSM 变量）
- `Vector3 PinArray Slot Target`（槽位目标世界坐标）
  - 由 PhaseControl/HandControl 在大招开始时写入

可选：
- `Bool PinArrayMode`（标记当前是否处于大招锁头模式）

### 6.3 新增事件（FingerBlade Control）

- `PINARRAY_ENTER`：进入大招链
- `PINARRAY_ATTACK`：锁头状态下收到该事件则进入 `Antic Pull`

> `PINARRAY_ATTACK` 不直接跳 `Shoot`，而是跳 `Antic Pull`，保证与原流程一致。

### 6.4 新增状态链（FingerBlade Control）

#### State：`PinArray MoveToSlot`
- **进入方式**：全局转换 `PINARRAY_ENTER -> PinArray MoveToSlot`
- **Actions**：
  - `AnimatePositionToV2`：移动到 `PinArray Slot Target`
    - 说明：`AnimatePositionToV2` 支持 vector 或 xyz 分量。
- **Transition**
  - `FINISHED -> PinArray Aim`

#### State：`PinArray Aim`
- **职责**：持续锁头瞄准玩家。
- **建议复用原版的锁头链（你文件里确认存在）：**
  - `GetAngleToTarget2D(storeAngle=Aim Angle, everyFrame=true)`
  - `FloatAdd(Aim Angle += 180, everyFrame=true)`
  - `FloatClamp(Aim Angle, min/max, everyFrame=true)`（区间沿用 Idle L/R 对应区间）
  - `RotateTo(targetAngle=Aim Angle, speed=360)`
- **Transition**
  - `PINARRAY_ATTACK -> Antic Pull`

> 这里 `PinArray Aim` 不用 FINISHED 出去（它是持续态），只等事件。

### 6.5 退出/收尾

在大招 Cleanup 时，PhaseControl 需要对 6 根 blade 下发：
- `BLADES RETURN` 或你自定义 `PINARRAY_EXIT`（如果要更干净）

---

## 7. Phase Control 在大招中的调度细节

### 7.1 `PinArray Prepare`
- **Actions**
  - `ATTACK STOP`
  - `PinArraySpecialAvailable=false`
  - 初始化中心点 `center`
  - 计算 6 个槽位世界坐标：`slotWorldPos = center + offset`
  - 将每根 blade 的 `PinArray Slot Target` 写入
  - 对每根 blade 发送 `PINARRAY_ENTER`

### 7.2 `PinArray Spawn+Layout`
- Spawn 40 pins，分组：
  - `WaveA`：index%2==0
  - `WaveB`：index%2==1

### 7.3 `PinArray ExpandRotate`
- 旋转展开 + 半径扩大 + Z 插值到 0。

### 7.4 `PinArray WaveA Slam Trigger`
- 对 `WaveA` pins 发送 `PINARRAY_SLAM`（进入它们新增链路：砸地→抬起→打乱→Ready）

### 7.5 `PinArray WaveB Lift+Scramble`
- 对 `WaveB` 做抬起/角度扰动（可在 PhaseControl 做，也可让 Pin FSM 也加类似状态；两种皆可）。

### 7.6 `PinArray Fire Sequence`
- **逐个发射 pins（WaveB 或 All）**：
  - `DIRECT_FIRE` → 延迟 `ATTACK`
- **同时控制 FingerBlade 出手**：
  - 这里不再发送 `SHOOT Hand X Blade i`
  - 而是轮询 6 根 blade 发送 `PINARRAY_ATTACK`，让它们从 `PinArray Aim` → `Antic Pull` → `Shoot`

> Finger 的出手节奏可以是“按总时长 6 等分”，也可以是“每 N 发 pin 触发一次”。本文档只固定“按 6 等分”为目标。

### 7.7 `PinArray Cleanup`
- `ATTACK START`
- 回收所有仍存活 pin（尤其 WaveA 若未进入原 Release 回收链）。
- blade 发送 `BLADES RETURN` 或 `PINARRAY_EXIT`

---

## 8. 建议新增的自定义 Action（职责列表，不涉及实现）

### 8.1 PhaseControl 侧
- `PinArrayComputeSlotsAction`
  - 输入：center, dx, dy
  - 输出：6 个 slot world pos

- `PinArraySpawnPinsAction`
  - 输出：pinsAll、WaveA、WaveB

- `PinArrayExpandRotateAction`
  - 控制阵列旋转/扩散/Z 归零

- `PinArraySequentialFireAction`
  - 逐个对 pin 发送 `DIRECT_FIRE` + 延迟 `ATTACK`

- `PinArrayDispatchFingerAttackAction`
  - 按节奏对 6 根 blade 发送 `PINARRAY_ATTACK`

### 8.2 Pin 侧
- `PinArrayScrambleAngleAction`
  - 计算“指向中心 + random 扰动 + 上下互指”的最终角度并设置 rotation

---

## 9. 你当前需要我进一步确认/补齐的信息（若要进入实现阶段）

- `Pin Projectile(Control)` 原版 `Fire/Thunk/Release Pin` 具体动作列表（尤其是哪些变量/碰撞/特效必须复制），以便你复制链路时不漏关键步骤。
- `FingerBlade Control` 中 `Antic Pull` 对 `Aim Angle/Attack Angle` 的依赖是否会导致角度二次偏移（需要决定在 `PinArray Aim` 中是否预偏移 180）。

---

## 10. 任务完成状态

- 本文档已按你的修正要求：
  - **FingerBlade**：通过全局事件进入“MoveToSlot → Aim → 等待 PINARRAY_ATTACK → Antic Pull”链路。
  - **WaveA Pin**：新增“复制 Fire/Thunk 的砸地链 → Recover → Lift → Scramble → Ready → 接收 ATTACK 回到原链路自动回收”。
  - 补充了“槽位坐标体系”的可理解解释与推荐公式。
