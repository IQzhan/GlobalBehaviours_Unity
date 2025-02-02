﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
#endif

namespace E
{
    /// <summary>
    /// <para>Manage all <see cref="GlobalBehaviour"/>, you can change instantiate function if you need.</para>
    /// <para>See also:
    /// <seealso cref="InitializeBeforeAllBehavioursMethodAttribute"/>,
    /// <seealso cref="ResetTypeInfosCallback"/>,
    /// <seealso cref="OverrideCreateInstanceCallback"/></para>
    /// </summary>
    public sealed partial class BehaviourManager
    {
        #region Public properties

        /// <summary>
        /// Already initialized?
        /// </summary>
        public static bool IsReady { get => instance.m_IsReady; }

        /// <summary>
        /// Get instance of <see cref="GlobalUpdater"/>.
        /// </summary>
        public static GlobalUpdater Updater { get => GlobalUpdater.Instance; }

        /// <summary>
        /// Call before auto create  <see cref="GlobalBehaviour"/> instances,
        /// you can reset all <see cref="GlobalBehaviour"/>'s <see cref="TypeInfo"/>
        /// makes them re-order or auto instantiate.
        /// Set this event in <see cref="InitializeBeforeAllBehavioursMethodAttribute"/> method.
        /// <para>See also:
        /// <seealso cref="AutoInstantiateAttribute"/>,
        /// <seealso cref="ExecuteAlways"/>,
        /// <seealso cref="ExecuteInEditMode"/></para>
        /// </summary>
        public static event Func<IEnumerable<TypeInfo>, IEnumerable<TypeInfo>> ResetTypeInfosCallback;

        /// <summary>
        /// Call when create <see cref="GlobalBehaviour"/> instances,
        /// this event will override original create method.
        /// Set this event in <see cref="InitializeBeforeAllBehavioursMethodAttribute"/> method.
        /// </summary>
        public static event Func<TypeInfo, GlobalBehaviour> OverrideCreateInstanceCallback;

        /// <summary>
        /// Call by <see cref="GlobalUpdater.FixedUpdate"/>.
        /// </summary>
        public static event Action FixedUpdateCallback;

        /// <summary>
        /// Call <see cref="GlobalUpdater.Update"/>.
        /// </summary>
        public static event Action UpdateCallback;

        /// <summary>
        /// Call by <see cref="GlobalUpdater.LateUpdate"/>.
        /// </summary>
        public static event Action LateUpdateCallback;

#if UNITY_EDITOR
        /// <summary>
        /// Call by <see cref="GlobalUpdater.OnDrawGizmos"/>.
        /// </summary>
        public static event Action OnDrawGizmosCallback;
#endif
        #endregion

        #region Private properties

        /// <summary>
        /// This manager instance.
        /// </summary>
        internal static BehaviourManager instance;

        private bool m_IsReady;

        private bool m_FirstUpdated;

        /// <summary>
        /// All <see cref="GlobalBehaviour"/>'s <see cref="TypeInfo"/>
        /// </summary>
        private SortedList<int, TypeInfo> m_TypeInfos;

        /// <summary>
        /// All created <see cref="GlobalBehaviour"/>.
        /// </summary>
        private BehaviourCollection m_Collection;

        /// <summary>
        /// Use to queue <see cref="GlobalBehaviour"/>'s id need to enable.
        /// </summary>
        private List<int> m_EnableQueue;

        /// <summary>
        /// Use to queue <see cref="GlobalBehaviour"/>'s id need to update.
        /// </summary>
        private List<int> m_UpdateQueue;

        /// <summary>
        /// Use to queue <see cref="GlobalBehaviour"/>'s id need to disable.
        /// </summary>
        private List<int> m_DisableQueue;

        private enum StateToCheck
        {
            Enable = 1,
            Update = 1 << 1,
            Disable = 1 << 2,
            All = Enable | Update | Disable,
            UpdateAndDisable = Update | Disable
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Create a <see cref="GlobalBehaviour"/> instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T CreateInstance<T>() where T : GlobalBehaviour
        {
            if (!instance.m_IsReady) return default;
            return instance.InternalCreateInstance<T>();
        }

        /// <summary>
        ///  Create a <see cref="GlobalBehaviour"/> instance.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static GlobalBehaviour CreateInstance(in Type type)
        {
            if (!instance.m_IsReady) return default;
            return instance.InternalCreateInstance(type);
        }

