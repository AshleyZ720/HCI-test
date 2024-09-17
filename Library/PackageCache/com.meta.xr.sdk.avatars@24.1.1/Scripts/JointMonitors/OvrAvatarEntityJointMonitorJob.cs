using System;
using System.Collections.Generic;
using UnityEngine;

using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;

using UnityEngine.Jobs;
using UnityEngine.Profiling;


namespace Oculus.Avatar2
{
    internal struct JointMonitorJobData : IDisposable
    {
        private const string logScope = "JointMonitorJobData";

        public bool MonitoredJointsChanged => _monitoredJointsChanged;

        public void MarkMonitoredJointsChanged()
        {
            _monitoredJointsChanged = true;
        }

        /* Transforms which will be updated - used to schedule the transform job */
        private TransformAccessArray _jobTransformAccess /*= default*/;
        /* Sized to the number of transforms which will actually be updated, stores the pose data used by the job */
        private NativeArray<JointPose> _jointJobNativeBuffer /*= default*/;

        /* Only needed for safe shutdown */
        private JobHandle _currentJob /*= default*/;

        /* Track whether the monitored joints have changed since the last update */
        private bool _monitoredJointsChanged /*= false*/;

        private readonly ProfilerMarker _marker;

        public JointMonitorJobData(string profileSegment)
        {
            _jobTransformAccess = default;
            _jointJobNativeBuffer = default;

            _currentJob = default;
            _monitoredJointsChanged = false;

            // TODO: T166227622 - Use custom ProfilerCategory
            _marker = new ProfilerMarker(ProfilerCategory.Animation, profileSegment);
        }

        public void Update<JointT>(
            int jointCount, float interpolationValue, ref JointT[] jobJoints, in Dictionary<CAPI.ovrAvatar2JointType, JointT>.ValueCollection jointData)
            where JointT : JointData
        {
            bool jointsDidChange = _monitoredJointsChanged;
            if (_monitoredJointsChanged)
            {
                Profiler.BeginSample("JointMonitorJob::RebuildBuffers");
                // Size all buffers to `jointCount`
                var newJobTransformAccess = new TransformAccessArray(jointCount);
                var newJointJobNativeBuffer = new NativeArray<JointPose>(
                    jointCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                if (jointCount != jobJoints.Length)
                {
                    Array.Resize(ref jobJoints, jointCount);
                }

                int regIdx = 0;
                Debug.Assert(jointData.Count == jointCount, logScope);

                // Populate new transformAccessArray and update current set of jobJoints
                foreach (var registeredJoint in jointData)
                {
                    jobJoints[regIdx++] = registeredJoint;
                    // note: filling via index does not work
                    newJobTransformAccess.Add(registeredJoint.JointTransform);
                }

                // Ensure current job is finished before disposing the arrays its using
                _currentJob.Complete();

                // Clean up old buffers
                if(_jobTransformAccess.isCreated) { _jobTransformAccess.Dispose(); }
                _jointJobNativeBuffer.Reset();

                // Swap in new ones
                _jobTransformAccess = newJobTransformAccess;
                _jointJobNativeBuffer = newJointJobNativeBuffer;

                _monitoredJointsChanged = false;

                Profiler.EndSample(); //"JointMonitorJob::RebuildBuffers"
            }

            /* Early out if we have 0 joints to update, attempting to schedule a 0 length job will crash */
            if (jointCount == 0) { return; }

            if (!jointsDidChange)
            {
                // Must wait for previous job to complete before updating `_jointJobNativeBuffer`
                _currentJob.Complete();
            }

            // Update all jointPoses
            _marker.Begin();
            int idx = 0;
            foreach (var joint in jobJoints)
            {
                // TODO: This could be moved into the IJob itself,
                // but we need to guard against adding new frames while it runs
                joint.CalculateUpdate(interpolationValue, out var pos, out var rot);
                _jointJobNativeBuffer[idx++] = new JointPose(in pos, in rot);
            }
            _marker.End();

            _currentJob = ScheduleUpdateTransformsJob(in _jointJobNativeBuffer, in _jobTransformAccess);
        }

        private static JobHandle ScheduleUpdateTransformsJob(in NativeArray<JointPose> joints, in TransformAccessArray transforms) {
            Debug.Assert(!joints.IsNull());
            Debug.Assert(joints.Length > 0);
            Debug.Assert(joints.Length == transforms.length);

            // TODO: Consider merging all updateTransforms jobs into one to kickoff in `SyncOutputComplete`
            // * Would reduce scheduling overhead at the cost of less granularity (higher chance to block on mainthread, for longer)
            var updateJob = new UpdateJointTransformsJob(joints);
            return updateJob.Schedule(transforms);
        }

        private static ProfilerMarker? _disposeMarker;
        private static ProfilerMarker DisposeMarker => _disposeMarker ??= new ProfilerMarker("JointMonitorJobData::Dispose");
        public void Dispose()
        {
            using var disposeScope = DisposeMarker.Auto();

            _currentJob.Complete();
            _currentJob = default;

            if (_jobTransformAccess.isCreated)
            {
                _jobTransformAccess.Dispose();
                _jobTransformAccess = default;
            }

            if (_jointJobNativeBuffer.IsCreated)
            {
                _jointJobNativeBuffer.Dispose();
                _jointJobNativeBuffer = default;
            }
        }

        /* Job Structs */

        private readonly struct JointPose
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;

            public JointPose(in Vector3 pos, in Quaternion rot)
            {
                Position = pos;
                Rotation = rot;
            }
        }

