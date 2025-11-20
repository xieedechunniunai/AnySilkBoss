# P6阶段Web攻击实现说明

## 实现概述

在P6阶段添加了特殊的三连Web攻击，每次攻击会在丝线交汇点生成小丝球并延迟0.5秒释放。

## 实现细节

### 1. Phase Control修改 (`PhaseControlBehavior.cs`)

**位置**: `ModifySetP6State()` 方法

**功能**: 在进入P6阶段时，设置`Do P6 Web Attack`标记为true

```csharp
private void ModifySetP6State()
{
    var setP6State = _phaseControl.FsmStates.FirstOrDefault(s => s.Name == "Set P6");
    // 添加SetFsmBool动作，设置Do P6 Web Attack = true到Attack Control FSM
}
```

### 2. Attack Control修改 (`AttackControlBehavior.cs` + `AttackControlBehavior_P6Web.cs`)

#### 2.1 事件注册
- 注册新事件：`P6 WEB ATTACK`
- 在`RegisterAttackControlEvents()`中添加

#### 2.2 Rubble Attack?状态修改
**方法**: `ModifyRubbleAttackForP6Web()`

**功能**: 在Rubble Attack?状态中添加对`Do P6 Web Attack`标记的检测

```csharp
// 在索引2位置插入BoolTest（在Do Phase Roar之后，Can Rubble Attack之前）
actions.Insert(2, new BoolTest
{
    boolVariable = new FsmBool("Do P6 Web Attack"),
    isTrue = _p6WebAttackEvent,
    isFalse = null,
    everyFrame = false
});
```

#### 2.3 P6 Web攻击状态链

创建了6个状态，形成完整的攻击流程：

1. **P6 Web Prepare** - 准备阶段
   - 消耗`Do P6 Web Attack`标记（设为false）
   - 复制原版Web Prepare的准备动作
   - 跳转: ATTACK PREPARED → P6 Web Cast

2. **P6 Web Cast** - 施法动画
   - 直接克隆原版Web Cast的所有动作
   - 跳转: FINISHED → P6 Web Attack 1

3. **P6 Web Attack 1** - 第一根丝网
   - 选择随机Pattern并激活
   - **调用`SpawnSilkBallsAtWebIntersections()`生成小丝球**
   - 等待2秒（延长间隔）
   - 跳转: FINISHED → P6 Web Attack 2

4. **P6 Web Attack 2** - 第二根丝网
   - 同Attack 1
   - 跳转: FINISHED → P6 Web Attack 3

5. **P6 Web Attack 3** - 第三根丝网
   - 同Attack 1
   - 跳转: FINISHED → P6 Web Recover

6. **P6 Web Recover** - 恢复阶段
   - 直接克隆原版Web Recover的所有动作
   - 跳转: FINISHED → Move Restart

### 3. 小丝球生成逻辑

#### 3.1 主流程
**方法**: `SpawnSilkBallsAtWebIntersections()` 和 `SpawnSilkBallsCoroutine()`

**流程**:
1. 获取当前激活的Web Pattern
2. 获取Pattern中所有WebStrand的位置
3. 计算交汇点位置
4. **等待0.5秒**（让丝网先出现）
5. 在每个交汇点上方3单位生成小丝球
6. 小丝球通过`SpawnSilkBall()`自动开始追踪玩家

#### 3.2 交汇点计算算法
**方法**: `CalculateIntersectionPoints()`

**策略**:
- 将所有丝线按X坐标排序
- 计算相邻丝线之间的中点作为交汇点
- 如果有3根以上丝线，额外添加整体中心点

**示例**:
- 3根丝线 → 3个交汇点（2个相邻中点 + 1个中心点）
- 5根丝线 → 5个交汇点（4个相邻中点 + 1个中心点）

#### 3.3 辅助方法
- `GetCurrentActiveWebPattern()` - 获取当前激活的Pattern
- `GetWebStrandPositions()` - 提取Pattern中所有丝线位置

## 关键参数

| 参数 | 值 | 说明 |
|------|-----|------|
| 攻击次数 | 3 | 必定触发三根丝网 |
| 攻击间隔 | 2秒 | 每根丝网之间的延迟 |
| 小丝球生成延迟 | 0.5秒 | 丝网出现后才生成小丝球 |
| 小丝球生成高度 | +3单位 | 相对交汇点 |

## 文件结构

```
Source/
├── Behaviours/
│   ├── AttackControlBehavior.cs (partial class)
│   ├── AttackControlBehavior_P6Web.cs (P6 Web扩展)
│   └── PhaseControlBehavior.cs
└── Managers/
    └── SilkBallManager.cs (使用现有API)
```

## 触发条件

1. Boss进入P6阶段（HP降到P6阈值）
2. Phase Control的Set P6状态自动设置`Do P6 Web Attack = true`
3. 在Idle状态下，ATTACK事件触发进入Rubble Attack?
4. Rubble Attack?检测到`Do P6 Web Attack`为true，触发P6 Web攻击

## 测试要点

1. ✅ P6阶段是否正确设置标记
2. ✅ Rubble Attack?是否正确检测标记
3. ✅ 是否必定触发三根丝网
4. ✅ 丝网间隔是否为2秒
5. ✅ 交汇点计算是否合理
6. ✅ 小丝球是否在丝网出现0.5秒后生成
7. ✅ 小丝球是否正确追踪玩家

## 注意事项

1. **状态-事件-跳转约束**: 每个状态通过唯一事件跳转到下一状态
2. **InitData/InitEvents**: 所有FSM修改后需要重新初始化
3. **FsmString Value**: 所有FsmString必须同时设置名称和值
4. **Partial Class**: AttackControlBehavior使用partial class结构
5. **API兼容**: SpawnSilkBall返回SilkBallBehavior，自动开始追踪

## 已知限制

1. 交汇点计算使用简化算法（相邻中点 + 中心点）
2. 未考虑复杂的丝线交叉情况
3. 小丝球数量取决于Pattern的丝线数量（通常3-5个）

## 未来优化方向

1. 更精确的交汇点算法（考虑丝线角度和实际交叉）
2. 可配置的小丝球参数（加速度、追踪时间等）
3. 根据玩家位置动态调整Pattern选择
4. 小丝球的视觉特效增强

---

**实现时间**: 2024年
**遵循规则**: FSM项目规则标准化（state-action-event.md）
