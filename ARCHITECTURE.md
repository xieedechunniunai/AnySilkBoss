# AnySilkBoss 架构文档

## 概述

本项目采用了更优秀的架构设计，参考了 ReaperBalance 项目的最佳实践。核心思想是使用持久化管理器统一管理所有组件，并采用单例模式和事件驱动设计。

## 架构设计

### 1. 持久化管理器 (AnySilkBossManager)

在游戏从主菜单加载存档时，会创建一个不可销毁的持久化管理器 GameObject：`AnySilkBossManager`

这个管理器包含以下组件：
- **AssetManager**: 资源管理器（单例模式）
- **SaveSwitchManager**: 存档切换管理器
- **DeathManager**: 死亡事件管理器
- **ToolRestoreManager**: 工具恢复管理器

### 2. 组件说明

#### AssetManager（资源管理器）
- **模式**: 单例模式 MonoBehaviour
- **职责**: 
  - 管理所有游戏资源的加载和卸载
  - 自动重新验证资源状态
  - 提供资源获取接口
- **改进点**:
  - 从静态类改为单例模式，生命周期管理更清晰
  - 自动处理场景切换时的资源验证
  - 更好的错误处理和日志记录

**使用示例**:
```csharp
// 获取资源
var asset = AssetManager.Instance.Get<GameObject>("资源名称");

// 检查是否已初始化
if (AssetManager.Instance.IsInitialized())
{
    // 做些什么
}
```

#### DeathManager（死亡管理器）
- **模式**: 单例模式 MonoBehaviour
- **职责**:
  - 检测玩家死亡事件
  - 检测玩家重生事件
  - 提供死亡相关的事件回调
  - 统计死亡次数等数据
- **事件**:
  - `OnPlayerDeath`: 玩家死亡时触发
  - `OnPlayerRespawn`: 玩家开始重生时触发
  - `OnPlayerFullyRespawned`: 玩家完全重生时触发

**使用示例**:
```csharp
// 订阅死亡事件
DeathManager.Instance.OnPlayerDeath += () =>
{
    Log.Info("玩家死了！");
};

// 订阅重生事件
DeathManager.Instance.OnPlayerFullyRespawned += () =>
{
    Log.Info("玩家重生了！");
};

// 获取死亡统计
int deathCount = DeathManager.Instance.GetDeathCount();
```

#### ToolRestoreManager（工具恢复管理器）
- **模式**: 单例模式 MonoBehaviour
- **职责**:
  - 监听死亡管理器的重生事件
  - 在玩家重生后恢复工具
  - 提供手动恢复工具的接口
- **特点**:
  - 职责单一，只负责工具恢复逻辑
  - 通过事件订阅与死亡管理器解耦

**使用示例**:
```csharp
// 手动恢复工具
ToolRestoreManager.Instance.ManualRestoreTools();

// 恢复特定工具（可扩展）
ToolRestoreManager.Instance.RestoreSpecificTool("工具名", 数量);
```

#### SaveSwitchManager（存档切换管理器）
- **模式**: 单例模式 MonoBehaviour
- **职责**:
  - 处理Boss存档和原存档的切换
  - 检测存档槽选择
  - 管理存档备份和恢复
- **特点**:
  - 保持原有功能不变
  - 集成到统一的管理器架构中

### 3. 架构优势

#### 单一职责原则
- 每个管理器只负责一件事
- DeathManager 只负责检测死亡和重生
- ToolRestoreManager 只负责恢复工具
- 代码更易维护和扩展

#### 事件驱动设计
- 组件之间通过事件通信，低耦合
- 易于添加新的死亡事件处理器
- 不需要修改现有代码就能扩展功能

#### 生命周期管理
- 所有管理器都是 MonoBehaviour 组件
- 统一的初始化和销毁流程
- DontDestroyOnLoad 确保跨场景持久化

#### 可扩展性
- 可以轻松添加新的管理器组件
- 死亡事件系统支持多个订阅者
- 工具恢复系统可扩展支持特定工具

### 4. 初始化流程

