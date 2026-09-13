// Nodra - UV test checker (URP), actually lit. Not part of the runtime package (see Sources/) - a debug aid for
// visually checking UV output (e.g. AutoUVNode) for stretching, mirroring or seams: distortion shows up as warped
// or unevenly-sized squares instead of a clean, evenly-tiled grid. Lit (main light + ambient probe, no shadows)
// rather than flat-unlit so normals are actually visible too - a flipped face goes dark under the main light
// instead of showing exactly the same brightness as its correctly-facing neighbors.
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
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
		LOD 100

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

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
				float3 normalWS : TEXCOORD0;
				float2 uv : TEXCOORD1;
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
				OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
				OUT.uv = IN.uv;
				OUT.color = IN.color;
				return OUT;
			}

			half4 Frag(Varyings IN) : SV_Target
			{
				float2 tile = floor(IN.uv * _Tiling);

				// HLSL's fmod keeps the sign of its input (fmod(-1, 2) == -1, not 1 like a true modulo) - any UV
				// with a negative component, e.g. straight off a box centered on the origin, would otherwise send
				// checker negative and make lerp extrapolate past _Color1 instead of landing on it.
				float checker = abs(fmod(tile.x + tile.y, 2.0));

				// Multiplied in rather than replacing the checker outright - VertexColorNode defaults to white, so
				// a mesh nobody painted still shows the plain checker instead of turning solid white.
				half4 albedo = lerp(_Color1, _Color2, checker) * IN.color;

				// Deliberately simple (no shadows, no specular) - this only needs to make normal direction
				// visible, not look good: GetMainLight() with no shadow coord skips the shadow map entirely, and
				// the ambient SH probe keeps a backfacing normal from going flat black instead of just dim.
				float3 normalWS = normalize(IN.normalWS);
				Light mainLight = GetMainLight();
				float NdotL = saturate(dot(normalWS, mainLight.direction));
				float3 lighting = mainLight.color * NdotL + SampleSH(normalWS);

				return half4(albedo.rgb * lighting, albedo.a);
			}
			ENDHLSL
		}
	}
}
