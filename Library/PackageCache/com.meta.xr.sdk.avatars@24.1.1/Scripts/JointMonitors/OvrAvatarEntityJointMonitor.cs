using System;
using System.Collections.Generic;

using UnityEngine;
using Object = UnityEngine.Object;

namespace Oculus.Avatar2
{
    internal abstract class JointData : IDisposable
    {
        protected JointData(Transform transform)
        {
            JointTransform = transform;
        }

        public readonly Transform JointTransform;

        public abstract bool TryGetPosAndOrientation(out Vector3 pos, out Quaternion quat);

        public abstract void CalculateUpdate(float interpolationValue, out Vector3 position, out Quaternion rotation);

        public void Dispose()
        {
            Object.Destroy(JointTransform.gameObject);
            GC.SuppressFinalize(this);
        }
    }

    internal abstract class EntityJointMonitorBase<T> : IJointMonitor where T : JointData
    {
        private readonly List<CAPI.ovrAvatar2JointType> _monitoredJoints = new List<CAPI.ovrAvatar2JointType>();

        private readonly Dictionary<CAPI.ovrAvatar2JointType, T> _jointsToData =
            new Dictionary<CAPI.ovrAvatar2JointType, T>();

        private OvrAvatarEntity _entity;

        protected abstract string LogScope { get; }

        private bool TryGetJointData(CAPI.ovrAvatar2JointType jointType, out T jointData)
        {
            return _jointsToData.TryGetValue(jointType, out jointData);
        }

        public bool TryGetTransform(CAPI.ovrAvatar2JointType jointType, out Transform tx)
        {
            if (TryGetJointData(jointType, out var jointData) && IsJointDataValid(jointData))
            {
                tx = jointData.JointTransform;
                return true;
            }

            tx = null;
            return false;
        }

        public bool TryGetPositionAndOrientation(CAPI.ovrAvatar2JointType jointType, out Vector3 pos
            , out Quaternion rot)
        {
            if (TryGetJointData(jointType, out var jointData) && jointData.TryGetPosAndOrientation(out pos, out rot))
            {
                return true;
            }
            pos = Vector3.zero;
            rot = Quaternion.identity;
            return false;
        }

        protected EntityJointMonitorBase(OvrAvatarEntity entity)
        {
            _entity = entity;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~EntityJointMonitorBase()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool isDispose)
        {
            _monitoredJoints.Clear();

#if UNITY_2021_1_OR_NEWER
            foreach (var (_, jointData) in _jointsToData)
            {
                jointData.Dispose();
            }
#else
            foreach (var jointData in _jointsToData.Values)
            {
                jointData.Dispose();
            }
#endif
            _jointsToData.Clear();

            _entity = null;
        }


        private T AddMonitoredJoint(CAPI.ovrAvatar2JointType jointType)
        {
            OvrAvatarLog.Assert(
                !_jointsToData.TryGetValue(jointType, out var prevJointData) || !IsJointDataValid(prevJointData),
                LogScope,
                _entity);

            var newJointData = CreateNewJointData(jointType);

            if (!_jointsToData.ContainsKey(jointType))
            {
                // Newly tracked joint
                _jointsToData.Add(jointType, newJointData);
            }
            else
            {
                // Had previously tracked joint, but was cleared out (null data)
                _jointsToData[jointType] = newJointData;
            }

            return newJointData;
        }

        void IJointMonitor.OnJointPosesUpdated(List<OvrAvatarJointPose> jointPoses)
        {
            int jointsUpdatedCount = 0;
            foreach (var jointPose in jointPoses)
            {
                var jointType = jointPose.jointType;
                T jointData;
                if (_jointsToData.TryGetValue(jointType, out jointData))
                {
                    // Null data (can happen if
                    // entity clears out joint that was previously monitored)
                    if (!IsJointDataValid(jointData))
                    {
                        jointData = AddMonitoredJoint(jointType);
                    }
                }
                else
                {
                    // OvrAvatarEntity added this joint - begin tracking it
                    _monitoredJoints.Add(jointType);
                    jointData = AddMonitoredJoint(jointType);
                }

                AddNewAnimationFrameForJoint(jointData, jointPose.objectSpacePosition, jointPose.objectSpaceOrientation);
                ++jointsUpdatedCount;
            }

            if (_jointsToData.Count != jointsUpdatedCount)
            {
                // Not all transforms we have were updated, so at least one joint no longer exists on the entity
                foreach (var jointType in _monitoredJoints)
                {
                    // Clear out previously monitored joints that the entity
                    // no longer has
                    if (!_entity.IsJointTypeLoaded(jointType))
                    {
                        if (_jointsToData.TryGetValue(jointType, out var jointData) && IsJointDataValid(jointData))
                        {
                            DisposeJointData(jointType, jointData);
                            _jointsToData[jointType] = default(T);
                        }
                    }
                }
            }

            JointPosesUpdated();
        }

