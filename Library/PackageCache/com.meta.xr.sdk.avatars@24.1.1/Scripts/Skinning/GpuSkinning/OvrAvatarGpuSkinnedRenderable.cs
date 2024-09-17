using System;
using System.Runtime.InteropServices;

using Oculus.Avatar2;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

/// @file OvrAvatarGpuSkinnedRenderable.cs

namespace Oculus.Skinning.GpuSkinning
{
    /**
     * Component that encapsulates the meshes of a skinned avatar.
     * This component implements skinning using the Avatar SDK
     * and uses the GPU. It performs skinning on every avatar
     * at each frame. It is used when the skinning configuration
     * is set to SkinningConfig.OVR_UNITY_GPU_FULL and motion smoothing
     * is *not* enabled in the GPU skinning configuration.
     *
     * @see OvrAvatarSkinnedRenderable
     * @see OvrAvatarGpuSkinnedRenderable
     * @see OvrGpuSkinningConfiguration.MotionSmoothing
     * @see OvrAvatarGpuInterpolatedSkinnedRenderable
     */
    public class OvrAvatarGpuSkinnedRenderable : OvrAvatarGpuSkinnedRenderableBase
    {
        // Only need single frame of "animation/skinning data" before validity
        protected override int NumAnimationFramesBeforeValidData => 1;
        protected override string LogScope => "OvrAvatarGpuSkinnedRenderable";

        protected override void OnAnimationEnabledChanged(bool isNowEnabled)
        {
            if (isNowEnabled)
            {
                NumValidAnimationFrames = 0;
            }
        }

        internal override void AnimationFrameUpdate()
        {
            // Mark this block as enabled and note that there will be
            // valid data this frame

            // ASSUMPTION: This call will always be followed by calls to update morphs and/or skinning.
            // With that assumption, new data will be written by the morph target combiner and/or skinner, so there
            // will be valid data at end of frame.
            IncrementValidAnimationFramesIfNeeded();
        }

        internal override void RenderFrameUpdate()
        {
            // Intentionally empty
        }
    }
}
