using Oculus.Avatar2;
using UnityEngine;
using static Oculus.Skinning.GpuSkinning.OvrComputeMeshAnimator;

/// @file OvrAvatarGpuInterpolatedSkinningRenderable

namespace Oculus.Skinning.GpuSkinning
{
    /**
     * Component that encapsulates the meshes of a skinned avatar.
     * This component implements skinning using the Avatar SDK
     * and uses the GPU. It performs skinning on every avatar
     * but not at every frame. Instead, it interpolates between
     * frames, reducing the performance overhead of skinning
     * when there are lots of avatars. It is used when the skinning configuration
     * is set to SkinningConfig.OVR_COMPUTE, "motion smoothing" and "support application spacewarp"
     * is enabled in the GPU skinning configuration.
     *
     * @see OvrAvatarSkinnedRenderable
     * @see OvrAvatarComputeSkinnedRenderable
     * @see OvrGpuSkinningConfiguration.MotionSmoothing
     */
    public class OvrAvatarComputeInterpolatedSkinnedMvRenderable : OvrAvatarComputeSkinnedRenderableBase
    {
        private struct RenderFrameData
        {
            public float InterpolationValue;
            public SkinningOutputFrame LatestOutputFrame;
            public SkinningOutputFrame PrevOutputFrame;

            public void Reset()
            {
                InterpolationValue = 1.0f;
                LatestOutputFrame = SkinningOutputFrame.FrameZero;
                PrevOutputFrame = SkinningOutputFrame.FrameZero;
            }
        }

        // Number of animation frames required to be considered "completely valid"
        protected override int NumAnimationFramesBeforeValidData => 2;

        protected override string LogScope => nameof(OvrAvatarComputeInterpolatedSkinnedMvRenderable);

        internal override OvrComputeUtils.MaxOutputFrames MeshAnimatorOutputFrames =>
            OvrComputeUtils.MaxOutputFrames.THREE;

        protected override bool InterpolateAttributes => true;

        public IInterpolationValueProvider InterpolationValueProvider { get; set; }

        private CAPI.ovrAvatar2Transform _prevSkinningOrigin;
        private CAPI.ovrAvatar2Transform _currentSkinningOrigin;

        private bool _hasValidPreviousRenderFrame;

        private SkinningOutputFrame _writeDestination = SkinningOutputFrame.FrameZero;
        private SkinningOutputFrame _prevWriteDestination = SkinningOutputFrame.FrameZero;

        private RenderFrameData _currentRenderFrameData;
        private RenderFrameData _prevRenderFrameData;

        protected override void Dispose(bool isDisposing)
        {
            InterpolationValueProvider = null;

            base.Dispose(isDisposing);
        }

        public override void UpdateSkinningOrigin(in CAPI.ovrAvatar2Transform skinningOrigin)
        {
            // Should be called every "animation frame"
            _prevSkinningOrigin = !IsAnimationDataCompletelyValid ? skinningOrigin : _currentSkinningOrigin;

            _currentSkinningOrigin = skinningOrigin;
        }

        protected override void OnAnimationEnabledChanged(bool isNowEnabled)
        {
            if (isNowEnabled)
            {
                // Reset valid frame counter on re-enabling animation
                NumValidAnimationFrames = 0;
                _writeDestination = SkinningOutputFrame.FrameZero;
                _prevWriteDestination = SkinningOutputFrame.FrameZero;

                _currentRenderFrameData.Reset();
                _prevRenderFrameData.Reset();

                _hasValidPreviousRenderFrame = false;
            }
        }

        protected virtual void OnEnable()
        {
            // No animation data yet since object just enabled (becoming visible)
            _hasValidPreviousRenderFrame = false;

            NumValidAnimationFrames = 0;
            _writeDestination = SkinningOutputFrame.FrameZero;
            _prevWriteDestination = SkinningOutputFrame.FrameZero;

            _currentRenderFrameData.Reset();
            _prevRenderFrameData.Reset();
        }

