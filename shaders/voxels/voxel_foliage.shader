HEADER
{
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Voxel Foliage";
    DevShader = true;
    DebugInfo = false;
}

FEATURES
{
    #include "common/features.hlsl"
    Feature( F_PREPASS_ALPHA_TEST, 0..1 );
    Feature( F_FOLIAGE_TYPE, 0..1(0="Generic", 1="TODO"), "Foliage Type" );
    Feature( F_ALPHA_TEST, 0..1, "Rendering" );
    Feature( F_ALPHA_CULLING, 0..1, "Rendering" );
    Feature( F_SPECULAR, 0..1, "Rendering" );
    FeatureRule( Requires1( F_ALPHA_CULLING, F_ALPHA_TEST ), "Requires alpha testing" );
    Feature( F_TRANSMISSIVE, 0..1, "Rendering" );
    Feature( F_TRANSMISSIVE_BACKFACE_NDOTL, 0..1, "Rendering" );
    FeatureRule( Requires1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_TRANSMISSIVE ), "Requires transmissive" );
}


MODES
{
    VrForward();													// Indicates this shader will be used for main rendering
    Depth( "culled_depth.vfx" ); 									// Shader that will be used for shadowing and depth prepass
    ToolsVis( S_MODE_TOOLS_VIS ); 									// Ability to see in the editor
    ToolsWireframe( "vr_tools_wireframe.vfx" ); 					// Allows for mat_wireframe to work
	ToolsShadingComplexity( "vr_tools_shading_complexity.vfx" ); 	// Shows how expensive drawing is in debug view
}

COMMON
{
    #include "common/shared.hlsl"

    #define S_TRANSLUCENT 0
    #define VS_INPUT_HAS_TANGENT_BASIS 1
    #define PS_INPUT_HAS_TANGENT_BASIS 1

    #define CUSTOM_TEXTURE_FILTERING
    SamplerState TextureFiltering < Filter( POINT ); MaxAniso( 8 ); >;

    #if ( PROGRAM != VFX_PROGRAM_PS ) // VS or GS only
        CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default3( 1.0f, 1.0f, 1.0f ) );

        #if( S_ALPHA_TEST )
            CreateInputTexture2D( TextureTranslucency, Linear, 8, "", "_trans", "Material,10/70", Default( 1.0f ) );
            // Store both alpha & color in a single texture for only 1 lookup
            #define COLOR_TEXTURE_CHANNELS Channel( RGB, AlphaWeighted( TextureColor, TextureTranslucency ), Srgb ); Channel( A, Box( TextureTranslucency ), Linear )
        #else
            #define COLOR_TEXTURE_CHANNELS Channel( RGB,  Box( TextureColor ), Srgb )
        #endif

        CreateTexture2DWithoutSampler( g_tColor )  < COLOR_TEXTURE_CHANNELS; OutputFormat( BC7 ); SrgbRead( true ); >;
    #endif
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

struct GS_INPUT
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"
	
	PixelInput MainVs( INSTANCED_SHADER_PARAMS( VS_INPUT i ) )
	{
		PixelInput o = ProcessVertex( i );
		// Add your vertex manipulation functions here
		return FinalizeVertex( o );
	}
}

