# AnySilkBoss

https://github.com/xieedechunniunai/AnySilkBoss

[English](#english) | [中文](#中文)

---

## English

A BepInEx mod for Silksong that enhances SilkBoss behaviors and adds a Memory Mode.

### Features

#### Dual Mode System
- **Normal Mode**: Enhanced version based on the original Boss behavior
- **Memory Mode**: A newly designed high-difficulty Boss fight experience with more complex attack patterns and phase transitions

#### Core Features
- Custom Boss attack control (Orbit, Climb, BlastBurst, P6Web, etc.)
- Phase control system
- Silk Ball system (SilkBall / BigSilkBall)
- Single Web attack
- Damage reduction system
- Damage stack mechanism
- Custom audio replacement
- Platform state reset
- Multi-language support (I18n)

---

### ⚡ How to Enter Memory Mode ⚡

> **This is the key feature of this mod!**

| Step | Action |
|------|--------|
| 1 | Use a **Act 3** save file |
| 2 | Enter the `Cradle_03_Destroyed` room |
| 3 | Hold **Elegy of the Deep** (Down + Needolin) for **3 seconds** |
| 4 | You will be transported to the Memory Mode boss fight |

```
Controls: ↓ + Needolin (Hold for 3 seconds)
```

---

### Installation

#### Requirements
- Silksong
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx)

#### Steps
1. Make sure BepInEx is properly installed
2. Download the latest `AnySilkBoss.zip`
3. Extract to `Silksong/BepInEx/plugins/` directory

### ⚠️ Disclaimer & License

- This mod is **completely free** and will never be sold
- All source code is **open source**
- The music used is from "Intro". If there is any copyright infringement, please contact us and it will be removed immediately
- **Resale, commercial use, and unauthorized redistribution are strictly prohibited**
- **Porting to other platforms without permission is prohibited**

For full license details, see [LICENSE.md](LICENSE.md)

---

## 中文

一个用于 Silksong（空洞骑士：丝之歌）的 BepInEx MOD，提供 SilkBoss 行为增强与记忆模式。

### 功能特性

#### 双模式系统
- **普通模式 (Normal)**：基于原版 Boss 行为的增强版本
- **记忆模式 (Memory)**：全新设计的高难度 Boss 战体验，包含更复杂的攻击模式和阶段转换

#### 核心功能
- 自定义 Boss 攻击控制（Orbit、Climb、BlastBurst、P6Web 等）
- 阶段控制系统（PhaseControl）
- 丝球系统（SilkBall / BigSilkBall）
- 单根丝线攻击（SingleWeb）
- 减伤系统（DamageReduction）
- 伤害叠加机制（DamageStack）
- 自定义音频替换
- 平台状态重置
- 多语言支持（I18n）

---

### ⚡ 如何进入记忆模式 ⚡

> **这是本 MOD 的重要玩法！**

| 步骤 | 操作 |
|------|------|
| 1 | 使用**第三幕**的存档 |
| 2 | 进入 `Cradle_03_Destroyed` 房间 |
| 3 | 按住**深渊挽歌**（↓ + 弹琴）持续 **3 秒** |
| 4 | 即可传送至记忆模式 Boss 战 |

```
操作方式：↓ + 弹琴（持续按住 3 秒）
```

---

### 安装

#### 前置要求
- Silksong 游戏本体
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx)

#### 安装步骤
1. 确保已正确安装 BepInEx
2. 下载最新版本的 `AnySilkBoss.zip`
3. 解压到 `Silksong/BepInEx/plugins/` 目录

### ⚠️ 免责声明与许可

- 本 MOD **完全免费**，不会以任何形式收费
- 源码**全部开源**
- 所使用音乐取自《Intro》，若有侵权敬请告知，将立即删除
- **严禁倒卖、商业用途、未经授权的转载分发**
- **未经许可禁止移植到其他平台**

完整许可证详见 [LICENSE.md](LICENSE.md)

---

## Development / 开发

### Build Requirements / 构建要求
- .NET Standard 2.1
- Visual Studio 2022 or Rider

### Build Steps / 构建步骤
1. Clone the repository / 克隆仓库
2. Create `SilksongPath.props` file / 创建 `SilksongPath.props` 文件：
   ```xml
   <Project>
     <PropertyGroup>
       <SilksongFolder>Your game installation path / 你的游戏安装路径</SilksongFolder>
     </PropertyGroup>
   </Project>
   ```
3. Run `dotnet build`

### Project Structure / 项目结构
```
Source/
├── Actions/        # Custom PlayMaker Actions / 自定义 PlayMaker Actions
├── Behaviours/     # Boss behavior logic / Boss 行为逻辑
│   ├── Common/     # Common behaviors / 通用行为
│   ├── Memory/     # Memory mode behaviors / 记忆模式行为
│   └── Normal/     # Normal mode behaviors / 普通模式行为
├── Handlers/       # Event handlers / 事件处理器
├── Managers/       # Various managers / 各类管理器
├── Patches/        # Harmony patches / Harmony 补丁
└── Tools/          # Utilities (FSM analyzer, state builder, etc.) / 工具类
```
