using System;
using System.Runtime.InteropServices;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using static Oculus.Avatar2.CAPI;

namespace Oculus.Avatar2
{
    public static partial class CAPI
    {
        private const string LOG_SCOPE = "OvrAvatarAPI_CompactSkinning";

        [StructLayout(LayoutKind.Sequential)]
        public readonly struct OvrAvatar2CompactSkinningMetaData
        {
            // Number of verts in mesh affected by at least one morph target
            public readonly UInt32 numMorphedVerts;

            // Number of verts affected by joints, but not morphs
            public readonly UInt32 numJointsOnlyVerts;

            // Number of verts not affected by morphs or joints
            public readonly UInt32 numStaticVerts;

            // Maximum amount of morphs that affect a single vert
            public readonly UInt32 maxAmountOfMorphsAffectingSingleVert;

            // Are the positions, normal, tangents, and morph target
            // deltas already in the "client coordinate space"
            public readonly UInt32 isInClientCoordSpace;

            // 4x4 matrix representing the transformation needed
            // to convert positions into the "client coordinate space"
            // (if they aren't already)
            public readonly ovrAvatar2Matrix4f clientCoordSpaceTransform;

            // 4x4 matrix representing the transformation needed
            // to convert normals into the "client coordinate space"
            // (if they aren't already)
            public readonly ovrAvatar2Matrix4f clientCoordSpaceNormalTransform;
        }

        public static bool OvrCompactMeshData_GetMetaData(
            ovrAvatar2VertexBufferId id,
            ovrAvatar2CompactMeshAttributes attributes,
            NativeArray<ovrAvatar2BufferMetaData> metaData,
            out UInt64 dataBufferSizeBytes)
        {
            unsafe
            {
                return ovrCompactMeshData_GetMetaData(id, attributes, metaData.GetPtr(), (ulong)metaData.Length, out dataBufferSizeBytes).EnsureSuccess(
                    "ovrCompactMeshData_GetMetaData",
                    LOG_SCOPE);
            }
        }

        public static bool OvrCompactMeshData_CopyBuffer(
            ovrAvatar2Id primitiveID,
            ovrAvatar2VertexBufferId vertBufferID,
            ovrAvatar2CompactMeshAttributes attributes,
            ovrAvatar2DataBlock dataBuffer)
        {
            return ovrCompactMeshData_CopyBuffer(primitiveID, vertBufferID, attributes, dataBuffer).EnsureSuccess(
                "ovrCompactMeshData_CopyBuffer",
                LOG_SCOPE);
        }