        private readonly struct UpdateJointTransformsJob : IJobParallelForTransform {
            public UpdateJointTransformsJob(in NativeArray<JointPose> joints) {
                _jointTransforms = joints;
            }

            [ReadOnly]
            private readonly NativeArray<JointPose> _jointTransforms;

            void IJobParallelForTransform.Execute(int index, TransformAccess txAccess)
            {
                // Avoid JointPose copy and NativeArray indexer
                unsafe
                {
                    var jointPose = _jointTransforms.GetReadonlyPtr() + index;

#if UNITY_2021_3_OR_NEWER
                    txAccess.SetLocalPositionAndRotation(jointPose->Position, jointPose->Rotation);
#else // ^^^ UNITY_2021_3_OR_NEWER / !UNITY_2021_3_OR_NEWER vvv
                    txAccess.localPosition = jointPose->Position;
                    txAccess.localRotation = jointPose->Rotation;
#endif // !UNITY_2021_3_OR_NEWER
                }
            }
        }
    }

    internal sealed class JobTransformHolder : TransformHolder
    {
        public JobTransformHolder(Transform tx) : base(tx) {}

        public Vector3 NextPosition = Vector3.zero;
        public Quaternion NextOrientation = Quaternion.identity;

        public override bool TryGetPosAndOrientation(out Vector3 pos, out Quaternion rot)
        {
            pos = NextPosition;
            rot = NextOrientation;
            return true;
        }

        public override void CalculateUpdate(float interpolationValue, out Vector3 position, out Quaternion rotation)
        {
            TryGetPosAndOrientation(out position, out rotation);
        }
    }

    internal sealed class OvrAvatarEntityJointJobMonitor : OvrAvatarEntityJointJobMonitorCRTP<JobTransformHolder>
    {
        protected override string LogScope => "OvrAvatarEntityJointJobMonitor";
        public OvrAvatarEntityJointJobMonitor(OvrAvatarEntity entity) : base(entity, "JointMonitorJob::UpdateJointPoses") { }

        protected override void AddNewAnimationFrameForJoint(
            JobTransformHolder jointData,
            in Vector3 objectSpacePosition,
            in Quaternion objectSpaceOrientation)
        {
            jointData.NextPosition = objectSpacePosition;
            jointData.NextOrientation = objectSpaceOrientation;
        }

        protected override JobTransformHolder CreateNewJointDataInstance(CAPI.ovrAvatar2JointType jointType)
        {
            var newTransform = CreateNewTransform(jointType);
            return new JobTransformHolder(newTransform);
        }

