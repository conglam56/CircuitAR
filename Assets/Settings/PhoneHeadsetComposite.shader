Shader "Hidden/CircuitAR/PhoneHeadsetComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_LeftEye);
            TEXTURE2D(_RightEye);
            float4 _HeadsetCursor;
            float _HeadsetAspect;

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                bool rightEye = input.texcoord.x >= 0.5;
                float2 lens = float2(frac(2.0 * input.texcoord.x), input.texcoord.y) * 2.0 - 1.0;
                float2 lensSquared = lens * lens;
                float mask = 1.0 - smoothstep(0.87, 0.94, dot(lensSquared, lensSquared));
                if (mask <= 0.0) return half4(0, 0, 0, 1);

                // Fixed, mild distortion for an uncalibrated phone viewer.
                float2 warped = lens * (0.93 + 0.12 * dot(lens, lens));
                float2 eyeUv = warped * 0.5 + 0.5;
                float2 cameraUv = float2(0.25 + 0.5 * eyeUv.x, eyeUv.y);
                half3 background = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, cameraUv).rgb;
                half4 eye = rightEye ? SAMPLE_TEXTURE2D(_RightEye, sampler_LinearClamp, eyeUv)
                                    : SAMPLE_TEXTURE2D(_LeftEye, sampler_LinearClamp, eyeUv);
                half3 color = eye.rgb + background * (1.0 - eye.a);

                if (_HeadsetCursor.z > 0.0)
                {
                    float2 cursor = _HeadsetCursor.xy;
                    #if UNITY_UV_STARTS_AT_TOP
                        cursor.y = 1.0 - cursor.y;
                    #endif
                    float2 delta = (cameraUv - cursor) * float2(_HeadsetAspect, 1.0);
                    float distance = length(delta) / _HeadsetCursor.z;
                    float aa = max(fwidth(distance), 0.01);
                    float outline = 1.0 - smoothstep(1.1 - aa, 1.1 + aa, distance);
                    float white = 1.0 - smoothstep(0.9 - aa, 0.9 + aa, distance);
                    half3 cursorFill = half3(1.0, 1.0, 1.0);
                    if (_HeadsetCursor.w > 1.5)
                    {
                        // Pinch active: Con trỏ phát sáng xanh ngọc Cyan rực rỡ báo hiệu chụm ngón tay thành công
                        cursorFill = half3(0.18, 0.95, 1.0);
                    }
                    else if (_HeadsetCursor.w > 0.5)
                    {
                        outline *= smoothstep(0.45 - aa, 0.45 + aa, distance);
                        white *= smoothstep(0.64 - aa, 0.64 + aa, distance);
                    }
                    color = lerp(color, half3(0.06, 0.08, 0.1), outline);
                    color = lerp(color, cursorFill, white);
                }
                return half4(color * mask, 1);
            }
            ENDHLSL
        }
    }
}
