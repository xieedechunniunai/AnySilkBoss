# AnySilkBoss 项目结构说明

## 📁 目录结构

```
AnySilkBoss/
│
├── 📄 AnySilkBoss.csproj          # 项目配置文件
├── 📄 AnySilkBoss.sln             # Visual Studio解决方案文件
├── 📄 README.md                   # 项目说明文档
├── 📄 CHANGELOG.md                # 变更日志
├── 📄 LICENSE.md                  # 开源许可证
├── 📄 REFACTORING_NOTES.md        # 重构说明文档
├── 📄 PROJECT_STRUCTURE.md        # 本文件
│
├── 📁 Assets/                     # 资源文件夹
│   └── 📄 AnySilkBoss.dat        # Boss专用存档文件
│
└── 📁 Source/                     # 源代码文件夹
    │
    ├── 📄 Plugin.cs               # ⭐ 主插件入口
    │   └── 功能：
    │       - 初始化mod
    │       - 管理场景切换
    │       - 创建持久化管理器
    │       - 应用Harmony补丁
    │
    ├── 📄 Log.cs                  # 日志系统
    │   └── 功能：封装BepInEx日志功能
    │
    ├── 📄 AssetManager.cs         # ⭐ 资源管理器
    │   └── 功能：
    │       - 加载AssetBundle
    │       - 缓存游戏资源
    │       - 资源生命周期管理
    │
    ├── 📄 PreloadOperation.cs     # 预加载操作
    │   └── 功能：异步存档预加载
    │
    ├── 📁 Behaviours/             # 行为控制器文件夹
    │   │
    │   ├── 📄 BossBehavior.cs           # ⭐ Boss行为基类
    │   │   └── 功能：
    │   │       - 通用Boss行为框架
    │   │       - 阶段管理
    │   │       - FSM修改接口
    │   │       - 辅助工具方法
    │   │
    │   ├── 📄 SingleBossBehavior.cs     # ⭐ 单体Boss行为
    │   │   └── 功能：
    │   │       - 多实体Boss中的单个实体控制
    │   │       - FSM状态修改
    │   │       - 资源实例化管理
    │   │
    │   ├── 📄 SaveSwitchManager.cs      # ⭐ 存档切换管理器
    │   │   └── 功能：
    │   │       - 监听乐器演奏
    │   │       - 自动切换Boss存档
    │   │       - 存档备份和恢复
    │   │       - 重生点管理
    │   │       - 存档槽选择监听
    │   │
    │   ├── 📄 DeathManager.cs           # ⭐ 死亡管理器
    │   │   └── 功能：
    │   │       - 使用Harmony拦截死亡/重生
    │   │       - 提供死亡和重生事件
    │   │       - 事件驱动的通知机制
    │   │
    │   └── 📄 ToolRestoreManager.cs     # 道具恢复管理器
    │       └── 功能：
    │           - 订阅死亡管理器事件
    │           - 自动恢复道具
    │
    └── 📁 Patches/                # 补丁文件夹
        │
        └── 📄 BossPatches.cs            # ⭐ Harmony补丁
            └── 功能：
                - 拦截PlayMakerFSM启动
                - 检测Boss对象
                - 注入自定义行为组件
                - 修改UI文本
                - 允许Boss战中使用乐器
```

## 🎯 核心文件说明

### 1. Plugin.cs
**作用**: Mod的入口点
**关键方法**:
- `Awake()`: 初始化Logger、Harmony、场景监听
- `OnSceneChange()`: 处理场景切换事件
- `CreatePersistentManager()`: 创建持久化GameObject

### 2. BossBehavior.cs
**作用**: Boss行为控制的基类
**可重写方法**:
- `SetupBoss()`: Boss初始化逻辑
- `GetComponents()`: 获取必要组件
- `ModifyParentFsm()`: 修改父FSM
- `IncreaseHealth()`: 调整血量
- `ModifyPhase2/3()`: 各阶段特定修改

**辅助方法**:
- `CreateGradientTexture()`: 创建渐变纹理
- `CreateDamageZone()`: 创建伤害区域

### 3. SingleBossBehavior.cs
**作用**: 单个Boss实体的行为控制（用于多实体Boss）
**可重写方法**:
- `SetupBoss()`: 实体初始化
- `GetComponents()`: 获取组件引用
- `ModifyFsm()`: 修改实体FSM
- `OnPhase2/3()`: 阶段处理

**辅助方法**:
- `CreateCustomObject()`: 创建自定义GameObject
- `ModifyStateActions()`: 修改FSM状态动作

### 4. BossPatches.cs
**作用**: Harmony补丁，用于拦截和修改游戏代码
**补丁方法**:
- `ModifyBoss()`: 在Boss启动时注入行为
- `ChangeBossTitle()`: 修改Boss名称显示
- `CanPlayNeedolinPatch`: 允许Boss战中使用乐器

