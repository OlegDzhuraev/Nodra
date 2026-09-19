// Nodra - UV test checker (HDRP), lit, casting and receiving shadows. Not part of the runtime package
// (see Sources/) - a debug aid for visually checking UV output (e.g. AutoUVNode) for stretching, mirroring or
// seams: distortion shows up as warped or unevenly-sized squares instead of a clean, evenly-tiled grid.
//
// PackageRequirements below tells Unity to skip compiling this SubShader entirely when the HDRP package isn't
// present, instead of failing on the "Packages/com.unity.render-pipelines.high-definition/..." includes below -
// so this file can sit in a URP-only project (see Checker_URP.shader) without spamming the console.
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
			"com.unity.render-pipelines.high-definition"
		}

		Tags { "RenderType" = "Opaque" "RenderPipeline" = "HDRenderPipeline" "Queue" = "Geometry" }
		LOD 100

		Pass
		{
			// Depth-only, run before "ForwardOnly" below. HDRP's depth-dependent effects (volumetric clouds/fog,
			// SSAO, SSR, TAA...) read the depth-prepass buffer, not the main forward pass's - an object with no
			// pass tagged "DepthForwardOnly" is simply absent from it, so e.g. volumetric clouds render as if
			// nothing were there and the object then looks alpha-blended with them once the forward pass catches
			// up and draws its color. See HDRP/Unlit.shader's own "DepthForwardOnly" pass for the same reason.
			Name "DepthForwardOnly"
			Tags { "LightMode" = "DepthForwardOnly" }

			ColorMask 0
			ZWrite On

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
			};

			Varyings Vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				return OUT;
			}

			half4 Frag(Varyings IN) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}

		Pass
		{
			// HDRP still renders unlit/hand-written shaders through a "ForwardOnly" pass (see HDRP/Unlit.shader) -
			// ZWrite/ZTest default to On/LEqual same as a normal opaque pass.
			Name "ForwardOnly"
			Tags { "LightMode" = "ForwardOnly" }

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag

			// HDShadowAlgorithms.hlsl #errors out if these aren't defined - it resolves FILTER_ALGORITHM macros
			// for punctual/directional/area lights from them with no fallback in a fragment shader (the fallback
			// it does have is only for non-fragment stages, e.g. compute). We only ever call the directional one
			// ourselves, but the header requires all three to be resolved regardless.
			#pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
			#pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
			#pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

			// No Material.hlsl/SurfaceData here on purpose - that whole chain exists to feed HDRP's LightLoop
			// (all light types, shadows, reflection probes...) which is way more than this debug shader needs.
			// Pulled in directly instead: Common.hlsl + SpaceTransforms.hlsl for the vertex transform (same as
			// URP), and HDRP's ShaderVariables.hlsl for camera/global uniforms - which already includes both the
			// light-loop's directional light buffer and the atmospheric-scattering ambient probe buffer (the one
			// that flips on AMBIENT_PROBE_BUFFER, making the shared AmbientProbe.hlsl's SampleSH() read HDRP's sky
			// probe instead of the per-object SH used by URP/Built-in) - including either again ourselves would
			// redeclare their globals, since neither file has an include guard.
			// SpaceTransforms.hlsl must come after ShaderVariables.hlsl - it uses the UNITY_MATRIX_M/VP macros
			// textually in its function bodies, and those are only #defined once HDRP's ShaderVariables.hlsl
			// (via ShaderVariablesMatrixDefsHDCamera.hlsl) has been included.
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"
			// Directional shadow map sampling - no LightLoop/SurfaceData involved, just the lower-level function
			// HDRP's own light loop calls internally (see LightLoop/HDShadow.hlsl's GetDirectionalShadowAttenuation).
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowContext.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"

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
			};

			CBUFFER_START(UnityPerMaterial)
				float4 _Color1;
				float4 _Color2;
				float _Tiling;
			CBUFFER_END

			Varyings Vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
				OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
				OUT.uv = IN.uv;
				OUT.color = IN.color;
				return OUT;
			}

			half4 Frag(Varyings IN) : SV_Target
			{
				float2 tile = floor(IN.uv * _Tiling);
				float checker = abs(fmod(tile.x + tile.y, 2.0));
				half4 albedo = lerp(_Color1, _Color2, checker) * IN.color;

				float3 normalWS = normalize(IN.normalWS);

				// No GetMainLight() equivalent outside Shader Graph in HDRP - read the light-loop's directional
				// light buffer directly. _DirectionalLightCount can be 0 (no directional light in the scene, or
				// it's disabled), so guard against reading an empty buffer. forward is the direction the light
				// travels, so the direction *to* the light is its negation (same convention URP's Light.direction
				// uses, just not pre-negated for us here).
				float3 lightColor = 0;
				float3 lightDirWS = float3(0, 1, 0);
				int shadowIndex = -1;
				if (_DirectionalLightCount > 0)
				{
					DirectionalLightData mainLight = _DirectionalLightDatas[0];
					lightColor = mainLight.color;
					lightDirWS = -mainLight.forward;
					shadowIndex = mainLight.shadowIndex;
				}

				// shadowIndex is -1 when the light doesn't cast shadows (or there's no directional light at all) -
				// InitShadowContext()/GetDirectionalShadowAttenuation expect a real index into the shadow atlas, so
				// skip the lookup entirely rather than sampling with a negative one.
				float shadowAttenuation = 1.0;
				if (shadowIndex >= 0)
				{
					HDShadowContext shadowContext = InitShadowContext();
					shadowAttenuation = GetDirectionalShadowAttenuation(shadowContext, IN.positionHCS.xy, IN.positionWS,
						normalWS, shadowIndex, lightDirWS);
				}

				float NdotL = saturate(dot(normalWS, lightDirWS)) * shadowAttenuation;
				float3 lighting = (lightColor * NdotL + SampleSH(normalWS)) * GetCurrentExposureMultiplier();

				return half4(albedo.rgb * lighting, albedo.a);
			}
			ENDHLSL
		}

		Pass
		{
			// Same shape as "DepthForwardOnly" above (position-only transform, no color output) - HDRP bakes its
			// shadow bias into the shadow map's rasterizer state (the light's own Normal/Constant Bias settings),
			// not into caster vertex positions the way URP's ApplyShadowBias does, so there's nothing extra to do
			// here (see HDRP/Unlit.shader's "ShadowCaster" pass, which is likewise just its depth pass retagged).
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ColorMask 0
			ZWrite On
			ZTest LEqual

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
			};

			Varyings Vert(Attributes IN)
			{
				Varyings OUT;
				OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
				return OUT;
			}

			half4 Frag(Varyings IN) : SV_Target
			{
				return 0;
			}
			ENDHLSL
		}
	}
}
