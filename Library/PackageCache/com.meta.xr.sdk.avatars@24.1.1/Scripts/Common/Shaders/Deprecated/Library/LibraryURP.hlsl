#pragma vertex Vertex_main_instancing
#pragma fragment Fragment_main
#pragma multi_compile_instancing

// Per vertex is faster than per pixel, and almost indistinguishable for our purpose
#define USE_SH_PER_VERTEX

// This is the URP pass so set this define to activate OvrUnityGlobalIllumination headers
#define USING_URP

// URP includes
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
#include "../../../../ShaderUtils/OvrUnityLightsURP.hlsl"
#include "../../../../ShaderUtils/OvrUnityGlobalIlluminationURP.hlsl"

#include_with_pragmas "LibraryCommon.hlsl"
