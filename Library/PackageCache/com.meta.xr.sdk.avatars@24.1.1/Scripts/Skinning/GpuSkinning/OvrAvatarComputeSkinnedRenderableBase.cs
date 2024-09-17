using System;

using Oculus.Avatar2;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;
using UnityEngine.Profiling;

namespace Oculus.Skinning.GpuSkinning
{
    public abstract class OvrAvatarComputeSkinnedRenderableBase : OvrAvatarSkinnedRenderable
    {

        private const int BYTES_PER_MATRIX = sizeof(float) * 16;
        internal const int JOINT_DATA_SIZE = BYTES_PER_MATRIX; // one single matrix per joint

        protected abstract string LogScope { get; }
        internal abstract OvrComputeUtils.MaxOutputFrames MeshAnimatorOutputFrames { get; }

        protected override VertexFetchMode VertexFetchType => VertexFetchMode.ExternalBuffers;

        // Specifies the skinning quality (many bones per vertex).
        public OvrSkinningTypes.SkinningQuality SkinningQuality
        {
            get => _skinningQuality;
            set
            {
                if (_skinningQuality != value)
                {
                    _skinningQuality = value;
                    UpdateSkinningQuality();
                }
            }
        }

        private OvrComputeMeshAnimator _meshAnimator;
        internal OvrComputeMeshAnimator MeshAnimator => _meshAnimator;

        // This is technically configurable, but mostly just for debugging
        [SerializeField]
        [Tooltip(
            "Configuration to override SkinningQuality, otherwise indicates which Quality was selected for this LOD")]
        private OvrSkinningTypes.SkinningQuality _skinningQuality = OvrSkinningTypes.SkinningQuality.Invalid;

        private static PropertyIds _propertyIds = default;
        protected static PropertyIds PropIds => _propertyIds;

        private static void CheckPropertyIdInit()
        {
            if (!_propertyIds.IsValid)
            {
                _propertyIds = new PropertyIds(PropertyIds.InitMethod.PropertyToId);
            }
        }

        protected override void Awake()
        {
            base.Awake();
            CheckPropertyIdInit();
        }

        protected override void Dispose(bool isMainThread)
        {
            if (isMainThread)
            {
                DestroyGpuSkinningObjects();
            }
            else
            {
                OvrAvatarLog.LogError(
                    $"{nameof(OvrAvatarComputeSkinnedRenderable)} was disposed not on main thread, memory has been leaked.",
                    LogScope);
            }

            _meshAnimator = null;
            base.Dispose(isMainThread);
        }

        private void DestroyGpuSkinningObjects()
        {
            _meshAnimator?.Dispose();
            _morphWeightsHandle.Dispose();
            _morphWeightsHandle.DisposeOfNativeArray();
            _jointMatricesArray.Reset();
        }

        private void UpdateSkinningQuality()
        {
            if (_meshAnimator != null)
            {
                _meshAnimator.SkinningQuality = _skinningQuality;
            }
        }

        protected internal override void ApplyMeshPrimitive(OvrAvatarPrimitive primitive)
        {
            // The base call adds a mesh filter already and material
            base.ApplyMeshPrimitive(primitive);

            try
            {
                if (_skinningQuality == OvrSkinningTypes.SkinningQuality.Invalid)
                {
                    _skinningQuality =
                        GpuSkinningConfiguration.Instance.GetQualityForLOD(primitive.HighestQualityLODIndex);
                }

                if (primitive.morphTargetCount != 0)
                {
                    // Create native array for morph target weights
                    _morphWeightsHandle.Resize(name, (int)primitive.morphTargetCount);
                }

                if (primitive.joints.Length != 0)
                {
                    // Create native array for joint matrices
                    _jointMatricesArray = new NativeArray<Matrix4x4>(primitive.joints.Length, Allocator.Persistent);
                }

                AddGpuSkinningObjects(primitive);
                ApplyGpuSkinningMaterial();
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogError($"Exception applying primitive ({primitive}) - {e}", LogScope, this);
            }
        }