        protected override void JointPosesUpdated()
        {
            var jointData = _jobData.MonitoredJointsChanged ? GetAllJointData() : default;
            _jobData.Update(_activeJoints.Count
                , 0.0f
                , ref _jobJoints
                , in jointData);
        }

        public override void UpdateJoints(float deltaTime)
        {
            // Intentionally empty - there is no per-frame update
        }
    }

    internal sealed class OvrAvatarEntitySmoothingJointJobMonitor : OvrAvatarEntityJointJobMonitorCRTP<InterpolatingJoint>
    {
        protected override string LogScope => "OvrAvatarEntitySmoothingJointJobMonitor";

        private readonly IInterpolationValueProvider _interpolationProvider;

        public OvrAvatarEntitySmoothingJointJobMonitor(OvrAvatarEntity entity
            , IInterpolationValueProvider interpolationValueProvider)
            : base(entity, "SmoothingJointMonitorJob::UpdateJointPoses")
        {
            _interpolationProvider = interpolationValueProvider;
        }

        protected override InterpolatingJoint CreateNewJointDataInstance(CAPI.ovrAvatar2JointType jointType)
        {
            var newTransform = CreateNewTransform(jointType);
            return new InterpolatingJoint(newTransform);
        }

        protected override void AddNewAnimationFrameForJoint(InterpolatingJoint jointData, in Vector3 objectSpacePosition, in Quaternion objectSpaceOrientation)
        {
            jointData.AddNewAnimationFrame(in objectSpacePosition, in objectSpaceOrientation);
        }

        protected override void JointPosesUpdated()
        {
            // No-op, no bulk update operations to perform with `InterpolatingJoint`s - job is scheduled per-frame
        }

        public override void UpdateJoints(float deltaTime)
        {
            var jointData = _jobData.MonitoredJointsChanged ? GetAllJointData() : default;
            _jobData.Update(_activeJoints.Count
                , _interpolationProvider.GetRenderInterpolationValue()
                , ref _jobJoints
                , in jointData);
        }
    }

    abstract class OvrAvatarEntityJointJobMonitorCRTP<JointDataT> : OvrAvatarEntityJointMonitorCRTP<JointDataT>
        where JointDataT : JointData
    {
        protected abstract JointDataT CreateNewJointDataInstance(CAPI.ovrAvatar2JointType jointType);

        /* Used to quickly determine when the set of monitored joints changes */
        protected readonly HashSet<JointDataT> _activeJoints = new();

        /* Used as fast storage for the current set of monitored interpolating joints
         - in order to update `jointJobNativeBuffer_` before scheduling the next transform job */
        protected JointDataT[] _jobJoints = Array.Empty<JointDataT>();

        protected JointMonitorJobData _jobData;

        protected OvrAvatarEntityJointJobMonitorCRTP(OvrAvatarEntity entity, string profilerSegment) : base(entity)
        {
            _jobData = new(profilerSegment);
        }

        protected sealed override JointDataT CreateNewJointData(CAPI.ovrAvatar2JointType jointType)
        {
            var joint = CreateNewJointDataInstance(jointType);
            // TODO: This shouldn't be necessary, any call to `CreateNewJointData` should indicate the joints changed
            // For now this acts as a factor of safety
            if (_activeJoints.Add(joint))
            {
                _jobData.MarkMonitoredJointsChanged();
            }
            return joint;
        }

        protected sealed override void DisposeJointData(CAPI.ovrAvatar2JointType jointType, JointDataT jointData)
        {
            // TODO: This shouldn't be necessary, any call to `DisposeJointData` should indicate the joints changed
            // For now this acts as a factor of safety
            if (_activeJoints.Remove(jointData))
            {
                _jobData.MarkMonitoredJointsChanged();
            }
            base.DisposeJointData(jointType, jointData);
        }

        protected sealed override void Dispose(bool isDispose)
        {
            _jobData.Dispose();
            _jobData = default;

            _activeJoints.Clear();
            _jobJoints = null;

            base.Dispose(isDispose);
        }
    }
}
