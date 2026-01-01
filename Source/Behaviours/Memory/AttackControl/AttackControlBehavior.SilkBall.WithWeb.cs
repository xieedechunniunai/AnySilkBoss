using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using AnySilkBoss.Source.Managers;
using AnySilkBoss.Source.Behaviours.Common;
using static AnySilkBoss.Source.Tools.FsmStateBuilder;

namespace AnySilkBoss.Source.Behaviours.Memory
{
    internal partial class MemoryAttackControlBehavior
    {
        private void CreateSilkBallWithWebAttackStates()
        {
            if (_attackControlFsm == null) return;

            _silkBallWebPrepareState = CreateAndAddState(_attackControlFsm, "Silk Ball Web Prepare", "");
            _silkBallWebAttackState = CreateAndAddState(_attackControlFsm, "Silk Ball Web Attack", "");
            _silkBallWebRecoverState = CreateAndAddState(_attackControlFsm, "Silk Ball Web Recover", "");

            {
                var actions = new List<FsmStateAction>();
                actions.AddRange(CloneStateActions("Silk Ball Ring Prepare"));

                if (_silkHair != null)
                {
                    actions.Add(new SendEventByName
                    {
                        eventTarget = new FsmEventTarget
                        {
                            target = FsmEventTarget.EventTarget.GameObject,
                            gameObject = new FsmOwnerDefault
                            {
                                OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                                gameObject = new FsmGameObject { Value = _silkHair }
                            }
                        },
                        sendEvent = "ROAR",
                        delay = new FsmFloat(0f),
                        everyFrame = false
                    });
                }

                if (_naChargeEffect != null)
                {
                    actions.Add(new ActivateGameObject
                    {
                        gameObject = new FsmOwnerDefault
                        {
                            OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                            gameObject = new FsmGameObject { Value = _naChargeEffect }
                        },
                        activate = new FsmBool(true),
                        recursive = new FsmBool(false),
                        resetOnExit = false,
                        everyFrame = false
                    });
                }

                _silkBallWebPrepareState.Actions = actions.ToArray();
            }

            _silkBallWebAttackState.Actions = new FsmStateAction[]
            {
                new CallMethod
                {
                    behaviour = new FsmObject { Value = this },
                    methodName = new FsmString("ExecuteSilkBallWithWebAttack") { Value = "ExecuteSilkBallWithWebAttack" },
                    parameters = new FsmVar[0]
                }
            };

            {
                var actions = new List<FsmStateAction>();
                if (_silkHair != null)
                {
                    actions.Add(new SendEventByName
                    {
                        eventTarget = new FsmEventTarget
                        {
                            target = FsmEventTarget.EventTarget.GameObject,
                            gameObject = new FsmOwnerDefault
                            {
                                OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                                gameObject = new FsmGameObject { Value = _silkHair }
                            }
                        },
                        sendEvent = "IDLE",
                        delay = new FsmFloat(0f),
                        everyFrame = false
                    });
                }
                _silkBallWebRecoverState.Actions = actions.ToArray();
            }

            SetFinishedTransition(_silkBallWebPrepareState, _silkBallWebAttackState);

            if (_silkBallWithWebAttackDoneEvent != null)
            {
                AddTransition(_silkBallWebAttackState, CreateTransition(_silkBallWithWebAttackDoneEvent, _silkBallWebRecoverState));
            }

            var moveRestartState = _moveRestartState;
            if (moveRestartState != null)
            {
                SetFinishedTransition(_silkBallWebRecoverState, moveRestartState);
            }

            var silkBallPrepareState = _silkBallPrepareState;
            if (silkBallPrepareState != null && _silkBallWithWebAttackEvent != null)
            {
                AddTransition(silkBallPrepareState, CreateTransition(_silkBallWithWebAttackEvent, _silkBallWebPrepareState));
            }
        }

        public void ExecuteSilkBallWithWebAttack()
        {
            if (_silkBallWithWebAttackCoroutine != null)
            {
                StopCoroutine(_silkBallWithWebAttackCoroutine);
                _silkBallWithWebAttackCoroutine = null;
            }

            _silkBallWithWebAttackCoroutine = StartCoroutine(SilkBallWithWebAttackCoroutine());
        }