        private void ApplyGpuSkinningMaterial()
        {
            rendererComponent.GetPropertyBlock(MatBlock);

            ComputeBuffer positionBuffer = null;
            ComputeBuffer frenetBuffer = null;

            var positionScale = Vector3.one;
            var positionBias = Vector3.zero;
            var posDataFormat = OvrComputeUtils.ShaderFormatValue.FLOAT;

            if (_meshAnimator != null)
            {
                var animatorOutputScale = _meshAnimator.PositionOutputScale;
                Debug.Assert(animatorOutputScale.x > 0.0f && animatorOutputScale.y > 0.0f && animatorOutputScale.z > 0.0f);

                positionBuffer = _meshAnimator.GetPositionOutputBuffer();
                frenetBuffer = _meshAnimator.GetFrenetOutputBuffer();

                positionScale = new Vector3(1.0f / animatorOutputScale.x, 1.0f /animatorOutputScale.y, 1.0f / animatorOutputScale.z);
                positionBias = -1.0f *_meshAnimator.PositionOutputBias;

                posDataFormat =
                    OvrComputeUtils.GetDataFormatShaderPropertyValue(
                        _meshAnimator.PositionOutputFormatAndStride.dataFormat);
            }

            MatBlock.SetBuffer(_propertyIds.PositionOutputBufferPropId, positionBuffer);
            MatBlock.SetBuffer(_propertyIds.FrenetOutputBufferPropId, frenetBuffer);
            MatBlock.SetVector(_propertyIds.PositionScalePropId, positionScale);
            MatBlock.SetVector(_propertyIds.PositionBiasPropId, positionBias);

            MatBlock.SetInt(_propertyIds.PositionDataFormatPropId, (int)posDataFormat);

            MatBlock.SetInt(_propertyIds.NumOutputEntriesPerAttributePropId, (int)MeshAnimatorOutputFrames);

            Debug.Assert(
                positionBuffer != null,
                "No position buffer for compute skinning, avatars may not be able to move.");
            Debug.Assert(
                frenetBuffer != null,
                "No frenet buffer for compute skinning, avatars may not be able to move.");

            rendererComponent.SetPropertyBlock(MatBlock);
        }

        public override void ApplySkeleton(Transform[] bones)
        {
            // No-op
        }

        public override IDisposableBuffer CheckoutMorphTargetBuffer(uint morphCount)
        {
            Debug.Assert(_morphWeightsHandle.NativeArr.Length >= morphCount);
            return _morphWeightsHandle;
        }

        public override void MorphTargetBufferUpdated(IDisposableBuffer buffer)
        {
            if (buffer != _morphWeightsHandle || _meshAnimator == null)
            {
                return;
            }

            var array = _morphWeightsHandle.NativeArr;
            _meshAnimator.SetMorphTargetWeights(array);
        }

        public override bool UpdateJointMatrices(
            CAPI.ovrAvatar2EntityId entityId,
            OvrAvatarPrimitive primitive,
            CAPI.ovrAvatar2PrimitiveRenderInstanceID primitiveInstanceId)
        {
            if (_meshAnimator == null || !_jointMatricesArray.IsCreated)
            {
                return false;
            }

            int jointsCount = primitive.joints.Length;
            UInt32 bufferSize = (UInt32)(JOINT_DATA_SIZE * jointsCount);

            IntPtr transformsPtr;
            unsafe
            {
                transformsPtr = (IntPtr)_jointMatricesArray.GetUnsafePtr();
            }

            Profiler.BeginSample("GetSkinTransforms");
            const bool interLeaveNormalMatrices = false;
            var result = CAPI.ovrAvatar2Render_GetSkinTransforms(
                entityId,
                primitiveInstanceId,
                transformsPtr,
                bufferSize,
                interLeaveNormalMatrices);
            Profiler.EndSample();

            if (result == CAPI.ovrAvatar2Result.Success)
            {
                _meshAnimator?.SetJointMatrices(_jointMatricesArray);
            }
            else
            {
                Debug.LogError($"[OvrAvatarComputeSkinnedRenderable] Error: GetSkinTransforms ({primitive}) {result}");
            }

            return true;
        }

        private void AddGpuSkinningObjects(OvrAvatarPrimitive primitive)
        {
            // For now, just create source textures at runtime
            // TODO*: The texture creation should really be part of pipeline
            // and part of the input files from SDK and should be handled via
            // native plugin, but, for now, create via C#
            if (MyMesh)
            {
                var gpuSkinningConfig = GpuSkinningConfiguration.Instance;

                int numMorphTargets = (int)primitive.morphTargetCount;
                int numJoints = primitive.joints.Length;

                // Before we begin, check to see if we already have a skinner/morph target system set up:
                Debug.Assert(_meshAnimator == null, "Only one compute animator system can be created for Renderable.");
                if (primitive.computePrimitive == null || primitive.computePrimitive.VertexBuffer == null)
                {
                    OvrAvatarLog.LogWarning("Found null or invalid compute primitive", LogScope);
                    return;
                }

                if (gpuSkinningConfig.MorphAndSkinningComputeShader == null)
                {
                    OvrAvatarLog.LogError("Found null compute shader for compute skinning. Please specify a compute shader to use in the GPU skinning configuration.", LogScope);
                    return;
                }

                _meshAnimator = new OvrComputeMeshAnimator(
                    primitive.shortName,
                    gpuSkinningConfig.MorphAndSkinningComputeShader,
                    (int)primitive.meshVertexCount,
                    numMorphTargets,
                    numJoints,
                    primitive.computePrimitive,
                    gpuSkinningConfig,
                    HasTangents,
                    MeshAnimatorOutputFrames);
                _meshAnimator.SkinningQuality = SkinningQuality;
            } // if has mesh
        }

