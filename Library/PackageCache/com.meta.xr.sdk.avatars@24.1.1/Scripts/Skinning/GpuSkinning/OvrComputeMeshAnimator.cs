// Due to Unity bug (fixed in version 2021.2), copy to a native array then copy native array to ComputeBuffer in one chunk
// (ComputeBuffer.SetData erases previously set data)
// https://issuetracker.unity3d.com/issues/partial-updates-of-computebuffer-slash-graphicsbuffer-using-setdata-dont-preserve-existing-data-when-using-opengl-es

// This is broken even in Unity 2021.2+, but only only on Quest 3. Will replace with persistent buffers for v25 hopefully.
// #define COMPUTE_BUFFER_PARTIAL_UPDATE_ALLOWED

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Oculus.Avatar2;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;

namespace Oculus.Skinning.GpuSkinning
{
    internal sealed class OvrComputeMeshAnimator : IDisposable
    {
        private const string LOG_SCOPE = "OvrComputeMeshAnimator";

        private const int WEIGHTS_STRIDE_BYTES = 4; // 32-bit float per morph target
        private const int JOINT_MATRIX_STRIDE_BYTES = 16 * 4; // 4x4 32-bit float matrices per joint matrix
        private const int SLICE_OUTPUT_STRIDE_BYTES = 4; // a single 32-bit int

        private const int VERTEX_BUFFER_META_DATA_OFFSET = 0; // always 0 (no batching)
        private const int VERTEX_INFO_DATA_OFFSET = 0; // always 0 (no batching)
        private const int POSITION_OUTPUT_OFFSET = 0; // always 0 (no batching)
        private const int FRENET_OUTPUT_OFFSET = 0; // always 0 (no batching)


        public OvrSkinningTypes.SkinningQuality SkinningQuality
        {
            get => _quality;
            set
            {
                _quality = value;
                _shader.SkinningQuality = value;
            }
        }

        private OvrSkinningTypes.SkinningQuality _quality;

        public OvrComputeMeshAnimator(
            string name,
            ComputeShader shader,
            int numMeshVerts,
            int numMorphTargets,
            int numJoints,
            OvrAvatarComputeSkinnedPrimitive gpuPrimitive,
            GpuSkinningConfiguration gpuSkinningConfiguration,
            bool hasTangents,
            OvrComputeUtils.MaxOutputFrames maxOutputFrames)
        {
            Debug.Assert(shader != null);
            Debug.Assert(gpuPrimitive != null);
            Debug.Assert(gpuPrimitive.VertexBuffer != null);

            _numMeshVerts = numMeshVerts;
            _numMorphTargetWeights = numMorphTargets;
            _numJointMatrices = numJoints;

            _vertexBuffer = gpuPrimitive.VertexBuffer;

            _hasMorphTargets = _vertexBuffer.NumMorphedVerts > 0;
            _hasJoints = _numJointMatrices > 0;

            if (IsNormalizedFormat(gpuSkinningConfiguration.PositionOutputFormat))
            {
                var configurationNormalizationBias = gpuSkinningConfiguration.SkinningPositionOutputNormalizationBias;
                var configurationNormalizationScale = gpuSkinningConfiguration.SkinningPositionOutputNormalizationScale;

                Debug.Assert(configurationNormalizationScale > 0.0f);

                // The configuration specifies where the "center" is
                // and the scale of the "bounds". So the mesh animator's
                // scale and bias and the inverse of the configuration
                // settings.
                _positionOutputBias = new Vector3(
                    -configurationNormalizationBias,
                    -configurationNormalizationBias,
                    -configurationNormalizationBias);
                _positionOutputScale = new Vector3(
                    1.0f / configurationNormalizationScale,
                    1.0f / configurationNormalizationScale,
                    1.0f / configurationNormalizationScale);
            }
            else
            {
                _positionOutputBias = Vector3.zero;
                _positionOutputScale = Vector3.one;
            }

            _maxOutputFrames = maxOutputFrames;

            var vertexInfosOffset = CreatePerInstanceBuffer(
                name,
                VERTEX_BUFFER_META_DATA_OFFSET,
                gpuPrimitive.MeshAndCompactSkinningIndices);

            _positionDataFormatAndStride = GetOutputPositionDataFormat(gpuSkinningConfiguration.PositionOutputFormat);

            CreateOutputBuffers(name, hasTangents);

            var shaderParams = new OvrComputeMeshAnimatorShader.InitParams
            {
                hasMorphTargets = _hasMorphTargets,
                hasTangents = hasTangents,
                numOutputSlices = _maxOutputFrames,
                vertexBuffer = gpuPrimitive.VertexBuffer.Buffer,
                perInstanceBuffer = _perInstanceBufferUpdater.PerInstanceBuffer,
                vertexInfoOffset = vertexInfosOffset,
                positionOutputBuffer = _positionOutputBuffer,
                frenetOutputBuffer = _frenetOutputBuffer,
                vertexBufferFormatsAndStrides = gpuPrimitive.VertexBuffer.FormatsAndStrides,
                positionOutputBufferDataFormatAndStride = _positionDataFormatAndStride,
                applyAdditionalTransform = !gpuPrimitive.VertexBuffer.IsDataInClientSpace,
                clientSpaceTransform = gpuPrimitive.VertexBuffer.ClientSpaceTransform,
            };

            _shader = new OvrComputeMeshAnimatorShader(shader, shaderParams);
            _shader.SkinningQuality = SkinningQuality;
            UpdateOutputs();
        }

