using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Managers;
using System.Collections;
using System.Collections.Generic;

namespace AnySilkBoss.Source.Actions
{
    /// <summary>
    /// 剑阵控制 Action - 控制 40 把飞针的展开、旋转、分批发射
    /// 
    /// 剑阵大招流程：
    /// 1. 召唤 40 针在场地中上（全部朝下）
    /// 2. 旋转展开
    /// 3. 展开完毕后，每隔一把向下攻击（20 针同时落下）
    /// 4. 砸地后不返回池，向上移动一点点
    /// 5. 对所有针角度打乱
    /// 6. 逐个发射剩余针
    /// 
    /// 使用场景：
    /// - 剑阵大招（AttackControlBehavior.PinArray）
    /// </summary>
    public class PinArrayControlAction : FsmStateAction
    {
        #region 参数配置
        [HutongGames.PlayMaker.Tooltip("飞针数量")]
        public FsmInt pinCount;

        [HutongGames.PlayMaker.Tooltip("生成中心位置")]
        public FsmVector3 spawnCenter;

        [HutongGames.PlayMaker.Tooltip("初始生成半径")]
        public FsmFloat initialRadius;

        [HutongGames.PlayMaker.Tooltip("展开后的最终半径")]
        public FsmFloat finalRadius;

        [HutongGames.PlayMaker.Tooltip("展开动画时长（秒）")]
        public FsmFloat expandDuration;

        [HutongGames.PlayMaker.Tooltip("展开时的旋转速度（度/秒）")]
        public FsmFloat expandRotationSpeed;

        [HutongGames.PlayMaker.Tooltip("第一波发射后的上升距离")]
        public FsmFloat riseAfterFirstWave;

        [HutongGames.PlayMaker.Tooltip("第一波攻击速度")]
        public FsmFloat firstWaveSpeed;

        [HutongGames.PlayMaker.Tooltip("剩余针的发射间隔范围 - 最小值（秒）")]
        public FsmFloat fireIntervalMin;

        [HutongGames.PlayMaker.Tooltip("剩余针的发射间隔范围 - 最大值（秒）")]
        public FsmFloat fireIntervalMax;

        [HutongGames.PlayMaker.Tooltip("剩余针的发射速度")]
        public FsmFloat remainingFireSpeed;

        [HutongGames.PlayMaker.Tooltip("角度打乱范围（度）")]
        public FsmFloat angleScrambleRange;
        #endregion

        #region 事件
        [HutongGames.PlayMaker.Tooltip("展开完成事件")]
        public FsmEvent? onExpandComplete;

        [HutongGames.PlayMaker.Tooltip("第一波发射完成事件")]
        public FsmEvent? onFirstWaveFired;

        [HutongGames.PlayMaker.Tooltip("所有针发射完成事件")]
        public FsmEvent? onAllFired;
        #endregion

        #region 状态枚举
        public enum PinArrayPhase
        {
            Idle,           // 空闲
            Spawning,       // 生成中
            Expanding,      // 展开中
            WaitingFirstWave,   // 等待第一波发射
            FiringFirstWave,    // 第一波发射中
            Rising,         // 上升中
            Scrambling,     // 角度打乱中
            FiringRemaining,    // 发射剩余针中
            Complete        // 完成
        }
        #endregion

        #region 运行时变量
        private FWPinManager? pinManager;
        private List<GameObject> allPins = new List<GameObject>();
        private List<GameObject> firstWavePins = new List<GameObject>();   // 奇数索引（隔一个）
        private List<GameObject> secondWavePins = new List<GameObject>();  // 偶数索引（剩余）
        private PinArrayPhase currentPhase = PinArrayPhase.Idle;
        private float phaseTimer;
        #endregion

        public override void Reset()
        {
            pinCount = 40;
            spawnCenter = new Vector3(37.5f, 20f, 0f);  // 场地中上
            initialRadius = 2f;
            finalRadius = 12f;
            expandDuration = 2f;
            expandRotationSpeed = 180f;
            riseAfterFirstWave = 2f;
            firstWaveSpeed = 30f;
            fireIntervalMin = 0.05f;
            fireIntervalMax = 0.15f;
            remainingFireSpeed = 25f;
            angleScrambleRange = 60f;
            onExpandComplete = null;
            onFirstWaveFired = null;
            onAllFired = null;
        }