GS
{
#if 0
    DynamicCombo( D_BAKED_LIGHTING_FROM_LIGHTMAP, 0..1, Sys( ALL ) );
    StaticCombo( S_ALPHA_CULLING, F_ALPHA_CULLING, Sys( ALL ) );

    bool FustrumCull( float4 vPositionPs0, float4 vPositionPs1, float4 vPositionPs2 )
    {
        // Discard if all the vertices are behind the near plane
        if ( ( vPositionPs0.z < 0.0 ) && ( vPositionPs1.z < 0.0 ) && ( vPositionPs2.z < 0.0 ) )
            return true;

        // Discard if all the vertices are behind the far plane
        if ( ( vPositionPs0.z > vPositionPs0.w ) && ( vPositionPs1.z > vPositionPs1.w ) && ( vPositionPs2.z > vPositionPs2.w ) )
        	return true;

        // Discard if all the vertices are outside one of the frustum sides
        if ( vPositionPs0.x < -vPositionPs0.w &&
        	 vPositionPs1.x < -vPositionPs1.w &&
        	 vPositionPs2.x < -vPositionPs2.w )
        	 return true;
        if ( vPositionPs0.y < -vPositionPs0.w &&
        	 vPositionPs1.y < -vPositionPs1.w &&
        	 vPositionPs2.y < -vPositionPs2.w )
        	 return true;
        if ( vPositionPs0.x > vPositionPs0.w &&
        	 vPositionPs1.x > vPositionPs1.w &&
        	 vPositionPs2.x > vPositionPs2.w )
        	 return true;
        if ( vPositionPs0.y > vPositionPs0.w &&
        	 vPositionPs1.y > vPositionPs1.w &&
        	 vPositionPs2.y > vPositionPs2.w )
        	 return true;

        return false;
    }

    [maxvertexcount( 3 )]
    void MainGs( triangle GS_INPUT i[ 3 ], inout TriangleStream< PS_INPUT > triStream )
    {
        PS_INPUT o = (PS_INPUT)0;

        if( FustrumCull(i[0].vPositionPs, i[1].vPositionPs, i[2].vPositionPs) )
        {
            return;
        }

        #if S_ALPHA_CULLING
            float2 vTextureDims = TextureDimensions2D( g_tColor, 0 ).xy;

            // Lets check dead center of the triangle to see if we have any texture data
            // this is the most likely place to have texture data
            {
                float2 v0 = i[0].vTextureCoords;
                float2 v1 = i[1].vTextureCoords;
                float2 v2 = i[2].vTextureCoords;

                float2 vP0 = lerp( v0, v1, 0.5f );
                float2 vP1 = lerp( vP0, v2, 0.5f );

                float2 vTexCoordsToTexture = frac(vP1) * vTextureDims;
                float flOpacity = Tex2DLoad( g_tColor, int3( vTexCoordsToTexture.xy, 0 ) ).a;
                if( flOpacity > 0.0f )
                {
                    [unroll]for( uint l = 0; l < 3; l++)
                    {
                        GSAppendVertex( triStream, i[l] );
                    }
                    GSRestartStrip( triStream );
                    return;
                }
            }

            // check each corner of our triangle to see if we have any texture data
            [unroll] for ( uint v = 0; v < 3; v++ )
            {
                float2 vTexCoordsToTexture = frac(i[v].vTextureCoords) * vTextureDims;
                float flOpacity = Tex2DLoad( g_tColor, int3( vTexCoordsToTexture.xy, 0 ) ).a;
                if( flOpacity > 0.0f )
                {
                    [unroll]for( uint j = 0; j < 3; j++)
                    {
                        GSAppendVertex( triStream, i[j] );
                    }
                    GSRestartStrip( triStream );
                    return;
                }
            }

            // We're still culling here but we didn't detect anything in the corners.
            // Lets check the the middle of each vertex to each other
            [unroll] for( uint j = 0; j < 3; j++ )
            {
                [unroll] for( uint k = 0; k < 3; k++ )
                {
                    if( j == k ) continue;

                     // Interpolate half way for each
                    float2 vTexCoordsToTexture = frac(lerp( i[j].vTextureCoords, i[k].vTextureCoords, 0.5f )) * vTextureDims;
                    float flOpacity = Tex2DLoad( g_tColor, int3( vTexCoordsToTexture.xy, 0 ) ).a;

                    if( flOpacity > 0.0f )
                    {
                        [unroll]for( uint l = 0; l < 3; l++)
                        {
                            GSAppendVertex( triStream, i[l] );
                        }
                        GSRestartStrip( triStream );
                        return;
                    }
                }   
            }

            // It's still possible that we missed something here but it's unlikely or miniscule
            // we can further refine this if needed to do extra checks or employ a different search method
            // Maybe generate an SDF for transparency?
        #else
            [unroll] for ( uint v = 0; v < 3; v++ )
            {
                GSAppendVertex( triStream, i[v] );
            }
            GSRestartStrip( triStream );
        #endif
    }
#endif
}

PS
{
    #include "common/pixel.hlsl"
    
    StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
    StaticCombo( S_TRANSMISSIVE, F_TRANSMISSIVE, Sys( ALL ) );
    StaticCombo( S_TRANSMISSIVE_BACKFACE_NDOTL, F_TRANSMISSIVE_BACKFACE_NDOTL, Sys( ALL ) );

    SamplerState g_sPointSampler < Filter( POINT ); AddressU( MIRROR ); AddressV( MIRROR ); >;

    #if S_ALPHA_TEST
        TextureAttribute( LightSim_Opacity_A, g_tColor );
    #endif

    #if (S_TRANSMISSIVE)
        CreateInputTexture2D( TextureTransmissiveColor, Srgb, 8, "", "_color", "Material,10/60", Default3( 1.0, 1.0, 1.0 ) );
        CreateTexture2DWithoutSampler( g_tTransmissiveColor ) < Channel( RGB, Box( TextureTransmissiveColor ), Srgb );  OutputFormat( BC7 ); SrgbRead( true ); >;
    #endif

    RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );

	float4 MainPs( PixelInput i ) : SV_Target0
	{
        const float flAlphaTestReference = 0.01;

        Material m = GatherMaterial( i );

        m.Albedo *= i.vVertexColor.rgb;

        #if (S_TRANSMISSIVE)
            float3 TransmissiveMask = Tex2DS( g_tTransmissiveColor, TextureFiltering, i.vTextureCoords.xy ).rgb;
        #else
            float3 TransmissiveMask = float3( 0.0f, 0.0f, 0.0f );
        #endif

        m.Transmission = TransmissiveMask;

        #if S_ALPHA_TEST
            m.Opacity = Tex2DS( g_tColor, TextureFiltering, i.vTextureCoords.xy ).a;
        #else
            m.Opacity = 1.0f;
        #endif

        ShadingModelValveStandard sm;

		return FinalizePixelMaterial( i, m, sm );
	}
}