        /// <summary>
        /// Get a created <see cref="GlobalBehaviour"/> instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetInstance<T>() where T : GlobalBehaviour
        {
            if (!instance.m_IsReady) return default;
            return instance.InternalGetInstance<T>();
        }

        /// <summary>
        /// Get a created <see cref="GlobalBehaviour"/> instance.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static GlobalBehaviour GetInstance(in Type type)
        {
            if (!instance.m_IsReady) return default;
            return instance.InternalGetInstance(type);
        }

        /// <summary>
        /// Get created <see cref="GlobalBehaviour"/> instances.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T[] GetInstances<T>() where T : GlobalBehaviour
        {
            if (!instance.m_IsReady) return default;
            return instance.InternalGetInstances<T>();
        }

        /// <summary>
        /// Get created <see cref="GlobalBehaviour"/> instances.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static GlobalBehaviour[] GetInstances(in Type type)
        {
            if (!instance.m_IsReady) return default;
            return instance.InternalGetInstances(type);
        }

        /// <summary>
        /// Destroy a created <see cref="GlobalBehaviour"/> instances.
        /// </summary>
        /// <param name="behaviour"></param>
        public static void DestroyInstance(in GlobalBehaviour behaviour)
        {
            if (!instance.m_IsReady) return;
            instance.InternalDestroyInstance(behaviour);
        }

        #endregion

        #region Initialize & Dispose

        /// <summary>
        /// Make sure m_Instance always exist.
        /// </summary>
        static BehaviourManager() => instance = new BehaviourManager();

        private BehaviourManager() { }

        ~BehaviourManager() { Destroy(); }

        internal static void InitializeOnLoad() => instance.Initialize();

        internal static void DestroyOnExit() => instance.Destroy();

        private void Initialize()
        {
            try
            {
                // make sure Initialize only once
                if (m_IsReady) return;
                CreateLifeCycleQueues();
                IEnumerable<Type> types = GetAllTypes();
                ExecuteBefore(types);
                CreateTypeInfos(types);
                m_FirstUpdated = false;
                m_IsReady = true;
            }
            catch (Exception e)
            {
                if (Utility.AllowLogError)
                {
                    Utility.LogException(e);
                }
            }
        }

        private void Destroy()
        {
            try
            {
                // make sure Destroy only once
                if (!m_IsReady) return;
                m_FirstUpdated = false;
                ReleaseCollection();
                ReleaseTypeInfos();
                ReleaseLifeCycleQueues();
                ClearCallbacks();
                m_IsReady = false;
            }
            catch (Exception e)
            {
                if (Utility.AllowLogError)
                {
                    Utility.LogException(e);
                }
                return;
            }
        }

        private IEnumerable<Type> GetAllTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());
        }

        private void ExecuteBefore(IEnumerable<Type> allTypes)
        {
            Type attrType = typeof(InitializeBeforeAllBehavioursMethodAttribute);
            var methods = allTypes
                .SelectMany(t => t.GetMethods()
                .Where(m => m.IsStatic && !m.IsGenericMethod && !m.IsConstructor &&
                (m.GetCustomAttributes(attrType, true).FirstOrDefault() is InitializeBeforeAllBehavioursMethodAttribute) &&
                (m.GetParameters().Length == 0)));
            var paramObjs = new object[0];
            foreach (var method in methods)
            {
                try
                {
                    method.Invoke(null, paramObjs);
                }
                catch (Exception e)
                {
                    if (Utility.AllowLogError)
                    {
                        Utility.LogException(e);
                    }
                }
            }
        }

        private void CreateTypeInfos(IEnumerable<Type> allTypes)
        {
            var baseType = typeof(GlobalBehaviour);
            IEnumerable<TypeInfo> typeInfos = allTypes
                .Where(t => baseType.IsAssignableFrom(t) &&
                t.IsClass &&
                !t.IsAbstract &&
                !t.IsGenericType)
                .Select(t => new TypeInfo(t))
                // ToList() makes sure this linq excuted.
                .ToList();
            if (ResetTypeInfosCallback != null)
            { typeInfos = ResetTypeInfosCallback(typeInfos); }
            m_TypeInfos = new SortedList<int, TypeInfo>
                (typeInfos.ToDictionary(i => i.TypeHashCode));
            m_Collection = new BehaviourCollection(typeInfos);
        }