        public ComputeBuffer GetPositionOutputBuffer()
        {
            return _positionOutputBuffer;
        }

        public ComputeBuffer GetFrenetOutputBuffer()
        {
            return _frenetOutputBuffer;
        }

        public Vector3 PositionOutputScale => _positionOutputScale;

        public Vector3 PositionOutputBias => _positionOutputBias;

        public OvrComputeUtils.DataFormatAndStride PositionOutputFormatAndStride => _positionDataFormatAndStride;

        public void SetMorphTargetWeights(in NativeArray<float> weights)
        {
            Debug.Assert(weights.Length == _numMorphTargetWeights);
            if (_hasMorphTargets)
            {
                _perInstanceBufferUpdater.SetMorphTargetWeights(weights);
            }
        }

        public void SetJointMatrices(in NativeArray<Matrix4x4> matrices)
        {
            Debug.Assert(matrices.Length == _numJointMatrices);
            if (_hasJoints)
            {
                _perInstanceBufferUpdater.SetJointMatrices(matrices);
            }
        }

        public void SetWriteDestination(SkinningOutputFrame writeDestination)
        {
            Debug.Assert((int)writeDestination < (int)_maxOutputFrames);
            var writeDestinationAsUint = (UInt32)writeDestination;
            _perInstanceBufferUpdater.SetWriteDestinationInBuffer(writeDestinationAsUint);
        }

        public void UpdateOutputs()
        {
            _perInstanceBufferUpdater.UpdateComputeBufferBeforeDispatch();

            // Just a single dispatch (sacrifice some GPU time for saving CPU time)
            // Potentially do three dispatches if there are morphs and skinning and neither
            const int START_INDEX = 0;
            _shader.Dispatch(START_INDEX, _numMeshVerts);
        }

        private bool IsNormalizedFormat(GpuSkinningConfiguration.PositionOutputDataFormat format)
        {
            switch (format)
            {
                case GpuSkinningConfiguration.PositionOutputDataFormat.Unorm16:
                    return true;
                case GpuSkinningConfiguration.PositionOutputDataFormat.Unorm8:
                    return true;
                default:
                    return false;
            }
        }

        private void InitializeJointMatrices()
        {
            using var identityMatrices = new NativeArray<Matrix4x4>(_numJointMatrices, Allocator.Temp);
            SetJointMatrices(identityMatrices);
        }

        private void InitializeMorphTargetWeights()
        {
            using var zeroWeights = new NativeArray<float>(_numMorphTargetWeights, Allocator.Temp);
            SetMorphTargetWeights(zeroWeights);
        }

        private void InitializeWriteDestination()
        {
            _perInstanceBufferUpdater.SetWriteDestinationInBuffer(0u);
        }

