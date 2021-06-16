Shader "Unlit/VolumeShader"
{
    Properties
    {
        _MainTex ("Texture", 3D) = "white" {}
        _Alpha ("Alpha", float) = 0.02
        _StepSize ("Step Size", float) = 1
        _ResWidth ("Resolution Width", float) = 0
        _ResHeight ("Resolution Height", float) = 0
        _ResDepth ("Resolution Depth", float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One OneMinusSrcAlpha
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            // Maximum amount of raymarching samples
            #define MAX_STEP_COUNT 128

            // Allowed floating point inaccuracy
            #define EPSILON 0.00001f

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 objectVertex : TEXCOORD0;
                float3 vectorToSurface : TEXCOORD1;
            };

            sampler3D _MainTex;
            float4 _MainTex_ST;
            float _Alpha;
            float _StepSize;
            float _ResWidth;
            float _ResHeight;
            float _ResDepth;

            v2f vert (appdata v)
            {
                v2f o;

                // Vertex in object space this will be the starting point of raymarching
                o.objectVertex = v.vertex;
                // o.objectVertex = float3(v.vertex.x*_ResWidth, v.vertex.y*_ResHeight, v.vertex.z*_ResDepth);

                // Calculate vector from camera to vertex in world space
                float3 worldVertex = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.vectorToSurface = worldVertex - _WorldSpaceCameraPos;

                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 BlendUnder(float4 color, float4 newColor)
            {
                color.rgb += (1.0 - color.a) * newColor.a * newColor.rgb;
                color.a += (1.0 - color.a) * newColor.a;
                return color;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Start raymarching at the front surface of the object
                float3 rayOrigin = i.objectVertex;

                // Use vector from camera to object surface to get ray direction
                float3 rayDirection = mul(unity_WorldToObject, float4(normalize(i.vectorToSurface), 1));

                float4 color = float4(0, 0, 0, 0);
                float3 samplePosition = rayOrigin;

                // Raymarch through object space
                // [unroll(MAX_STEP_COUNT)]
                for (int i = 0; i < MAX_STEP_COUNT; i++)
                {
                    // Accumulate color only within unit cube bounds
                    if(max(abs(samplePosition.x), max(abs(samplePosition.y), abs(samplePosition.z))) < 0.5f + EPSILON)
                    {
                        float4 sampledColor = tex3D(_MainTex, samplePosition + float3(0.5f, 0.5f, 0.5f));
                        // sampledColor = tex3D(_MainTex, samplePosition);
                        // float3 tmp = float3(samplePosition.x*_ResWidth, samplePosition.y*_ResHeight, samplePosition.z*_ResDepth);
                        // sampledColor = tex3D(_MainTex, tmp + float3(0.5f, 0.5f, 0.5f));
                        // sampledColor = tex3D(_MainTex, samplePosition + float3(5.0f, 5.0f, 5.0f));

                        sampledColor.a *= _Alpha;
                        color = BlendUnder(color, sampledColor);
                        // color = sampledColor;

                        // if (sampledColor.x > 0 || sampledColor.y > 0 || sampledColor.z > 0 || sampledColor.a > 0) {
                        //     // sampledColor.a *= _Alpha;
                        //     sampledColor.a *= _Alpha;
                        //     color = float4(0,1,0,1);
                        //     // color = BlendUnder(float4(0,1,0,1), sampledColor);
                        //     break;
                        // }

                        samplePosition += rayDirection * _StepSize;
                    }
                }

                return color;
            }
            ENDCG
        }
    }
}