        private void AutoCreateInstances()
        {
            TypeInfo[] autoOrder = m_TypeInfos.Select(kv => kv.Value)
                   .Where(i => i.isAutoInstantiation)
                   .OrderBy(i => i.order)
                   .ToArray();
            for (int i = 0; i < autoOrder.Length; i++)
            { InternalCreateInstance(autoOrder[i].Value); }
        }

        private void ReleaseTypeInfos()
        {
            if (m_TypeInfos == null) return;
            m_TypeInfos.Clear();
            m_TypeInfos = null;
        }

        private void ReleaseCollection()
        {
            if (m_Collection == null) return;
            foreach (GlobalBehaviour behaviour in m_Collection)
            {
                behaviour?.InternalDestroy();
            }
            m_Collection.Clear();
            m_Collection = null;
        }

        private void CreateLifeCycleQueues()
        {
            m_EnableQueue = new List<int>();
            m_UpdateQueue = new List<int>();
            m_DisableQueue = new List<int>();
        }

        private void ReleaseLifeCycleQueues()
        {
            ReleaseLifeCycleQueue(ref m_EnableQueue);
            ReleaseLifeCycleQueue(ref m_UpdateQueue);
            ReleaseLifeCycleQueue(ref m_DisableQueue);
        }

        private void ReleaseLifeCycleQueue(ref List<int> queue)
        {
            if (queue != null)
            {
                queue.Clear();
                queue = null;
            }
        }

        private void ClearLifeCycleQueues()
        {
            m_EnableQueue.Clear();
            m_UpdateQueue.Clear();
            m_DisableQueue.Clear();
        }

        private void ClearCallbacks()
        {
            ResetTypeInfosCallback = null;
            OverrideCreateInstanceCallback = null;
            FixedUpdateCallback = null;
            UpdateCallback = null;
            LateUpdateCallback = null;
#if UNITY_EDITOR
            OnDrawGizmosCallback = null;
#endif
        }

        #endregion

        #region Life Cycle methods

        private T InternalCreateInstance<T>() where T : GlobalBehaviour
        {
            return InternalCreateInstance(typeof(T)) as T;
        }

        private GlobalBehaviour InternalCreateInstance(in Type type)
        {
            int typeHashCode = type.GetHashCode();
            if (!GetTypeInfo(typeHashCode, out TypeInfo typeInfo)) return null;
            GlobalBehaviour behaviour = CreateInstanceByTypeInfo(typeInfo);
            if (behaviour == null) return null;
            m_Collection.Add(behaviour);
            if (behaviour.InternalAwake())
            {
                CheckLifeCycleState(behaviour, StateToCheck.Enable);
            }
            return behaviour;
        }

        private GlobalBehaviour CreateInstanceByTypeInfo(in TypeInfo typeInfo)
        {
            try
            {
                GlobalBehaviour behaviour;
                if (OverrideCreateInstanceCallback != null)
                {
                    behaviour = OverrideCreateInstanceCallback(typeInfo);
                }
                else
                {
                    behaviour = Activator.CreateInstance(typeInfo.Value, true) as GlobalBehaviour;
                }
                if (behaviour == null)
                {
                    if (Utility.AllowLogError)
                    {
                        Utility.LogError($"Create a null value of type '{typeInfo.Value}'.");
                    }
                    return null;
                }
                behaviour.typeHashCode = typeInfo.TypeHashCode;
                behaviour.isExecuteInEditorMode = typeInfo.isExecuteInEditorMode;
                return behaviour;
            }
            catch (Exception e)
            {
                if (Utility.AllowLogError)
                {
                    Utility.LogException(e);
                }
                return null;
            }
        }

        private T InternalGetInstance<T>() where T : GlobalBehaviour
        {
            return InternalGetInstance(typeof(T)) as T;
        }

        private GlobalBehaviour InternalGetInstance(in Type type)
        {
            int typeHashCode = type.GetHashCode();
            if (!GetTypeInfo(typeHashCode, out TypeInfo _)) return null;
            return m_Collection.Get(typeHashCode);
        }

        private T[] InternalGetInstances<T>() where T : GlobalBehaviour
        {
            int typeHashCode = typeof(T).GetHashCode();
            if (!GetTypeInfo(typeHashCode, out TypeInfo _)) return null;
            return m_Collection.Gets<T>(typeHashCode);
        }

