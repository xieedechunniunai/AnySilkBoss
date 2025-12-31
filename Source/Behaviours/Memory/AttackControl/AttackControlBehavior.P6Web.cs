using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Behaviours.Common;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    /// <summary>
    /// AttackControlBehavior的P6 领域次元斩扩展部分（partial class）
    /// </summary>
    internal partial class MemoryAttackControlBehavior
    {
        #region P6 领域次元斩

        /// <summary>
        /// Web位置点信息
        /// </summary>
        private class WebSlotInfo
        {
            public Vector3 Position;           // 世界坐标
            public float Angle;                // Z轴旋转角度
            public SingleWebBehavior[] WebPair; // 双web轮换 [0]和[1]
            public int CurrentWebIndex;        // 当前使用哪根 (0或1)

            public WebSlotInfo(Vector3 position, float angle)
            {
                Position = position;
                Angle = angle;
                WebPair = new SingleWebBehavior[2];
                CurrentWebIndex = 0;
            }
        }

        // 配置参数
        private float[] _waveBaseAngles = { 45f, 60f, 30f, 75f, 15f }; // 每波基础角度
        private float _angleRandomRange = 0f;  // 角度随机范围 ±10°
        private float _parallelDistance = 3f;   // 平行间距
        private float _burstDelay = 0.75f;      // 预警延迟
        private float _waveInterval = 0.25f;       // 波次间隔（减少为1秒）

        // 领域配置
        private float _initialRadius = 18f;  // 初始安全半径（世界单位）
        // 5 波攻击，实际会缩圈 4 次：final = initial - shrinkPerWave * 4
        // 目标：最后收缩到 5（与 minRadius 对齐，避免“minRadius 看起来没用”）
        private float _shrinkPerWave = 2.5f; // 18 - 2.5*4 = 8
        private float _minRadius = 8f;        // 最小安全半径（世界单位），同时用于伤害判定与洞大小

        // 位置管理器
        private List<WebSlotInfo> _webSlots = new List<WebSlotInfo>();
        
        // P6 Domain Slash 完成事件（不使用 FINISHED）
        private FsmEvent? _p6DomainSlashDoneEvent;

        /// <summary>
        /// 修改Rubble Attack?状态，添加P6 Web Attack监听
        /// </summary>
        private void ModifyRubbleAttackForP6Web()
        {
            if (_attackControlFsm == null) return;

            var rubbleAttackState = _rubbleAttackQuestionState;
            if (rubbleAttackState == null)
            {
                Log.Warn("未找到Rubble Attack?状态，无法添加P6 Web Attack监听");
                return;
            }

            var actions = rubbleAttackState.Actions.ToList();

            // 在第2个Action（检查Do Phase Roar）之后插入P6 Web Attack检查
            actions.Insert(2, new BoolTest
            {
                boolVariable = _attackControlFsm.FsmVariables.BoolVariables.ToList().FirstOrDefault(v => v.Name == "Do P6 Web Attack"),
                isTrue = _p6WebAttackEvent,
                isFalse = FsmEvent.GetFsmEvent("NULL"),
                everyFrame = false
            });

            rubbleAttackState.Actions = actions.ToArray();

            // 使用 AddTransition 添加跳转
            if (_p6DomainSlashState != null)
            {
                AddTransition(rubbleAttackState, CreateTransition(_p6WebAttackEvent!, _p6DomainSlashState));
            }

            Log.Info("Rubble Attack?状态已添加P6 Web Attack监听");
        }

        /// <summary>
        /// 创建P6 领域次元斩状态
        /// </summary>
        private void CreateP6WebAttackStates()
        {
            Log.Info("=== 开始创建P6 领域次元斩状态 ===");

            // 注册 P6 Domain Slash 完成事件（不使用 FINISHED）
            RegisterEvents(_attackControlFsm!, "P6 DOMAIN SLASH DONE");
            _p6DomainSlashDoneEvent = FsmEvent.GetFsmEvent("P6 DOMAIN SLASH DONE");

            // 创建单个状态
            var domainSlashState = CreateState(_attackControlFsm!.Fsm, "P6 Domain Slash", "P6领域次元斩：5波递增攻击+领域结界");
            AddStateToFsm(_attackControlFsm, domainSlashState);
            _p6DomainSlashState = domainSlashState;

            // 设置状态动作：调用协程方法
            domainSlashState.Actions = new FsmStateAction[]
            {
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("ExecuteDomainSlash") { Value = "ExecuteDomainSlash" },
                    parameters = new FsmVar[0],
                    everyFrame = false
                }
            };

            // 设置状态转换：使用自定义事件 P6 DOMAIN SLASH DONE 跳转到 Move Restart
            if (_moveRestartState != null && _p6DomainSlashDoneEvent != null)
            {
                domainSlashState.Transitions = new FsmTransition[]
                {
                    CreateTransition(_p6DomainSlashDoneEvent, _moveRestartState)
                };
            }

            Log.Info("=== P6 领域次元斩状态创建完成 ===");
        }

        /// <summary>
        /// 执行领域次元斩（协程方法）
        /// </summary>
        public void ExecuteDomainSlash()
        {
            StartCoroutine(ExecuteDomainSlashCoroutine());
        }

        /// <summary>
        /// 领域次元斩主协程
        /// </summary>
        private IEnumerator ExecuteDomainSlashCoroutine()
        {
            Log.Info("=== 开始执行P6 领域次元斩 ===");

            // 0. 消耗标记（设置为false）
            if (_attackControlFsm != null)
            {
                var doP6WebAttackVar = _attackControlFsm.FsmVariables.FindFsmBool("Do P6 Web Attack");
                if (doP6WebAttackVar != null)
                {
                    doP6WebAttackVar.Value = false;
                }
            }

            // 1. 准备阶段：Boss无敌 + 领域激活
            SetBossInvincible(true);
            
            if (_domainBehavior != null && gameObject != null)
            {
                _domainBehavior.ActivateDomain(transform.position, _initialRadius);
            }
            
            yield return new WaitForSeconds(0.5f);

            // 清空位置记录
            _webSlots.Clear();

            float currentRadius = _initialRadius;

            // 2. 5波攻击循环
            for (int wave = 1; wave <= 5; wave++)
            {
                Log.Info($"=== 第{wave}波攻击开始 ===");

                // 执行本波web攻击
                yield return StartCoroutine(ExecuteWave(wave));

                // 缩圈（最后一波不缩）
                if (wave < 5 && _domainBehavior != null)
                {
                    currentRadius -= _shrinkPerWave;
                    currentRadius = Mathf.Max(currentRadius, _minRadius);
                    _domainBehavior.ShrinkDomain(currentRadius, 0.5f);
                    yield return new WaitForSeconds(_waveInterval);
                }
            }

            // 3. 结束阶段：清理所有web和领域
            yield return new WaitForSeconds(0.5f);
            
            // 清理所有web
            CleanupAllWebs();
            
            // 停用领域
            if (_domainBehavior != null)
            {
                _domainBehavior.DeactivateDomain();
            }
            
            // 恢复Boss无敌状态
            SetBossInvincible(false);

            Log.Info("=== P6 领域次元斩执行完成 ===");

            // 发送 P6 DOMAIN SLASH DONE 事件（不使用 FINISHED）
            if (_attackControlFsm != null)
            {
                _attackControlFsm.SendEvent("P6 DOMAIN SLASH DONE");
            }
        }

        /// <summary>
        /// 清理所有web
        /// </summary>
        private void CleanupAllWebs()
        {
            Log.Info("=== 开始清理所有P6 Web ===");
            
            int cleanedCount = 0;
            foreach (var slot in _webSlots)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (slot.WebPair[i] != null)
                    {
                        slot.WebPair[i].StopAttack();
                        slot.WebPair[i].ResetCooldown();
                        cleanedCount++;
                    }
                }
            }
            
            // 清空位置记录
            _webSlots.Clear();
            
            Log.Info($"已清理 {cleanedCount} 根web");
        }

        /// <summary>
        /// 执行单波攻击
        /// </summary>
        private IEnumerator ExecuteWave(int waveIndex)
        {
            // 第N波需要的位置数 = N*(N+1)/2（三角数）
            int totalSlotsNeeded = waveIndex * (waveIndex + 1) / 2;
            int newSlotsNeeded = totalSlotsNeeded - _webSlots.Count;

            Log.Info($"第{waveIndex}波：需要{totalSlotsNeeded}个位置，新增{newSlotsNeeded}个位置");

            // 获取玩家位置
            var heroController = FindFirstObjectByType<HeroController>();
            Vector3 playerPos = heroController != null ? heroController.transform.position : transform.position;

            // 创建新位置
            for (int i = 0; i < newSlotsNeeded; i++)
            {
                Vector3 newPos = CalculateNewSlotPosition(playerPos, waveIndex);
                float angle = CalculateSlotAngle(waveIndex);
                _webSlots.Add(new WebSlotInfo(newPos, angle));
            }

            // 激活所有位置的web攻击
            List<Coroutine> attackCoroutines = new List<Coroutine>();
            for (int i = 0; i < totalSlotsNeeded && i < _webSlots.Count; i++)
            {
                var slot = _webSlots[i];
                var coroutine = StartCoroutine(ActivateSlotWeb(slot));
                attackCoroutines.Add(coroutine);
            }

            // 等待所有web攻击完成（预警延迟 + 攻击持续时间）
            yield return new WaitForSeconds(_burstDelay + 1f);
        }

        /// <summary>
        /// 计算新位置点（平行扩展逻辑）
        /// </summary>
        private Vector3 CalculateNewSlotPosition(Vector3 playerPos, int waveIndex)
        {
            if (_webSlots.Count == 0)
            {
                // 第一个位置：玩家位置
                return playerPos;
            }

            // 找到最接近玩家的已存在位置
            WebSlotInfo? closestSlot = null;
            float minDist = float.MaxValue;
            foreach (var slot in _webSlots)
            {
                float dist = Vector2.Distance(new Vector2(slot.Position.x, slot.Position.y), 
                                             new Vector2(playerPos.x, playerPos.y));
                if (dist < minDist)
                {
                    minDist = dist;
                    closestSlot = slot;
                }
            }

            if (closestSlot == null)
            {
                return playerPos;
            }

            // 计算从最近位置到玩家的方向（垂直于web角度方向）
            Vector2 toPlayer = new Vector2(playerPos.x - closestSlot.Position.x, 
                                          playerPos.y - closestSlot.Position.y);
            
            // 垂直于web角度的方向
            float webAngleRad = closestSlot.Angle * Mathf.Deg2Rad;
            Vector2 webDir = new Vector2(Mathf.Cos(webAngleRad), Mathf.Sin(webAngleRad));
            Vector2 perpendicularDir = new Vector2(-webDir.y, webDir.x).normalized;

            // 确定扩展方向（向玩家方向）
            if (Vector2.Dot(perpendicularDir, toPlayer.normalized) < 0)
            {
                perpendicularDir = -perpendicularDir;
            }

            // 从最近位置开始，沿垂直方向每隔parallelDistance检查是否有空位
            Vector3 candidatePos = closestSlot.Position;
            for (int step = 1; step <= 10; step++) // 最多检查10步
            {
                candidatePos = closestSlot.Position + new Vector3(
                    perpendicularDir.x * _parallelDistance * step,
                    perpendicularDir.y * _parallelDistance * step,
                    0
                );

                // 检查是否与已有位置太近
                bool tooClose = false;
                foreach (var existingSlot in _webSlots)
                {
                    float dist = Vector2.Distance(
                        new Vector2(candidatePos.x, candidatePos.y),
                        new Vector2(existingSlot.Position.x, existingSlot.Position.y)
                    );
                    if (dist < _parallelDistance * 0.5f)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    return candidatePos;
                }
            }

            // 如果找不到合适位置，返回玩家位置
            return playerPos;
        }

        /// <summary>
        /// 计算位置的角度
        /// </summary>
        private float CalculateSlotAngle(int waveIndex)
        {
            if (waveIndex < 1 || waveIndex > _waveBaseAngles.Length)
            {
                return 45f;
            }

            float baseAngle = _waveBaseAngles[waveIndex - 1];
            float randomOffset = Random.Range(-_angleRandomRange, _angleRandomRange);
            return baseAngle + randomOffset;
        }

        /// <summary>
        /// 激活单个位置的web攻击（双web轮换）
        /// </summary>
        private IEnumerator ActivateSlotWeb(WebSlotInfo slot)
        {
            if (_singleWebManager == null)
            {
                Log.Warn("SingleWebManager未找到，无法激活web");
                yield break;
            }

            // 选择当前使用的web索引
            int webIndex = slot.CurrentWebIndex;
            
            // 如果当前web不可用，尝试切换到另一根
            if (slot.WebPair[webIndex] != null && !slot.WebPair[webIndex].IsAvailable)
            {
                webIndex = 1 - webIndex;
            }

            // 如果两根都不可用，创建新的
            if (slot.WebPair[webIndex] == null || !slot.WebPair[webIndex].IsAvailable)
            {
                var newWeb = _singleWebManager.SpawnAndAttack(
                    slot.Position,
                    new Vector3(0f, 0f, slot.Angle),
                    new Vector3(3.2f, 1f, 1f),
                    0f,
                    _burstDelay
                );

                if (newWeb != null)
                {
                    slot.WebPair[webIndex] = newWeb;
                    slot.CurrentWebIndex = webIndex;
                }
            }
            else
            {
                // 使用已有的web
                slot.WebPair[webIndex].transform.position = slot.Position;
                slot.WebPair[webIndex].transform.eulerAngles = new Vector3(0f, 0f, slot.Angle);
                slot.WebPair[webIndex].TriggerAttack(0f, _burstDelay);
            }

            // 下次使用另一根
            slot.CurrentWebIndex = 1 - slot.CurrentWebIndex;
        }

        /// <summary>
        /// 设置Boss无敌状态
        /// </summary>
        private void SetBossInvincible(bool invincible)
        {
            if (gameObject == null) return;

            // 直接设置GameObject的Layer
            gameObject.layer = invincible ? 2 : LayerMask.NameToLayer("Enemies");  // Layer 2 = Invincible
        }

        #endregion
    }
}
