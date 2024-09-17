#ifndef OVR_MORPHS_COMPUTE_INCLUDED
#define OVR_MORPHS_COMPUTE_INCLUDED

#include "../../../ShaderUtils/OvrDecodeUtils.cginc"
#include "../../../ShaderUtils/OvrDecodeFormats.cginc"

groupshared uint _groupMaxNumMorphs = 0u;

struct OvrCompactMorphsParams {
  uint posDeltasStartAddress;
  uint normDeltasStartAddress;
  uint morphIndicesStartAddress;
  uint nextEntriesStartAddress;
  uint morphTargetWeightsStartAddress;
  uint vertIndex;

  int morphWeightsFormat;
  uint morphWeightsStride;

  int morphIndicesFormat;
  uint morphIndicesStride;

  int nextEntriesFormat;
  uint nextEntriesStride;

  int deltasFormat;
  uint deltasStride;

  float3 posScale;
  float3 normScale;
};

struct OvrCompactMorphsTangentParams {
  uint tanDeltasStartAddress;
  float3 tanScale;
};

struct OvrCompactMorphsVars {
  float3 posSum;
  float3 normSum;

  uint entryIndex;
};

struct OvrCompactMorphsTangentVars {
  float3 tanSum;
};

uint OvrGetMorphWeightsStride(int format) {
  UNITY_BRANCH switch (format) {
    case OVR_FORMAT_FLOAT_32:
      return 4u;
    default:
      // Unhandled, error
      return 0u;
  }
}

uint OvrGetMorphDeltasStride(int format) {
  UNITY_BRANCH switch (format) {
    case OVR_FORMAT_SNORM_10_10_10_2:
      return 4u;
    default:
      // Unhandled, error
      return 0u;
  }
}

uint OvrGetMorphIndexStride(int format) {
  UNITY_BRANCH switch (format) {
    case OVR_FORMAT_UINT_16:
      return 2u;
    case OVR_FORMAT_UINT_8:
      return 1u;
    default:
      // Unhandled, error
      return 0u;
  }
}

uint OvrGetNextEntryStride(int format) {
  UNITY_BRANCH switch (format) {
    case OVR_FORMAT_UINT_32:
      return 4u;
    case OVR_FORMAT_UINT_16:
      return 2u;
    case OVR_FORMAT_UINT_8:
      return 1u;
    default:
      // Unhandled, error
      return 0u;
  }
}

uint OvrGetNumMorphTargetsAffectingVertex(
  in ByteAddressBuffer vertexBuffer,
  in uint numMorphsBufferStartAddress,
  in uint vertexIndex)
{
  uint byteOffset = numMorphsBufferStartAddress;
#if defined(OVR_MORPH_INDEX_FORMAT_UINT16)
  byteOffset += OvrGetMorphIndexStride(OVR_FORMAT_UINT_16) * vertexIndex;
  return OvrUnpackUint1x16NonAligned(vertexBuffer, byteOffset);
#else
  byteOffset += OvrGetMorphIndexStride(OVR_FORMAT_UINT_8) * vertexIndex;
  return OvrUnpackUint1x8NonAligned(vertexBuffer, byteOffset);
#endif
}

void OvrLoadMaxNumMorphsForGroup(in uint numMorphsForThisVert)
{
  // Compare with the maximum for the group
  GroupMemoryBarrierWithGroupSync();
  InterlockedMax(_groupMaxNumMorphs, numMorphsForThisVert);
  GroupMemoryBarrierWithGroupSync();
}

uint OvrCompactMorphsGetMorphIndex(
    in ByteAddressBuffer staticDataBuffer,
    in OvrCompactMorphsVars vars,
    in OvrCompactMorphsParams params) {
  const uint byteOffset = params.morphIndicesStride * vars.entryIndex + params.morphIndicesStartAddress;

  // Branching on a uniform shouldn't be that bad
  UNITY_BRANCH switch (params.morphIndicesFormat) {
    case OVR_FORMAT_UINT_16:
      return OvrUnpackUint1x16NonAligned(staticDataBuffer, byteOffset);
    case OVR_FORMAT_UINT_8:
      return OvrUnpackUint1x8NonAligned(staticDataBuffer, byteOffset);
    default:
      // Error
      return 0;
  }
}

uint OvrCompactMorphsGetNextEntryIndex(
    in ByteAddressBuffer staticDataBuffer,
    in OvrCompactMorphsVars vars,
    in OvrCompactMorphsParams params) {
  const uint byteOffset = params.nextEntriesStride * vars.entryIndex + params.nextEntriesStartAddress;

  // Branching on a uniform shouldn't be that bad
  UNITY_BRANCH switch (params.nextEntriesFormat) {
    case OVR_FORMAT_UINT_32:
      return OvrLoadUint(staticDataBuffer, byteOffset);
    case OVR_FORMAT_UINT_16:
      return OvrUnpackUint1x16NonAligned(staticDataBuffer, byteOffset);
    case OVR_FORMAT_UINT_8:
      return OvrUnpackUint1x8NonAligned(staticDataBuffer, byteOffset);
    default:
      // Error
      return 0;
  }
}

float3 OvrCompactMorphsGetPositionDelta(
    in ByteAddressBuffer staticDataBuffer,
    in OvrCompactMorphsVars vars,
    in OvrCompactMorphsParams params) {
  const uint byteOffset = params.deltasStride * vars.entryIndex + params.posDeltasStartAddress;

  return OvrUnpackSnorm3x10_10_10_2WithBonusScale(staticDataBuffer, byteOffset);
}

