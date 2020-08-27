Shader "Hidden/StochasticScreenSpaceReflection" {

	CGINCLUDE
		#include "SSRPass.cginc"
	ENDCG

	SubShader {
		ZTest Always 
		ZWrite Off
		Cull Front
		// 0
		Pass 
		{
			Name"Pass_HierarchicalZBuffer_Pass"
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment Hierarchical_ZBuffer
				#pragma enable_d3d11_debug_symbols
			ENDCG
		}
		// 1
		Pass 
		{
			Name"Pass_Hierarchical_ZTrace_MultiSampler"
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment Hierarchical_ZTrace_MultiSPP
				#pragma enable_d3d11_debug_symbols
			ENDCG
		} 
		// 2
		Pass 
		{
			Name"Pass_Spatiofilter_MultiSampler"
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment Spatiofilter_MultiSPP
				#pragma enable_d3d11_debug_symbols
			ENDCG
		} 
		// 3
		Pass 
		{
			Name"Pass_Temporalfilter_MultiSampler"
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment Temporalfilter_MultiSPP
				#pragma enable_d3d11_debug_symbols
			ENDCG
		} 
		// 4
		Pass 
		{
			Name"Pass_CombineReflection"
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment CombineReflectionColor
				#pragma enable_d3d11_debug_symbols
			ENDCG
		}
		// 5
		Pass 
		{
			Name"Pass_DeBug_SSRColor"
			CGPROGRAM
				#pragma vertex vert
				#pragma fragment DeBug_SSRColor
				#pragma enable_d3d11_debug_symbols
			ENDCG
		}
		
	}
}