        internal override void AnimationFrameUpdate()
        {
            // Replaces logic in base class

            // ASSUMPTION: This call will always follow calls to update morphs and/or skinning.
            // With that assumption, new data will be written by the morph target combiner and/or skinner, so there
            // will be valid data at end of frame.
            IncrementValidAnimationFramesIfNeeded();

            // Update "current" and "previous" animation frame related data
            _prevWriteDestination = _writeDestination;
            _writeDestination = GetNextOutputFrame(_writeDestination, MeshAnimatorOutputFrames);

            if (MeshAnimator != null)
            {
                MeshAnimator.SetWriteDestination(_writeDestination);
                OvrAvatarManager.Instance.SkinningController.AddActivateComputeAnimator(MeshAnimator);
            }
        }

        internal override void RenderFrameUpdate()
        {
            Debug.Assert(InterpolationValueProvider != null);

            float lerpValue = InterpolationValueProvider.GetRenderInterpolationValue();

            // Guard against insufficient animation frames available
            // by "slamming" value to be 1.0 ("the newest value").
            // Should hopefully not happen frequently/at all if caller manages state well (maybe on first enabling)
            if (!IsAnimationDataCompletelyValid)
            {
                lerpValue = 1.0f;
            }

            // Update "current" and "previous" render frame related data
            // Slam "previous" values to be current values if there was no previous render frame
            if (!_hasValidPreviousRenderFrame)
            {
                _prevRenderFrameData.InterpolationValue = lerpValue;
                _prevRenderFrameData.LatestOutputFrame = _writeDestination;
                _prevRenderFrameData.PrevOutputFrame = _prevWriteDestination;
                _hasValidPreviousRenderFrame = true;
            }
            else
            {
                // Set previous render frame's data to be the "current" (which is about to get updated)
                _prevRenderFrameData.InterpolationValue = _currentRenderFrameData.InterpolationValue;
                _prevRenderFrameData.LatestOutputFrame = _currentRenderFrameData.LatestOutputFrame;
                _prevRenderFrameData.PrevOutputFrame = _currentRenderFrameData.PrevOutputFrame;
            }

            // Update current values
            _currentRenderFrameData.InterpolationValue = lerpValue;
            _currentRenderFrameData.LatestOutputFrame = _writeDestination;
            _currentRenderFrameData.PrevOutputFrame = _prevWriteDestination;

            InterpolateSkinningOrigin(lerpValue);
            SetAnimationInterpolationValuesInMaterial();
        }

        private void SetAnimationInterpolationValuesInMaterial()
        {
            // Update the interpolation value
            rendererComponent.GetPropertyBlock(MatBlock);

            MatBlock.SetInt(PropIds.AttributeOutputLatestAnimFrameEntryOffset, (int)_currentRenderFrameData.LatestOutputFrame);
            MatBlock.SetInt(PropIds.AttributeOutputPrevAnimFrameEntryOffset, (int)_currentRenderFrameData.PrevOutputFrame);
            MatBlock.SetFloat(PropIds.AttributeLerpValuePropId, _currentRenderFrameData.InterpolationValue);

            MatBlock.SetInt(PropIds.AttributeOutputPrevRenderFrameLatestAnimFrameOffset, (int)_prevRenderFrameData.LatestOutputFrame);
            MatBlock.SetInt(PropIds.AttributeOutputPrevRenderFramePrevAnimFrameOffset, (int)_prevRenderFrameData.PrevOutputFrame);
            MatBlock.SetFloat(PropIds.AttributePrevRenderFrameLerpValuePropId, _prevRenderFrameData.InterpolationValue);

            rendererComponent.SetPropertyBlock(MatBlock);
        }

        private void InterpolateSkinningOrigin(float lerpValue)
        {
            // Update the "skinning origin" via lerp/slerp.
            // NOTE: This feels dirty as we are converting from `OvrAvatar2Vector3f/Quat` to Unity
            // versions just to do the lerp/slerp. Unnecessary conversions
            transform.localPosition = Vector3.Lerp(
                _prevSkinningOrigin.position,
                _currentSkinningOrigin.position,
                lerpValue);
            transform.localRotation = Quaternion.Slerp(
                _prevSkinningOrigin.orientation,
                _currentSkinningOrigin.orientation,
                lerpValue);
            transform.localScale = Vector3.Lerp(
                _prevSkinningOrigin.scale,
                _currentSkinningOrigin.scale,
                lerpValue);
        }
    }
}
