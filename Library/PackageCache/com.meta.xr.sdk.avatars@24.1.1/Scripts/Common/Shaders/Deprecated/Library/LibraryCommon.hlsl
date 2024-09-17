// VERTEX COLORS: activate this to transmit lo-fi model vert colors or sub mesh information in the alpha channel
#define HAS_VERTEX_COLOR_float4

// SHADER OPTIONS: For the Shader Library System, include either the next set or pragmas OR
// the #defines below them:

// In order to toggle these values in editor, uncomment the following set:
#pragma multi_compile _ HAS_NORMAL_MAP_ON
#pragma multi_compile _ SKIN_ON
#pragma multi_compile _ EYE_GLINTS_ON
#pragma multi_compile _ ENABLE_HAIR_ON
#pragma multi_compile _ ENABLE_RIM_LIGHT_ON

// To reduce permutations in the final product, hard code these as shown in this example:
// #define HAS_NORMAL_MAP_ONk
// #define SKIN_ON
// #define EYE_GLINTS_ON
// #define ENABLE_HAIR_ON
// #define ENABLE_RIM_LIGHT_ON

// MATERIAL_MODES: Must match the Properties specified above
#pragma multi_compile MATERIAL_MODE_TEXTURE MATERIAL_MODE_VERTEX

// Some platforms don't support reading from external buffers, provide a keyword to toggle
#pragma multi_compile EXTERNAL_BUFFERS_ENABLED EXTERNAL_BUFFERS_DISABLED

// Include app-specific dependencies
#include "app_specific/app_declarations.hlsl"

// Include the Vertex Shader
#include "../../../../ShaderUtils/AvatarCustom.cginc"
#include "replacement/options_common.hlsl"
#include "replacement/structs_vert.hlsl"
#include "replacement/platform_vert.hlsl"
#include "export/pbr_vert.unity.hlsl"
#include "replacement/platform_vert_main.hlsl"

// Include the Pixel Shader
#include "replacement/platform_frag.hlsl"
#include "export/pbr_frag.unity.hlsl"
#include "app_specific/app_functions.hlsl"
