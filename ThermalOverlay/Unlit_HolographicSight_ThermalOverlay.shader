// 
// This is a modified version of GTFO's Unlit/HolographicSight_Thermal shader
// Decompiled using a modified version of Asset Ripper
//

Shader "Unlit/HolographicSight_ThermalOverlay" {
	Properties {
		[Header(PICTURE CONFIG)] [Space(5)] 
        _Zoom ("Pixel Zoom", Range(0, 1)) = 0
		_RatioAdjust ("Aspect Ratio Adjust", Range(0, 2)) = 1
		_ScreenIntensity ("Screen Brightness", Range(0, 1)) = 0.2
		_OffAngleFade ("Off-Angle Fade", Range(0, 1)) = 0.95
		
        [Space(20)] 
        [Header(THERMAL CONFIG)] [Space(5)] 
        [NoScaleOffset] _HeatTex ("Heat Gradient", 2D) = "white" {}
		_HeatFalloff ("Distance Falloff", Range(0, 1)) = 0.01
		_FogFalloff ("Fog Falloff", Range(0, 1)) = 0.1
        _AlphaMult ("Coefficient for all computed alpha values", Float) = 1
		
        [Space(10)] 
        _BackgroundTemp ("Sky Temperature", Range(0, 1)) = 0.05
		_AmbientColorFactor ("Screen Color Factor", Range(0, 10)) = 5
		_AlbedoColorFactor ("Screen Albedo Factor", Range(0, 10)) = 0.5
		_AmbientTemp ("Temperature Scale", Range(0, 1)) = 0.15

		[Space(5)] 
        _OcclusionHeat ("Ambient Occlusion Factor", Float) = 0.5
		_BodyOcclusionHeat ("Ambient Occlusion Factor (Skin)", Float) = 2.5

		[Space(20)] 
        [Header(DISTORTION)] [Space(5)] 
        [NoScaleOffset] _DistortionTex ("Distortion Texture", 2D) = "gray" {}
		_DistortionCenter ("Distortion Center", Range(0, 1)) = 0.5
		_DistortionScale ("Distortion Scale", Float) = 1
		_DistortionSpeed ("Distortion Speed", Float) = 1
		
        [Space(20)] 
        [NoScaleOffset] _DistortionSignal ("Distortion Signal", 2D) = "black" {}
		_DistortionSignalSpeed ("Distortion Signal Speed", Float) = 0.025
		_DistortionMin ("Distortion Min", Range(0, 1)) = 0.01
		_DistortionMax ("Distortion Max", Range(0, 1)) = 0.4
		
        [Space(20)] 
        [Header(SHADOW ENEMIES)] [Space(5)] 
        _DistortionMinShadowEnemies ("Min Shadow Enemy Distortion", Range(0, 1)) = 0.2
		_DistortionMaxShadowEnemies ("Max Shadow Enemy Distortion", Range(0, 1)) = 1
		_DistortionSignalSpeedShadowEnemies ("Distortion Signal Speed Shadow enemies", Float) = 0.025
		_ShadowEnemyFresnel ("Shadow Enemy Fresnel", Float) = 10
		_ShadowEnemyHeat ("Shadow Enemy Heat", Range(0, 1)) = 0.1
		
        [Space(40)] 
        [Header(SIGHT CONFIG)] [Space(5)] 
        _ScopeCenter ("Scope Center (Object Space)", Vector) = (0, 0, 0, 1)
        _CenterWhenUnscoped ("When unsoped, use center-of-screen thermals?", Range(0, 1)) = 1
        _UncenterWhenScoped ("When scoped, use passthrough thermals?", Range(0, 1)) = 1
        [NoScaleOffset] _MainTex ("Sight R:Shadow G:Dirt", 2D) = "white" {}
		[NoScaleOffset] _ReticuleA ("ReticuleA R:Sharp G:Blurry", 2D) = "black" {}
		[NoScaleOffset] _ReticuleB ("ReticuleB R:Sharp G:Blurry", 2D) = "black" {}
		[NoScaleOffset] _ReticuleC ("ReticuleC R:Sharp G:Blurry", 2D) = "black" {}
		
        [Space(20)] 
        [HDR] _ReticuleColorA ("ReticuleA color", Color) = (1,1,1,1)
		[HDR] _ReticuleColorB ("ReticuleB color", Color) = (1,1,1,1)
		[HDR] _ReticuleColorC ("ReticuleC color", Color) = (1,1,1,1)
		
        [Space(20)] 
        _SightDirt ("Sight dirt", Range(0, 20)) = 1
		
        [Space(20)] 
        [HideInInspector] _SupportsFPSRendering ("Supports FPS Rendering", Float) = 1
		[Toggle(ENABLE_FPS_RENDERING)] 
        [HideInInspector] _EnableFPSRendering ("FPS Rendering?", Float) = 0
		
        [Space(20)] 
        [Toggle] _LitGlass ("Lit Glass", Float) = 1
		
        [Space(20)] 
        [Toggle] _ClipBorders ("Clip Borders", Float) = 1
		_AxisX ("X Axis", Vector) = (1,0,0,0)
		_AxisY ("Y Axis", Vector) = (0,1,0,0)
		_AxisZ ("Z Axis", Vector) = (0,0,1,0)
		[Toggle] _Flip ("Flip", Float) = 1
		_ProjDist1 ("Distance 1", Range(1, 100)) = 100
		_ProjDist2 ("Distance 2", Range(1, 100)) = 66
		_ProjDist3 ("Distance 3", Range(1, 100)) = 33
		_ProjSize1 ("Size 1", Range(0, 3)) = 1
		_ProjSize2 ("Size 2", Range(0, 3)) = 1
		_ProjSize3 ("Size 3", Range(0, 3)) = 1
		_ZeroOffset ("Zeroing", Range(-1, 1)) = 0
	}
	SubShader {
		Tags { "IGNOREPROJECTOR"="True" "QUEUE"="Transparent" "RenderType"="Transparent" }
		Pass {
			Tags { "IGNOREPROJECTOR"="True" "QUEUE"="Transparent" "RenderType"="Transparent" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            
            CGPROGRAM
            #pragma multi_compile __ EDITOR_RENDERING_ENABLED
            #pragma multi_compile __ ENABLE_FPS_RENDERING
            //#pragma shader_feature EDITOR_RENDERING_ENABLED ENABLE_FPS_RENDERING
            #define FPS_RENDERING_ALLOWED 1

            #if defined(FPS_RENDERING_ALLOWED) && defined(ENABLE_FPS_RENDERING)
                #define FPS_RENDERING
            #endif
			
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			struct v2f
			{
				float2 texcoord : TEXCOORD0;
				float2 texcoord10 : TEXCOORD10;
				float4 position : SV_POSITION0;
				float3 texcoord5 : TEXCOORD5;
				float3 texcoord6 : TEXCOORD6;
				float3 texcoord7 : TEXCOORD7;
				float4 texcoord8 : TEXCOORD8;
				float3 texcoord9 : TEXCOORD9;
				float3 texcoord11 : TEXCOORD11;
				float4 texcoord13 : TEXCOORD13;
			};

			struct fout
			{
				float4 sv_target : SV_Target0;
			};

            // $Globals ConstantBuffers for Vertex Shader
        #ifdef FPS_RENDERING
            float4x4 _FPS_VP;
        #endif
            float _FPSWeight;
            float _GlobalAimWeight;
            float _CenterWhenUnscoped;
            float _UncenterWhenScoped;
            float _DistortionSignalSpeed;
            float _DistortionMin;
            float _DistortionMax;
            float _DistortionMinShadowEnemies;
            float _DistortionMaxShadowEnemies;
            float _DistortionSignalSpeedShadowEnemies;
            float4 _ScopeCenter;
            float3 _AxisX;
            float3 _AxisY;
            float3 _AxisZ;
            float _OffAngleFade;

            // $Globals ConstantBuffers for Fragment Shader
        #ifndef EDITOR_RENDERING_ENABLED
            float3 _LitVolumeSize;
            float3 _LitVolumeSizeRcp;
            float _FogConversionFrom;
        #endif
            float4 _ShadowEnemyDepthTexture_TexelSize;
            int _FrameIndex;
        #ifndef EDITOR_RENDERING_ENABLED
            float _VolumesEnabled;
        #endif
            float _AmbientTemp;
            float _BackgroundTemp;
            float _AlbedoColorFactor;
            float _AmbientColorFactor;
            float _OcclusionHeat;
            float _BodyOcclusionHeat;
            float _ScreenIntensity;
            float _HeatFalloff;
        #ifndef EDITOR_RENDERING_ENABLED
            float _FogFalloff;
        #endif
            float _AlphaMult;
            float _Noise;
            float _RatioAdjust;
            float4 _DistortionTex_TexelSize;
            float _DistortionScale;
            float _DistortionSpeed;
            float _ShadowEnemyFresnel;
            float _ShadowEnemyHeat;
            float _Zoom;
            float _SightDirt;
            float4 _ReticuleColorA;
            float4 _ReticuleColorB;
            float4 _ReticuleColorC;
            float _ProjSize1;
            float _ProjSize2;
            float _ProjSize3;

            // Custom ConstantBuffers for Vertex Shader
            CBUFFER_START( CameraParams )
                float3 _CameraUp;
            CBUFFER_END
        
        #ifndef EDITOR_RENDERING_ENABLED
            // Custom ConstantBuffers for Fragment Shader
            CBUFFER_START( CameraParams )
                float3 _CameraForward;
            CBUFFER_END
            CBUFFER_START( FogParams )
                float4 _FogColor;
            CBUFFER_END
        #endif
        
            // Texture params for Vertex Shader
            sampler2D _DistortionSignal;

            // Texture params for Fragment Shader
            sampler2D _MainTex;
            sampler2D _DistortionTex;
            sampler2D _CameraDepthTexture;
            sampler2D _LitBufferCopy;
        #ifndef EDITOR_RENDERING_ENABLED
            sampler3D _FogVolume;
        #endif
            sampler2D _HeatTex;
            sampler2D _ReticuleA;
            sampler2D _ReticuleB;
            sampler2D _ReticuleC;
            sampler2D _CameraGBufferTexture0;
            sampler2D _CameraGBufferTexture2;
            sampler2D _ShadingTypeTexture;
            sampler2D _ShadowEnemyDepthTexture;
            Texture2D _Bluenoise64;

            v2f vert(appdata_full v)
            {
                v2f o;
                float4 tmp0;
                float4 tmp1;
                float4 tmp2;

                tmp0.xy = v.vertex.xy * float2(0.5, 0.5) + float2(0.5, 0.5);
                o.texcoord10.xy = v.texcoord.xy - tmp0.xy;
                o.texcoord.xy = v.texcoord.xy;
                o.texcoord.xy = v.texcoord.xy;
                #ifdef FPS_RENDERING
                    tmp0 = v.vertex.yyyy * unity_ObjectToWorld._m01_m11_m21_m31;
                    tmp0 = unity_ObjectToWorld._m00_m10_m20_m30 * v.vertex.xxxx + tmp0;
                    tmp0 = unity_ObjectToWorld._m02_m12_m22_m32 * v.vertex.zzzz + tmp0;
                    tmp1 = unity_ObjectToWorld._m03_m13_m23_m33 * v.vertex.wwww + tmp0;
                    tmp0 = tmp0 + unity_ObjectToWorld._m03_m13_m23_m33;
                    tmp2 = tmp1.yyyy * _FPS_VP._m01_m11_m21_m31;
                    tmp2 = _FPS_VP._m00_m10_m20_m30 * tmp1.xxxx + tmp2;
                    tmp2 = _FPS_VP._m02_m12_m22_m32 * tmp1.zzzz + tmp2;
                    tmp2 = _FPS_VP._m03_m13_m23_m33 * tmp1.wwww + tmp2;
                    o.texcoord7.xyz = tmp1.xyz;
                    tmp1 = tmp0.yyyy * unity_MatrixVP._m01_m11_m21_m31;
                    tmp1 = unity_MatrixVP._m00_m10_m20_m30 * tmp0.xxxx + tmp1;
                    tmp1 = unity_MatrixVP._m02_m12_m22_m32 * tmp0.zzzz + tmp1;
                    tmp0 = unity_MatrixVP._m03_m13_m23_m33 * tmp0.wwww + tmp1;
                    tmp1 = tmp2 - tmp0;
                    tmp1 = _FPSWeight.xxxx * tmp1 + tmp0;
                    tmp2.x = _FPSWeight > 0.0;
                    tmp0 = tmp2.xxxx ? tmp1 : tmp0;
                #else
                    tmp0 = v.vertex.yyyy * unity_ObjectToWorld._m01_m11_m21_m31;
                    tmp0 = unity_ObjectToWorld._m00_m10_m20_m30 * v.vertex.xxxx + tmp0;
                    tmp0 = unity_ObjectToWorld._m02_m12_m22_m32 * v.vertex.zzzz + tmp0;
                    tmp1 = tmp0 + unity_ObjectToWorld._m03_m13_m23_m33;
                    o.texcoord7.xyz = unity_ObjectToWorld._m03_m13_m23 * v.vertex.www + tmp0.xyz;
                    tmp0 = tmp1.yyyy * unity_MatrixVP._m01_m11_m21_m31;
                    tmp0 = unity_MatrixVP._m00_m10_m20_m30 * tmp1.xxxx + tmp0;
                    tmp0 = unity_MatrixVP._m02_m12_m22_m32 * tmp1.zzzz + tmp0;
                    tmp0 = unity_MatrixVP._m03_m13_m23_m33 * tmp1.wwww + tmp0;
                #endif
                o.position = tmp0;
                tmp1.xyz = _WorldSpaceCameraPos * unity_WorldToObject._m01_m11_m21;
                tmp1.xyz = unity_WorldToObject._m00_m10_m20 * _WorldSpaceCameraPos + tmp1.xyz;
                tmp1.xyz = unity_WorldToObject._m02_m12_m22 * _WorldSpaceCameraPos + tmp1.xyz;
                tmp1.xyz = tmp1.xyz + unity_WorldToObject._m03_m13_m23;
                o.texcoord5.x = dot(_AxisX.xy, tmp1.xyz);
                o.texcoord5.y = dot(_AxisY, tmp1.xyz);
                o.texcoord5.z = dot(_AxisZ, tmp1.xyz);
                o.texcoord6.x = dot(_AxisX.xy, v.vertex.xyz);
                o.texcoord6.y = dot(_AxisY, v.vertex.xyz);
                o.texcoord6.z = dot(_AxisZ, v.vertex.xyz);
                tmp0.y = tmp0.y * _ProjectionParams.x;
                tmp1.xzw = tmp0.xwy * float3(0.5, 0.5, 0.5);
                o.texcoord8.zw = tmp0.zw;
                o.texcoord8.xy = tmp1.zz + tmp1.xw;
                tmp0.x = unity_ObjectToWorld._m20;
                tmp0.y = unity_ObjectToWorld._m21;
                tmp0.z = unity_ObjectToWorld._m22;
                tmp0.w = dot(tmp0.xyz, tmp0.xyz);
                tmp0.w = rsqrt(tmp0.w);
                o.texcoord9.xyz = tmp0.www * tmp0.xyz;
                tmp0.x = dot(v.normal.xyz, unity_WorldToObject._m00_m10_m20);
                tmp0.y = dot(v.normal.xyz, unity_WorldToObject._m01_m11_m21);
                tmp0.z = dot(v.normal.xyz, unity_WorldToObject._m02_m12_m22);
                tmp0.w = dot(tmp0.xyz, tmp0.xyz);
                tmp0.w = rsqrt(tmp0.w);
                tmp0.xyz = tmp0.www * tmp0.xyz;
                tmp1.xyz = tmp0.yyy * unity_MatrixV._m01_m11_m21;
                tmp0.xyw = unity_MatrixV._m00_m10_m20 * tmp0.xxx + tmp1.xyz;
                tmp0.xyz = unity_MatrixV._m02_m12_m22 * tmp0.zzz + tmp0.xyw;
                tmp0.x = dot(tmp0.xyz, tmp0.xyz);
                tmp0.x = rsqrt(tmp0.x);
                tmp0.x = tmp0.x * tmp0.z;
                tmp0.x = abs(tmp0.x) - _OffAngleFade;
                tmp0.y = 1.0 - _OffAngleFade;
                tmp0.y = 1.0 / tmp0.y;
                tmp0.x = saturate(tmp0.y * tmp0.x);
                tmp0.y = tmp0.x * -2.0 + 3.0;
                tmp0.x = tmp0.x * tmp0.x;
                tmp0.x = tmp0.y * tmp0.x + -1.0;
                o.texcoord11.y = _FPSWeight * tmp0.x + 1.0;
                tmp0.x = _DistortionSignalSpeed * _Time.y;
                tmp0.yw = float2(0.0, 0.0);
                tmp1 = tex2Dlod(_DistortionSignal, float4(tmp0.xy, 0, 0.0));
                tmp0.xy = -float2(_DistortionMin.x, _DistortionMinShadowEnemies.x) + float2(_DistortionMax.x, _DistortionMaxShadowEnemies.x);
                o.texcoord11.x = tmp1.x * tmp0.x + _DistortionMin;
                tmp0.z = _DistortionSignalSpeedShadowEnemies * _Time.y;
                tmp1 = tex2Dlod(_DistortionSignal, float4(tmp0.zw, 0, 0.0));
                o.texcoord11.z = tmp1.x * tmp0.y + _DistortionMinShadowEnemies;

                // ARCHIVE
                    //tmp1.xyz = _CameraUp;
                    //tmp0.xyz = unity_WorldToObject._m01_m11_m21 * tmp1;
                    //tmp0.xyz = unity_WorldToObject._m00_m10_m20 * tmp1 + tmp0.xyz;
                    //tmp0.xyz = unity_WorldToObject._m02_m12_m22 * tmp1 + tmp0.xyz;
                    //tmp0.z = dot(tmp0.xyz, tmp0.xyz);
                    //tmp0.z = rsqrt(tmp0.z);
                    //tmp0.xy = tmp0.zz * tmp0.xy;
                    //tmp0.z = dot(tmp0.xy, tmp0.xy);
                    //tmp0.z = rsqrt(tmp0.z);
                    //tmp0.xy = tmp0.zz * tmp0.xy;
                    //
                    //tmp0.z = max(abs(tmp0.y), abs(tmp0.x));
                    //tmp0.z = 1.0 / tmp0.z;
                    //tmp0.w = min(abs(tmp0.y), abs(tmp0.x));
                    //tmp0.z = tmp0.z * tmp0.w;
                    //tmp0.w = tmp0.z * tmp0.z;
                    //tmp1.x = tmp0.w * 0.0208351 + -0.085133;
                    //tmp1.x = tmp0.w * tmp1.x + 0.180141;
                    //tmp1.x = tmp0.w * tmp1.x + -0.3302995;
                    //tmp0.w = tmp0.w * tmp1.x + 0.999866;
                    //tmp1.x = tmp0.w * tmp0.z;
                    //tmp1.x = tmp1.x * -2.0 + 1.570796;
                    //tmp1.y = abs(tmp0.y) < abs(tmp0.x);
                    //tmp1.x = tmp1.y ? tmp1.x : 0.0;
                    //tmp0.z = tmp0.z * tmp0.w + tmp1.x;
                    //tmp0.w = tmp0.y < -tmp0.y;
                    //tmp0.w = tmp0.w ? -3.141593 : 0.0;
                    //tmp0.z = tmp0.w + tmp0.z;
                    //tmp0.w = min(tmp0.y, tmp0.x);
                    //tmp0.x = max(tmp0.y, tmp0.x);
                    //tmp0.x = tmp0.x >= -tmp0.x;
                    //tmp0.y = tmp0.w < -tmp0.w;
                    //tmp0.x = tmp0.x ? tmp0.y : 0.0;
                    //tmp0.x = tmp0.x ? -tmp0.z : tmp0.z;
                    //
                    //tmp1.x = sin(tmp0.x);
                    //tmp2.x = cos(tmp0.x);
                    //tmp0.x = sin(-tmp0.x);
                    //tmp0.z = tmp1.x;
                    //tmp0.y = tmp2.x;
                    
                    //o.texcoord12.xy = tmp0.yz;
                    //o.texcoord13.xy = tmp0.xy;

                    // These two vectors are used to apply rotation to the thermal image
                    //  - The thermal image always pulls from the center of the screen
                    //  - These two vectors effectively rotate the sample to match if the scope is tilted, as
                    //    opposed to it ignoring tilt and thus being a straight rip of the player's view
                    // The above is the original, but the decomp is bugged and causes it to spin for some reason I cannot fathom
                    //  - The above puts _CameraUp in object space, calculates its xy (azimuth) angle, then stores the 
                    //    vector corresponding to that angle as well as its perpendicular
                
                    // This replacement works decently well. It calcs up and right in viewspace. However, some scopes are tilted so that z is up (ie. veruta)
                    //o.texcoord12.xy = normalize(mul((float3x3)unity_MatrixV, UnityObjectToWorldDir(float3(1, 0, 0)))).xy;
                    //o.texcoord13.xy = normalize(mul((float3x3)unity_MatrixV, UnityObjectToWorldDir(float3(0, 1, 0)))).xy;

                    // This replacement is about ideal. We get the direction, normalize, then adapt for clipspace for perspective correction
                    //float3x3 transform;
                    //transform = transpose((float3x3)unity_WorldToObject);
                    //transform = mul((float3x3)UNITY_MATRIX_V, transform);
                    //
                    //float3 tangent = mul(transform, v.tangent);
                    //tangent = normalize(tangent);
                    //tangent = mul(UNITY_MATRIX_P, tangent);
                    //
                    //// Normally, you multiply by tangent.w; however, we'd need to unflip it later, so we skip that
                    //float3 bitangent = mul(transform, cross(v.normal, v.tangent.xyz));
                    //bitangent = normalize(bitangent);
                    //bitangent = mul(UNITY_MATRIX_P, bitangent);
                    //
                    //o.texcoord12.xy = tangent;
                    //o.texcoord13.xy = bitangent;

                // Calculate screen-space coords, but only allow z-offset from the camera
                float4 clipPos = o.position;
                float offAimRatio = (1 - _GlobalAimWeight * _UncenterWhenScoped) * _CenterWhenUnscoped;
                if (offAimRatio > 0) {
                    float4 originPos = _ScopeCenter;
                    #ifdef FPS_RENDERING
                        originPos = mul(unity_ObjectToWorld, originPos);
                        originPos = mul(_FPS_VP, originPos);
                    #else
                        originPos = UnityObjectToClipPos(originPos);
                    #endif
                    clipPos.xy = clipPos.xy - (originPos.xy * offAimRatio.xx);
                }
                o.texcoord13 = ComputeScreenPos(clipPos);
                tmp0.x = 0.5 * o.texcoord13.ww;
                o.texcoord13.xy = (o.texcoord13.xy - tmp0.xx) * (1 - _Zoom).xx + tmp0.xx;
                return o;
            }

            // Unfortunately, the vert functions are different enough that I don't want to bother combining them

            // Keywords: <none>
            #if !defined(EDITOR_RENDERING_ENABLED) && !defined(FPS_RENDERING)
			fout frag(v2f inp)
			{
                fout o;
                float4 tmp0;
                float4 tmp1;
                float4 tmp2;
                float4 tmp3;
                float4 tmp4;
                float4 tmp5;
                float4 tmp6;
                float4 tmp7;
                float4 tmp8;
                tmp0.zw = inp.texcoord8.xy / inp.texcoord8.ww;
                tmp1.xyz = inp.texcoord7.xyz - _WorldSpaceCameraPos;
                tmp1.w = dot(tmp1.xyz, tmp1.xyz);
                tmp1.w = sqrt(tmp1.w);
                tmp1.xyz = tmp1.xyz / tmp1.www;
                tmp2.xy = tmp0.zw * _ScreenParams.xy;
                tmp2.zw = tmp2.xy * _DistortionTex_TexelSize.xy;
                tmp3.xw = float2(0.0, 0.0);
                tmp3.y = _DistortionSpeed * _Time.y;
                tmp2.zw = tmp2.zw * _DistortionScale.xx + tmp3.xy;
                tmp4 = tex2D(_DistortionTex, tmp2.zw);
                tmp2.z = tmp4.x * 2.0 + -1.0;
                tmp2.z = tmp2.z * 0.01;
                tmp4.y = tmp2.z * inp.texcoord11.z;
                tmp0.x = tmp2.z * inp.texcoord11.x + tmp0.z;
                tmp5 = tex2D(_CameraDepthTexture, tmp0.xw);
                tmp2.zw = _ZBufferParams.xz * tmp5.xx + _ZBufferParams.yw;
                tmp2.zw = float2(1.0, 1.0) / tmp2.zw;
                tmp5 = tex2D(_CameraGBufferTexture0, tmp0.xw);
                tmp6 = tex2D(_CameraGBufferTexture2, tmp0.xw);
                tmp6.xyz = tmp6.xyz * float3(2.0, 2.0, 2.0) + float3(-1.0, -1.0, -1.0);
                tmp7 = tex2D(_ShadingTypeTexture, tmp0.xw);
                tmp8 = tex2Dlod(_LitBufferCopy, float4(tmp0.xw, 0, 2.0));
                tmp3.x = dot(-tmp1.xyz, tmp6.xyz);
                tmp3.x = max(tmp3.x, 0.0);
                tmp3.y = dot(tmp5.xyz, float3(0.0396819, 0.4580218, 0.0060965));
                tmp3.y = 1.0 - tmp3.y;
                tmp3.y = tmp3.y * _AlbedoColorFactor + 1.0;
                tmp4.x = dot(tmp8.xyz, float3(0.0396819, 0.4580218, 0.0060965));
                tmp3.y = tmp4.x * _AmbientColorFactor + tmp3.y;
                tmp6 = tmp7.xxxx == float4(3.0 / 255.0, 5.0 / 255.0, 7.0 / 255.0, 2.0 / 255.0);
                tmp6 = tmp6 ? 1.0 : 0.0;
                tmp7 = tmp3.xxxx * tmp6;
                tmp3.x = tmp7.x * 0.5;
                tmp3.x = tmp3.y * _AmbientTemp + tmp3.x;
                tmp3.x = tmp7.y * 0.15 + tmp3.x;
                tmp3.x = tmp7.z * 0.075 + tmp3.x;
                tmp3.x = tmp7.w * 0.05 + tmp3.x;
                tmp3.y = 1.0 - tmp5.w;
                tmp4.x = _BodyOcclusionHeat - _OcclusionHeat;
                tmp4.x = tmp6.x * tmp4.x + _OcclusionHeat;
                tmp3.y = tmp3.y * tmp4.x + 1.0;
                tmp3.x = tmp3.y * tmp3.x;
                tmp3.y = -tmp2.w * _HeatFalloff;
                tmp3.y = tmp3.y * 1.442695;
                tmp3.y = exp(tmp3.y);
                tmp3.x = tmp3.y * tmp3.x;
                tmp2.z = tmp2.z == 1.0;
                tmp2.z = tmp2.z ? _BackgroundTemp : tmp3.x;
                tmp5 = tmp2.xyxy * float4(64.0, 64.0, 64.0, 64.0);
                tmp5 = tmp5 >= -tmp5.zwzw;
                tmp5 = tmp5 ? float4(64.0, 64.0, 0.015625, 0.015625) : float4(-64.0, -64.0, -0.015625, -0.015625);
                tmp2.xy = tmp2.xy * tmp5.zw;
                tmp2.xy = frac(tmp2.xy);
                tmp2.xy = tmp2.xy * tmp5.xy;
                tmp5.zw = float2(0.0, 0.0);
                tmp5 = _Bluenoise64.Load( tmp5.xyz);
                tmp2.x = floor(_FrameIndex);
                tmp2.x = tmp2.x * 1.618034 + tmp5.x;
                tmp2.x = frac(tmp2.x);
                tmp2.x = tmp2.x - 0.5;
                tmp2.x = tmp2.x * _Noise + tmp2.z;
                tmp2.x = max(tmp2.x, 0.0);
                tmp2.y = _VolumesEnabled != 0.0;
                if (tmp2.y) {
                    tmp1.x = dot(tmp1.xyz, _CameraForward);
                    tmp1.x = tmp2.w / tmp1.x;
                    tmp1.x = tmp1.x / _ProjectionParams.z;
                    tmp1.x = saturate(tmp1.x * _FogConversionFrom);
                    tmp1.x = sqrt(tmp1.x);
                    tmp1.x = tmp1.x * _LitVolumeSize.z + -0.5;
                    tmp1.y = frac(tmp1.x);
                    tmp1.x = tmp1.x - tmp1.y;
                    tmp5.xyz = float3(1.0, 2.0, 3.0) - tmp1.yyy;
                    tmp6.xyz = tmp5.xyz * tmp5.xyz;
                    tmp7.xy = tmp5.xy * tmp6.xy;
                    tmp1.yz = tmp7.xy * float2(4.0, 4.0);
                    tmp8.xy = tmp6.yz * tmp5.yz + -tmp1.yz;
                    tmp7.w = tmp7.x * 6.0 + tmp8.y;
                    tmp1.y = -tmp6.x * tmp5.x + 6.0;
                    tmp1.y = tmp1.y - tmp8.x;
                    tmp8.z = tmp1.y - tmp7.w;
                    tmp1.xy = tmp1.xx + float2(-0.5, 1.5);
                    tmp3.xy = tmp7.xw + tmp8.xz;
                    tmp4.xw = tmp8.xz / tmp3.xy;
                    tmp1.xy = tmp1.xy + tmp4.xw;
                    tmp0.xy = tmp1.yx * _LitVolumeSizeRcp.zz;
                    tmp5 = tex3D(_FogVolume, tmp0.zwy);
                    tmp6 = tex3D(_FogVolume, tmp0.zwx);
                    tmp1.x = tmp3.y + tmp3.x;
                    tmp1.x = tmp3.x / tmp1.x;
                    tmp1.y = tmp5.w - tmp6.w;
                    tmp1.x = tmp1.x * tmp1.y + tmp6.w;
                } else {
                    tmp1.x = 1.0;
                }
                tmp1.x = -tmp1.x * _FogFalloff;
                tmp1.x = tmp1.x * 1.442695;
                tmp1.x = exp(tmp1.x);
                tmp1.y = tmp2.x - _AmbientTemp;
                tmp1.x = tmp1.x * tmp1.y + _AmbientTemp;
                tmp4.z = 0.0;
                tmp4 = tmp0.zwzw + tmp4.yzyz;
                tmp5 = tex2D(_ShadowEnemyDepthTexture, tmp4.zw);
                tmp1.y = _ZBufferParams.z * tmp5.x + _ZBufferParams.w;
                tmp1.y = 1.0 / tmp1.y;
                tmp5.xw = _ShadowEnemyDepthTexture_TexelSize.xx;
                tmp5.yz = float2(0.0, 0.0);
                tmp4 = tmp5 * float4(2.0, 2.0, 2.0, 2.0) + tmp4;
                tmp5 = tex2D(_ShadowEnemyDepthTexture, tmp4.xy);
                tmp1.z = _ZBufferParams.z * tmp5.x + _ZBufferParams.w;
                tmp1.z = 1.0 / tmp1.z;
                tmp4 = tex2D(_ShadowEnemyDepthTexture, tmp4.zw);
                tmp2.x = _ZBufferParams.z * tmp4.x + _ZBufferParams.w;
                tmp2.x = 1.0 / tmp2.x;
                tmp2.z = tmp1.z - tmp1.y;
                tmp3.x = tmp2.x - tmp1.y;
                tmp2.z = max(abs(tmp2.z), abs(tmp3.x));
                tmp2.z = tmp2.z * _ShadowEnemyFresnel;
                tmp2.z = tmp2.z * -1.442695;
                tmp2.z = exp(tmp2.z);
                tmp2.z = 1.0 - tmp2.z;
                tmp3.x = tmp1.y < tmp2.w;
                tmp1.z = tmp1.z < tmp2.w;
                tmp1.z = tmp1.z ? tmp3.x : 0.0;
                tmp2.x = tmp2.x < tmp2.w;
                tmp1.z = tmp1.z ? tmp2.x : 0.0;
                tmp2.x = tmp3.x ? 1.0 : 0.0;
                tmp2.x = tmp2.z * tmp2.x;
                tmp2.x = tmp2.x * _ShadowEnemyHeat;
                tmp1.y = -tmp1.y * _HeatFalloff;
                tmp1.y = tmp1.y * 1.442695;
                tmp1.y = exp(tmp1.y);
                tmp1.y = tmp1.y * tmp2.x;
                tmp1.y = max(tmp1.y, tmp1.x);
                tmp3.z = tmp1.z ? tmp1.y : tmp1.x;
                float4 heatSample = tex2Dlod(_HeatTex, float4(tmp3.zw, 0, 0.0));
                tmp3 = heatSample;
                tmp1.xyz = tmp3.xyz * _ScreenIntensity.xxx;
                tmp4 = inp.texcoord.xyxy - float4(0.5, 0.5, 0.5, 0.5);
                tmp2.xz = tmp4.zw / _ProjSize1.xx;
                tmp5.x = 1.0;
                tmp5.y = _RatioAdjust;
                tmp5.zw = tmp2.xz * tmp5.xy;
                tmp2.xz = tmp2.xz * tmp5.xy + float2(0.5, 0.5);
                tmp6 = tex2D(_ReticuleA, tmp2.xz);
                tmp2.xz = abs(tmp5.zw) < float2(0.5, 0.5);
                tmp2.x = tmp2.z ? tmp2.x : 0.0;
                tmp2.x = tmp2.x ? 1.0 : 0.0;
                tmp2.x = tmp2.x * tmp6.x;
                tmp4 = tmp4 / float4(_ProjSize2.xx, _ProjSize3.xx);
                tmp6 = tmp5.xyxy * tmp4;
                tmp4 = tmp4 * tmp5.xyxy + float4(0.5, 0.5, 0.5, 0.5);
                tmp5 = tex2D(_ReticuleB, tmp4.xy);
                tmp6 = abs(tmp6) < float4(0.5, 0.5, 0.5, 0.5);
                tmp2.zw = tmp6.yw ? tmp6.xz : 0.0;
                tmp2.zw = tmp2.zw ? 1.0 : 0.0;
                tmp2.z = tmp2.z * tmp5.x;
                tmp4 = tex2D(_ReticuleC, tmp4.zw);
                tmp2.w = tmp2.w * tmp4.x;
                tmp3.xyz = -tmp3.xyz * _ScreenIntensity.xxx + _ReticuleColorA.xyz;
                tmp1.xyz = tmp2.xxx * tmp3.xyz + tmp1.xyz;
                tmp3.xyz = _ReticuleColorB.xyz - tmp1.xyz;
                tmp1.xyz = tmp2.zzz * tmp3.xyz + tmp1.xyz;
                tmp3.xyz = _ReticuleColorC.xyz - tmp1.xyz;
                tmp1.xyz = tmp2.www * tmp3.xyz + tmp1.xyz;
                if (tmp2.y) {
                    tmp1.w = tmp1.w / _ProjectionParams.z;
                    tmp1.w = saturate(tmp1.w * _FogConversionFrom);
                    tmp1.w = sqrt(tmp1.w);
                    tmp1.w = tmp1.w * _LitVolumeSize.z + -0.5;
                    tmp2.x = frac(tmp1.w);
                    tmp1.w = tmp1.w - tmp2.x;
                    tmp2.xyz = float3(1.0, 2.0, 3.0) - tmp2.xxx;
                    tmp3.xyz = tmp2.xyz * tmp2.xyz;
                    tmp4.xy = tmp2.xy * tmp3.xy;
                    tmp4.yz = tmp4.xy * float2(4.0, 4.0);
                    tmp5.xy = tmp3.yz * tmp2.yz + -tmp4.yz;
                    tmp4.w = tmp4.x * 6.0 + tmp5.y;
                    tmp2.x = -tmp3.x * tmp2.x + 6.0;
                    tmp2.x = tmp2.x - tmp5.x;
                    tmp5.z = tmp2.x - tmp4.w;
                    tmp2.xy = tmp1.ww + float2(-0.5, 1.5);
                    tmp2.zw = tmp4.xw + tmp5.xz;
                    tmp3.xy = tmp5.xz / tmp2.zw;
                    tmp2.xy = tmp2.xy + tmp3.xy;
                    tmp0.xy = tmp2.xy * _LitVolumeSizeRcp.zz;
                    tmp3 = tex3D(_FogVolume, tmp0.zwx);
                    tmp0 = tex3D(_FogVolume, tmp0.zwy);
                    tmp1.w = tmp2.w + tmp2.z;
                    tmp1.w = tmp2.z / tmp1.w;
                    tmp2 = tmp3 - tmp0;
                    tmp0 = tmp1.wwww * tmp2 + tmp0;
                } else {
                    tmp0 = float4(0.0, 0.0, 0.0, 1.0);
                }
                tmp2.xyz = -tmp0.www * _FogColor.xyz;
                tmp2.xyz = tmp2.xyz * float3(1.442695, 1.442695, 1.442695);
                tmp2.xyz = exp(tmp2.xyz);
                o.sv_target.xyz = tmp1.xyz * tmp2.xyz + tmp0.xyz;

                // Red is alpha limit/cap, green is dirt (darkens image)
                float4 mainSample = tex2D(_MainTex, inp.texcoord.xy);
                o.sv_target.xyz *=  (1 - mainSample.y * _SightDirt).xxx;
                o.sv_target.w = saturate(heatSample.w * mainSample.x * _AlphaMult);
                return o;
			}
            #endif
            
			// Keywords: EDITOR_RENDERING_ENABLED
            #if defined(EDITOR_RENDERING_ENABLED) && !defined(FPS_RENDERING)
			fout frag(v2f inp)
			{
                fout o;
                float4 tmp0;
                float4 tmp1;
                float4 tmp2;
                float4 tmp3;
                float4 tmp4;
                float4 tmp5;
                float4 tmp6;
                float4 tmp7;
                tmp0.xyz = inp.texcoord7.xyz - _WorldSpaceCameraPos;
                tmp0.w = dot(tmp0.xyz, tmp0.xyz);
                tmp0.w = sqrt(tmp0.w);
                tmp0.xyz = tmp0.xyz / tmp0.www;
                tmp1.y = _DistortionSpeed * _Time.y;
                tmp1.xw = float2(0.0, 0.0);
                tmp2.yz = inp.texcoord8.xy / inp.texcoord8.ww;
                tmp3.xy = tmp2.yz * _ScreenParams.xy;
                tmp3.zw = tmp3.xy * _DistortionTex_TexelSize.xy;
                tmp1.xy = tmp3.zw * _DistortionScale.xx + tmp1.xy;
                tmp4 = tex2D(_DistortionTex, tmp1.xy);
                tmp0.w = tmp4.x * 2.0 + -1.0;
                tmp0.w = tmp0.w * 0.01;
                tmp2.x = tmp0.w * inp.texcoord11.x + tmp2.y;
                tmp4.y = tmp0.w * inp.texcoord11.z;
                tmp5 = tex2D(_CameraGBufferTexture2, tmp2.xz);
                tmp5.xyz = tmp5.xyz * float3(2.0, 2.0, 2.0) + float3(-1.0, -1.0, -1.0);
                tmp0.x = dot(-tmp0.xyz, tmp5.xyz);
                tmp0.x = max(tmp0.x, 0.0);
                tmp5 = tex2D(_ShadingTypeTexture, tmp2.xz);
                tmp5 = tmp5.xxxx == float4(3.0 / 255.0, 5.0 / 255.0, 7.0 / 255.0, 2.0 / 255.0);
                tmp5 = tmp5 ? 1.0 : 0.0;
                tmp0 = tmp0.xxxx * tmp5;
                tmp0.x = tmp0.x * 0.5;
                tmp6 = tex2D(_CameraGBufferTexture0, tmp2.xz);
                tmp1.x = dot(tmp6.xyz, float3(0.0396819, 0.4580218, 0.0060965));
                tmp1.y = 1.0 - tmp6.w;
                tmp1.x = 1.0 - tmp1.x;
                tmp1.x = tmp1.x * _AlbedoColorFactor + 1.0;
                tmp6 = tex2Dlod(_LitBufferCopy, float4(tmp2.xz, 0, 2.0));
                tmp7 = tex2D(_CameraDepthTexture, tmp2.xz);
                tmp2.xw = _ZBufferParams.xz * tmp7.xx + _ZBufferParams.yw;
                tmp2.xw = float2(1.0, 1.0) / tmp2.xw;
                tmp3.z = dot(tmp6.xyz, float3(0.0396819, 0.4580218, 0.0060965));
                tmp1.x = tmp3.z * _AmbientColorFactor + tmp1.x;
                tmp0.x = tmp1.x * _AmbientTemp + tmp0.x;
                tmp0.x = tmp0.y * 0.15 + tmp0.x;
                tmp0.x = tmp0.z * 0.075 + tmp0.x;
                tmp0.x = tmp0.w * 0.05 + tmp0.x;
                tmp0.y = _BodyOcclusionHeat - _OcclusionHeat;
                tmp0.y = tmp5.x * tmp0.y + _OcclusionHeat;
                tmp0.y = tmp1.y * tmp0.y + 1.0;
                tmp0.x = tmp0.y * tmp0.x;
                tmp0.y = -tmp2.w * _HeatFalloff;
                tmp0.y = tmp0.y * 1.442695;
                tmp0.y = exp(tmp0.y);
                tmp0.x = tmp0.y * tmp0.x;
                tmp0.y = tmp2.x == 1.0;
                tmp0.x = tmp0.y ? _BackgroundTemp : tmp0.x;
                tmp5 = tmp3.xyxy * float4(64.0, 64.0, 64.0, 64.0);
                tmp5 = tmp5 >= -tmp5.zwzw;
                tmp5 = tmp5 ? float4(64.0, 64.0, 0.015625, 0.015625) : float4(-64.0, -64.0, -0.015625, -0.015625);
                tmp0.yz = tmp3.xy * tmp5.zw;
                tmp0.yz = frac(tmp0.yz);
                tmp0.yz = tmp0.yz * tmp5.xy;
                tmp3.zw = float2(0.0, 0.0);
                tmp3 = _Bluenoise64.Load( tmp3.xyz);
                tmp0.y = floor(_FrameIndex);
                tmp0.y = tmp0.y * 1.618034 + tmp3.x;
                tmp0.y = frac(tmp0.y);
                tmp0.y = tmp0.y - 0.5;
                tmp0.x = tmp0.y * _Noise + tmp0.x;
                tmp0.x = max(tmp0.x, 0.0);
                tmp4.z = 0.0;
                tmp3 = tmp2.yzyz + tmp4.yzyz;
                tmp4.xw = _ShadowEnemyDepthTexture_TexelSize.xx;
                tmp4.yz = float2(0.0, 0.0);
                tmp4 = tmp4 * float4(2.0, 2.0, 2.0, 2.0) + tmp3;
                tmp3 = tex2D(_ShadowEnemyDepthTexture, tmp3.zw);
                tmp0.y = _ZBufferParams.z * tmp3.x + _ZBufferParams.w;
                tmp0.y = 1.0 / tmp0.y;
                tmp3 = tex2D(_ShadowEnemyDepthTexture, tmp4.xy);
                tmp4 = tex2D(_ShadowEnemyDepthTexture, tmp4.zw);
                tmp0.z = _ZBufferParams.z * tmp4.x + _ZBufferParams.w;
                tmp0.z = 1.0 / tmp0.z;
                tmp0.w = _ZBufferParams.z * tmp3.x + _ZBufferParams.w;
                tmp0.w = 1.0 / tmp0.w;
                tmp1.xy = tmp0.wz - tmp0.yy;
                tmp0.zw = tmp0.zw < tmp2.ww;
                tmp2.x = tmp0.y < tmp2.w;
                tmp0.y = -tmp0.y * _HeatFalloff;
                tmp0.y = tmp0.y * 1.442695;
                tmp0.y = exp(tmp0.y);
                tmp1.x = max(abs(tmp1.y), abs(tmp1.x));
                tmp1.x = tmp1.x * _ShadowEnemyFresnel;
                tmp1.x = tmp1.x * -1.442695;
                tmp1.x = exp(tmp1.x);
                tmp1.x = 1.0 - tmp1.x;
                tmp1.y = tmp2.x ? 1.0 : 0.0;
                tmp0.w = tmp0.w ? tmp2.x : 0.0;
                tmp0.z = tmp0.z ? tmp0.w : 0.0;
                tmp0.w = tmp1.x * tmp1.y;
                tmp0.w = tmp0.w * _ShadowEnemyHeat;
                tmp0.y = tmp0.y * tmp0.w;
                tmp0.y = max(tmp0.y, tmp0.x);
                tmp1.z = tmp0.z ? tmp0.y : tmp0.x;
                float4 heatSample = tex2Dlod(_HeatTex, float4(tmp1.zw, 0, 0.0));
                tmp0 = heatSample;
                tmp1.xyz = tmp0.xyz * _ScreenIntensity.xxx;
                tmp0.xyz = -tmp0.xyz * _ScreenIntensity.xxx + _ReticuleColorA.xyz;
                tmp2 = inp.texcoord.xyxy - float4(0.5, 0.5, 0.5, 0.5);
                tmp3.xy = tmp2.zw / _ProjSize1.xx;
                tmp2 = tmp2 / float4(_ProjSize2.xx, _ProjSize3.xx);
                tmp4.x = 1.0;
                tmp4.y = _RatioAdjust;
                tmp3.zw = tmp3.xy * tmp4.xy;
                tmp3.xy = tmp3.xy * tmp4.xy + float2(0.5, 0.5);
                tmp5 = tex2D(_ReticuleA, tmp3.xy);
                tmp3.xy = abs(tmp3.zw) < float2(0.5, 0.5);
                tmp0.w = tmp3.y ? tmp3.x : 0.0;
                tmp0.w = tmp0.w ? 1.0 : 0.0;
                tmp0.w = tmp0.w * tmp5.x;
                tmp0.xyz = tmp0.www * tmp0.xyz + tmp1.xyz;
                tmp1.xyz = _ReticuleColorB.xyz - tmp0.xyz;
                tmp3 = tmp2 * tmp4.xyxy;
                tmp2 = tmp2 * tmp4.xyxy + float4(0.5, 0.5, 0.5, 0.5);
                tmp3 = abs(tmp3) < float4(0.5, 0.5, 0.5, 0.5);
                tmp3.xy = tmp3.yw ? tmp3.xz : 0.0;
                tmp3.xy = tmp3.xy ? 1.0 : 0.0;
                tmp4 = tex2D(_ReticuleB, tmp2.xy);
                tmp2 = tex2D(_ReticuleC, tmp2.zw);
                tmp0.w = tmp3.y * tmp2.x;
                tmp1.w = tmp3.x * tmp4.x;
                tmp0.xyz = tmp1.www * tmp1.xyz + tmp0.xyz;
                tmp1.xyz = _ReticuleColorC.xyz - tmp0.xyz;
                tmp0.xyz = tmp0.www * tmp1.xyz + tmp0.xyz;
                o.sv_target.xyz = tmp0.xyz * float3(4.0, 4.0, 4.0);

                // Red is alpha limit/cap, green is dirt (darkens image)
                float4 mainSample = tex2D(_MainTex, inp.texcoord.xy);
                o.sv_target.xyz *=  (1 - mainSample.y * _SightDirt).xxx;
                o.sv_target.w = saturate(heatSample.w * mainSample.x * _AlphaMult);
                return o;
			}
            #endif
            
			// Keywords: ENABLE_FPS_RENDERING, FPS_RENDERING_ALLOWED
            #if !defined(EDITOR_RENDERING_ENABLED) && defined(FPS_RENDERING)
			fout frag(v2f inp)
			{
                fout o;
                float4 tmp0;
                float4 tmp1;
                float4 tmp2;
                float4 tmp3;
                float4 tmp4;
                float4 tmp5;
                float4 tmp6;
                float4 tmp7;
                float4 tmp8;
                float4 tmp9;
                float4 tmp10;
                
                tmp0.xy = inp.texcoord8.xy / inp.texcoord8.ww;
                tmp1.xyz = inp.texcoord7.xyz - _WorldSpaceCameraPos;
                tmp1.w = dot(tmp1.xyz, tmp1.xyz);
                tmp1.w = sqrt(tmp1.w);
                tmp1.xyz = tmp1.xyz / tmp1.www;
                tmp2.xy = tmp0.xy * _ScreenParams.xy;
                tmp2.zw = tmp2.xy * _DistortionTex_TexelSize.xy;
                tmp3.xw = float2(0.0, 0.0);
                tmp3.y = _DistortionSpeed * _Time.y;
                tmp2.zw = tmp2.zw * _DistortionScale.xx + tmp3.xy;
                tmp4 = tex2D(_DistortionTex, tmp2.zw);
                tmp2.z = tmp4.x * 2.0 + -1.0;
                tmp2.z = tmp2.z * 0.01;

                tmp5 = inp.texcoord.xyxy - float4(0.5, 0.5, 0.5, 0.5);
                //tmp3.xy = tmp5.ww * inp.texcoord13.xy;
                //tmp3.xy = inp.texcoord12.xy * tmp5.zz + tmp3.xy;
                tmp3.xy = inp.texcoord13.xy / inp.texcoord13.ww;

                //tmp2.w = 1.0 - _Zoom;
                //tmp3.xy = tmp2.ww * tmp3.xy;
                tmp4.y = _ScreenParams.y / _ScreenParams.x;
                tmp4.z = _RatioAdjust;
                //tmp3.xy = tmp3.xy * tmp4.yz + float2(0.5, 0.5);
                tmp3.xy = tmp3.xy - tmp0.xy;
                tmp4.yz = _FPSWeight.xx * tmp3.xy + tmp0.xy;
                tmp6.y = tmp2.z * inp.texcoord11.z;
                tmp4.x = tmp2.z * inp.texcoord11.x + tmp4.y;
                tmp7 = tex2D(_CameraDepthTexture, tmp4.xz);
                tmp2.zw = _ZBufferParams.xz * tmp7.xx + _ZBufferParams.yw;
                tmp2.zw = float2(1.0, 1.0) / tmp2.zw;
                tmp7 = tex2D(_CameraGBufferTexture0, tmp4.xz);
                tmp8 = tex2D(_CameraGBufferTexture2, tmp4.xz);
                tmp8.xyz = tmp8.xyz * float3(2.0, 2.0, 2.0) + float3(-1.0, -1.0, -1.0);
                tmp9 = tex2D(_ShadingTypeTexture, tmp4.xz);
                tmp10 = tex2Dlod(_LitBufferCopy, float4(tmp4.xz, 0, 2.0));
                tmp3.x = dot(-tmp1.xyz, tmp8.xyz);
                tmp3.x = max(tmp3.x, 0.0);
                tmp3.y = dot(tmp7.xyz, float3(0.0396819, 0.4580218, 0.0060965));
                tmp3.y = 1.0 - tmp3.y;
                tmp3.y = tmp3.y * _AlbedoColorFactor + 1.0;
                tmp6.x = dot(tmp10.xyz, float3(0.0396819, 0.4580218, 0.0060965));
                tmp3.y = tmp6.x * _AmbientColorFactor + tmp3.y;
                tmp8 = tmp9.xxxx == float4(3.0 / 255.0, 5.0 / 255.0, 7.0 / 255.0, 2.0 / 255.0);
                tmp8 = tmp8 ? 1.0 : 0.0;
                tmp9 = tmp3.xxxx * tmp8;
                tmp3.x = tmp9.x * 0.5;
                tmp3.x = tmp3.y * _AmbientTemp + tmp3.x;
                tmp3.x = tmp9.y * 0.15 + tmp3.x;
                tmp3.x = tmp9.z * 0.075 + tmp3.x;
                tmp3.x = tmp9.w * 0.05 + tmp3.x;
                tmp3.y = 1.0 - tmp7.w;
                tmp6.x = _BodyOcclusionHeat - _OcclusionHeat;
                tmp6.x = tmp8.x * tmp6.x + _OcclusionHeat;
                tmp3.y = tmp3.y * tmp6.x + 1.0;
                tmp3.x = tmp3.y * tmp3.x;
                tmp3.y = -tmp2.w * _HeatFalloff;
                tmp3.y = tmp3.y * 1.442695;
                tmp3.y = exp(tmp3.y);
                tmp3.x = tmp3.y * tmp3.x;
                tmp2.z = tmp2.z == 1.0;
                tmp2.z = tmp2.z ? _BackgroundTemp : tmp3.x;
                tmp7 = tmp2.xyxy * float4(64.0, 64.0, 64.0, 64.0);
                tmp7 = tmp7 >= -tmp7.zwzw;
                tmp7 = tmp7 ? float4(64.0, 64.0, 0.015625, 0.015625) : float4(-64.0, -64.0, -0.015625, -0.015625);
                tmp2.xy = tmp2.xy * tmp7.zw;
                tmp2.xy = frac(tmp2.xy);
                tmp2.xy = tmp2.xy * tmp7.xy;
                tmp7.zw = float2(0.0, 0.0);
                tmp7 = _Bluenoise64.Load(tmp7.xyz);
                tmp2.x = floor(_FrameIndex);
                tmp2.x = tmp2.x * 1.618034 + tmp7.x;
                tmp2.x = frac(tmp2.x);
                tmp2.x = tmp2.x - 0.5;
                tmp2.x = tmp2.x * _Noise + tmp2.z;
                tmp2.x = max(tmp2.x, 0.0);
                tmp2.y = _VolumesEnabled != 0.0;
                if (tmp2.y) {
                    tmp1.x = dot(tmp1.xyz, _CameraForward);
                    tmp1.x = tmp2.w / tmp1.x;
                    tmp1.x = tmp1.x / _ProjectionParams.z;
                    tmp1.x = saturate(tmp1.x * _FogConversionFrom);
                    tmp1.x = sqrt(tmp1.x);
                    tmp1.x = tmp1.x * _LitVolumeSize.z + -0.5;
                    tmp1.y = frac(tmp1.x);
                    tmp1.x = tmp1.x - tmp1.y;
                    tmp7.xyz = float3(1.0, 2.0, 3.0) - tmp1.yyy;
                    tmp8.xyz = tmp7.xyz * tmp7.xyz;
                    tmp9.xy = tmp7.xy * tmp8.xy;
                    tmp1.yz = tmp9.xy * float2(4.0, 4.0);
                    tmp10.xy = tmp8.yz * tmp7.yz + -tmp1.yz;
                    tmp9.w = tmp9.x * 6.0 + tmp10.y;
                    tmp1.y = -tmp8.x * tmp7.x + 6.0;
                    tmp1.y = tmp1.y - tmp10.x;
                    tmp10.z = tmp1.y - tmp9.w;
                    tmp1.xy = tmp1.xx + float2(-0.5, 1.5);
                    tmp3.xy = tmp9.xw + tmp10.xz;
                    tmp6.xw = tmp10.xz / tmp3.xy;
                    tmp1.xy = tmp1.xy + tmp6.xw;
                    tmp4.xw = tmp1.yx * _LitVolumeSizeRcp.zz;
                    tmp7 = tex3D(_FogVolume, tmp4.yzw);
                    tmp8 = tex3D(_FogVolume, tmp4.yzx);
                    tmp1.x = tmp3.y + tmp3.x;
                    tmp1.x = tmp3.x / tmp1.x;
                    tmp1.y = tmp7.w - tmp8.w;
                    tmp1.x = tmp1.x * tmp1.y + tmp8.w;
                } else {
                    tmp1.x = 1.0;
                }
                tmp1.x = -tmp1.x * _FogFalloff;
                tmp1.x = tmp1.x * 1.442695;
                tmp1.x = exp(tmp1.x);
                tmp1.y = tmp2.x - _AmbientTemp;
                tmp1.x = tmp1.x * tmp1.y + _AmbientTemp;
                tmp6.z = 0.0;
                tmp4 = tmp4.yzyz + tmp6.yzyz;
                tmp6 = tex2D(_ShadowEnemyDepthTexture, tmp4.zw);
                tmp1.y = _ZBufferParams.z * tmp6.x + _ZBufferParams.w;
                tmp1.y = 1.0 / tmp1.y;
                tmp6.xw = _ShadowEnemyDepthTexture_TexelSize.xx;
                tmp6.yz = float2(0.0, 0.0);
                tmp4 = tmp6 * float4(2.0, 2.0, 2.0, 2.0) + tmp4;
                tmp6 = tex2D(_ShadowEnemyDepthTexture, tmp4.xy);
                tmp1.z = _ZBufferParams.z * tmp6.x + _ZBufferParams.w;
                tmp1.z = 1.0 / tmp1.z;
                tmp4 = tex2D(_ShadowEnemyDepthTexture, tmp4.zw);
                tmp2.x = _ZBufferParams.z * tmp4.x + _ZBufferParams.w;
                tmp2.x = 1.0 / tmp2.x;
                tmp2.z = tmp1.z - tmp1.y;
                tmp3.x = tmp2.x - tmp1.y;
                tmp2.z = max(abs(tmp2.z), abs(tmp3.x));
                tmp2.z = tmp2.z * _ShadowEnemyFresnel;
                tmp2.z = tmp2.z * -1.442695;
                tmp2.z = exp(tmp2.z);
                tmp2.z = 1.0 - tmp2.z;
                tmp3.x = tmp1.y < tmp2.w;
                tmp1.z = tmp1.z < tmp2.w;
                tmp1.z = tmp1.z ? tmp3.x : 0.0;
                tmp2.x = tmp2.x < tmp2.w;
                tmp1.z = tmp1.z ? tmp2.x : 0.0;
                tmp2.x = tmp3.x ? 1.0 : 0.0;
                tmp2.x = tmp2.z * tmp2.x;
                tmp2.x = tmp2.x * _ShadowEnemyHeat;
                tmp1.y = -tmp1.y * _HeatFalloff;
                tmp1.y = tmp1.y * 1.442695;
                tmp1.y = exp(tmp1.y);
                tmp1.y = tmp1.y * tmp2.x;
                tmp1.y = max(tmp1.y, tmp1.x);
                tmp1.x = tmp1.z ? tmp1.y : tmp1.x;
                tmp3.z = tmp1.x * inp.texcoord11.y;
                float4 heatSample = tex2Dlod(_HeatTex, float4(tmp3.zw, 0, 0.0));
                tmp3 = heatSample;
                tmp1.xyz = tmp3.xyz * _ScreenIntensity.xxx;
                tmp2.xz = tmp5.zw / _ProjSize1.xx;
                tmp4.x = 1.0;
                tmp4.y = _RatioAdjust;
                tmp4.zw = tmp2.xz * tmp4.xy;
                tmp2.xz = tmp2.xz * tmp4.xy + float2(0.5, 0.5);
                tmp6 = tex2D(_ReticuleA, tmp2.xz);
                tmp2.xz = abs(tmp4.zw) < float2(0.5, 0.5);
                tmp2.x = tmp2.z ? tmp2.x : 0.0;
                tmp2.x = tmp2.x ? 1.0 : 0.0;
                tmp2.x = tmp2.x * tmp6.x;
                tmp5 = tmp5 / float4(_ProjSize2.xx, _ProjSize3.xx);
                tmp6 = tmp4.xyxy * tmp5;
                tmp4 = tmp5 * tmp4.xyxy + float4(0.5, 0.5, 0.5, 0.5);
                tmp5 = tex2D(_ReticuleB, tmp4.xy);
                tmp6 = abs(tmp6) < float4(0.5, 0.5, 0.5, 0.5);
                tmp2.zw = tmp6.yw ? tmp6.xz : 0.0;
                tmp2.zw = tmp2.zw ? 1.0 : 0.0;
                tmp2.z = tmp2.z * tmp5.x;
                tmp4 = tex2D(_ReticuleC, tmp4.zw);
                tmp2.w = tmp2.w * tmp4.x;
                tmp3.xyz = -tmp3.xyz * _ScreenIntensity.xxx + _ReticuleColorA.xyz;
                tmp1.xyz = tmp2.xxx * tmp3.xyz + tmp1.xyz;
                tmp3.xyz = _ReticuleColorB.xyz - tmp1.xyz;
                tmp1.xyz = tmp2.zzz * tmp3.xyz + tmp1.xyz;
                tmp3.xyz = _ReticuleColorC.xyz - tmp1.xyz;
                tmp1.xyz = tmp2.www * tmp3.xyz + tmp1.xyz;
                if (tmp2.y) {
                    tmp1.w = tmp1.w / _ProjectionParams.z;
                    tmp1.w = saturate(tmp1.w * _FogConversionFrom);
                    tmp1.w = sqrt(tmp1.w);
                    tmp1.w = log(tmp1.w);
                    tmp1.w = tmp1.w * 1.5;
                    tmp1.w = exp(tmp1.w);
                    tmp1.w = tmp1.w * _LitVolumeSize.z + -0.5;
                    tmp2.x = frac(tmp1.w);
                    tmp1.w = tmp1.w - tmp2.x;
                    tmp2.xyz = float3(1.0, 2.0, 3.0) - tmp2.xxx;
                    tmp3.xyz = tmp2.xyz * tmp2.xyz;
                    tmp4.xy = tmp2.xy * tmp3.xy;
                    tmp4.yz = tmp4.xy * float2(4.0, 4.0);
                    tmp5.xy = tmp3.yz * tmp2.yz + -tmp4.yz;
                    tmp4.w = tmp4.x * 6.0 + tmp5.y;
                    tmp2.x = -tmp3.x * tmp2.x + 6.0;
                    tmp2.x = tmp2.x - tmp5.x;
                    tmp5.z = tmp2.x - tmp4.w;
                    tmp2.xy = tmp1.ww + float2(-0.5, 1.5);
                    tmp2.zw = tmp4.xw + tmp5.xz;
                    tmp3.xy = tmp5.xz / tmp2.zw;
                    tmp2.xy = tmp2.xy + tmp3.xy;
                    tmp0.zw = tmp2.xy * _LitVolumeSizeRcp.zz;
                    tmp3 = tex3D(_FogVolume, tmp0.xyz);
                    tmp0 = tex3D(_FogVolume, tmp0.xyw);
                    tmp1.w = tmp2.w + tmp2.z;
                    tmp1.w = tmp2.z / tmp1.w;
                    tmp2 = tmp3 - tmp0;
                    tmp0 = tmp1.wwww * tmp2 + tmp0;
                } else {
                    tmp0 = float4(0.0, 0.0, 0.0, 1.0);
                }
                tmp2.xyz = -tmp0.www * _FogColor.xyz;
                tmp2.xyz = tmp2.xyz * float3(1.442695, 1.442695, 1.442695);
                tmp2.xyz = exp(tmp2.xyz);
                o.sv_target.xyz = tmp1.xyz * tmp2.xyz + tmp0.xyz;

                // Red is alpha limit/cap, green is dirt (darkens image)
                float4 mainSample = tex2D(_MainTex, inp.texcoord.xy);
                o.sv_target.xyz *=  (1 - mainSample.y * _SightDirt).xxx;
                o.sv_target.w = saturate(heatSample.w * mainSample.x * _AlphaMult);
                return o;
			}
            #endif
            
			// Keywords: EDITOR_RENDERING_ENABLED, ENABLE_FPS_RENDERING, FPS_RENDERING_ALLOWED
            #if defined(EDITOR_RENDERING_ENABLED) && defined(FPS_RENDERING)
			fout frag(v2f inp)
			{
                fout o;
                float4 tmp0;
                float4 tmp1;
                float4 tmp2;
                float4 tmp3;
                float4 tmp4;
                float4 tmp5;
                float4 tmp6;
                float4 tmp7;
                float4 tmp8;
                tmp0.xyz = inp.texcoord7.xyz - _WorldSpaceCameraPos;
                tmp0.w = dot(tmp0.xyz, tmp0.xyz);
                tmp0.w = sqrt(tmp0.w);
                tmp0.xyz = tmp0.xyz / tmp0.www;
                tmp1.y = _DistortionSpeed * _Time.y;
                tmp1.xw = float2(0.0, 0.0);
                tmp2.xy = inp.texcoord8.xy / inp.texcoord8.ww;
                tmp2.zw = tmp2.xy * _ScreenParams.xy;
                tmp3.xy = tmp2.zw * _DistortionTex_TexelSize.xy;
                tmp1.xy = tmp3.xy * _DistortionScale.xx + tmp1.xy;
                tmp3 = tex2D(_DistortionTex, tmp1.xy);
                tmp0.w = tmp3.x * 2.0 + -1.0;
                tmp0.w = tmp0.w * 0.01;

                tmp4 = inp.texcoord.xyxy - float4(0.5, 0.5, 0.5, 0.5);
                //tmp3.xw = tmp4.ww * inp.texcoord13.xy;
                //tmp3.xw = inp.texcoord12.xy * tmp4.zz + tmp3.xw;
                tmp3.xw = inp.texcoord13.xy / inp.texcoord13.ww;
                
                //tmp1.x = 1.0 - _Zoom;
                //tmp1.xy = tmp1.xx * tmp3.xw;
                tmp3.y = _ScreenParams.y / _ScreenParams.x;
                tmp3.z = _RatioAdjust;
                //tmp1.xy = tmp1.xy * tmp3.yz + float2(0.5, 0.5);
                tmp1.xy = tmp1.xy - tmp2.xy;
                tmp3.yz = _FPSWeight.xx * tmp1.xy + tmp2.xy;
                tmp3.x = tmp0.w * inp.texcoord11.x + tmp3.y;
                tmp5.y = tmp0.w * inp.texcoord11.z;
                tmp6 = tex2D(_CameraGBufferTexture2, tmp3.xz);
                tmp6.xyz = tmp6.xyz * float3(2.0, 2.0, 2.0) + float3(-1.0, -1.0, -1.0);
                tmp0.x = dot(-tmp0.xyz, tmp6.xyz);
                tmp0.x = max(tmp0.x, 0.0);
                tmp6 = tex2D(_ShadingTypeTexture, tmp3.xz);
                tmp6 = tmp6.xxxx == float4(3.0 / 255.0, 5.0 / 255.0, 7.0 / 255.0, 2.0 / 255.0);
                tmp6 = tmp6 ? 1.0 : 0.0;
                tmp0 = tmp0.xxxx * tmp6;
                tmp0.x = tmp0.x * 0.5;
                tmp7 = tex2Dlod(_LitBufferCopy, float4(tmp3.xz, 0, 2.0));
                tmp1.x = dot(tmp7.xyz, float3(0.0396819, 0.4580218, 0.0060965));
                tmp7 = tex2D(_CameraGBufferTexture0, tmp3.xz);
                tmp8 = tex2D(_CameraDepthTexture, tmp3.xz);
                tmp2.xy = _ZBufferParams.xz * tmp8.xx + _ZBufferParams.yw;
                tmp2.xy = float2(1.0, 1.0) / tmp2.xy;
                tmp1.y = dot(tmp7.xyz, float3(0.0396819, 0.4580218, 0.0060965));
                tmp3.x = 1.0 - tmp7.w;
                tmp1.y = 1.0 - tmp1.y;
                tmp1.y = tmp1.y * _AlbedoColorFactor + 1.0;
                tmp1.x = tmp1.x * _AmbientColorFactor + tmp1.y;
                tmp0.x = tmp1.x * _AmbientTemp + tmp0.x;
                tmp0.x = tmp0.y * 0.15 + tmp0.x;
                tmp0.x = tmp0.z * 0.075 + tmp0.x;
                tmp0.x = tmp0.w * 0.05 + tmp0.x;
                tmp0.y = _BodyOcclusionHeat - _OcclusionHeat;
                tmp0.y = tmp6.x * tmp0.y + _OcclusionHeat;
                tmp0.y = tmp3.x * tmp0.y + 1.0;
                tmp0.x = tmp0.y * tmp0.x;
                tmp0.y = -tmp2.y * _HeatFalloff;
                tmp0.y = tmp0.y * 1.442695;
                tmp0.y = exp(tmp0.y);
                tmp0.x = tmp0.y * tmp0.x;
                tmp0.y = tmp2.x == 1.0;
                tmp0.x = tmp0.y ? _BackgroundTemp : tmp0.x;
                tmp6 = tmp2.zwzw * float4(64.0, 64.0, 64.0, 64.0);
                tmp6 = tmp6 >= -tmp6.zwzw;
                tmp6 = tmp6 ? float4(64.0, 64.0, 0.015625, 0.015625) : float4(-64.0, -64.0, -0.015625, -0.015625);
                tmp0.yz = tmp2.zw * tmp6.zw;
                tmp0.yz = frac(tmp0.yz);
                tmp0.yz = tmp0.yz * tmp6.xy;
                tmp6.zw = float2(0.0, 0.0);
                tmp6 = _Bluenoise64.Load( tmp6.xyz);
                tmp0.y = floor(_FrameIndex);
                tmp0.y = tmp0.y * 1.618034 + tmp6.x;
                tmp0.y = frac(tmp0.y);
                tmp0.y = tmp0.y - 0.5;
                tmp0.x = tmp0.y * _Noise + tmp0.x;
                tmp0.x = max(tmp0.x, 0.0);
                tmp5.z = 0.0;
                tmp3 = tmp3.yzyz + tmp5.yzyz;
                tmp5.xw = _ShadowEnemyDepthTexture_TexelSize.xx;
                tmp5.yz = float2(0.0, 0.0);
                tmp5 = tmp5 * float4(2.0, 2.0, 2.0, 2.0) + tmp3;
                tmp3 = tex2D(_ShadowEnemyDepthTexture, tmp3.zw);
                tmp0.y = _ZBufferParams.z * tmp3.x + _ZBufferParams.w;
                tmp0.y = 1.0 / tmp0.y;
                tmp3 = tex2D(_ShadowEnemyDepthTexture, tmp5.xy);
                tmp5 = tex2D(_ShadowEnemyDepthTexture, tmp5.zw);
                tmp0.z = _ZBufferParams.z * tmp5.x + _ZBufferParams.w;
                tmp0.z = 1.0 / tmp0.z;
                tmp0.w = _ZBufferParams.z * tmp3.x + _ZBufferParams.w;
                tmp0.w = 1.0 / tmp0.w;
                tmp1.xy = tmp0.wz - tmp0.yy;
                tmp0.zw = tmp0.zw < tmp2.yy;
                tmp2.x = tmp0.y < tmp2.y;
                tmp0.y = -tmp0.y * _HeatFalloff;
                tmp0.y = tmp0.y * 1.442695;
                tmp0.y = exp(tmp0.y);
                tmp1.x = max(abs(tmp1.y), abs(tmp1.x));
                tmp1.x = tmp1.x * _ShadowEnemyFresnel;
                tmp1.x = tmp1.x * -1.442695;
                tmp1.x = exp(tmp1.x);
                tmp1.x = 1.0 - tmp1.x;
                tmp1.y = tmp2.x ? 1.0 : 0.0;
                tmp0.w = tmp0.w ? tmp2.x : 0.0;
                tmp0.z = tmp0.z ? tmp0.w : 0.0;
                tmp0.w = tmp1.x * tmp1.y;
                tmp0.w = tmp0.w * _ShadowEnemyHeat;
                tmp0.y = tmp0.y * tmp0.w;
                tmp0.y = max(tmp0.y, tmp0.x);
                tmp0.x = tmp0.z ? tmp0.y : tmp0.x;
                tmp1.z = tmp0.x * inp.texcoord11.y;
                float4 heatSample = tex2Dlod(_HeatTex, float4(tmp1.zw, 0, 0.0));
                tmp0 = heatSample;
                tmp1.xyz = tmp0.xyz * _ScreenIntensity.xxx;
                tmp0.xyz = -tmp0.xyz * _ScreenIntensity.xxx + _ReticuleColorA.xyz;
                tmp2.xy = tmp4.zw / _ProjSize1.xx;
                tmp3 = tmp4 / float4(_ProjSize2.xx, _ProjSize3.xx);
                tmp4.x = 1.0;
                tmp4.y = _RatioAdjust;
                tmp2.zw = tmp2.xy * tmp4.xy;
                tmp2.xy = tmp2.xy * tmp4.xy + float2(0.5, 0.5);
                tmp5 = tex2D(_ReticuleA, tmp2.xy);
                tmp2.xy = abs(tmp2.zw) < float2(0.5, 0.5);
                tmp0.w = tmp2.y ? tmp2.x : 0.0;
                tmp0.w = tmp0.w ? 1.0 : 0.0;
                tmp0.w = tmp0.w * tmp5.x;
                tmp0.xyz = tmp0.www * tmp0.xyz + tmp1.xyz;
                tmp1.xyz = _ReticuleColorB.xyz - tmp0.xyz;
                tmp2 = tmp3 * tmp4.xyxy;
                tmp3 = tmp3 * tmp4.xyxy + float4(0.5, 0.5, 0.5, 0.5);
                tmp2 = abs(tmp2) < float4(0.5, 0.5, 0.5, 0.5);
                tmp2.xy = tmp2.yw ? tmp2.xz : 0.0;
                tmp2.xy = tmp2.xy ? 1.0 : 0.0;
                tmp4 = tex2D(_ReticuleB, tmp3.xy);
                tmp3 = tex2D(_ReticuleC, tmp3.zw);
                tmp0.w = tmp2.y * tmp3.x;
                tmp1.w = tmp2.x * tmp4.x;
                tmp0.xyz = tmp1.www * tmp1.xyz + tmp0.xyz;
                tmp1.xyz = _ReticuleColorC.xyz - tmp0.xyz;
                tmp0.xyz = tmp0.www * tmp1.xyz + tmp0.xyz;
                o.sv_target.xyz = tmp0.xyz * float3(4.0, 4.0, 4.0);

                // Red is alpha limit/cap, green is dirt (darkens image)
                float4 mainSample = tex2D(_MainTex, inp.texcoord.xy);
                o.sv_target.xyz *=  (1 - mainSample.y * _SightDirt).xxx;
                o.sv_target.w = saturate(heatSample.w * mainSample.x * _AlphaMult);
                return o;
			}
            #endif

			ENDCG
		}
	}
}