        protected Transform CreateNewTransform(CAPI.ovrAvatar2JointType jointType)
        {
            var go = new GameObject(
#if UNITY_EDITOR
                $"Joint {jointType}"
#else // ^^^ UNITY_EDITOR / !UNITY_EDITOR vvv
                "AvatarCriticalJoint"
#endif
                );

            var newTx = go.transform;
            newTx.SetParent(_entity._baseTransform, false);
            return newTx;
        }

        protected Dictionary<CAPI.ovrAvatar2JointType, T>.ValueCollection GetAllJointData()
        {
            return _jointsToData.Values;
        }

        private static bool IsJointDataValid(T jointData)
        {
            return !EqualityComparer<T>.Default.Equals(jointData, default(T));
        }

        protected abstract T CreateNewJointData(CAPI.ovrAvatar2JointType jointType);

        protected virtual void DisposeJointData(CAPI.ovrAvatar2JointType jointType, T jointData)
        {
            jointData.Dispose();
        }

        protected abstract void AddNewAnimationFrameForJoint(
            T jointData,
            in Vector3 objectSpacePosition,
            in Quaternion objectSpaceOrientation);

        protected abstract void JointPosesUpdated();

        public abstract void UpdateJoints(float deltaTime);
    } // end class

    internal class TransformHolder : JointData
    {
        public TransformHolder(Transform tx) : base(tx) {}

        public override bool TryGetPosAndOrientation(out Vector3 pos, out Quaternion quat)
        {
            Debug.Assert(JointTransform != null);

#if UNITY_2021_3_OR_NEWER
            JointTransform.GetLocalPositionAndRotation(out pos, out quat);
#else // ^^^ UNITY_2021_3_OR_NEWER / !UNITY_2021_3_OR_NEWER vvv
            pos = JointTransform.localPosition;
            quat = JointTransform.localRotation;
#endif // !UNITY_2021_3_OR_NEWER
            return true;
        }

        public override void CalculateUpdate(float interpolationValue, out Vector3 position, out Quaternion rotation)
        {
            // Not used for non-job based updates
            TryGetPosAndOrientation(out position, out rotation);
        }
    }

    // CRTP - Curiously Recurring Template Pattern, method to effective "pass" a template (generic, in this case) type
    // such that it can be used for implementation logic - bridging the base class with the top level implementation
    // Technically this isn't recurring - since it isn't passing the type of _itself_ but since the `JointData` type is fundamental
    // to the operation of JointMonitors - it feels close enough :)
    internal class OvrAvatarEntityJointMonitor : OvrAvatarEntityJointMonitorCRTP<TransformHolder>
    {
        protected override string LogScope => "OvrAvatarEntityJointMonitor";

        public OvrAvatarEntityJointMonitor(OvrAvatarEntity entity) : base(entity) {}

        protected override TransformHolder CreateNewJointData(CAPI.ovrAvatar2JointType jointType)
        {
            var newTransform = CreateNewTransform(jointType);
            return new TransformHolder(newTransform);
        }

        protected override void JointPosesUpdated()
        {
            // No-op, no bulk operations to perform with `TransformHolder`s
        }
    }