        public static bool OvrCompactSkinningData_Initialize(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            return ovrCompactSkinningData_Initialize(compactSkinningDataId).EnsureSuccess(
                "ovrCompactSkinningData_Initialize",
                LOG_SCOPE);
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetPositionsMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetPositionsMetaData(compactSkinningDataId, out var metaData).EnsureSuccess(
                "ovrCompactSkinningData_GetPositionsMetaData",
                LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyPositions(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyPositions(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes,
                    out normalizationOffset,
                    out normalizationScale);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetNormalsMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetNormalsMetaData(compactSkinningDataId, out var metaData).EnsureSuccess(
                "ovrCompactSkinningData_GetNormalsMetaData",
                LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyNormals(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyNormals(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes,
                    out normalizationOffset,
                    out normalizationScale);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetTangentsMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetTangentsMetaData(compactSkinningDataId, out var metaData).EnsureSuccess(
                "ovrCompactSkinningData_GetTangentsMetaData",
                LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyTangents(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyTangents(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes,
                    out normalizationOffset,
                    out normalizationScale);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetVertexReorderMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetVertexReorderMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetVertexReorderMetaData", LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyVertexReorder(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyVertexReorder(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetVertexInverseReorderMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetVertexInverseReorderMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetVertexInverseReorderMetaData", LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyVertexInverseReorder(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyVertexInverseReorder(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetNumMorphsBufferMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetNumMorphsBufferMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetNumMorphsBufferMetaData", LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyNumMorphsBuffer(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyNumMorphsBuffer(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetMorphPositionDeltasMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetMorphPositionDeltasMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetMorphPositionDeltasMetaData"))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyMorphPositionDeltas(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyMorphPositionDeltas(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes,
                    out normalizationOffset,
                    out normalizationScale);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetMorphNormalDeltasMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetMorphNormalDeltasMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetMorphNormalDeltasMetaData", LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyMorphNormalDeltas(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyMorphNormalDeltas(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes,
                    out normalizationOffset,
                    out normalizationScale);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetMorphTangentDeltasMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetMorphTangentDeltasMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetMorphTangentDeltasMetaData", LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyMorphTangentDeltas(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyMorphTangentDeltas(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes,
                    out normalizationOffset,
                    out normalizationScale);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetMorphIndicesMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetMorphIndicesMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetMorphIndicesMetaData", LOG_SCOPE))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyMorphIndices(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyMorphIndices(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetMorphNextEntriesMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetMorphNextEntriesMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetMorphNextEntriesMetaData"))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyMorphNextEntries(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyMorphNextEntries(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetJointIndicesMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetJointIndicesMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetJointIndicesMetaData"))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyJointIndices(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyJointIndices(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes);
            }
        }

        public static ovrAvatar2BufferMetaData OvrCompactSkinningData_GetJointWeightsMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetJointWeightsMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetJointWeightsMetaData"))
            {
                return metaData;
            }

            return default;
        }

        public static ovrAvatar2Result OvrCompactSkinningData_CopyJointWeights(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            NativeArray<byte> dataBuffer,
            uint dataBufferStrideBytes)
        {
            unsafe
            {
                return ovrCompactSkinningData_CopyJointWeights(
                    compactSkinningDataId,
                    dataBuffer.GetPtr(),
                    (UInt32)dataBuffer.Length,
                    dataBufferStrideBytes);
            }
        }

        public static OvrAvatar2CompactSkinningMetaData OvrCompactSkinningData_GetMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId)
        {
            if (ovrCompactSkinningData_GetMetaData(compactSkinningDataId, out var metaData)
                .EnsureSuccess("ovrCompactSkinningData_GetMetaData"))
            {
                return metaData;
            }

            return default;
        }

        #region extern methods

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactMeshData_GetMetaData(
            ovrAvatar2VertexBufferId id,
            ovrAvatar2CompactMeshAttributes attributes,
            ovrAvatar2BufferMetaData* metaData,
            UInt64 metaDataSize,
            out UInt64 dataBufferSizeBytes);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactMeshData_CopyBuffer(
            ovrAvatar2Id primitiveID,
            ovrAvatar2VertexBufferId vertBufferID,
            ovrAvatar2CompactMeshAttributes attributes,
            ovrAvatar2DataBlock dataBuffer);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_Initialize(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetPositionsMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyPositions(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetNormalsMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyNormals(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetTangentsMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyTangents(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetVertexReorderMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyVertexReorder(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetVertexInverseReorderMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyVertexInverseReorder(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetNumMorphsBufferMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyNumMorphsBuffer(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetMorphPositionDeltasMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyMorphPositionDeltas(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetMorphNormalDeltasMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyMorphNormalDeltas(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetMorphTangentDeltasMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyMorphTangentDeltas(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes,
            out ovrAvatar2Vector3f normalizationOffset,
            out ovrAvatar2Vector3f normalizationScale);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetMorphIndicesMetaData(
            ovrAvatar2CompactSkinningDataId id,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyMorphIndices(
            ovrAvatar2CompactSkinningDataId id,
            Byte* dataBuffer,
            UInt32 dataBufferSizeBytes,
            UInt32 dataBufferStrideBytes);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetMorphNextEntriesMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyMorphNextEntries(
            ovrAvatar2CompactSkinningDataId id,
            Byte* dataBuffer,
            UInt32 dataBufferSizeBytes,
            UInt32 dataBufferStrideBytes);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetJointIndicesMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyJointIndices(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetJointWeightsMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out ovrAvatar2BufferMetaData metaData);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe ovrAvatar2Result ovrCompactSkinningData_CopyJointWeights(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            Byte* dataBuffer,
            UInt32 bufferSizeBytes,
            UInt32 dataBufferStrideBytes);

        [DllImport(LibFile, CallingConvention = CallingConvention.Cdecl)]
        private static extern ovrAvatar2Result ovrCompactSkinningData_GetMetaData(
            ovrAvatar2CompactSkinningDataId compactSkinningDataId,
            out OvrAvatar2CompactSkinningMetaData metaData);

        #endregion // extern methods
    }
}