        public override void OnEnter()
        {
            // 获取管理器
            GetManagerReferences();
            if (pinManager == null)
            {
                Log("PinArrayControlAction: 未找到 FWPinManager");
                Finish();
                return;
            }

            // 初始化
            allPins.Clear();
            firstWavePins.Clear();
            secondWavePins.Clear();
            currentPhase = PinArrayPhase.Spawning;

            // 开始执行
            StartCoroutine(ExecutePinArray());
        }

        public override void OnExit()
        {
            currentPhase = PinArrayPhase.Idle;
            // 注意：不在这里清理针，让它们自然完成或由管理器回收
        }

        /// <summary>
        /// 获取管理器引用
        /// </summary>
        private void GetManagerReferences()
        {
            var managerObj = GameObject.Find("AnySilkBossManager");
            if (managerObj != null)
            {
                pinManager = managerObj.GetComponent<FWPinManager>();
            }
        }

        /// <summary>
        /// 执行剑阵大招的主协程
        /// </summary>
        private IEnumerator ExecutePinArray()
        {
            // 阶段1：生成所有针
            yield return SpawnAllPins();

            // 阶段2：展开动画
            currentPhase = PinArrayPhase.Expanding;
            yield return ExpandPins();

            if (onExpandComplete != null)
            {
                Fsm.Event(onExpandComplete);
            }

            // 阶段3：第一波发射（隔一个向下攻击）
            currentPhase = PinArrayPhase.FiringFirstWave;
            yield return FireFirstWave();

            if (onFirstWaveFired != null)
            {
                Fsm.Event(onFirstWaveFired);
            }

            // 阶段4：剩余针上升
            currentPhase = PinArrayPhase.Rising;
            yield return RiseRemainingPins();

            // 阶段5：角度打乱
            currentPhase = PinArrayPhase.Scrambling;
            ScramblePinAngles();
            yield return new WaitForSeconds(0.3f);

            // 阶段6：逐个发射剩余针
            currentPhase = PinArrayPhase.FiringRemaining;
            yield return FireRemainingPins();

            if (onAllFired != null)
            {
                Fsm.Event(onAllFired);
            }

            currentPhase = PinArrayPhase.Complete;
            Finish();
        }

        /// <summary>
        /// 生成所有针
        /// </summary>
        private IEnumerator SpawnAllPins()
        {
            int count = pinCount.Value;
            Vector3 center = spawnCenter.Value;

            for (int i = 0; i < count; i++)
            {
                // 计算初始位置（紧密排列在小圆内）
                float angle = (360f / count) * i;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                Vector3 spawnPos = center + new Vector3(direction.x, direction.y, 0) * initialRadius.Value;

                // 生成针（全部朝下，即 rotation.z = -90 或 270）
                var pin = pinManager?.SpawnPinProjectile(spawnPos);

                if (pin != null)
                {
                    pin.transform.rotation = Quaternion.Euler(0, 0, -90f);
                    allPins.Add(pin);

                    // 分配到两个波次
                    if (i % 2 == 0)
                    {
                        firstWavePins.Add(pin);  // 偶数索引 -> 第一波
                    }
                    else
                    {
                        secondWavePins.Add(pin); // 奇数索引 -> 第二波
                    }
                }
            }

            yield return null;
        }

        /// <summary>
        /// 展开针阵
        /// </summary>
        private IEnumerator ExpandPins()
        {
            float elapsed = 0f;
            int count = allPins.Count;
            Vector3 center = spawnCenter.Value;

            // 记录每个针的初始和目标位置
            var initialPositions = new Vector3[count];
            var targetPositions = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                if (allPins[i] == null) continue;

                initialPositions[i] = allPins[i].transform.position;

                float angle = (360f / count) * i;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                targetPositions[i] = center + new Vector3(direction.x, direction.y, 0) * finalRadius.Value;
            }

            while (elapsed < expandDuration.Value)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / expandDuration.Value;
                t = Mathf.SmoothStep(0f, 1f, t);  // 使用平滑插值

                // 整体旋转
                float currentRotation = expandRotationSpeed.Value * elapsed;