        private OvrComputeUtils.DataFormatAndStride GetOutputPositionDataFormat(
            GpuSkinningConfiguration.PositionOutputDataFormat format)
        {
            // Positions need to be vec4s (with the 1.0 in w component) and are expected
            // to be on 4 byte boundary
            switch (format)
            {
                case GpuSkinningConfiguration.PositionOutputDataFormat.Float:
                    return new OvrComputeUtils.DataFormatAndStride(
                        CAPI.ovrAvatar2DataFormat.F32,
                        UnsafeUtility.SizeOf<Vector4>());
                case GpuSkinningConfiguration.PositionOutputDataFormat.Half:
                    // Output expected to be on 4 byte boundary
                    return new OvrComputeUtils.DataFormatAndStride(
                        CAPI.ovrAvatar2DataFormat.F16,
                        (int)OvrComputeUtils.GetUintAlignedSize(4 * 2));
                case GpuSkinningConfiguration.PositionOutputDataFormat.Unorm16:
                    // Output expected to be on 4 byte boundary
                    return new OvrComputeUtils.DataFormatAndStride(
                        CAPI.ovrAvatar2DataFormat.Unorm16,
                        (int)OvrComputeUtils.GetUintAlignedSize(4 * 2));
                case GpuSkinningConfiguration.PositionOutputDataFormat.Unorm8:
                    // Output expected to be on 4 byte boundary
                    return new OvrComputeUtils.DataFormatAndStride(
                        CAPI.ovrAvatar2DataFormat.Unorm8,
                        (int)OvrComputeUtils.GetUintAlignedSize(4 * 1));
                default:
                    OvrAvatarLog.LogInfo("Unhandled output position format, using default", LOG_SCOPE);
                    return new OvrComputeUtils.DataFormatAndStride(
                        CAPI.ovrAvatar2DataFormat.F32,
                        UnsafeUtility.SizeOf<Vector4>());
            }
        }