### 5. SaveSwitchManager.cs
**作用**: 管理Boss专用存档的切换
**关键功能**:
- 监听乐器演奏（3秒触发）
- 自动备份原存档
- 切换到Boss存档
- 场景切换时恢复存档
- 处理存档槽选择

**配置常量**:
```csharp
BOSS_SAVE_FILE = "AnySilkBoss.dat"  // Boss存档文件名
TARGET_SCENE = "Cog_Dancers"         // Boss场景名（需修改）
TARGET_POS_X/Y/Z                      // 重生点坐标（需修改）
```

### 6. DeathManager.cs
**作用**: 使用Harmony补丁拦截游戏的死亡和重生机制
**事件**:
```csharp
OnPlayerDied              // 玩家死亡时触发
OnHazardRespawnStart      // 危险重生开始时触发
OnPlayerFullyRespawned    // 玩家完全重生后触发（已落地）
```

**工作原理**:
- 使用Harmony补丁拦截`HeroController.HazardRespawn()`方法
- 拦截`HeroController.Die()`方法检测死亡
- 拦截`HeroController.StartRebirthRoutine()`作为备用方案
- 提供事件驱动的通知，避免轮询检测

**使用示例**:
```csharp
// 在其他管理器中订阅事件
DeathManager.Instance.OnPlayerFullyRespawned += OnRespawn;

private void OnRespawn()
{
    // 重生后的处理逻辑
    RestoreTools();
}
```

### 7. AssetManager.cs
**作用**: 管理游戏资源的加载和缓存
**配置数组**:
```csharp
_bundleNames  // AssetBundle列表
_assetNames   // 需要加载的资源名称列表（需修改）
```

**关键方法**:
- `Initialize()`: 初始化资源管理器
- `Get<T>()`: 获取已加载的资源
- `ManuallyLoadBundles()`: 手动加载AssetBundle

## 🔧 开发流程

### 步骤1: 准备资源
1. 将Boss专用存档放入 `Assets/AnySilkBoss.dat`
2. 在 `AssetManager.cs` 中添加所需资源名称

### 步骤2: 配置Boss检测
1. 打开 `Source/Patches/BossPatches.cs`
2. 在 `ModifyBoss()` 方法中添加Boss检测逻辑
3. 示例代码已在注释中提供

### 步骤3: 实现Boss行为
**方式A - 直接修改**:
1. 在 `BossBehavior.cs` 中实现行为逻辑
2. 重写虚方法添加自定义功能

**方式B - 继承扩展**:
1. 创建新类继承 `BossBehavior`
2. 重写需要的方法
3. 在 `BossPatches.cs` 中添加新类

### 步骤4: 配置场景信息
1. 打开 `Source/Behaviours/SaveSwitchManager.cs`
2. 修改以下常量：
   ```csharp
   TARGET_SCENE = "你的Boss场景名"
   TARGET_POS_X/Y/Z = 你的重生点坐标
   ```

### 步骤5: 测试和调试
1. 编译项目
2. 将DLL复制到游戏BepInEx插件目录
3. 启动游戏测试
4. 查看BepInEx日志定位问题

## 📝 示例代码参考

所有主要类文件中都包含了详细的注释示例，包括：
- 机枢舞者Boss的实现示例（已注释）
- 常见操作的模板代码
- 最佳实践建议

## 🔍 调试技巧

### 日志输出
```csharp
Log.Info("普通信息");
Log.Warn("警告信息");
Log.Error("错误信息");
Log.Debug("调试信息");
```

### 查看FSM状态
游戏中的PlayMakerFSM包含了大量Boss逻辑，可以通过以下方式查看：
1. 获取FSM引用
2. 遍历 `FsmStates` 查看所有状态
3. 检查每个状态的 `Actions` 和 `Transitions`

### 资源查找
如果不确定资源名称：
1. 在 `AssetManager.cs` 的 `ProcessBundleAssets()` 中添加日志
2. 输出所有找到的资源名称
3. 找到需要的资源后添加到 `_assetNames`

## 🚨 常见问题

**Q: 编译错误 "找不到类型或命名空间"**
A: 检查所有using语句是否正确，命名空间是否已更新为 `AnySilkBoss.Source`

**Q: 存档切换不工作**
A: 
1. 确认在正确的Boss场景中
2. 检查是否演奏乐器至少3秒
3. 查看日志是否有错误信息

**Q: Boss行为没有应用**
A: 
1. 检查 `BossPatches.cs` 中的Boss名称匹配是否正确
2. 确认补丁已正确应用（查看日志）
3. 验证组件是否成功添加到GameObject上

**Q: 资源加载失败**
A:
1. 检查资源名称拼写
2. 确认AssetBundle路径正确
3. 查看 `AssetManager` 的日志输出

## 📚 相关文档

- `README.md` - 项目概述和快速开始
- `REFACTORING_NOTES.md` - 详细的重构说明
- `CHANGELOG.md` - 版本变更历史

---
最后更新: 2025-10-13

