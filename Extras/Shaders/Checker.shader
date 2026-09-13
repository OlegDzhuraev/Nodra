// Nodra - UV test checker (URP). Not part of the runtime package (see Sources/) - a debug aid for visually
// checking UV output (e.g. AutoUVNode) for stretching, mirroring or seams: distortion shows up as warped or
// unevenly-sized squares instead of a clean, evenly-tiled grid.
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

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
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
				OUT.uv = IN.uv;
				return OUT;
			}

			half4 Frag(Varyings IN) : SV_Target
			{
				float2 tile = floor(IN.uv * _Tiling);

				// HLSL's fmod keeps the sign of its input (fmod(-1, 2) == -1, not 1 like a true modulo) - any UV
				// with a negative component, e.g. straight off a box centered on the origin, would otherwise send
				// checker negative and make lerp extrapolate past _Color1 instead of landing on it.
				float checker = abs(fmod(tile.x + tile.y, 2.0));
				return lerp(_Color1, _Color2, checker);
			}
			ENDHLSL
		}
	}
}
