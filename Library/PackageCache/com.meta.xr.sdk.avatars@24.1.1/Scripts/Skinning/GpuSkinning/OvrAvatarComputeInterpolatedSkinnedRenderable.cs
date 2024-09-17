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
     * is set to SkinningConfig.OVR_COMPUTE and motion smoothing
     * is enabled in the GPU skinning configuration.
     *
     * @see OvrAvatarSkinnedRenderable
     * @see OvrAvatarComputeSkinnedRenderable
     * @see OvrGpuSkinningConfiguration.MotionSmoothing
     */
    public class OvrAvatarComputeInterpolatedSkinnedRenderable : OvrAvatarComputeSkinnedRenderableBase
    {
        // Number of animation frames required to be considered "completely valid"
        protected override int NumAnimationFramesBeforeValidData => 2;

        protected override string LogScope => nameof(OvrAvatarComputeInterpolatedSkinnedRenderable);

        internal override OvrComputeUtils.MaxOutputFrames MeshAnimatorOutputFrames =>
            OvrComputeUtils.MaxOutputFrames.TWO;

        protected override bool InterpolateAttributes => true;

        public IInterpolationValueProvider InterpolationValueProvider { get; set; }

        private CAPI.ovrAvatar2Transform _skinningOrigin;
        private CAPI.ovrAvatar2Transform _prevSkinningOrigin;

        private bool _invertInterpolationValue;

        private SkinningOutputFrame _writeDestination = SkinningOutputFrame.FrameZero;
        private SkinningOutputFrame _prevAnimFrameWriteDest = SkinningOutputFrame.FrameZero;

        protected override void Dispose(bool isDisposing)
        {
            InterpolationValueProvider = null;

            base.Dispose(isDisposing);
        }

        public override void UpdateSkinningOrigin(in CAPI.ovrAvatar2Transform skinningOrigin)
        {
            _prevSkinningOrigin = _skinningOrigin;
            _skinningOrigin = skinningOrigin;
        }

        protected override void OnAnimationEnabledChanged(bool isNowEnabled)
        {
            if (isNowEnabled)
            {
                // Reset valid frame counter on re-enabling animation
                NumValidAnimationFrames = 0;
                _writeDestination = SkinningOutputFrame.FrameZero;
                _prevAnimFrameWriteDest = _writeDestination;
            }
        }

        internal override void AnimationFrameUpdate()
        {
            // Replaces logic in base class

            // ASSUMPTION: This call will always follow calls to update morphs and/or skinning.
            // With that assumption, new data will be written by the morph target combiner and/or skinner, so there
            // will be valid data at end of frame.

            // Set "previous anim frame" field
            _prevAnimFrameWriteDest = _writeDestination;
            _writeDestination = GetNextOutputFrame(_writeDestination, MeshAnimatorOutputFrames);

            IncrementValidAnimationFramesIfNeeded();

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

            InterpolateSkinningOrigin(lerpValue);
            SetAnimationInterpolationValuesInMaterial(lerpValue);
        }

        private void SetAnimationInterpolationValuesInMaterial(float lerpValue)
        {
            // Update the interpolation value
            rendererComponent.GetPropertyBlock(MatBlock);

            MatBlock.SetFloat(PropIds.AttributeLerpValuePropId, lerpValue);
            MatBlock.SetInt(PropIds.AttributeOutputLatestAnimFrameEntryOffset, (int)_writeDestination);
            MatBlock.SetInt(PropIds.AttributeOutputPrevAnimFrameEntryOffset, (int)_prevAnimFrameWriteDest);

            rendererComponent.SetPropertyBlock(MatBlock);
        }

        private void InterpolateSkinningOrigin(float lerpValue)
        {
            // Update the "skinning origin" via lerp/slerp.
            // NOTE: This feels dirty as we are converting from `OvrAvatar2Vector3f/Quat` to Unity
            // versions just to do the lerp/slerp. Unnecessary conversions
            transform.localPosition = Vector3.Lerp(
                _prevSkinningOrigin.position,
                _skinningOrigin.position,
                lerpValue);
            transform.localRotation = Quaternion.Slerp(
                _prevSkinningOrigin.orientation,
                _skinningOrigin.orientation,
                lerpValue);
            transform.localScale = Vector3.Lerp(
                _prevSkinningOrigin.scale,
                _skinningOrigin.scale,
                lerpValue);
        }
    }
}
