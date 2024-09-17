using System;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;
using UnitySkinningQuality = UnityEngine.SkinWeights;

/// @file OvrAvatarManager_Skinning.cs

namespace Oculus.Avatar2
{
    public partial class OvrAvatarManager
    {
        ///
        /// Skinning implementation types.
        /// Used by @ref OvrAvatarManager to designate skinning implementation.
        [Flags]
        [System.Serializable]
        public enum SkinnerSupport
        {
            /// NO RENDER - No rendering data is built or stored, sim only (headless server)
            [InspectorName("None")]
            NONE = 0,
            /// Mesh data is loaded into standard `Unity.Mesh` fields
            [InspectorName("Unity Built-In Skinning")]
            UNITY = 1 << 0,
            /// Animation mesh data is stored in AvatarSDK internal buffers, it is not available to Unity systems
            [InspectorName("(Not Implemented) CPU Skinning")]
            OVR_CPU = 1 << 1,
            /// Mesh data is primarily stored in textures and compute buffers, it is not available to Unity systems
            [InspectorName("(Deprecated) GPU Skinning")]
            OVR_GPU = 1 << 2,
            [InspectorName("Compute Shader Skinning")]
            OVR_COMPUTE = 1 << 3,
            /// DEBUG ONLY - All modes are supported, wastes lots of memory
            [InspectorName("(Debug Only) All")]
            ALL = ~0
        }

        [Header("Skinning Settings")]
        [Tooltip("Skinning type to be used on all avatars. We recommend only setting one skinningType (multiple skinning types use large amounts of resources). We also recommend OVR_COMPUTE skinning because it uses significantly less resources than other skinners. If you must use multiple skinners, enable them here and add the OvrAvatarSkinningOverride component alongside your OvrAvatarEntity.")]
        [SerializeField]
        private SkinnerSupport _skinningType = SkinnerSupport.OVR_COMPUTE;

        private bool _skinnersValidated;

        [Header("Unity Skinning")]
        [SerializeField]
        private SkinQuality[] _skinQualityPerLOD = Array.Empty<SkinQuality>();

        public bool UnitySMRSupported => (_skinningType & SkinnerSupport.UNITY) == SkinnerSupport.UNITY;


        private const int GpuSkinningRequiredFeatureLevel = 45;
        private const int ComputeSkinningRequiredFeatureLevel = 45;

        // OVR_CPU skinner currently unimplemented
        public bool OvrCPUSkinnerSupported => false;

        public bool OvrGPUSkinnerSupported =>
            gpuSkinningShaderLevelSupported && (_skinningType & SkinnerSupport.OVR_GPU) == SkinnerSupport.OVR_GPU;

        public bool OvrComputeSkinnerSupported => systemSupportsComputeSkinner &&
            ((_skinningType & SkinnerSupport.OVR_COMPUTE) == SkinnerSupport.OVR_COMPUTE);

        public bool UnitySkinnerSupported =>
            (_skinningType & SkinnerSupport.UNITY) == SkinnerSupport.UNITY;

        // Set via `Initialize`
        private int _shaderLevelSupport = -1;
        internal bool gpuSkinningShaderLevelSupported
        {
            get
            {
                Debug.Assert(_shaderLevelSupport >= 0);
                return _shaderLevelSupport >= GpuSkinningRequiredFeatureLevel;
            }
        }

        internal bool systemSupportsComputeSkinner =>
            (_shaderLevelSupport >= ComputeSkinningRequiredFeatureLevel) &&
            (SystemInfo.maxComputeBufferInputsVertex > 2) && (SystemInfo.supportsComputeShaders);

        public SkinQuality GetUnitySkinQualityForLODIndex(uint lodIndex)
        {
            return lodIndex < _skinQualityPerLOD.Length ?
                (SkinQuality)Mathf.Min((int)_skinQualityPerLOD[lodIndex], (int)HighestUnitySkinningQuality)
                : HighestUnitySkinningQuality;
        }

        // Helper to query Unity skinWeights/boneWeights configuration as SkinningQuality enum
        public SkinQuality HighestUnitySkinningQuality
        {
            get
            {
                switch (QualitySettings.skinWeights)
                {
                    case UnitySkinningQuality.OneBone:
                        return SkinQuality.Bone1;
                    case UnitySkinningQuality.TwoBones:
                        return SkinQuality.Bone2;
                    case UnitySkinningQuality.FourBones:
                        return SkinQuality.Bone4;
                }
                return SkinQuality.Auto;
            }
        }

        private static bool IsMoreThanOneBitSet(int n)
        {
            return n != 0 && (n & (n - 1)) != 0;
        }

        private void ValidateSupportedSkinners()
        {
            if (!gpuSkinningShaderLevelSupported && (_skinningType & SkinnerSupport.OVR_GPU) == SkinnerSupport.OVR_GPU)
            {
                // gpu skinning not actually supported so remove from supported list.
                _skinningType &= ~SkinnerSupport.OVR_GPU;
                if (_skinningType == SkinnerSupport.NONE)
                {
                    _skinningType = SkinnerSupport.UNITY;
                }
            }

            // See if compute skinner was chosen but not compatible with system
            bool computeSkinnerSelected =
                (_skinningType & SkinnerSupport.OVR_COMPUTE) == SkinnerSupport.OVR_COMPUTE;
            if (!systemSupportsComputeSkinner && computeSkinnerSelected)
            {
                // compute skinning not actually supported so remove from supported list.
                _skinningType &= ~SkinnerSupport.OVR_COMPUTE;
                if (_skinningType == SkinnerSupport.NONE)
                {
                    _skinningType = SkinnerSupport.UNITY;
                }
            }

            if (IsMoreThanOneBitSet((int)_skinningType))
            {
                OvrAvatarLog.LogWarning("There is more than one supported skinner set. This is discouraged outside " +
                                        "of debugging because it will use a large amount of memory.");
            }

            _skinnersValidated = true;
        }

        public OvrAvatarEntity.SkinningConfig GetBestSupportedSkinningConfig()
        {
            Debug.Assert(_skinnersValidated, "Please call ValidateSupportedSkinners before GetBestSupportedSkinner");

            if ((_skinningType & SkinnerSupport.OVR_COMPUTE) != 0)
            {
                return OvrAvatarEntity.SkinningConfig.OVR_COMPUTE;
            }
            if ((_skinningType & SkinnerSupport.OVR_GPU) != 0)
            {
                return OvrAvatarEntity.SkinningConfig.OVR_GPU;
            }
            if ((_skinningType & SkinnerSupport.UNITY) != 0)
            {
                return OvrAvatarEntity.SkinningConfig.UNITY;
            }

            if ((_skinningType & SkinnerSupport.OVR_CPU) != 0)
            {
                Debug.LogWarning("OVR_CPU skinning is unimplemented. We recommend setting the skinning type to OVR_COMPUTE");
                // fallthru, return SkinningConfig.NONE
            }

            return OvrAvatarEntity.SkinningConfig.NONE;
        }

    }
}
