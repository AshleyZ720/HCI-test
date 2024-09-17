#ifndef OVR_VERTEX_COMPUTE_INCLUDED
#define OVR_VERTEX_COMPUTE_INCLUDED

#include "../../../ShaderUtils/OvrDecodeUtils.cginc"
#include "../../../ShaderUtils/OvrDecodeFormats.cginc"

struct Vertex {
  float4 position;
  float3 normal;
  uint4 jointIndices;
  float4 jointWeights;
  uint vertexBufferIndex;
  uint outputBufferIndex;
};

///////////////////////////////////////////////////
// Neutral Pose
///////////////////////////////////////////////////

float4 OvrGetNeutralPosePosition(
    ByteAddressBuffer data_buffer,
    uint positions_start_address,
    float3 bias,
    float3 scale,
    uint vertex_index,
    int format,
    uint stride) {
  // ASSUMPTION: required to be on 4 byte boundaries
  float4 position = float4(0.0, 0.0, 0.0, 1.0);

  UNITY_BRANCH switch (format) {
    case OVR_FORMAT_FLOAT_32: {
      position.xyz = OvrUnpackFloat3x32(
          data_buffer,
          mad(vertex_index, stride, positions_start_address));
    }
    break;
    case OVR_FORMAT_HALF_16: {
      position.xyz = OvrUnpackHalf3x16(
          data_buffer,
          mad(vertex_index, stride, positions_start_address));
    }
    break;
    case OVR_FORMAT_UNORM_16: {
      position.xyz = OvrUnpackUnorm3x16(
          data_buffer,
          mad(vertex_index, stride, positions_start_address));
    }
    break;
    case OVR_FORMAT_SNORM_16: {
      position.xyz = OvrUnpackSnorm3x16(
          data_buffer,
          mad(vertex_index, stride, positions_start_address));
    }
    break;
    case OVR_FORMAT_SNORM_10_10_10_2: {
      position.xyz = OvrUnpackSnorm3x10_10_10_2(
          data_buffer,
          mad(vertex_index, stride, positions_start_address));
    }
    break;
    case OVR_FORMAT_UNORM_8: {
      position.xyz = OvrUnpackUnorm3x8(
          data_buffer,
          mad(vertex_index, stride, positions_start_address));
    }
    break;
    default:
      break;
  }

  // Apply scale and bias
  position.xyz = mad(position.xyz, scale, bias);

  return position;
}

float3 OvrGetNeutralPoseNormal(
    ByteAddressBuffer data_buffer,
    uint normals_start_address,
    uint vertex_index) {
  // Only supporting 10-10-10-2 snorm at the moment
  static const uint STRIDE = 1u * 4u; // 1 32-bit uint for 3 10-bit SNorm and 2 unused bits
  return normalize(
      OvrUnpackSnorm3x10_10_10_2(
          data_buffer,
          mad(vertex_index, STRIDE, normals_start_address)));
}

float4 OvrGetNeutralPoseTangent(
    ByteAddressBuffer data_buffer,
    uint tangents_start_address,
    uint vertex_index) {
  // Only supporting full floats for positions at the moment
  static const uint STRIDE = 1u * 4u; // 1 32-bit uint for 3 10-bit snorm and 2 bits for w
  float4 tangent = OvrUnpackSnorm4x10_10_10_2(
      data_buffer,
      mad(vertex_index, STRIDE, tangents_start_address));

  tangent.xyz = normalize(tangent.xyz);

  return tangent;
}

float4 OvrGetJointWeights(
    in ByteAddressBuffer data_buffer,
    uint joint_weights_address,
    uint vertex_index,
    int format,
    uint stride) {
  // ASSUMPTION: 4 weights per vertex
  float4 weights = float4(0.0, 0.0, 0.0, 0.0);

  UNITY_BRANCH switch (format) {
    case OVR_FORMAT_FLOAT_32:
      // 4 32-bit uints for 4 32-bit floats
      weights = OvrUnpackFloat4x32(
          data_buffer,
          mad(vertex_index, stride, joint_weights_address));
      break;
    case OVR_FORMAT_HALF_16:
      // 2 32-bit uints for 4 16 bit halfs
      weights = OvrUnpackHalf4x16(
          data_buffer,
          mad(vertex_index, stride, joint_weights_address));
      break;
    case OVR_FORMAT_UNORM_16:
      weights = OvrUnpackUnorm4x16(
          data_buffer,
          mad(vertex_index, stride, joint_weights_address));
      break;
    case OVR_FORMAT_UNORM_8:
      weights = OvrUnpackUnorm4x8(
          data_buffer,
          mad(vertex_index, stride, joint_weights_address));
      break;
    default:
      break;
  }

  return weights;
}

uint4 OvrGetJointIndices(
    in ByteAddressBuffer data_buffer,
    uint joint_indices_address,
    uint vertex_index,
    int format,
    uint stride) {
  // ASSUMPTION: 4 indices per vertex
  uint4 indices = uint4(0u, 0u, 0u, 0u);

  UNITY_BRANCH switch (format) {
    case OVR_FORMAT_UINT_16:
      indices = OvrUnpackUint4x16(
          data_buffer,
          mad(vertex_index, stride, joint_indices_address));
      break;
    case OVR_FORMAT_UINT_8:
      indices = OvrUnpackUint4x8(
          data_buffer,
          mad(vertex_index, stride, joint_indices_address));
      break;
    default:
      break;
  }

  return indices;
}

Vertex OvrGetVertexData(
    in ByteAddressBuffer staticDataBuffer,
    uint positionsOffsetBytes,
    int positionFormat,
    uint positionStride,
    float3 positionBias,
    float3 positionScale,
    uint normalsOffsetBytes,
    uint jointWeightsOffsetBytes,
    int jointWeightsFormat,
    uint jointWeightsStride,
    uint jointIndicesOffsetBytes,
    int jointIndicesFormat,
    uint jointIndicesStride,
    uint vertexBufferIndex,
    uint outputBufferIndex) {
  Vertex vertex;

  vertex.position = OvrGetNeutralPosePosition(
      staticDataBuffer,
      positionsOffsetBytes,
      positionBias.xyz,
      positionScale.xyz,
      vertexBufferIndex,
      positionFormat,
      positionStride);
  vertex.normal = OvrGetNeutralPoseNormal(
      staticDataBuffer,
      normalsOffsetBytes,
      vertexBufferIndex);

  const uint4 jointIndices = OvrGetJointIndices(
      staticDataBuffer,
      jointIndicesOffsetBytes,
      vertexBufferIndex,
      jointIndicesFormat,
      jointIndicesStride);

  vertex.jointWeights = OvrGetJointWeights(
      staticDataBuffer,
      jointWeightsOffsetBytes,
      vertexBufferIndex,
      jointWeightsFormat,
      jointWeightsStride);
  vertex.jointIndices = jointIndices;

  vertex.outputBufferIndex = outputBufferIndex;
  vertex.vertexBufferIndex = vertexBufferIndex;
  return vertex;
}

#endif
