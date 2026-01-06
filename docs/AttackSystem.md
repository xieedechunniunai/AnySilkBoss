# 记忆版 Boss 出招系统 (MOD 最终版)

> **FSM**: Attack Control | **GameObject**: Silk Boss

---

## 出招流程树

```
Idle
 │ (等待 Attack Time 后触发 ATTACK)
 ▼
Rubble Attack?
 ├─[0] Do Phase Roar = true ──────────────────────▶ Roar Prepare
 ├─[1] Do P6 Web Attack = true ───────────────────▶ P6 Domain Slash (领域次元斩)
 │
 ▼ (否则)
Attack Choice
 ├─[0] SpikeAttackPending = true ─────────────────▶ Spike Trigger
 ├─[1] Has Done Hand Attack = false ──────────────▶ Set Primary Hand (强制首次手部攻击)
 ├─[2] BoolTestMulti (Can Web Strand Attack=true 且 Did Web Strand Attack=false) ▶ Web Prepare
 │
 ├─[3] SendRandomEventV4 (Can Web Strand Attack = true)
 │      │  总权重: 1.0
 │      │
 │      ├─ HAND ATTACK ────── 53.0% (0.53/1.0) ──▶ Set Primary Hand
 │      ├─ DASH ATTACK ────── 10.0% (0.10/1.0) ──▶ DashAttack Prepare
 │      ├─ WEB ATTACK ─────── 12.0% (0.12/1.0) ──▶ Web Prepare
 │      └─ SILK BALL ATTACK ─ 25.0% (0.25/1.0) ──▶ Silk Ball Prepare
 │         eventMax: 3/1/1/2  missedMax: 3/5/5/4
 │
 └─[4] SendRandomEventV4 (始终执行)
        │  总权重: 1.0
        │
        ├─ HAND ATTACK ────── 60.0% (0.6/1.0) ───▶ Set Primary Hand
        ├─ DASH ATTACK ────── 15.0% (0.15/1.0) ──▶ DashAttack Prepare
        └─ SILK BALL ATTACK ─ 25.0% (0.25/1.0) ──▶ Silk Ball Prepare
           eventMax: 2/1/1  missedMax: 2/4/3
```

---

```
Set Primary Hand
 │ (FINISHED)
 ▼
Hand Ptn Choice
 ├─[0] SendEvent (已禁用) ────────────────────────▶ (不执行)
 │
 ├─[1] SendRandomEventV4 (Can Claw Combo = true)
 │      │  总权重: 11.5
 │      │
 │      ├─ PINCER STOMP ────── 8.7% (1/11.5) ────▶ Pincer Stomp
 │      ├─ STOMP SWIPE ─────── 8.7% (1/11.5) ────▶ Stomp Swipe
 │      ├─ SWIPE STOMP ─────── 8.7% (1/11.5) ────▶ Swipe Stomp
 │      ├─ PINCER SWIPE ────── 8.7% (1/11.5) ────▶ Pincer Swipe
 │      ├─ SWIPE STOMP SWIPE ─ 8.7% (1/11.5) ────▶ Swp Stomp Swp
 │      ├─ STOMP SWIPE STOMP ─ 8.7% (1/11.5) ────▶ Stomp Swp Stomp
 │      ├─ ORBIT ATTACK ────── 17.4% (2/11.5) ───▶ Orbit Attack ⭐
 │      ├─ BLAST BURST 1 ───── 13.0% (1.5/11.5) ─▶ Blast Burst 1 Prepare ⭐
 │      ├─ BLAST BURST 2 ───── 8.7% (1/11.5) ────▶ Blast Burst 2 Prepare ⭐
 │      └─ BLAST BURST 3 ───── 8.7% (1/11.5) ────▶ Blast Burst 3 Prepare ⭐
 │         eventMax: 全部为1
 │         missedMax: 7/7/7/3/3/3/4/5/6/6
 │
 └─[2] SendRandomEventV4 (始终执行)
        │  总权重: 9.5
        │
        ├─ PINCER STOMP ────── 10.5% (1/9.5) ────▶ Pincer Stomp
        ├─ STOMP SWIPE ─────── 10.5% (1/9.5) ────▶ Stomp Swipe
        ├─ SWIPE STOMP ─────── 10.5% (1/9.5) ────▶ Swipe Stomp
        ├─ PINCER SWIPE ────── 10.5% (1/9.5) ────▶ Pincer Swipe
        ├─ ORBIT ATTACK ────── 21.1% (2/9.5) ────▶ Orbit Attack ⭐
        ├─ BLAST BURST 1 ───── 15.8% (1.5/9.5) ──▶ Blast Burst 1 Prepare ⭐
        ├─ BLAST BURST 2 ───── 10.5% (1/9.5) ────▶ Blast Burst 2 Prepare ⭐
        └─ BLAST BURST 3 ───── 10.5% (1/9.5) ────▶ Blast Burst 3 Prepare ⭐
           eventMax: 全部为1
           missedMax: 5/5/5/5/4/5/6/6
```

