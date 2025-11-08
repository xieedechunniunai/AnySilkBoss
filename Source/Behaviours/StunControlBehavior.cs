using System.Collections;
using UnityEngine;
using HutongGames.PlayMaker;
using AnySilkBoss.Source.Tools;
namespace AnySilkBoss.Source.Behaviours
{
    /// <summary>
    /// 晕眩控制器行为
    /// 负责管理Boss的晕眩状态和行为
    /// </summary>
    internal class StunControlBehavior : MonoBehaviour
    {
        // FSM引用
        private PlayMakerFSM _stunControl = null!;
        
        // 晕眩状态变量
        private bool _isStunned = false;
        private float _stunDuration = 0f;
        private float _stunTimer = 0f;

        private void Awake()
        {
            // 初始化在Start中进行
        }

        private void Start()
        {
            StartCoroutine(DelayedSetup());
        }

        private void Update()
        {
            // 更新晕眩状态
            //UpdateStunState();
        }

        /// <summary>
        /// 延迟初始化
        /// </summary>
        private IEnumerator DelayedSetup()
        {
            yield return null; // 等待一帧
            StartCoroutine(SetupStunControl());
        }

        /// <summary>
        /// 设置晕眩控制器
        /// </summary>
        private IEnumerator SetupStunControl()
        {
            GetComponents();
            ModifyStunBehavior();
            Log.Info("晕眩控制器行为初始化完成");
            yield return null;
        }

        /// <summary>
        /// 获取必要的组件
        /// </summary>
        private void GetComponents()
        {
            _stunControl = FSMUtility.LocateMyFSM(gameObject, "Stun Control");
            
            if (_stunControl == null)
            {
                Log.Error("未找到Stun Control FSM");
                return;
            }
            
            Log.Info("成功获取Stun Control FSM");
        }

        /// <summary>
        /// 修改晕眩行为
        /// </summary>
        private void ModifyStunBehavior()
        {
            if (_stunControl == null) return;

            // ========== 在这里添加你的晕眩修改逻辑 ==========
            
            Log.Info("晕眩行为修改完成");
        }

    }
}