    abstract class OvrAvatarEntityJointMonitorCRTP<JointDataT>
        : EntityJointMonitorBase<JointDataT> where JointDataT : JointData
    {
        public OvrAvatarEntityJointMonitorCRTP(OvrAvatarEntity entity) : base(entity) {}

        protected override string LogScope => "OvrAvatarEntityJointMonitorCRTP";

        protected override void AddNewAnimationFrameForJoint(
            JointDataT jointData,
            in Vector3 objectSpacePosition,
            in Quaternion objectSpaceOrientation)
        {
            // Animation frames just overwrite the transform directly
#if UNITY_2021_3_OR_NEWER
            jointData.JointTransform.SetLocalPositionAndRotation(objectSpacePosition, objectSpaceOrientation);
#else // ^^^ UNITY_2021_3_OR_NEWER / !UNITY_2021_3_OR_NEWER vvv
            jointData.JointTransform.localPosition = objectSpacePosition;
            jointData.JointTransform.localRotation = objectSpaceOrientation;
#endif // !UNITY_2021_3_OR_NEWER
        }

        public override void UpdateJoints(float deltaTime)
        {
            // Intentionally empty
        }
    }

    internal class InterpolatingJoint : JointData
    {
        private Vector3 _objectSpacePosition0;
        private Vector3 _objectSpacePosition1;

        private Quaternion _objectSpaceOrientation0;
        private Quaternion _objectSpaceOrientation1;

        private float _lastInterpolationValue = 0.0f;

        public InterpolatingJoint(Transform tx) : base(tx) {}

        public override bool TryGetPosAndOrientation(out Vector3 pos, out Quaternion quat)
        {
            CalculateUpdate(_lastInterpolationValue, out pos, out quat);
            return true;
        }

        public void AddNewAnimationFrame(in Vector3 objectSpacePosition, in Quaternion objectSpaceOrientation)
        {
            // Shift the "latest frame"'s data to the "earliest frame"'s data and then
            // add in the new frame
            _objectSpacePosition0 = _objectSpacePosition1;
            _objectSpaceOrientation0 = _objectSpaceOrientation1;

            _objectSpacePosition1 = objectSpacePosition;
            _objectSpaceOrientation1 = objectSpaceOrientation;
        }

        public override void CalculateUpdate(float interpolationValue, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.Lerp(_objectSpacePosition0, _objectSpacePosition1, interpolationValue);
            rotation = Quaternion.Slerp(_objectSpaceOrientation0, _objectSpaceOrientation1, interpolationValue);
        }

        public void UpdateTransform(float interpolationValue)
        {
            _lastInterpolationValue = interpolationValue;

            CalculateUpdate(interpolationValue, out var pos, out var rot);

#if UNITY_2021_3_OR_NEWER
            JointTransform.SetLocalPositionAndRotation(pos, rot);
#else // ^^^ UNITY_2021_3_OR_NEWER / !UNITY_2021_3_OR_NEWER vvv
            JointTransform.localPosition = objectSpacePosition;
            JointTransform.localRotation = objectSpaceOrientation;
#endif // !UNITY_2021_3_OR_NEWER
        }
    }

    internal class OvrAvatarEntitySmoothingJointMonitor : EntityJointMonitorBase<InterpolatingJoint>
    {
        protected readonly IInterpolationValueProvider _interpolationProvider;

        public OvrAvatarEntitySmoothingJointMonitor(OvrAvatarEntity entity, IInterpolationValueProvider interpolationValueProvider) : base(entity)
        {
            _interpolationProvider = interpolationValueProvider;
        }

        protected override string LogScope => "SmoothingJointMonitor";

        protected override InterpolatingJoint CreateNewJointData(CAPI.ovrAvatar2JointType jointType)
        {
            var newTransform = CreateNewTransform(jointType);
            return new InterpolatingJoint(newTransform);
        }

        protected override void AddNewAnimationFrameForJoint(InterpolatingJoint jointData, in Vector3 objectSpacePosition, in Quaternion objectSpaceOrientation)
        {
            jointData.AddNewAnimationFrame(in objectSpacePosition, in objectSpaceOrientation);
        }

        public override void UpdateJoints(float deltaTime)
        {
            float interpolationValue = _interpolationProvider.GetRenderInterpolationValue();

            // Update all joints
            foreach (var joint in GetAllJointData())
            {
                joint.UpdateTransform(interpolationValue);
            }
        }

        protected override void JointPosesUpdated()
        {
            // No-op, no bulk operations to perform with `TransformHolder`s
        }
    }
}