        // Returns the "vertexInfoOffset"
        private int CreatePerInstanceBuffer(
            string name,
            uint vertexBufferMetaDataOffsetBytes,
            in NativeArray<OvrAvatarComputeSkinnedPrimitive.VertexIndices> vertIndices)
        {
            // Data layout for "per instance" buffer is
            // [a single mesh_instance_meta_data]
            // [_numJointMatrices float4x4] -> An array joint matrices
            // [_numMorphTargetWeights floats] -> An array of morph target weights
            // [output_slice as uint] -> output slice

            // Calculate the necessary offset/sizes
            var meshInstanceMetaDataSize = UnsafeUtility.SizeOf<MeshInstanceMetaData>();
            var vertexInfoDataSize = UnsafeUtility.SizeOf<VertexInfo>();

            var sizeOfVertexInfos = _numMeshVerts * vertexInfoDataSize;
            var sizeOfMeshInstanceMetaDatas = meshInstanceMetaDataSize;
            var sizeOfJointMatrices = _numJointMatrices * JOINT_MATRIX_STRIDE_BYTES;
            var sizeOfMorphTargetWeights = _numMorphTargetWeights * WEIGHTS_STRIDE_BYTES;
            var sizeOfOutputSlice = SLICE_OUTPUT_STRIDE_BYTES;

            var totalBufferSize = sizeOfVertexInfos + sizeOfMeshInstanceMetaDatas + sizeOfJointMatrices +
                sizeOfMorphTargetWeights + sizeOfOutputSlice;

            var vertexInfosOffset = VERTEX_INFO_DATA_OFFSET;
            var meshInstanceMetaDataOffset = vertexInfosOffset + sizeOfVertexInfos;
            var jointMatricesOffset = meshInstanceMetaDataOffset + sizeOfMeshInstanceMetaDatas;
            var morphTargetWeightsOffset = jointMatricesOffset + sizeOfJointMatrices;
            var outputSliceOffset = morphTargetWeightsOffset + sizeOfMorphTargetWeights;

            /////////////////////////////////////////////////////////////////////////////////////////////////
            // First, create an array of "VertexInfos" which will be at the beginning of the per instance
            // data buffer.
            // Then create single MeshInstanceMetaData
            var vertexInfos = new NativeArray<VertexInfo>(
                _numMeshVerts,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            var singleMeshInstanceMetaData = new NativeArray<MeshInstanceMetaData>(
                1,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            try
            {
                ////////////////////////////////////////////////////////////////////////////////////////////////////
                // Now write out the single MeshInstanceMetaData
                singleMeshInstanceMetaData[0] = new MeshInstanceMetaData
                {
                    vertexBufferMetaDataOffsetBytes = vertexBufferMetaDataOffsetBytes,
                    morphTargetWeightsOffsetBytes = (uint)morphTargetWeightsOffset,
                    jointMatricesOffsetBytes = (uint)jointMatricesOffset,
                    outputPositionsOffsetBytes = POSITION_OUTPUT_OFFSET,
                    outputFrenetOffsetBytes = FRENET_OUTPUT_OFFSET,
                    outputSliceOffsetBytes = (uint)outputSliceOffset,
                    vertexOutputPositionBias = _positionOutputBias,
                    vertexOutputPositionScale = _positionOutputScale,
                };

                // The data in the "vertexInfos", for better compute shader cache usage
                // should be in "vertex buffer index" order. Spend time here doing that sort
                for (int index = 0; index < vertIndices.Length; ++index)
                {
                    var indices = vertIndices[index];

                    vertexInfos[index] = new VertexInfo
                    {
                        meshInstanceDataOffsetBytes = (uint)meshInstanceMetaDataOffset,
                        vertexBufferIndex = indices.compactSkinningIndex,
                        outputBufferIndex = indices.outputBufferIndex,
                    };
                }

#if (COMPUTE_BUFFER_PARTIAL_UPDATE_ALLOWED)
                _perInstanceBufferUpdater = new PerInstanceBufferDirectUpdater(
                    name,
                    singleMeshInstanceMetaData,
                    vertexInfos,
                    totalBufferSize,
                    meshInstanceMetaDataOffset,
                    morphTargetWeightsOffset,
                    jointMatricesOffset,
                    outputSliceOffset,
                    vertexInfosOffset);
#else
                _perInstanceBufferUpdater = new PerInstanceBufferUpdaterViaNativeArray(
                    name,
                    singleMeshInstanceMetaData,
                    vertexInfos,
                    totalBufferSize,
                    meshInstanceMetaDataOffset,
                    morphTargetWeightsOffset,
                    jointMatricesOffset,
                    outputSliceOffset,
                    vertexInfosOffset);
#endif
            }
            finally
            {
                vertexInfos.Reset();
                singleMeshInstanceMetaData.Reset();
            }

            InitializeJointMatrices();
            InitializeMorphTargetWeights();
            InitializeWriteDestination();

            return vertexInfosOffset;
        }

        private void CreateOutputBuffers(string name, bool hasTangents)
        {
            const string POS_OUTPUT_SUFFIX = "PositionOutput";
            const string FRENET_OUTPUT_SUFFIX = "FrenetOutput";

            int numOutputSlices = (int)_maxOutputFrames;
            _positionOutputBuffer = OvrComputeUtils.CreateRawComputeBuffer(
                GetPositionOutputBufferSize(_numMeshVerts, _positionDataFormatAndStride.strideBytes, numOutputSlices));
            _frenetOutputBuffer = OvrComputeUtils.CreateRawComputeBuffer(
                GetFrenetOutputBufferSize(_numMeshVerts, hasTangents, numOutputSlices));

            _positionOutputBuffer.name = $"{name}_{POS_OUTPUT_SUFFIX}";
            _frenetOutputBuffer.name = $"{name}_{FRENET_OUTPUT_SUFFIX}";
        }

        private static uint GetPositionOutputBufferSize(int numMeshVerts, int positionStrideBytes, int numOutputSlices)
        {
            return (uint)(positionStrideBytes * numMeshVerts * numOutputSlices);
        }

        private static uint GetFrenetOutputBufferSize(int numMeshVerts, bool hasTangents, int numOutputSlices)
        {
            const int FRENET_ATTRIBUTE_STRIDE_BYTES = 4; // only supporting 10-10-10-2 format for normal/tangents

            return (uint)(FRENET_ATTRIBUTE_STRIDE_BYTES * numMeshVerts * (hasTangents ? 2 : 1) * numOutputSlices);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MeshInstanceMetaData
        {
            // Make sure this matches the shader
            public uint vertexBufferMetaDataOffsetBytes;
            public uint morphTargetWeightsOffsetBytes;
            public uint jointMatricesOffsetBytes;
            public uint outputPositionsOffsetBytes;
            public uint outputFrenetOffsetBytes;
            public uint outputSliceOffsetBytes;

            public Vector3 vertexOutputPositionBias;
            public Vector3 vertexOutputPositionScale;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VertexInfo
        {
            // Make sure this matches the shader
            public uint meshInstanceDataOffsetBytes;
            public uint vertexBufferIndex; // Index in the vertex buffer
            public uint outputBufferIndex; // Index into the output buffer
        }

        private readonly int _numMeshVerts;
        private readonly int _numMorphTargetWeights;
        private readonly int _numJointMatrices;

        private readonly bool _hasMorphTargets;
        private readonly bool _hasJoints;

        private readonly OvrComputeUtils.MaxOutputFrames _maxOutputFrames;

        private ComputeBuffer _positionOutputBuffer;
        private ComputeBuffer _frenetOutputBuffer;

        private PerInstanceBufferUpdaterBase _perInstanceBufferUpdater;

        private readonly Vector3 _positionOutputScale;
        private readonly Vector3 _positionOutputBias;
        private readonly OvrComputeUtils.DataFormatAndStride _positionDataFormatAndStride;

        private readonly OvrComputeMeshAnimatorShader _shader;

        private readonly OvrAvatarComputeSkinningVertexBuffer _vertexBuffer;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool isDispose)
        {
            if (isDispose)
            {
                _perInstanceBufferUpdater?.Dispose();
                _positionOutputBuffer?.Dispose();
                _frenetOutputBuffer?.Dispose();
                _shader.Dispose();
            }
            else
            {
                if (_perInstanceBufferUpdater != null || _positionOutputBuffer != null || _frenetOutputBuffer != null)
                    OvrAvatarLog.LogError($"OvrComputeMeshAnimator was not disposed before being destroyed", LOG_SCOPE);
            }

            _perInstanceBufferUpdater = null;
            _positionOutputBuffer = null;
            _frenetOutputBuffer = null;
        }

        ~OvrComputeMeshAnimator()
        {
            Dispose(false);
        }

        private abstract class PerInstanceBufferUpdaterBase : IDisposable
        {
            private readonly ComputeBuffer _perInstanceBuffer;

            protected int _weightsOffsetBytes;
            protected int _matricesOffsetBytes;
            protected int _writeDestinationOffsetBytes;

            public abstract void SetMorphTargetWeights(in NativeArray<float> weights);
            public abstract void SetJointMatrices(in NativeArray<Matrix4x4> matrices);
            public abstract void SetWriteDestinationInBuffer(uint writeDestination);

            public abstract void UpdateComputeBufferBeforeDispatch();

            public ComputeBuffer PerInstanceBuffer => _perInstanceBuffer;

            protected PerInstanceBufferUpdaterBase(
                string name,
                int bufferSizeBytes,
                int weightsOffsetBytes,
                int matricesOffsetBytes,
                int writeDestinationOffsetBytes)
            {
                const string NAME_SUFFIX = "PerInstanceBuffer";

                _weightsOffsetBytes = weightsOffsetBytes;
                _matricesOffsetBytes = matricesOffsetBytes;
                _writeDestinationOffsetBytes = writeDestinationOffsetBytes;

                _perInstanceBuffer = OvrComputeUtils.CreateRawComputeBuffer((uint)bufferSizeBytes);
                _perInstanceBuffer.name = $"{name}_{NAME_SUFFIX}";
            }

            public virtual void Dispose()
            {
                _perInstanceBuffer.Dispose();
            }
        }

        private class PerInstanceBufferDirectUpdater : PerInstanceBufferUpdaterBase
        {
            private readonly List<uint> _writeDestinationList = new List<uint>(1);

            public PerInstanceBufferDirectUpdater(
                string name,
                in NativeArray<MeshInstanceMetaData> meshInstanceMetaDatas,
                in NativeArray<VertexInfo> vertexInfos,
                int bufferSizeBytes,
                int meshInstanceMetaDataOffsetBytes,
                int weightsOffsetBytes,
                int matricesOffsetBytes,
                int writeDestinationOffsetBytes,
                int vertexInfosOffsetBytes) : base(
                name,
                bufferSizeBytes,
                weightsOffsetBytes,
                matricesOffsetBytes,
                writeDestinationOffsetBytes)
            {
                _writeDestinationList.Add(0); // Have to add a random number here

                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    PerInstanceBuffer,
                    meshInstanceMetaDatas,
                    meshInstanceMetaDataOffsetBytes);
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    PerInstanceBuffer,
                    vertexInfos,
                    vertexInfosOffsetBytes);
            }

            public override void SetMorphTargetWeights(in NativeArray<float> weights)
            {
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(PerInstanceBuffer, weights, _weightsOffsetBytes);
            }

            public override void SetJointMatrices(in NativeArray<Matrix4x4> matrices)
            {
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    PerInstanceBuffer,
                    matrices,
                    _matricesOffsetBytes);
            }

            public override void SetWriteDestinationInBuffer(uint writeDestinationAsUint)
            {
                _writeDestinationList[0] = writeDestinationAsUint;

                var stride = PerInstanceBuffer.stride;
                PerInstanceBuffer.SetData(
                    _writeDestinationList,
                    0,
                    _writeDestinationOffsetBytes / stride,
                    _writeDestinationList.Count);
            }

            public override void UpdateComputeBufferBeforeDispatch()
            {
                // Intentionally empty
            }
        }