float3 OvrCompactMorphsGetNormalDelta(
    in ByteAddressBuffer staticDataBuffer,
    in OvrCompactMorphsVars vars,
    in OvrCompactMorphsParams params) {
  const uint byteOffset = params.deltasStride * vars.entryIndex + params.normDeltasStartAddress;

  return OvrUnpackSnorm3x10_10_10_2WithBonusScale(staticDataBuffer, byteOffset);
}

float3 OvrCompactMorphsGetTangentDelta(
    in ByteAddressBuffer staticDataBuffer,
    in OvrCompactMorphsVars vars,
    in OvrCompactMorphsParams params,
    in OvrCompactMorphsTangentParams tanParams) {
  const uint byteOffset = params.deltasStride * vars.entryIndex + tanParams.tanDeltasStartAddress;

  return OvrUnpackSnorm3x10_10_10_2WithBonusScale(staticDataBuffer, byteOffset);
}

float OvrCompactMorphsGetMorphWeight(
    in ByteAddressBuffer dynamicDataBuffer,
    uint morphTargetIndex,
    in OvrCompactMorphsVars vars,
    in OvrCompactMorphsParams params) {
  const uint byteOffset = params.morphWeightsStride * morphTargetIndex + params.
      morphTargetWeightsStartAddress;

  return OvrUnpackFloat1x32(dynamicDataBuffer, byteOffset);
}

OvrCompactMorphsVars GetInitialVars(OvrCompactMorphsParams params) {
  OvrCompactMorphsVars result;

  result.posSum = 0.0;
  result.normSum = 0.0;
  result.entryIndex = params.vertIndex;

  return result;
}

OvrCompactMorphsTangentVars GetInitialVars() {
  OvrCompactMorphsTangentVars result;

  result.tanSum = 0.0;

  return result;
}

void OvrApplyAccumulatedPositionAndNormal(
    inout float4 position,
    inout float3 normal,
    in OvrCompactMorphsParams params,
    in OvrCompactMorphsVars vars) {
  position.xyz += params.posScale * vars.posSum;
  normal += params.normScale * vars.normSum;
  normal = normalize(normal);
}

void OvrApplyAccumulatedTangent(
    inout float4 tangent,
    in OvrCompactMorphsTangentParams params,
    in OvrCompactMorphsTangentVars vars) {
  tangent.xyz += params.tanScale * vars.tanSum;
  tangent.xyz = normalize(tangent.xyz);
}

void OvrApplyCompactMorphsNoTangents(
    in ByteAddressBuffer vertexBuffer,
    in ByteAddressBuffer perInstanceBuffer,
    in OvrCompactMorphsParams params,
    inout float4 position,
    inout float3 normal) {
  OvrCompactMorphsVars vars = GetInitialVars(params);

  for (uint index = 0; index < _groupMaxNumMorphs; ++index) {
    const uint morphIndex = OvrCompactMorphsGetMorphIndex(vertexBuffer, vars, params);
    const float3 posDelta = OvrCompactMorphsGetPositionDelta(vertexBuffer, vars, params);
    const float3 normDelta = OvrCompactMorphsGetNormalDelta(vertexBuffer, vars, params);

    const float weight = OvrCompactMorphsGetMorphWeight(
        perInstanceBuffer,
        morphIndex,
        vars,
        params);

    vars.posSum += weight * posDelta;
    vars.normSum += weight * normDelta;

    // Update entry index
    vars.entryIndex = OvrCompactMorphsGetNextEntryIndex(vertexBuffer, vars, params);
  }

  OvrApplyAccumulatedPositionAndNormal(position, normal, params, vars);
}

void OvrApplyCompactMorphsWithTangents(
    in ByteAddressBuffer vertexBuffer,
    in ByteAddressBuffer perInstanceBuffer,
    in OvrCompactMorphsParams params,
    in OvrCompactMorphsTangentParams tanParams,
    inout float4 position,
    inout float3 normal,
    inout float4 tangent) {
  OvrCompactMorphsVars vars = GetInitialVars(params);
  OvrCompactMorphsTangentVars tanVars = GetInitialVars();

  for (uint index = 0; index < _groupMaxNumMorphs; ++index) {
    const uint morphIndex = OvrCompactMorphsGetMorphIndex(vertexBuffer, vars, params);
    const float3 posDelta = OvrCompactMorphsGetPositionDelta(vertexBuffer, vars, params);
    const float3 normDelta = OvrCompactMorphsGetNormalDelta(vertexBuffer, vars, params);
    const float3 tanDelta = OvrCompactMorphsGetTangentDelta(vertexBuffer, vars, params, tanParams);

    const float weight = OvrCompactMorphsGetMorphWeight(
        perInstanceBuffer,
        morphIndex,
        vars,
        params);

    vars.posSum += weight * posDelta;
    vars.normSum += weight * normDelta;
    tanVars.tanSum += weight * tanDelta;

    // Update entry index
    vars.entryIndex = OvrCompactMorphsGetNextEntryIndex(vertexBuffer, vars, params);
  }

  OvrApplyAccumulatedPositionAndNormal(position, normal, params, vars);
  OvrApplyAccumulatedTangent(tangent, tanParams, tanVars);
}

#endif
