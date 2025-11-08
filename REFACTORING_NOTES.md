# AnySilkBoss 项目重构说明

## 概述

本项目已从特定的GodDance Boss Mod重构为通用的Boss Mod开发框架。

## 主要变更

### 1. 项目重命名
- **项目名称**: GodDance → AnySilkBoss
- **命名空间**: `GodDance.Source` → `AnySilkBoss.Source`
- **GUID**: `GodDance` → `AnySilkBoss`
- **版本**: 重置为 0.1.0

### 2. 文件重命名和重构

#### 核心行为类
- `Source/Behaviours/GodDance.cs` → `Source/Behaviours/BossBehavior.cs`
  - 移除了机枢舞者特定的实现细节
  - 保留了通用的Boss行为框架
  - 添加了大量注释和示例代码

- `Source/Behaviours/singleGodDance.cs` → `Source/Behaviours/SingleBossBehavior.cs`
  - 简化为通用的单体Boss行为基类
  - 移除了866行的机枢舞者特定代码
  - 保留了可扩展的框架结构

#### 补丁系统
- `Source/Patches/GodDancePatches.cs` → `Source/Patches/BossPatches.cs`
  - 将机枢舞者特定的补丁代码注释掉作为示例
  - 提供了清晰的模板供开发者使用

#### 资源文件
- `Assets/GodDance.dat` → `Assets/AnySilkBoss.dat`
  - 存档文件已重命名

### 3. 代码清理

#### 移除的特定实现
以下机枢舞者特定的功能已被移除或转为注释示例：
- 阶段特定的攻击模式修改
- 自定义伤害区域创建
- 隐身机制
- 特定的FSM状态修改
- 硬编码的Boss属性值

#### 保留的通用框架
- 存档切换系统（SaveSwitchManager）
- 道具恢复系统（ToolRestoreManager）
- 资源管理系统（AssetManager）
- 日志系统（Log）
- 预加载操作（PreloadOperation）
- 基础Boss行为控制器框架

### 4. 文档更新

#### README.md
- 完全重写，专注于框架使用说明
- 添加了详细的开发指南
- 提供了代码结构说明
- 包含使用示例和故障排除

#### CHANGELOG.md
- 记录了重构历史
- 保留了之前GodDance版本的历史记录

### 5. 配置更新

#### AnySilkBoss.csproj
```xml
<BepInExPluginGuid>AnySilkBoss</BepInExPluginGuid>
<BepInExPluginName>AnySilkBoss</BepInExPluginName>
<Version>0.1.0</Version>
```

#### 持久化对象名称
- `GodDancePersistentManager` → `AnySilkBossPersistentManager`

### 6. 代码注释

所有主要类和方法都添加了详细的中英文注释，包括：
- 类的用途说明
- 方法的功能描述
- 使用示例
- 可扩展点的说明

## 如何使用新框架

### 1. 基本步骤

1. **准备Boss存档**
   - 替换 `Assets/AnySilkBoss.dat` 为你的Boss专用存档

2. **配置Boss检测**
   - 编辑 `Source/Patches/BossPatches.cs`
   - 取消注释示例代码或添加新的Boss匹配逻辑

3. **实现Boss行为**
   - 继承 `BossBehavior` 或 `SingleBossBehavior` 类
   - 重写虚方法以实现自定义行为
   - 或直接在现有类中添加逻辑

4. **配置场景信息**
   - 更新 `SaveSwitchManager.cs` 中的场景名称和重生点

5. **添加所需资源**
   - 在 `AssetManager.cs` 中添加资源名称

### 2. 示例代码位置

框架中保留了机枢舞者的实现作为注释示例：
- `BossPatches.cs`: 第24-51行（Boss检测）
- `BossPatches.cs`: 第57-80行（标题修改）
- `BossBehavior.cs`: 整个文件包含示例注释
- `SingleBossBehavior.cs`: 整个文件包含示例注释

## 技术改进

### 1. 代码组织
- 更清晰的类职责划分
- 更好的命名约定
- 统一的注释风格

### 2. 可扩展性
- 虚方法允许子类重写
- 保护级别的成员供派生类使用
- 辅助方法简化常见操作

### 3. 可维护性
- 移除硬编码值
- 添加常量配置
- 详细的日志输出

## 兼容性说明

### 破坏性变更
- 所有命名空间已更改
- 类名已更改
- GUID已更改
- 这是一个**完全不兼容**的版本，不能作为GodDance mod的更新

### 迁移指南
如果你想基于新框架重新实现GodDance：
1. 从注释中的示例代码开始
2. 取消注释相关的Boss检测代码
3. 根据需要实现特定的Boss行为
4. 使用原始的GodDance.dat存档文件（重命名为AnySilkBoss.dat）

## 文件清单

### 已删除
- `Source/Behaviours/GodDance.cs`
- `Source/Behaviours/singleGodDance.cs`
- `Source/Patches/GodDancePatches.cs`
- `bin/` (编译输出)
- `obj/` (编译缓存)

### 已创建
- `Source/Behaviours/BossBehavior.cs`
- `Source/Behaviours/SingleBossBehavior.cs`
- `Source/Patches/BossPatches.cs`
- `REFACTORING_NOTES.md` (本文件)

### 已修改
- `AnySilkBoss.csproj`
- `README.md`
- `CHANGELOG.md`
- `Source/Plugin.cs`
- `Source/Log.cs`
- `Source/AssetManager.cs`
- `Source/PreloadOperation.cs`
- `Source/Behaviours/SaveSwitchManager.cs`
- `Source/Behaviours/ToolRestoreManager.cs`
- `Assets/GodDance.dat` → `Assets/AnySilkBoss.dat`

## 后续建议

### 对于使用者
1. 阅读 README.md 了解框架使用方法
2. 查看注释示例代码
3. 从简单的Boss行为修改开始
4. 逐步添加复杂功能

### 对于贡献者
1. 保持代码通用性，避免特定Boss的硬编码
2. 添加更多辅助方法和工具类
3. 完善文档和示例
4. 考虑添加更多Boss行为模板

## 总结

本次重构将一个特定的Boss Mod转变为一个通用的开发框架，大大降低了开发新Boss Mod的门槛。通过保留完整的基础设施和提供清晰的示例，开发者可以专注于Boss行为的创意实现，而不需要从头构建存档管理、资源加载等基础功能。

---
重构完成日期: 2025-10-13

