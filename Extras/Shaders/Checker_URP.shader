// Nodra - UV test checker (URP), lit, casting and receiving shadows. Not part of the runtime package
// (see Sources/) - a debug aid for visually checking UV output (e.g. AutoUVNode) for stretching, mirroring or
// seams: distortion shows up as warped or unevenly-sized squares instead of a clean, evenly-tiled grid.
//
// PackageRequirements below tells Unity to skip compiling this SubShader entirely when the URP package isn't
// present, instead of failing on the "Packages/com.unity.render-pipelines.universal/..." includes below - so
// this file can sit in an HDRP-only project (see Checker_HDRP.shader) without spamming the console.
Shader "Nodra/Checker"
{
	Properties
	{
		_Color1 ("Color A", Color) = (0.85, 0.85, 0.85, 1)
		_Color2 ("Color B", Color) = (0.1, 0.1, 0.1, 1)
		_Tiling ("Tiling", Float) = 8
	}

	SubShader
	{
		PackageRequirements
		{
			"com.unity.render-pipelines.universal"
		}

		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
		LOD 100

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile_fragment _ _SHADOWS_SOFT
			#pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float2 uv : TEXCOORD2;
				float4 color : COLOR;
				float4 shadowCoord : TEXCOORD3;
			};

			CBUFFER_START(UnityPerMaterial)
				float4 _Color1;
				float4 _Color2;
				float _Tiling;
			CBUFFER_END

			Varyings Vert(Attributes IN)
			{
				Varyings OUT;
				VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);
				OUT.positionHCS = vertexInput.positionCS;
				OUT.positionWS = vertexInput.positionWS;
				OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
				OUT.uv = IN.uv;
				OUT.color = IN.color;
				OUT.shadowCoord = GetShadowCoord(vertexInput);
				return OUT;
			}

			half4 Frag(Varyings IN) : SV_Target
			{
				float2 tile = floor(IN.uv * _Tiling);
				float checker = abs(fmod(tile.x + tile.y, 2.0));
				half4 albedo = lerp(_Color1, _Color2, checker) * IN.color;

			#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
				float4 shadowCoord = IN.shadowCoord;
			#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
				float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
			#else
				float4 shadowCoord = float4(0, 0, 0, 0);
			#endif

				float3 normalWS = normalize(IN.normalWS);
				Light mainLight = GetMainLight(shadowCoord);
				float NdotL = saturate(dot(normalWS, mainLight.direction));
				float3 lighting = mainLight.color * NdotL * mainLight.shadowAttenuation + SampleSH(normalWS);

				return half4(albedo.rgb * lighting, albedo.a);
			}
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest LEqual
			ColorMask 0

			HLSLPROGRAM
			#pragma vertex ShadowVert
			#pragma fragment ShadowFrag
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

			float3 _LightDirection;
			float3 _LightPosition;

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
			};

			Varyings ShadowVert(Attributes IN)
			{
				Varyings OUT;
				float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
				float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

			#if _CASTING_PUNCTUAL_LIGHT_SHADOW
				float3 lightDirectionWS = normalize(_LightPosition - positionWS);
			#else
				float3 lightDirectionWS = _LightDirection;
			#endif

				float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
				OUT.positionCS = ApplyShadowClamping(positionCS);
				return OUT;
			}

			half4 ShadowFrag(Varyings IN) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}
	}
}
