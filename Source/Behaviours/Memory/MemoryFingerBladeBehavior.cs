using System.Collections;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using AnySilkBoss.Source.Managers;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// 梦境版 Finger Blade 控制器
    /// 在普通版基础上，每次 Shoot 时额外发射一个 Pin Projectile
    /// </summary>
    internal class MemoryFingerBladeBehavior : FingerBladeBehavior
    {
        /// <summary>FirstWeaverManager 引用</summary>
        private FirstWeaverManager? _firstWeaverManager;

        /// <summary>Control FSM 引用（从基类获取）</summary>
        private PlayMakerFSM? _controlFSM;

        /// <summary>
        /// 初始化 Memory Finger Blade
        /// </summary>
        public new void Initialize(int index, string handName, HandControlBehavior hand)
        {
            // 调用基类初始化
            base.Initialize(index, handName, hand);

            // 获取 FirstWeaverManager
            _firstWeaverManager = FindFirstObjectByType<FirstWeaverManager>();
            if (_firstWeaverManager == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 FirstWeaverManager，Pin 发射功能将不可用");
            }

            // 获取 Control FSM
            _controlFSM = GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "Control");
            if (_controlFSM == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 Control FSM");
                return;
            }

            // 修改 Shoot 状态，在开头插入 CallMethod 动作
            ModifyShootState();

            Log.Info($"[MemoryFingerBlade {bladeIndex}] 梦境版初始化完成");
        }

        /// <summary>
        /// 修改 Shoot 状态，在开头插入 CallMethod 动作调用 SpawnAndFirePin
        /// </summary>
        private void ModifyShootState()
        {
            if (_controlFSM == null) return;

            // 查找 Shoot 状态
            var shootState = _controlFSM.FsmStates.FirstOrDefault(s => s.Name == "Shoot");
            if (shootState == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 Shoot 状态");
                return;
            }

            // 创建 CallMethod 动作
            var callMethodAction = new CallMethod
            {
                behaviour = this,
                methodName = "SpawnAndFirePin",
                parameters = new FsmVar[0],
                everyFrame = false
            };

            // 在 Actions 数组开头插入
            var actions = shootState.Actions.ToList();
            actions.Insert(0, callMethodAction);
            shootState.Actions = actions.ToArray();

            Log.Info($"[MemoryFingerBlade {bladeIndex}] Shoot 状态已修改，添加 SpawnAndFirePin 动作");
        }

        /// <summary>
        /// 生成并发射 Pin Projectile
        /// 在 Shoot 状态开头被 FSM 调用
        /// </summary>
        public void SpawnAndFirePin()
        {
            if (_firstWeaverManager == null || !_firstWeaverManager.IsInitialized)
            {
                Log.Debug($"[MemoryFingerBlade {bladeIndex}] FirstWeaverManager 未就绪，跳过 Pin 发射");
                return;
            }

            if (_controlFSM == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] Control FSM 为空，无法发射 Pin");
                return;
            }

            // 1. 获取当前位置
            Vector3 pinPosition = transform.position;

            // 2. 获取攻击角度（从 FSM 变量 Attack Angle 获取）
            float attackAngle = 0f;
            var attackAngleVar = _controlFSM.FsmVariables.GetFsmFloat("Attack Angle");
            if (attackAngleVar != null)
            {
                attackAngle = attackAngleVar.Value;
            }
            else
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 未找到 Attack Angle 变量，使用默认角度 0");
            }

            // 3. 从池子获取 Pin
            var pin = _firstWeaverManager.SpawnPinProjectile(pinPosition);
            if (pin == null)
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] 无法获取 Pin Projectile");
                return;
            }

            // 4. 设置 Pin 的旋转角度（Fire 状态会用 GetRotation 获取这个角度）
            pin.transform.rotation = Quaternion.Euler(0f, 0f, attackAngle);

            // 5. 发送 DIRECT_FIRE 事件触发 Pin 进入 Antic 状态
            var pinFsm = pin.LocateMyFSM("Control");
            if (pinFsm != null)
            {
                pinFsm.SendEvent("DIRECT_FIRE");
                // 进入 Antic 后等待 0.5s 再触发 ATTACK 事件，让 FSM 继续攻击流程
                StartCoroutine(DelayedAttack(pinFsm));
                Log.Debug($"[MemoryFingerBlade {bladeIndex}] Pin 已发射，角度: {attackAngle}°，位置: {pinPosition}");
            }
            else
            {
                Log.Warn($"[MemoryFingerBlade {bladeIndex}] Pin 未找到 Control FSM");
            }
        }

        /// <summary>
        /// 延迟发送 ATTACK 事件，驱动 Pin FSM 从 Antic 进入后续攻击
        /// </summary>
        private IEnumerator DelayedAttack(PlayMakerFSM fsm)
        {
            yield return new WaitForSeconds(0.5f);
            if (fsm != null)
            {
                fsm.SendEvent("ATTACK");
            }
        }
    }
}