---

```
Silk Ball Prepare
 │
 └─[0] SendRandomEventV4 (始终执行)
        │  总权重: 2.25
        │
        ├─ SILK BALL STATIC ────── 44.4% (1/2.25) ──▶ Silk Ball Ring Prepare
        ├─ SILK BALL DASH ──────── 44.4% (1/2.25) ──▶ Silk Ball Move Prepare
        └─ SILK BALL WITH WEB ──── 11.1% (0.25/2.25) ▶ Silk Ball Web Prepare
           eventMax: 2/2/1  missedMax: 3/3/4
```

---

```
Climb Attack Choice (全局事件 CLIMB PHASE ATTACK 触发)
 │
 └─[0] SendRandomEventV4 (始终执行)
        │  总权重: 2.8
        │
        ├─ CLIMB ORBIT ATTACK ───── 35.7% (1.0/2.8) ─▶ Climb Orbit Attack
        ├─ CLIMB SILK BALL ATTACK ─ 21.4% (0.6/2.8) ─▶ Climb Silk Ball Attack
        └─ CLIMB PIN ATTACK ─────── 42.9% (1.2/2.8) ─▶ Climb Pin Attack
           eventMax: 1/1/1  missedMax: 2/3/2
```

---

## 阶段差异 (Special Attack = true 时)

| 攻击 | 普通模式 | P2模式 (Special Attack = true) |
|:-----|:---------|:-------------------------------|
| ORBIT ATTACK | 双Hand顺时针, 间隔0.5s | Hand L顺时针, Hand R逆时针, 间隔0.4s |
| BLAST BURST 1 | 只使用径向爆发模式 | 50%反向加速度, 50%径向爆发 |
| SILK BALL STATIC | 8个丝球, 随机顺/逆时针 | 内圈6球 + 外圈3球(1.75x大小) |
| SILK BALL DASH | 普通路线: 2个点位 | P2路线: 增加Special点位(3个点位) |

---

## P6 Domain Slash 详情

| 参数 | 值 |
|:-----|:---|
| 初始半径 | 18 |
| 每波缩圈 | 2.5 |
| 最小半径 | 8 |
| 总波次 | 5波 (位置数: 1→3→6→10→15) |

---

## Climb 攻击详情

| 攻击 | 参数 |
|:-----|:-----|
| CLIMB ORBIT | 双Hand 6针, 间隔0.7s |
| CLIMB SILK BALL | 1球(1.2x), 玩家上/下方12单位, 4波旋转丝网, 忽略墙壁, 不可清除 |
| CLIMB PIN | 12根Pin, BOSS上方2单位, 前6根瞄准左/后6根瞄准右, 随机顺序发射 |

---

## 条件变量

| 变量 | 设置时机 | 作用 |
|:-----|:---------|:-----|
| `Can Claw Combo` | P3 | 激活爪击连段池 |
| `Can Web Strand Attack` | P2 (Set P2) | 激活蛛网攻击池 |
| `Did Web Strand Attack` | Web Prepare 状态 | 标记已执行过蛛网攻击（防止连续触发） |
| `Special Attack` | P4 (Set P4) | P2阶段标志 |
| `Do P6 Web Attack` | P6 | 激活P6领域攻击 |
| `SpikeAttackPending` | 各阶段 | 地刺攻击待触发 |
| `Has Done Hand Attack` | 首次手部攻击后 | 强制首次HAND ATTACK |

---

## 参数说明

- **eventMax**: 最多连续触发次数
- **missedMax**: 最多跳过次数，超过后强制触发
- **⭐**: MOD 新增攻击

---

*MOD 最终版 | 2026-01-06*
