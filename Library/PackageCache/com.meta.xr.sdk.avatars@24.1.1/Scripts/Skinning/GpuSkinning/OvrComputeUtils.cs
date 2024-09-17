using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Oculus.Avatar2;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;

namespace Oculus.Skinning.GpuSkinning
{
    internal static class OvrComputeUtils
    {
        private const string LOG_SCOPE = nameof(OvrComputeUtils);

        private const uint SIZE_OF_UINT = sizeof(uint); // in bytes

        public enum MaxOutputFrames
        {
            INVALID = 0,
            ONE = 1,
            TWO = 2,
            THREE = 3,
        }

        // These int values are pulled from the compute shader, make sure that match
        // TODO*: Code generate these or vice versa so that they always match
        public enum ShaderFormatValue
        {
            FLOAT = 0,
            HALF = 1,
            UNORM_16 = 2,
            UINT_16 = 3,
            SNORM_10_10_10_2 = 4,
            UNORM_8 = 5,
            UINT_8 = 6,
            SNORM_16 = 7,
            UINT_32 = 8,
        }

        public struct DataFormatAndStride
        {
            public DataFormatAndStride(CAPI.ovrAvatar2DataFormat dataFormat, int strideBytes)
            {
                this.dataFormat = dataFormat;
                this.strideBytes = strideBytes;
            }

            public readonly CAPI.ovrAvatar2DataFormat dataFormat;
            public readonly int strideBytes;
        }

        public static ShaderFormatValue GetDataFormatShaderPropertyValue(CAPI.ovrAvatar2DataFormat dataFormat)
        {
            switch (dataFormat)
            {
                case CAPI.ovrAvatar2DataFormat.U8:
                    return ShaderFormatValue.UINT_8;
                case CAPI.ovrAvatar2DataFormat.U16:
                    return ShaderFormatValue.UINT_16;
                case CAPI.ovrAvatar2DataFormat.F16:
                    return ShaderFormatValue.HALF;
                case CAPI.ovrAvatar2DataFormat.F32:
                    return ShaderFormatValue.FLOAT;
                case CAPI.ovrAvatar2DataFormat.Unorm8:
                    return ShaderFormatValue.UNORM_8;
                case CAPI.ovrAvatar2DataFormat.Unorm16:
                    return ShaderFormatValue.UNORM_16;
                case CAPI.ovrAvatar2DataFormat.Snorm10_10_10_2:
                    return ShaderFormatValue.SNORM_10_10_10_2;
                case CAPI.ovrAvatar2DataFormat.Snorm16:
                    return ShaderFormatValue.SNORM_16;
                case CAPI.ovrAvatar2DataFormat.U32:
                    return ShaderFormatValue.UINT_32;
                case CAPI.ovrAvatar2DataFormat.Invalid:
                default:
                    OvrAvatarLog.LogError($"Unsupported format in compute shader {dataFormat}.", LOG_SCOPE);
                    return ShaderFormatValue.FLOAT;
            }
        }

        public static MaxOutputFrames GetMaxOutputFramesForConfiguration(
            bool motionSmoothing,
            bool supportApplicationSpacewarp)
        {
            int numExtraSlicesForMotionSmoothing = motionSmoothing ? 1 : 0;
            int numExtraSlicesForAppSpacewarp = supportApplicationSpacewarp ? 1 : 0;
            return (MaxOutputFrames)(1 + numExtraSlicesForAppSpacewarp + numExtraSlicesForMotionSmoothing);
        }

        public static void SetRawComputeBufferDataFromNativeArray<T>(
            ComputeBuffer computeBuffer,
            NativeArray<T> nativeArr,
            int byteOffsetInComputeBuffer) where T : struct
        {
            if (!nativeArr.IsCreated) { return; }

            var stride = computeBuffer.stride;
            var arrayOfUints = nativeArr.Reinterpret<uint>(UnsafeUtility.SizeOf<T>());
            computeBuffer.SetData(
                arrayOfUints,
                0,
                byteOffsetInComputeBuffer / stride,
                arrayOfUints.Length);
        }

        public static void SetRawComputeBufferDataFromNativeArray<T>(
            ComputeBuffer computeBuffer,
            NativeArray<T> nativeArr,
            uint byteOffsetInComputeBuffer) where T : struct
        {
            if (!nativeArr.IsCreated) { return; }

            var stride = computeBuffer.stride;
            var arrayOfUints = nativeArr.Reinterpret<uint>(UnsafeUtility.SizeOf<T>());
            computeBuffer.SetData(
                arrayOfUints,
                0,
                (int)byteOffsetInComputeBuffer / stride,
                arrayOfUints.Length);
        }

        public static void CopyNativeArrayToNativeByteArray<T>(
            NativeArray<byte> byteArray,
            NativeArray<T> nativeArr,
            int byteOffset) where T : struct
        {
            if (!nativeArr.IsCreated) { return; }

            var sourceBytes = nativeArr.Reinterpret<byte>(UnsafeUtility.SizeOf<T>());
            var sourceSlice = sourceBytes.Slice(); // Copy whole source
            var destSlice = byteArray.Slice(byteOffset, sourceBytes.Length);
            destSlice.CopyFrom(sourceSlice);
        }

        public static void CopyNativeArrayToNativeByteArray<T>(
            NativeArray<byte> byteArray,
            NativeArray<T> nativeArr,
            uint byteOffset) where T : struct
        {
            if (!nativeArr.IsCreated) { return; }

            var sourceBytes = nativeArr.Reinterpret<byte>(UnsafeUtility.SizeOf<T>());
            var sourceSlice = sourceBytes.Slice(); // Copy whole source
            var destSlice = byteArray.Slice((int)byteOffset, sourceBytes.Length);
            destSlice.CopyFrom(sourceSlice);
        }

        public static uint GetUintAlignedSize(uint numBytes)
        {
            return GetNumUintsNeeded(numBytes) * SIZE_OF_UINT;
        }

        public static uint GetNumUintsNeeded(uint numBytes)
        {
            return (numBytes + SIZE_OF_UINT - 1) / SIZE_OF_UINT;
        }

        public static ComputeBuffer CreateRawComputeBuffer(uint sizeBytes)
        {
            return new ComputeBuffer((int)GetNumUintsNeeded(sizeBytes), (int)SIZE_OF_UINT, ComputeBufferType.Raw);
        }
    }
}
