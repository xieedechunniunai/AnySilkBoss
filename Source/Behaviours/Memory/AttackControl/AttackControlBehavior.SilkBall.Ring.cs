using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Tools;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    internal partial class MemoryAttackControlBehavior
    {
        #region 丝球环绕攻击方法
        public IEnumerator SummonSilkBallsAtHighPointCoroutine()
        {
            var specialAttackVar = _attackControlFsm?.FsmVariables.FindFsmBool("Special Attack");
            bool isPhase2 = specialAttackVar != null && specialAttackVar.Value;

            if (isPhase2)
            {
                yield return SummonPhase2DoubleSilkBalls();
            }
            else
            {
                yield return SummonNormalSilkBalls();
            }
        }

        private IEnumerator SummonNormalSilkBalls()
        {
            // 随机决定顺时针或逆时针
            bool clockwise = Random.value < 0.5f;
            Log.Info($"=== 开始召唤普通版8个丝球（{(clockwise ? "顺时针" : "逆时针")}）===");
            _activeSilkBalls.Clear();
            Vector3 bossPosition = transform.position;
            float radius = 6f;

            for (int i = 0; i < 8; i++)
            {
                // 顺时针：0°, -45°, -90°... 逆时针：0°, 45°, 90°...
                float angle = clockwise ? -i * 45f : i * 45f;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(
                    Mathf.Cos(radians) * radius,
                    Mathf.Sin(radians) * radius,
                    0f
                );
                Vector3 spawnPosition = bossPosition + offset;
                spawnPosition.z = 0f;
                var behavior = _silkBallManager?.SpawnSilkBall(spawnPosition, 30f, 25f, 8f, 1f, true);
                if (behavior != null)
                {
                    _activeSilkBalls.Add(behavior.gameObject);
                    Log.Info($"召唤第 {i + 1} 个丝球（{angle}°）");
                }

                yield return new WaitForSeconds(0.1f);
            }

            Log.Info($"=== 8个丝球召唤完成，共 {_activeSilkBalls.Count} 个 ===");
        }

        private IEnumerator SummonPhase2DoubleSilkBalls()
        {
            // 随机决定顺时针或逆时针
            bool clockwise = Random.value < 0.5f;
            Log.Info($"=== 开始召唤Memory Phase2丝球（内圈6球 + 外圈3球，{(clockwise ? "顺时针" : "逆时针")}）===");
            _activeSilkBalls.Clear();
            Vector3 bossPosition = transform.position;
            float innerRadius = 6f;
            float outerRadius = 14f;

            // 外圈角度：顺时针时在右边（30°, 330°, 270°），逆时针时在左边（150°, 210°, 270°）
            float[] outerAngles = clockwise
                ? new float[] { 30f, 330f, 270f }
                : new float[] { 150f, 210f, 270f };

            int outerIndex = 0;

            for (int i = 0; i < 6; i++)
            {
                // 内圈从90°（上方）开始，顺时针递减，逆时针递增
                float innerAngle = clockwise ? 90f - i * 60f : 90f + i * 60f;
                float innerRadians = innerAngle * Mathf.Deg2Rad;
                Vector3 innerOffset = new Vector3(
                    Mathf.Cos(innerRadians) * innerRadius,
                    Mathf.Sin(innerRadians) * innerRadius,
                    0f
                );
                Vector3 innerSpawnPosition = bossPosition + innerOffset;
                innerSpawnPosition.z = 0f;
                var innerBehavior = _silkBallManager?.SpawnSilkBall(innerSpawnPosition, 30f, 25f, 8f, 1f, true);
                if (innerBehavior != null)
                {
                    _activeSilkBalls.Add(innerBehavior.gameObject);
                    Log.Info($"召唤内圈丝球 {i + 1}/6（{innerAngle}°）");
                }

                // 每隔一个内圈球生成一个外圈球（共3个）
                if (i % 2 == 0 && outerIndex < outerAngles.Length)
                {
                    float outerAngle = outerAngles[outerIndex];
                    float outerRadians = outerAngle * Mathf.Deg2Rad;
                    Vector3 outerOffset = new Vector3(
                        Mathf.Cos(outerRadians) * outerRadius,
                        Mathf.Sin(outerRadians) * outerRadius,
                        0f
                    );
                    Vector3 outerSpawnPosition = bossPosition + outerOffset;
                    outerSpawnPosition.z = 0f;
                    // 外圈：1.75倍大小，更低加速度（15f vs 30f）
                    var outerBehavior = _silkBallManager?.SpawnSilkBall(outerSpawnPosition, 15f, 25f, 8f, 1.75f, true);
                    if (outerBehavior != null)
                    {
                        _activeSilkBalls.Add(outerBehavior.gameObject);
                        outerBehavior.StartProtectionTime(2.5f);
                        Log.Info($"召唤外圈丝球 {outerIndex + 1}/3（{outerAngle}°，1.75x大小）");
                    }
                    outerIndex++;
                }

                yield return new WaitForSeconds(0.12f);
            }

            Log.Info($"=== Memory Phase2丝球召唤完成，共 {_activeSilkBalls.Count} 个（内圈6 + 外圈3）===");
        }

        public void StartSilkBallSummonAtHighPoint()
        {
            Log.Info("供FSM调用：开始在高点召唤丝球");
            if (_silkBallSummonCoroutine != null)
            {
                StopCoroutine(_silkBallSummonCoroutine);
            }
            _silkBallSummonCoroutine = StartCoroutine(SummonSilkBallsAtHighPointCoroutine());
        }

        public void ReleaseSilkBalls()
        {
            StartCoroutine(ReleaseSilkBallsCoroutine());
        }

        private IEnumerator ReleaseSilkBallsCoroutine()
        {
            yield return new WaitForSeconds(0.82f);

            Log.Info("=== 准备释放丝球 ===");

            if (_cachedRoarEmitter != null)
            {
                _cachedRoarEmitter.OnEnter();
            }

            if (_cachedPlayRoarAudio != null)
            {
                _cachedPlayRoarAudio.OnEnter();
            }

            Log.Info("=== 广播 SILK BALL RELEASE 事件，释放所有已准备的丝球 ===");
            EventRegister.SendEvent("SILK BALL RELEASE");

            _activeSilkBalls.Clear();

            yield return new WaitForSeconds(0.2f);
            if (_cachedRoarEmitter != null)
            {
                _cachedRoarEmitter.OnExit();
            }
        }
        #endregion
    }
}