        private IEnumerator SilkBallWithWebAttackCoroutine()
        {
            float attackDuration = 6f;
            float protectionDuration = 8f;
            float startDelay = 0.5f;

            try
            {
                if (_attackControlFsm == null)
                {
                    yield break;
                }

                if (_silkBallManager == null || _singleWebManager == null)
                {
                    yield break;
                }

                var hero = HeroController.instance;
                if (hero == null)
                {
                    yield break;
                }

                var heroTransform = hero.transform;
                Vector3 heroPos = heroTransform.position;

                Vector3 leftSpawn = heroPos + new Vector3(-8f, 6f, 0f);
                Vector3 rightSpawn = heroPos + new Vector3(8f, 6f, 0f);

                var silkBalls = new List<SilkBallBehavior?>();
                var webAngleOffsets = new Dictionary<SilkBallBehavior, float>();
                float? firstOffset = null;

                var leftBall = _silkBallManager.SpawnSilkBall(
                    leftSpawn,
                    acceleration: 12f,
                    maxSpeed: 8f,
                    chaseTime: attackDuration,
                    scale: 1.33f,
                    enableRotation: true,
                    customTarget: heroTransform,
                    ignoreWall: true
                );
                if (leftBall != null)
                {
                    leftBall.StartProtectionTime(protectionDuration);
                    silkBalls.Add(leftBall);

                    float offset = Random.Range(0f, 90f);
                    webAngleOffsets[leftBall] = offset;
                    firstOffset = offset;
                }

                var rightBall = _silkBallManager.SpawnSilkBall(
                    rightSpawn,
                    acceleration: 12f,
                    maxSpeed: 8f,
                    chaseTime: attackDuration,
                    scale: 1.33f,
                    enableRotation: true,
                    customTarget: heroTransform,
                    ignoreWall: true
                );
                if (rightBall != null)
                {
                    rightBall.StartProtectionTime(protectionDuration);
                    silkBalls.Add(rightBall);

                    float offset = Random.Range(0f, 90f);
                    if (firstOffset.HasValue)
                    {
                        offset = (firstOffset.Value + Random.Range(15f, 75f)) % 90f;
                    }
                    webAngleOffsets[rightBall] = offset;
                }

                // 确保 Prepare 状态里的 MarkAsPrepared 先执行完，再发 RELEASE
                foreach (var ball in silkBalls)
                {
                    if (ball == null) continue;
                    yield return WaitForSilkBallPrepared(ball, 0.5f);
                }

                // 生成后延迟一段时间，再开始追踪与丝网旋转
                if (startDelay > 0f)
                {
                    yield return new WaitForSeconds(startDelay);
                }
                _bossControlFsm!.Fsm.Event("MOVE START");
                foreach (var ball in silkBalls)
                {
                    ball?.SendFsmEvent("SILK BALL RELEASE");
                }

                float elapsed = 0f;
                float waveInterval = 1.0f;
                float rotationSpeed = 45f;
                int wave = 0;

                // 持续刷波次，确保覆盖整个 attackDuration
                while (elapsed < attackDuration)
                {
                    bool isFirstBall = true;
                    foreach (var ball in silkBalls)
                    {
                        if (ball == null) continue;

                        float offset = 0f;
                        webAngleOffsets.TryGetValue(ball, out offset);
                        float baseAngle = (wave * 15f + offset) % 90f;
                        
                        // 每波只让第一个球播放音效，第二个球静音
                        SpawnCrossWeb(ball.transform, baseAngle, rotationSpeed, playAudio: isFirstBall);
                        isFirstBall = false;
                    }

                    wave++;
                    elapsed += waveInterval;
                    yield return new WaitForSeconds(waveInterval);
                }
            }
            finally
            {
                _silkBallWithWebAttackCoroutine = null;

                if (_naChargeEffect != null)
                {
                    _naChargeEffect.SetActive(false);
                }

                if (_attackControlFsm != null && _silkBallWithWebAttackDoneEvent != null)
                {
                    _attackControlFsm.Fsm.Event(_silkBallWithWebAttackDoneEvent);
                }
            }
        }

        private IEnumerator WaitForSilkBallPrepared(SilkBallBehavior ball, float maxWait)
        {
            float waited = 0f;
            while (!ball.isPrepared && waited < maxWait)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        private void SpawnCrossWeb(Transform followTarget, float baseAngle, float rotationSpeed, bool playAudio = false)
        {
            if (_singleWebManager == null) return;

            Vector3 pos = followTarget.position;
            Vector3 scale = new Vector3(2.4f, 1.1f, 1f);

            // 第一根丝线：根据参数决定是否播放音效
            var w1 = _singleWebManager.SpawnAndAttack(pos, new Vector3(0f, 0f, baseAngle), scale, 0f, 0.75f);
            if (w1 != null)
            {
                if (!playAudio)
                {
                    w1.SetAudioEnabled(false);
                }
                w1.ConfigureFollowTarget(followTarget);
                w1.ConfigureContinuousRotation(true, rotationSpeed);
            }

            // 第二根丝线：始终静音
            var w2 = _singleWebManager.SpawnAndAttack(pos, new Vector3(0f, 0f, baseAngle + 90f), scale, 0f, 0.75f);
            if (w2 != null)
            {
                w2.SetAudioEnabled(false);
                w2.ConfigureFollowTarget(followTarget);
                w2.ConfigureContinuousRotation(true, rotationSpeed);
            }
        }
    }
}