                for (int i = 0; i < count; i++)
                {
                    if (allPins[i] == null) continue;

                    // 插值位置
                    Vector3 basePos = Vector3.Lerp(initialPositions[i], targetPositions[i], t);

                    // 应用旋转（绕中心点）
                    Vector3 offset = basePos - center;
                    float rotRad = currentRotation * Mathf.Deg2Rad;
                    Vector3 rotatedOffset = new Vector3(
                        offset.x * Mathf.Cos(rotRad) - offset.y * Mathf.Sin(rotRad),
                        offset.x * Mathf.Sin(rotRad) + offset.y * Mathf.Cos(rotRad),
                        offset.z
                    );

                    allPins[i].transform.position = center + rotatedOffset;
                }

                yield return null;
            }
        }

        /// <summary>
        /// 发射第一波（隔一个向下攻击）
        /// </summary>
        private IEnumerator FireFirstWave()
        {
            // 同时发射所有第一波针
            foreach (var pin in firstWavePins)
            {
                if (pin == null) continue;

                // 设置朝下
                pin.transform.rotation = Quaternion.Euler(0, 0, -90);

                // 获取刚体并设置速度
                var rb = pin.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.down * firstWaveSpeed.Value;
                }

                // 触发 FSM 发射状态
                var fsm = pin.GetComponent<PlayMakerFSM>();
                if (fsm != null)
                {
                    fsm.SendEvent("DIRECT_FIRE");
                }
            }

            // 等待第一波落地（根据高度和速度估算）
            float fallTime = spawnCenter.Value.y / firstWaveSpeed.Value;
            yield return new WaitForSeconds(fallTime + 0.5f);
        }

        /// <summary>
        /// 剩余针上升
        /// </summary>
        private IEnumerator RiseRemainingPins()
        {
            float riseDuration = 0.5f;
            float elapsed = 0f;

            var initialPositions = new Dictionary<GameObject, Vector3>();
            foreach (var pin in secondWavePins)
            {
                if (pin != null)
                {
                    initialPositions[pin] = pin.transform.position;
                }
            }

            while (elapsed < riseDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / riseDuration;

                foreach (var pin in secondWavePins)
                {
                    if (pin == null || !initialPositions.ContainsKey(pin)) continue;

                    Vector3 targetPos = initialPositions[pin] + Vector3.up * riseAfterFirstWave.Value;
                    pin.transform.position = Vector3.Lerp(initialPositions[pin], targetPos, t);
                }

                yield return null;
            }
        }

        /// <summary>
        /// 打乱针的角度
        /// </summary>
        private void ScramblePinAngles()
        {
            foreach (var pin in secondWavePins)
            {
                if (pin == null) continue;

                // 随机角度偏移
                float randomAngle = Random.Range(-angleScrambleRange.Value, angleScrambleRange.Value);
                float baseAngle = -90f;  // 基础朝下
                pin.transform.rotation = Quaternion.Euler(0, 0, baseAngle + randomAngle);
            }
        }

        /// <summary>
        /// 逐个发射剩余针
        /// </summary>
        private IEnumerator FireRemainingPins()
        {
            // 随机打乱发射顺序
            var shuffledPins = new List<GameObject>(secondWavePins);
            for (int i = shuffledPins.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var temp = shuffledPins[i];
                shuffledPins[i] = shuffledPins[j];
                shuffledPins[j] = temp;
            }

            foreach (var pin in shuffledPins)
            {
                if (pin == null) continue;

                // 获取当前朝向
                float angle = pin.transform.eulerAngles.z;
                float angleRad = angle * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

                // 设置速度
                var rb = pin.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = direction * remainingFireSpeed.Value;
                }

                // 触发 FSM 发射状态
                var fsm = pin.GetComponent<PlayMakerFSM>();
                if (fsm != null)
                {
                    fsm.SendEvent("DIRECT_FIRE");
                }

                // 随机间隔
                float interval = Random.Range(fireIntervalMin.Value, fireIntervalMax.Value);
                yield return new WaitForSeconds(interval);
            }
        }

        /// <summary>
        /// 获取当前阶段
        /// </summary>
        public PinArrayPhase CurrentPhase => currentPhase;

        /// <summary>
        /// 获取剩余针数量
        /// </summary>
        public int RemainingPinCount => secondWavePins.Count;
    }
}
