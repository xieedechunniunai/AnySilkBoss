# P6 领域次元斩 - 状态与跳转流程

## 状态机流程

```
Phase Control (Set P6状态)
    ↓
设置 "Do P6 Web Attack" = True
    ↓
Attack Control (Rubble Attack?状态)
    ↓
检测 "Do P6 Web Attack" == True?
    ├─ True  → P6 WEB ATTACK事件 → P6 Domain Slash状态
    └─ False → 继续其他攻击选择
    ↓
P6 Domain Slash状态
    ├─ 执行ExecuteDomainSlash()协程
    │   ├─ 消耗标记（设置"Do P6 Web Attack" = False）
    │   ├─ Boss无敌（Layer = 2）
    │   ├─ 激活领域结界（初始半径12）
    │   ├─ 第1波攻击（1条web）
    │   ├─ 缩圈（12 → 10.5）+ 等待1秒
    │   ├─ 第2波攻击（3条web）
    │   ├─ 缩圈（10.5 → 9）+ 等待1秒
    │   ├─ 第3波攻击（6条web）
    │   ├─ 缩圈（9 → 7.5）+ 等待1秒
    │   ├─ 第4波攻击（10条web）
    │   ├─ 缩圈（7.5 → 6）+ 等待1秒
    │   ├─ 第5波攻击（15条web）
    │   ├─ 清理所有web
    │   ├─ 停用领域结界
    │   ├─ 恢复Boss无敌状态（Layer = 0）
    │   └─ 发送P6 DOMAIN SLASH DONE事件
    ↓
P6 DOMAIN SLASH DONE事件（不使用FINISHED）
    ↓
Move Restart状态
```

## 状态列表

### Attack Control FSM

| 状态名称 | 描述 | 跳转条件 | 目标状态 |
|---------|------|---------|---------|
| **Rubble Attack?** | 攻击选择状态 | `Do P6 Web Attack == True` | → P6 Domain Slash |
| | | `Do P6 Web Attack == False` | → 其他攻击选择 |
| **P6 Domain Slash** | P6领域次元斩主状态 | `P6 DOMAIN SLASH DONE` | → Move Restart |

### Phase Control FSM

| 状态名称 | 描述 | 动作 |
|---------|------|------|
| **Set P6** | P6阶段设置 | 设置 `Attack Control.Do P6 Web Attack = True` |

## 关键变量

### Attack Control FSM变量

- **`Do P6 Web Attack`** (Bool)
  - 用途：标记是否应该执行P6 Web攻击
  - 设置位置：Phase Control的Set P6状态
  - 消耗位置：P6 Domain Slash状态开始时

## 配置参数

### Web配置
- **池子容量**: 70根（MEMORY_MIN_POOL_SIZE = 70）
- **Web长度**: 2.5倍（X缩放 = 5f）
- **预警延迟**: 0.75秒
- **攻击间隔**: 1秒（波次间隔）

### 领域配置
- **初始半径**: 12
- **每波缩小**: 1.5
- **最小半径**: 4.5
- **伤害间隔**: 1秒
- **伤害量**: 1

## 清理机制

### Web清理
- 位置：`ExecuteDomainSlashCoroutine()`结束阶段
- 方法：`CleanupAllWebs()`
- 操作：
  - 停止所有web攻击
  - 重置所有web冷却
  - 清空位置记录列表

### 领域清理
- 位置：`ExecuteDomainSlashCoroutine()`结束阶段
- 方法：`DomainBehavior.DeactivateDomain()`
- 操作：
  - 淡出圆形遮罩
  - 停止伤害检测

### Boss无敌状态恢复
- 位置：`ExecuteDomainSlashCoroutine()`结束阶段
- 方法：`SetBossInvincible(false)`
- 操作：将Boss Layer从2（Invincible）恢复为0（Default）

## 实现细节

### 状态创建顺序
1. `CreateP6WebAttackStates()` - 创建P6 Domain Slash状态
2. `ModifyRubbleAttackForP6Web()` - 修改Rubble Attack?状态添加检测

### 事件流程
1. Phase Control设置变量 → `Do P6 Web Attack = True`
2. Attack Control检测变量 → 触发`P6 WEB ATTACK`事件
3. 跳转到P6 Domain Slash状态
4. 执行协程，消耗标记
5. 完成后发送`P6 DOMAIN SLASH DONE`事件，回到Move Restart

### 领域结界实现
- 不再依赖外部Shader（CircleMaskShader）
- 使用运行时生成的圆形遮罩Sprite
- 圆内透明，圆外黑色
- 支持淡入淡出动画
- 支持缩圈动画