        private GlobalBehaviour[] InternalGetInstances(in Type type)
        {
            int typeHashCode = type.GetHashCode();
            if (!GetTypeInfo(typeHashCode, out TypeInfo _)) return null;
            return m_Collection.Gets(typeHashCode);
        }

        private void InternalDestroyInstance(in GlobalBehaviour behaviour)
        {
            if (!GetTypeInfo(behaviour.typeHashCode, out TypeInfo _)) return;
            if (behaviour.InternalDestroy())
            {
                m_Collection.Remove(behaviour);
            }
        }

        private bool GetTypeInfo(int typeHashCode, out TypeInfo typeInfo)
        {
            if (!m_TypeInfos.TryGetValue(typeHashCode, out typeInfo))
            {
                if (Utility.AllowLogError)
                {
                    Utility.LogError($"Type '{typeInfo.Value}' is not inherit from type '{typeof(GlobalBehaviour)}'.");
                }
                return false;
            }
            return true;
        }

        internal void FixedUpdate(bool allowUpdate) => CallUpdate(allowUpdate, FixedUpdateCallback);

        internal void Update(bool allowUpdate) => CallUpdate(allowUpdate, UpdateCallback);

        internal void LateUpdate(bool allowUpdate) => CallUpdate(allowUpdate, LateUpdateCallback);

        private void CallUpdate(bool allowUpdate, in Action updateCallback)
        {
            if (m_IsReady)
            {
                if (allowUpdate) UpdateLifeCycle();
                LogTryCatchEvent(updateCallback);
            }
        }

#if UNITY_EDITOR
        internal void OnDrawGizmos()
        { LogTryCatchEvent(OnDrawGizmosCallback); }
#endif

        private void LogTryCatchEvent(in Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                if (Utility.AllowLogError)
                {
                    Utility.LogException(e);
                }
            }
        }

        private void UpdateLifeCycle()
        {
            if (!m_FirstUpdated)
            {
                AutoCreateInstances();
                m_FirstUpdated = true;
            }
            ClearLifeCycleQueues();
            CheckAllLifeCycleState();
            InternalLifeCycleBody();
        }

        private void CheckAllLifeCycleState()
        {
            foreach (GlobalBehaviour behaviour in m_Collection)
            {
                CheckLifeCycleState(behaviour, StateToCheck.All);
            }
        }

        private void CheckLifeCycleState(in GlobalBehaviour behaviour, StateToCheck stateToCheck)
        {
            try
            {
                if ((stateToCheck & StateToCheck.Enable) != 0 &&
                behaviour.CheckEnable())
                {
                    m_EnableQueue.Add(behaviour.ID);
                }
                if ((stateToCheck & StateToCheck.Update) != 0 &&
                    behaviour.CheckUpdate())
                {
                    m_UpdateQueue.Add(behaviour.ID);
                }
                if ((stateToCheck & StateToCheck.Disable) != 0 &&
                    behaviour.CheckDisable())
                {
                    m_DisableQueue.Add(behaviour.ID);
                }
            }
            catch (Exception e)
            {
                if (Utility.AllowLogError)
                {
                    Utility.LogException(e);
                }
            }
        }

        private void InternalLifeCycleBody()
        {
            InternalEnable();
            InternalUpdate();
            InternalDisable();
        }

        private void InternalEnable()
        {
            int index = 0;
            while (index < m_EnableQueue.Count)
            {
                int id = m_EnableQueue[index];
                GlobalBehaviour behaviour = m_Collection[id];
                if (behaviour != null && behaviour.InternalEnable())
                {
                    CheckLifeCycleState(behaviour, StateToCheck.UpdateAndDisable);
                }
                index++;
            }
        }

        private void InternalUpdate()
        {
            int index = 0;
            while (index < m_UpdateQueue.Count)
            {
                int id = m_UpdateQueue[index];
                GlobalBehaviour behaviour = m_Collection[id];
                if (behaviour != null && behaviour.InternalUpdate())
                {
                    CheckLifeCycleState(behaviour, StateToCheck.Disable);
                }
                index++;
            }
        }

        private void InternalDisable()
        {
            int index = 0;
            while (index < m_DisableQueue.Count)
            {
                int id = m_DisableQueue[index];
                GlobalBehaviour behaviour = m_Collection[id];
                behaviour?.InternalDisable();
                index++;
            }
        }

        #endregion
    }
}