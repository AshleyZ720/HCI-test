#pragma vertex Vertex_main_instancing
#pragma fragment Fragment_main

#pragma multi_compile DIRECTIONAL POINT SPOT

// Per vertex is faster than per pixel, and almost indistinguishable for our purpose
#define LIGHTPROBE_SH 1

// Built-in pipeline includes
#include "UnityCG.cginc"
#include "UnityGIAvatar.hlsl"
#include "UnityLightingCommon.cginc"
#include "UnityStandardInput.cginc"
#include "../../../../ShaderUtils/OvrUnityGlobalIlluminationBuiltIn.hlsl"

#include "LibraryCommon.hlsl"
