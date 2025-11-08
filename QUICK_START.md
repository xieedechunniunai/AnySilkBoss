# AnySilkBoss 快速入门指南

## 🎯 重构完成！

AnySilkBoss 已经成功重构，采用了更优秀的架构设计！

## 📋 主要改动

### ✅ 已完成的改进

1. **AssetManager 重构为单例模式**
   - 从静态类改为 MonoBehaviour 单例
   - 自动资源管理和验证
   - 更好的生命周期控制

2. **Plugin.cs 架构升级**
   - 创建持久化管理器 `AnySilkBossManager`
   - 统一管理所有组件
   - 清晰的初始化流程

3. **DeathManager 死亡管理器**（全新）
   - 专门检测玩家死亡事件
   - 提供死亡/重生事件回调
   - 支持死亡统计
   - 易于扩展更多死亡相关功能

4. **ToolRestoreManager 重构**
   - 职责更单一，只负责恢复工具
   - 通过事件订阅 DeathManager
   - 支持手动恢复和特定工具恢复（可扩展）

## 🚀 快速使用

### 1. 使用死亡事件系统

```csharp
// 在你的代码中订阅死亡事件
using AnySilkBoss.Source.Behaviours;

// 玩家死亡时
DeathManager.Instance.OnPlayerDeath += () =>
{
    Log.Info("玩家死亡了！");
    // 你的逻辑...
};

// 玩家重生时
DeathManager.Instance.OnPlayerFullyRespawned += () =>
{
    Log.Info("玩家重生了！");
    // 你的逻辑...
};

// 获取死亡次数
int deaths = DeathManager.Instance.GetDeathCount();
```

### 2. 访问资源

```csharp
// 旧方式（已弃用）
// var asset = AssetManager.Get<GameObject>("资源名");

// 新方式
var asset = AssetManager.Instance.Get<GameObject>("资源名");

// 检查是否初始化
if (AssetManager.Instance.IsInitialized())
{
    // 使用资源...
}
```

### 3. 手动恢复工具

```csharp
// 手动触发工具恢复
ToolRestoreManager.Instance.ManualRestoreTools();

// 恢复特定工具（未来可扩展）
ToolRestoreManager.Instance.RestoreSpecificTool("工具名", 数量);
```

## 🔧 扩展新功能

### 添加死亡事件处理

在 `Behaviours` 文件夹创建新的管理器：

```csharp
using AnySilkBoss.Source.Behaviours;
using UnityEngine;

namespace AnySilkBoss.Source.Behaviours
{
    internal class MyCustomManager : MonoBehaviour
    {
        private void Awake()
        {
            // 订阅死亡事件
            DeathManager.Instance.OnPlayerDeath += OnDeath;
            DeathManager.Instance.OnPlayerFullyRespawned += OnRespawn;
        }

        private void OnDestroy()
        {
            // 取消订阅
            if (DeathManager.Instance != null)
            {
                DeathManager.Instance.OnPlayerDeath -= OnDeath;
                DeathManager.Instance.OnPlayerFullyRespawned -= OnRespawn;
            }
        }

        private void OnDeath()
        {
            // 处理死亡
            Log.Info("处理玩家死亡...");
        }

        private void OnRespawn()
        {
            // 处理重生
            Log.Info("处理玩家重生...");
        }
    }
}
```

然后在 `Plugin.cs` 的 `CreateManager()` 方法中添加：

```csharp
// 添加你的自定义管理器
AnySilkBossManager.AddComponent<MyCustomManager>();
```

## 📊 架构图

```
AnySilkBossManager (持久化GameObject)
├── AssetManager           # 资源管理（单例）
├── SaveSwitchManager      # 存档管理（单例）
├── DeathManager           # 死亡检测（单例）
│   ├── OnPlayerDeath      # 事件：玩家死亡
│   ├── OnPlayerRespawn    # 事件：开始重生
│   └── OnPlayerFullyRespawned  # 事件：完全重生
└── ToolRestoreManager     # 工具恢复（单例）
    └── 订阅 DeathManager.OnPlayerFullyRespawned
```

## 🎨 死亡事件扩展示例

### 示例1：死亡音效播放器

```csharp
internal class DeathSoundManager : MonoBehaviour
{
    private void Awake()
    {
        DeathManager.Instance.OnPlayerDeath += PlayDeathSound;
    }

    private void PlayDeathSound()
    {
        // 播放死亡音效
        AudioSource.PlayClipAtPoint(deathSound, transform.position);
    }
}
```

### 示例2：死亡UI显示

```csharp
internal class DeathUIManager : MonoBehaviour
{
    private void Awake()
    {
        DeathManager.Instance.OnPlayerDeath += ShowDeathUI;
        DeathManager.Instance.OnPlayerFullyRespawned += HideDeathUI;
    }

    private void ShowDeathUI()
    {
        // 显示死亡UI
        deathPanel.SetActive(true);
    }

    private void HideDeathUI()
    {
        // 隐藏死亡UI
        deathPanel.SetActive(false);
    }
}
```

### 示例3：死亡统计记录

```csharp
internal class DeathStatsManager : MonoBehaviour
{
    private int totalDeaths = 0;
    private float totalDeathTime = 0f;

    private void Awake()
    {
        DeathManager.Instance.OnPlayerDeath += RecordDeath;
    }

    private void RecordDeath()
    {
        totalDeaths = DeathManager.Instance.GetDeathCount();
        totalDeathTime = DeathManager.Instance.GetLastDeathTime();
        
        Log.Info($"总死亡次数: {totalDeaths}");
        Log.Info($"上次死亡时间: {totalDeathTime}");
    }
}
```

## ⚠️ 注意事项

1. **事件订阅清理**
   - 订阅事件后务必在 `OnDestroy` 中取消订阅
   - 防止内存泄漏和重复调用

2. **单例访问**
   - 使用单例前检查是否为 null
   - 例如：`if (DeathManager.Instance != null)`

3. **初始化时机**
   - 管理器在从主菜单加载存档后创建
   - 如果需要早期访问，使用延迟初始化

4. **Boss场景检测**
   - DeathManager 只在Boss场景激活
   - 通过 `DeathManager.Instance.IsActive()` 检查

## 📚 更多文档

- 详细架构说明：查看 `ARCHITECTURE.md`
- 项目结构：查看 `PROJECT_STRUCTURE.md`
- 重构笔记：查看 `REFACTORING_NOTES.md`

## 🎉 开始使用

现在你可以：
1. ✅ 使用死亡事件系统扩展功能
2. ✅ 通过 AssetManager.Instance 访问资源
3. ✅ 添加新的管理器组件到持久化对象
4. ✅ 享受更清晰的代码架构和更好的可维护性

祝开发顺利！ 🚀