```
1. 游戏启动
   └─> Plugin.Awake()
       └─> 初始化日志系统
       └─> 应用 Harmony 补丁
       └─> 监听场景切换事件

2. 从主菜单加载存档
   └─> OnSceneChange(Menu_Title -> 游戏场景)
       └─> CreateManager()
           └─> 创建 AnySilkBossManager GameObject
           └─> 添加 AssetManager 组件
               └─> AssetManager.Awake()
                   └─> 初始化资源管理
           └─> 添加 SaveSwitchManager 组件
           └─> 添加 DeathManager 组件
               └─> DeathManager.Awake()
                   └─> 监听场景切换
           └─> 添加 ToolRestoreManager 组件
               └─> ToolRestoreManager.Awake()
                   └─> 订阅 DeathManager 的重生事件

3. 进入 Boss 场景
   └─> DeathManager.OnSceneChanged()
       └─> 检测到 Boss 场景，激活死亡检测
   └─> 开始监听玩家死亡

4. 玩家死亡
   └─> DeathManager 检测到死亡
       └─> 触发 OnPlayerDeath 事件
       └─> 等待重生

5. 玩家重生
   └─> DeathManager 检测到重生完成
       └─> 触发 OnPlayerFullyRespawned 事件
           └─> ToolRestoreManager 接收事件
               └─> 恢复工具和贝壳碎片
```

### 5. 如何扩展

#### 添加新的死亡事件处理器

```csharp
// 在任何地方订阅死亡事件
DeathManager.Instance.OnPlayerDeath += OnPlayerDeathHandler;

private void OnPlayerDeathHandler()
{
    // 处理死亡事件
    // 例如：播放音效、显示UI、记录统计等
}
```

#### 添加新的管理器组件

在 `Plugin.CreateManager()` 中添加：
```csharp
// 添加新的管理器组件
AnySilkBossManager.AddComponent<YourNewManager>();
```

#### 扩展工具恢复功能

在 `ToolRestoreManager` 中实现：
```csharp
public void RestoreSpecificTool(string toolName, int amount = 1)
{
    // 实现特定工具恢复逻辑
}
```

### 6. 注意事项

1. **组件初始化顺序**: 由于组件是按顺序添加的，如果有依赖关系，需要使用延迟订阅（参考 ToolRestoreManager 的实现）

2. **事件订阅清理**: 所有订阅事件的地方都要在 OnDestroy 中取消订阅，防止内存泄漏

3. **单例实例检查**: 使用单例实例前要检查是否为 null

4. **资源访问**: AssetManager 改为单例模式后，使用 `AssetManager.Instance.Get<T>()` 而不是静态方法

5. **Boss 场景检测**: 死亡检测只在 Boss 场景且 Boss 存档已加载时激活

### 7. 与 ReaperBalance 的对比

| 特性 | ReaperBalance | AnySilkBoss |
|------|---------------|-------------|
| 持久化管理器 | ✓ | ✓ |
| AssetManager 单例 | ✓ | ✓ |
| 组件化设计 | ✓ | ✓ |
| 配置系统 | ✓ (BepInEx Config) | ✗ (可扩展) |
| GUI 配置界面 | ✓ | ✗ (可扩展) |
| 死亡事件系统 | ✗ | ✓ |
| 事件驱动架构 | 部分 | ✓ |

### 8. 未来扩展方向

1. **配置系统**: 添加 BepInEx 配置支持，让用户自定义各种参数
2. **GUI 界面**: 添加游戏内配置界面
3. **更多事件**: 扩展死亡事件系统，支持更多游戏事件
4. **统计系统**: 记录更多游戏数据（死亡次数、Boss 战斗时间等）
5. **调试工具**: 添加开发者调试面板

## 总结

新架构采用了现代化的设计模式，具有以下优势：
- **可维护性**: 代码结构清晰，职责分明
- **可扩展性**: 易于添加新功能
- **可靠性**: 更好的错误处理和资源管理
- **性能**: 优化的资源加载和事件系统

这为未来的功能扩展打下了坚实的基础。

