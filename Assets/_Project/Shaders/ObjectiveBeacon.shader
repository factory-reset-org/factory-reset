// Objective beacon: unlit, additive, double-sided. Used by the beam, the ground ring and the
// arrow of ObjectiveBeacon (one material each, the same shader).
// - Stripes scroll up the part (UV.y), so the beam reads as rising light.
// - Soft silhouette: the beam fades where its surface turns edge-on to the camera.
// - Top fade: the beam dissolves towards its top (UV.y 0..1).
// - Depth fade: fades where the beam meets the floor or a wall (needs the URP depth texture).
// - Near fade: the whole beacon dims as the camera gets close, down to _NearMin inside
//   _NearFadeEnd metres, measured flat (XZ) to the object's pivot so all parts dim together.
// All material values sit in UnityPerMaterial, so the SRP Batcher can batch the three parts.
Shader "ToyFactory/ObjectiveBeacon"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.79, 0.2, 1)
        _Intensity ("Intensity", Float) = 1
        _StripeDensity ("Stripes per metre", Float) = 1.5
        _StripeStrength ("Stripe strength", Range(0, 1)) = 0.5
        _ScrollSpeed ("Scroll speed (m/s)", Float) = 0.8
        _EdgeSoftness ("Edge softness (0 = off)", Range(0, 1)) = 0.6
        _TopFade ("Top fade start (UV.y, 1 = off)", Range(0, 1)) = 0.55
        _DepthFade ("Depth fade distance (m, 0 = off)", Float) = 0.35
        _NearFadeStart ("Near fade start (m)", Float) = 12
        _NearFadeEnd ("Near fade end (m)", Float) = 2
        _NearMin ("Brightness when near", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Beacon"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _StripeDensity;
                half _StripeStrength;
                half _ScrollSpeed;
                half _EdgeSoftness;
                half _TopFade;
                half _DepthFade;
                half _NearFadeStart;
                half _NearFadeEnd;
                half _NearMin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float heightOS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.screenPos = ComputeScreenPos(position.positionCS);
                output.uv = input.uv;
                output.heightOS = input.positionOS.y;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Rising stripes, in metres along the part.
                half wave = 0.5h + 0.5h * sin(TWO_PI * (input.heightOS * _StripeDensity - _Time.y * _ScrollSpeed * _StripeDensity));
                half stripes = lerp(1.0h, wave, _StripeStrength);

                // Soft silhouette: bright face-on, gone edge-on.
                half3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = abs(dot(normalize(input.normalWS), viewDirection));
                half edge = _EdgeSoftness > 0.001h ? smoothstep(0.0h, _EdgeSoftness, facing) : 1.0h;

                half top = _TopFade < 0.999h ? 1.0h - smoothstep(_TopFade, 1.0h, input.uv.y) : 1.0h;   // 1 = off

                // Soft intersection with the scene.
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragmentDepth = input.screenPos.w;
                half depth = _DepthFade > 0.001h ? saturate((sceneDepth - fragmentDepth) / _DepthFade) : 1.0h;

                // Dims as the camera walks up to the beacon.
                float3 pivot = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float flatDistance = distance(GetCameraPositionWS().xz, pivot.xz);
                half near = lerp(_NearMin, 1.0h, saturate((flatDistance - _NearFadeEnd) / max(_NearFadeStart - _NearFadeEnd, 0.01h)));

                half3 colour = _Color.rgb * (_Intensity * _Color.a * stripes * edge * top * depth * near);
                colour = MixFogColor(colour, half3(0, 0, 0), input.fogFactor);   // additive: fog fades to nothing
                return half4(colour, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