        // Due to a Unity bug, have a version which updates a NativeArray and then copies
        // that whole to the compute buffer, once per frame
        private class PerInstanceBufferUpdaterViaNativeArray : PerInstanceBufferUpdaterBase
        {
            private NativeArray<byte> _backingBuffer;

            public PerInstanceBufferUpdaterViaNativeArray(
                string name,
                in NativeArray<MeshInstanceMetaData> meshInstanceMetaDatas,
                in NativeArray<VertexInfo> vertexInfos,
                int bufferSizeBytes,
                int meshInstanceMetaDataOffsetBytes,
                int weightsOffsetBytes,
                int matricesOffsetBytes,
                int writeDestinationOffsetBytes,
                int vertexInfosOffsetBytes) : base(
                name,
                bufferSizeBytes,
                weightsOffsetBytes,
                matricesOffsetBytes,
                writeDestinationOffsetBytes)
            {
                // Declare a "backing" native array that is the same size
                // as the compute buffer which is updated and then copied to compute buffer
                _backingBuffer = new NativeArray<byte>(
                    bufferSizeBytes,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    _backingBuffer,
                    meshInstanceMetaDatas,
                    meshInstanceMetaDataOffsetBytes);
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(_backingBuffer, vertexInfos, vertexInfosOffsetBytes);
            }

            public override void Dispose()
            {
                base.Dispose();

                _backingBuffer.Reset();
            }

            public override void SetMorphTargetWeights(in NativeArray<float> weights)
            {
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(_backingBuffer, weights, _weightsOffsetBytes);
            }

            public override void SetJointMatrices(in NativeArray<Matrix4x4> matrices)
            {
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(_backingBuffer, matrices, _matricesOffsetBytes);
            }

            public override void SetWriteDestinationInBuffer(uint writeDestinationAsUint)
            {
                _backingBuffer.ReinterpretStore(_writeDestinationOffsetBytes, writeDestinationAsUint);
            }

            public override void UpdateComputeBufferBeforeDispatch()
            {
                // Copy whole thing to compute buffer
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(PerInstanceBuffer, _backingBuffer, 0);
            }
        }
    }
}
