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
            float4 _HeadsetSecondCursor;
            float _HeadsetAspect;
            float _HeadsetViewScale;
            float _HeadsetImageOffset;

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                bool rightEye = input.texcoord.x >= 0.5;
                float2 lens = float2(frac(2.0 * input.texcoord.x), input.texcoord.y) * 2.0 - 1.0;
                // Lens outline stays fixed. Image alignment/scaling must not shrink the eye aperture.
                float2 frameSquared = lens * lens;
                float mask = 1.0 - smoothstep(0.87, 0.94, dot(frameSquared, frameSquared));
                if (mask <= 0.0) return half4(0, 0, 0, 1);
                lens.x -= rightEye ? _HeadsetImageOffset : -_HeadsetImageOffset;
                lens /= max(_HeadsetViewScale, 0.65);
                // Fixed, mild distortion for an uncalibrated phone viewer.
                float2 warped = lens * (0.93 + 0.12 * dot(lens, lens));
                float2 eyeUv = warped * 0.5 + 0.5;
                // Match the full-height SBS projection; do not letterbox the eye vertically.
                float2 cameraUv = 0.5 + (eyeUv - 0.5) * float2(0.5, 1.0) * 1.20;
                bool cameraValid = all(cameraUv >= 0.0) && all(cameraUv <= 1.0);
                half3 background = cameraValid ? SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, cameraUv).rgb : half3(0,0,0);
                half4 eye = rightEye ? SAMPLE_TEXTURE2D(_RightEye, sampler_LinearClamp, eyeUv)
                                    : SAMPLE_TEXTURE2D(_LeftEye, sampler_LinearClamp, eyeUv);
                if (any(eyeUv < 0.0) || any(eyeUv > 1.0)) eye = 0;
                half3 color = eye.rgb + background * (1.0 - eye.a);

                [unroll] for (int cursorIndex = 0; cursorIndex < 2; cursorIndex++)
                {
                    float4 activeCursor = cursorIndex == 0 ? _HeadsetCursor : _HeadsetSecondCursor;
                    if (activeCursor.z <= 0.0) continue;
                    float2 cursor = activeCursor.xy;
                    #if UNITY_UV_STARTS_AT_TOP
                        cursor.y = 1.0 - cursor.y;
                    #endif
                    float2 delta = (cameraUv - cursor) * float2(_HeadsetAspect, 1.0);
                    float distance = length(delta) / activeCursor.z;
                    float aa = max(fwidth(distance), 0.01);
                    float outline = 1.0 - smoothstep(1.1 - aa, 1.1 + aa, distance);
                    float white = 1.0 - smoothstep(0.9 - aa, 0.9 + aa, distance);
                    half3 cursorFill = half3(1.0, 1.0, 1.0);
                    if (activeCursor.w > 1.5)
                    {
                        // Pinch active: Con trỏ phát sáng xanh ngọc Cyan rực rỡ báo hiệu chụm ngón tay thành công
                        cursorFill = half3(0.18, 0.95, 1.0);
                    }
                    else if (activeCursor.w > 0.5)
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