        internal static SkinningOutputFrame GetNextOutputFrame(
            SkinningOutputFrame current,
            OvrComputeUtils.MaxOutputFrames maxFrames)
        {
            return (SkinningOutputFrame)(((int)current + 1) % (int)maxFrames);
        }

        private class BufferHandle<T> : IDisposableBuffer where T : struct
        {
            private NativeArray<T> _nativeArr;
            public NativeArray<T> NativeArr => _nativeArr;

            public IntPtr BufferPtr
            {
                get
                {
                    IntPtr bufferPtr = IntPtr.Zero;
                    if (_nativeArr.IsCreated)
                    {
                        unsafe
                        {
                            bufferPtr = (IntPtr)_nativeArr.GetUnsafePtr();
                        }
                    }

                    return bufferPtr;
                }
            }

            public void Dispose()
            {
                // Buffer is persistent, no need for cleanup
                // TODO: Reference count to cover race conditions
            }

            public void DisposeOfNativeArray()
            {
                _nativeArr.Reset();
            }

            public void Resize(string name, int newCount)
            {
                _nativeArr.Reset();
                _nativeArr = new NativeArray<T>(newCount, Allocator.Persistent);
            }
        }

        // Really only 1 native array across all avatars is needed for these
        // since their data is only temporary as it should be immediately uploaded to GPU
        private readonly BufferHandle<float> _morphWeightsHandle = new BufferHandle<float>();
        private NativeArray<Matrix4x4> _jointMatricesArray;

    #if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            if (MyMeshFilter != null)
            {
                Mesh m = MyMeshFilter.sharedMesh;
                if (m != null)
                {
                    Gizmos.matrix = MyMeshFilter.transform.localToWorldMatrix;
                    Gizmos.DrawWireCube(m.bounds.center, m.bounds.size);
                }
            }
        }

        protected void OnValidate()
        {
            UpdateSkinningQuality();
        }
    #endif

        protected readonly struct PropertyIds
        {
            public readonly int PositionOutputBufferPropId;
            public readonly int FrenetOutputBufferPropId;
            public readonly int PositionScalePropId;
            public readonly int PositionBiasPropId;
            public readonly int PositionDataFormatPropId;
            public readonly int AttributeLerpValuePropId;

            public readonly int NumOutputEntriesPerAttributePropId;

            public readonly int AttributeOutputLatestAnimFrameEntryOffset;
            public readonly int AttributeOutputPrevAnimFrameEntryOffset;

            public readonly int AttributeOutputPrevRenderFrameLatestAnimFrameOffset;
            public readonly int AttributeOutputPrevRenderFramePrevAnimFrameOffset;
            public readonly int AttributePrevRenderFrameLerpValuePropId;

            // This will be 0 if default initialized, otherwise they are guaranteed unique
            public bool IsValid => FrenetOutputBufferPropId != PositionOutputBufferPropId;

            public enum InitMethod
            {
                PropertyToId
            }

            public PropertyIds(InitMethod initMethod)
            {
                PositionOutputBufferPropId = Shader.PropertyToID("_OvrPositionBuffer");
                FrenetOutputBufferPropId = Shader.PropertyToID("_OvrFrenetBuffer");

                PositionScalePropId = Shader.PropertyToID("_OvrPositionScale");
                PositionBiasPropId = Shader.PropertyToID("_OvrPositionBias");
                PositionDataFormatPropId = Shader.PropertyToID("_OvrPositionDataFormat");

                AttributeLerpValuePropId = Shader.PropertyToID("_OvrAttributeInterpolationValue");

                AttributeOutputLatestAnimFrameEntryOffset =
                    Shader.PropertyToID("_OvrAttributeOutputLatestAnimFrameEntryOffset");
                AttributeOutputPrevAnimFrameEntryOffset =
                    Shader.PropertyToID("_OvrAttributeOutputPrevAnimFrameEntryOffset");

                NumOutputEntriesPerAttributePropId = Shader.PropertyToID("_OvrNumOutputEntriesPerAttribute");

                // Motion vectors (application spacewarp) related properties
                AttributeOutputPrevRenderFrameLatestAnimFrameOffset =
                    Shader.PropertyToID("_OvrAttributeOutputPrevRenderFrameLatestAnimFrameOffset");
                AttributeOutputPrevRenderFramePrevAnimFrameOffset =
                    Shader.PropertyToID("_OvrAttributeOutputPrevRenderFramePrevAnimFrameOffset");
                AttributePrevRenderFrameLerpValuePropId = Shader.PropertyToID("_OvrPrevRenderFrameInterpolationValue");
            }
        }
    }
}
