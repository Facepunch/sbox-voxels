//=========================================================================================================================
// Optional
//=========================================================================================================================
HEADER
{
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Voxel";
	DebugInfo = false;
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
FEATURES
{
	#include "common/features.hlsl"

	Feature( F_ALPHA_TEST, 0..1, "Translucent" );
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
	FeatureRule( Allow1( F_TRANSLUCENT, F_ALPHA_TEST ), "Translucent and Alpha Test are not compatible" );
	Feature( F_PREPASS_ALPHA_TEST, 0..1, "Translucent" );
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
MODES
{
	VrForward();
	Depth( S_MODE_DEPTH );
	ToolsVis( S_MODE_TOOLS_VIS );
	ToolsWireframe( S_MODE_TOOLS_WIREFRAME );
	ToolsShadingComplexity( "vr_tools_shading_complexity.vfx" );
}

//=========================================================================================================================
COMMON
{
	#define VS_INPUT_HAS_TANGENT_BASIS 1
	#define PS_INPUT_HAS_TANGENT_BASIS 1
	
	#include "system.fxc" // This should always be the first include in COMMON
	#include "sbox_shared.fxc"
	float g_flVoxelSize < Default( 32.0 ); >;

	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
}

//=========================================================================================================================

struct VertexInput
{
	#include "common/vertexinput.hlsl"

	uint3 vData : TEXCOORD10 < Semantic( None ); >;
};

//=========================================================================================================================

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float2 vTexCoord : TEXCOORD9;
	uint nTextureIndex : TEXCOORD10;
	int3 vPositionOs : TEXCOORD11;
	float3 vTintColor : TEXCOORD13;
	int iHueShift : TEXCOORD14;
	int iNormal : TEXCOORD15;
};

//=========================================================================================================================

VS
{
	#include "common/vertex.hlsl"
	

	PixelInput MainVs( INSTANCED_SHADER_PARAMS( VertexInput i ) )
	{
		int3 chunkPosition = int3( i.vData.y & 0x3F, (i.vData.y >> 6) & 0x3F, (i.vData.y >> 12) & 0x3F );
		float3 position = float3( float( i.vData.x & ( 0x3F ) ), float( ( i.vData.x >> 6 ) & ( 0x3F ) ), float( ( i.vData.x >> 12 ) & ( 0x3F ) ) );
		int texindex = int( ( i.vData.x >> 18 ) & ( 0x1ff ) );
		int normal = int( ( i.vData.x >> 27 ) & ( 0x7 ) );
		int HueShift = int( ( i.vData.y >> 18 ) & ( 0x3F ) );

		i.vPositionOs = position * g_flVoxelSize;

		float3 vNormalOs = float3( 0, 0, 1.0f );
		if ( normal == 1 ) vNormalOs = float3( 0, 0, -1 );
		else if ( normal == 2 ) vNormalOs = float3( 0, -1, 0 );
		else if ( normal == 3 ) vNormalOs = float3( 0, 1, 0 );
		else if ( normal == 4 ) vNormalOs = float3( -1, 0, 0 );
		else if ( normal == 5 ) vNormalOs = float3( 1, 0, 0 );

		float3 vTangentOs = float3( 1, 0, 0 );
		if ( normal == 1 ) vTangentOs = float3( 1, 0, 0 );
		else if ( normal == 2 ) vTangentOs = float3( 0, 0, -1 );
		else if ( normal == 3 ) vTangentOs = float3( 0, 0, -1 );
		else if ( normal == 4 ) vTangentOs = float3( 0, 0, -1 );
		else if ( normal == 5 ) vTangentOs = float3( 0, 0, -1 );

		float3 vBinormalOs = cross( vNormalOs, vTangentOs );
		float2 vTexCoord = float2( dot( vBinormalOs, i.vPositionOs ), dot( vTangentOs, i.vPositionOs ) );
		PixelInput o = ProcessVertex( i );
		o.vNormalWs = vNormalOs;
		o.vTintColor = float3( float((i.vData.z >> 16) & 0x0ff) / 255.0f, float((i.vData.z >> 8) & 0x0ff) / 255.0f, float(i.vData.z & 0x0ff) / 255.0f );
		o.vTangentUWs = vBinormalOs;
		o.vTangentVWs = vTangentOs;
		o.vTexCoord = vTexCoord.xy;
		o.iHueShift = HueShift;
		o.nTextureIndex = texindex;
		o.iNormal = normal;

		// Fudge the number up a little so we're not checking the current(in ground) blocks
		o.vPositionOs = chunkPosition + vNormalOs;
		return FinalizeVertex( o );
	}
}

//=========================================================================================================================

PS
{
	#define S_NON_DIRECTIONAL_DIFFUSE_LIGHTING 1
	float g_flTextureAtlasCellSize< Default(64.0f); >;
	StaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );
	StaticCombo( S_MODE_TOOLS_WIREFRAME, 0..1, Sys( ALL ) );
	StaticCombo( S_DO_NOT_CAST_SHADOWS, F_DO_NOT_CAST_SHADOWS, Sys( ALL ) );

	#if ( S_MODE_TOOLS_WIREFRAME )
		RenderState( FillMode, WIREFRAME );
		RenderState( SlopeScaleDepthBias, -0.5 );
		RenderState( DepthBiasClamp, -0.0005 );
		RenderState( DepthWriteEnable, false );
		#define DEPTH_STATE_ALREADY_SET
	#endif

	#include "common/pixel.hlsl"

	CreateInputTexture2D( TextureAtlasColor, Srgb, 8, "", "_color", "Voxel Atlas,10/10", Default4( 1.0f, 1.0f, 1.0f, 1.0f ) );

	SamplerState g_sPointSampler < Filter( POINT ); AddressU( MIRROR ); AddressV( MIRROR ); >;

	CreateTexture2DWithoutSampler( g_tAtlasColor )  < Channel( RGBA,  None( TextureAtlasColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); Filter( POINT ); AddressU( MIRROR ); AddressV( MIRROR ); >;

	CreateInputTexture2D( TextureAtlasNormal,           Linear, 8, "NormalizeNormals", "_normal", 		"Voxel Atlas,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( TextureAtlasRoughness,        Linear, 8, "",                 "_rough",  		"Voxel Atlas,10/30", Default( 0.5 ) );
	CreateInputTexture2D( TextureAtlasMetalness,        Linear, 8, "",                 "_metal",  		"Voxel Atlas,10/40", Default( 1.0 ) );
	CreateInputTexture2D( TextureAtlasAmbientOcclusion, Linear, 8, "",                 "_ao",     		"Voxel Atlas,10/50", Default( 1.0 ) );
	CreateInputTexture2D( TextureAtlasEmission, 		Linear, 8, "",                 "_emission",     "Voxel Atlas,10/60", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( TextureAtlasHue, 				Linear, 8, "",                 "_hue",     		"Voxel Atlas,10/70", Default3( 1.0f, 1.0f, 1.0f ) );

	CreateTexture2DWithoutSampler( g_tAtlasNormal )   < Channel( RGBA, HemiOctNormal( TextureAtlasNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTexture2DWithoutSampler( g_tAtlasRma )      < Channel( R,    None( TextureAtlasRoughness ), Linear ); Channel( G, None( TextureAtlasMetalness ), Linear ); Channel( B, None( TextureAtlasAmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTexture2DWithoutSampler( g_tAtlasEmission ) < Channel( RGB, None( TextureAtlasEmission ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTexture2DWithoutSampler( g_tAtlasHue )  	  < Channel( RGB,  None( TextureAtlasHue ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); Filter( POINT ); AddressU( MIRROR ); AddressV( MIRROR ); >;
	
	CreateInputTexture3D( TextureDataMap, Linear, 32, "", "_datamap", "Voxel Atlas,10/70", Default3( 1.0, 1.0, 1.0 ) );

	CreateTexture3DWithoutSampler( g_tDataMap ) < Channel( R, None( TextureDataMap ), Linear ); SrgbRead( false ); Filter( POINT ); OutputFormat( R32F ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); > ;

	float g_flVoxelOpacity< Range(0.0f, 1.0f); Default(1.0f); >;
	float g_flVoxelBrightness< Range(0.0f, 1.0f); Default(1.0f); >;
	float g_flDiffuseBoost< Range(0.0f, 1.0f); Default(0.0f); >;
	float g_flSpecularBoost< Range(0.1f, 32.0f); Default(1.0f); >;
	float g_flLightDirectionBias< Range(0.0f, 1.0f); Default(0.0f); >;

	BoolAttribute( DoNotCastShadows, F_DO_NOT_CAST_SHADOWS ? true : false );

	class ShadingModelValveWithDiffuse : ShadingModel
	{
		CombinerInput Input;
		ShadeParams shadeParams;

		CombinerInput MaterialToCombinerInput( PixelInput i, Material m )
		{
			CombinerInput o;

			o = PS_CommonProcessing( i );
			
			#if ( S_ALPHA_TEST )
			{
				o.flOpacity = m.Opacity * o.flOpacity;
				clip( o.flOpacity - .001 );

				o.flOpacity = AdjustOpacityForAlphaToCoverage( o.flOpacity, g_flAlphaTestReference, g_flAntiAliasedEdgeStrength, i.vTextureCoords.xy );
				clip( o.flOpacity - 0.001 );
			}
			#elif ( S_TRANSLUCENT )
			{
				o.flOpacity *= m.Opacity * g_flOpacityScale;
			}
			#endif

			o = CalculateDiffuseAndSpecularFromAlbedoAndMetalness( o, m.Albedo.rgb, m.Metalness );

			PS_CommonTransformNormal( i, o, DecodeHemiOctahedronNormal( m.Normal.rg ) );
			o.vRoughness = m.Roughness;
			o.flRetroReflectivity = 1.0f;
			o.vEmissive = m.Emission;
			o.flAmbientOcclusion = m.AmbientOcclusion.x;
			o.vTransmissiveMask = m.Transmission;

			return o;
		}

		void Init( const PixelInput pixelInput, const Material material )
		{
			Input = MaterialToCombinerInput( pixelInput, material );
			shadeParams = ShadeParams::ProcessMaterial( pixelInput, material );
		}
		
		LightShade Direct( const LightData light )
		{
			LightShade o;
			o.Diffuse = 0;
			o.Specular = 0;
			return o;
		}

		void ComputeDiffuseAndSpecularTermsForVoxel( out float3 o_vDiffuseTerm, out float3 o_vSpecularTerm, bool bSpecular, const FinalCombinerInput_t f, float3 vPositionToLightDirWs, float3 vPositionToCameraDirWs, float2 vDiffuseExponent )
		{
			float3 vNormalWs = f.vNormalWs.xyz;
			float flNDotL = max( ClampToPositive( lerp(dot( vNormalWs.xyz, vPositionToLightDirWs.xyz ), dot( vNormalWs.xyz, vPositionToCameraDirWs.xyz ), g_flLightDirectionBias) ), g_flDiffuseBoost );
			float flDiffuseExponent = ( vDiffuseExponent.x + vDiffuseExponent.y ) * 0.5;
			o_vDiffuseTerm.rgb = pow( flNDotL, flDiffuseExponent ) * ( ( flDiffuseExponent + 1.0 ) * 0.5 ).xxx;
			[branch] if ( bSpecular )
			{
				float3 vHalfAngleDirWs = normalize( vPositionToLightDirWs.xyz + vPositionToCameraDirWs.xyz );
				float flNdotH = dot( vHalfAngleDirWs.xyz, vNormalWs.xyz );
				float flNdotV = ClampToPositive( dot( vNormalWs.xyz, vPositionToCameraDirWs.xyz ) );
				float flSpecularTerm = ComputeGGXBRDF( f.vRoughness.xx, 1.0f, flNdotV, flNdotH, f.vPositionSs.xy ).x;
				flSpecularTerm *= g_flSpecularBoost;

				float flLDotH = ClampToPositive( dot( vPositionToLightDirWs.xyz, vHalfAngleDirWs.xyz ) );
				float3 vFresnel = f.vSpecularColor + ( ( 1.0 - f.vSpecularColor ) * pow( 1.0 - flLDotH, f.flFresnelExponent ) );
				o_vSpecularTerm.rgb = vFresnel * flSpecularTerm;
			}
			else
			{
				o_vSpecularTerm = float3( 0.0, 0.0, 0.0 );
			}
		}

		void CalculateDirectLightingForVoxel( inout LightingTerms_t o, const FinalCombinerInput_t f )
		{
			o.vDiffuse = float3( 0.0, 0.0, 0.0 );
			o.vSpecular = float3( 0.0, 0.0, 0.0 );
			
			float2 vDiffuseExponent = ( ( 1.0 - f.vRoughness.xy ) * 0.8 ) + 0.6;
			float3 vPositionToCameraDirWs = CalculatePositionToCameraDirWs( f.vPositionWs.xyz );

			[loop]
			for ( int i = 0; i < g_nNumLights; i++ )
			{
				float3 vPositionToLightRayWs = g_vLightPosition_flInvRadius[i].xyz - f.vPositionWithOffsetWs.xyz;
				float flDistToLightSq = dot( vPositionToLightRayWs.xyz, vPositionToLightRayWs.xyz );
				if ( flDistToLightSq > g_vLightFalloffParams[ i ].z ) continue;

				float3 vPositionToLightDirWs = normalize( vPositionToLightRayWs.xyz );
				float flOuterConeCos = g_vSpotLightInnerOuterConeCosines[ i ].y;
				float flTemp = dot( vPositionToLightDirWs.xyz, -g_vLightDirection[ i ].xyz ) - flOuterConeCos;
				if ( flTemp <= 0.0 ) continue;
				float3 vSpotAtten = saturate( flTemp * g_vSpotLightInnerOuterConeCosines[ i ].z ).xxx;

				float4 vLightCookieTexel = float4( 1.0, 1.0, 1.0, 1.0 );
				[branch] if ( g_vLightParams[ i ].y != 0 )
				{
					float4 vPositionTextureSpace = mul( float4( f.vPositionWithOffsetWs.xyz, 1.0 ), g_matWorldToLightCookie[i] );
					vPositionTextureSpace.xyz /= vPositionTextureSpace.w;
					vLightCookieTexel = SampleLightCookieTexture( vPositionTextureSpace.xyz ).rgba;
				}

				float flLightFalloff = CalculateDistanceFalloff( flDistToLightSq, g_vLightFalloffParams[ i ].xyzw, 1.0 );
				float3 vLightMask = g_vLightColor[i].rgb * flLightFalloff * vSpotAtten.rgb;

				float3 vDiffuseTerm, vSpecularTerm;
				ComputeDiffuseAndSpecularTermsForVoxel(vDiffuseTerm, vSpecularTerm, g_vLightParams[i].w != 0, f, vPositionToLightDirWs.xyz, vPositionToCameraDirWs.xyz, vDiffuseExponent.xy );
				o.vDiffuse.rgb += vDiffuseTerm.rgb * vLightMask;
				o.vSpecular.rgb += vSpecularTerm.rgb * vLightMask;
			}

			if ( g_nSunShadowCascadeCount > 0 )
			{
				float flShadowScalar = ComputeSunShadowScalar( f.vPositionWs.xyz );
				float3 vDiffuseTerm, vSpecularTerm;
				ComputeDiffuseAndSpecularTermsForVoxel( vDiffuseTerm, vSpecularTerm,  true, f, g_vSunLightDir.xyz, vPositionToCameraDirWs.xyz, vDiffuseExponent.xy );
				float3 vLightMask = g_vSunLightColor.rgb * flShadowScalar;
				o.vDiffuse.rgb += vDiffuseTerm.rgb * vLightMask;
				o.vSpecular.rgb += vSpecularTerm.rgb * vLightMask;
			}
		}
		
		LightShade Indirect()
		{
			LightShade o;
			
			LightingTerms_t lightingTerms = InitLightingTerms();
			Input.vRoughness.xy = AdjustRoughnessByGeometricNormal( Input.vRoughness.xy, Input.vGeometricNormalWs.xyz );
			CalculateDirectLightingForVoxel( lightingTerms, Input );
			CalculateIndirectLighting( lightingTerms, Input );

			float3 vDiffuseAO = CalculateDiffuseAmbientOcclusion( Input, lightingTerms );
			lightingTerms.vIndirectDiffuse.rgb *= vDiffuseAO.rgb;
			lightingTerms.vDiffuse.rgb *= lerp( float3( 1.0, 1.0, 1.0 ), vDiffuseAO.rgb, Input.flAmbientOcclusionDirectDiffuse );

			float3 vSpecularAO = CalculateSpecularAmbientOcclusion( Input, lightingTerms );
			lightingTerms.vIndirectSpecular.rgb *= vSpecularAO.rgb;
			lightingTerms.vSpecular.rgb *= lerp( float3( 1.0, 1.0, 1.0 ), vSpecularAO.rgb, Input.flAmbientOcclusionDirectSpecular );

			float3 vColor = ( lightingTerms.vDiffuse.rgb + lightingTerms.vIndirectDiffuse.rgb ) * Input.vDiffuseColor.rgb;
			vColor.rgb += Input.vEmissive.rgb;
			vColor.rgb += lightingTerms.vSpecular.rgb;
			vColor.rgb += lightingTerms.vIndirectSpecular.rgb;
			vColor.rgb += lightingTerms.vTransmissive.rgb * Input.vTransmissiveMask.rgb;
			
			float3 vPositionWs = Input.vPositionWs;
			float4 vPositionSs = Input.vPositionSs;

			if ( g_bFogEnabled )
			{
				float3 vPositionToCameraWs = vPositionWs.xyz - g_vCameraPositionWsMultiview[ Input.nView ].xyz;
				vColor.rgb = ApplyGradientFog( vColor.rgb, vPositionWs.xyz, vPositionToCameraWs.xyz );
				vColor.rgb = ApplyCubemapFog( vColor.rgb, vPositionWs.xyz, vPositionToCameraWs.xyz );
				vColor.rgb = ApplyVolumetricFog( Input.nView, vColor.rgb, vPositionWs.xyz, vPositionSs.xy );
				vColor.rgb = ApplySphericalVignette( vColor.rgb, vPositionWs.xyz );
			}

			o.Diffuse = vColor;
			o.Specular = 0;
			return o;
		}

		//
		// Applying any post-processing effects after all lighting is complete
		//
		float4 PostProcess( float4 vColor )
		{
			return vColor;
		}
	};


	struct DataMap
	{
		float Health;
	};

	DataMap UnpackDataMap(Texture3D texture, int3 offset )
	{
		float texDataFl = Tex3DLoad(texture, int4( offset, 0 ) ).r;
		uint texData = asuint(texDataFl) & 0xbfffffff;

		DataMap o;
		o.Health = saturate(float((texData.r >> 16) & 0x7f) / 100.0f);
		return o;
	}
	
	DataMap FetchDataMap( int3 vPositionOs )
	{
		return UnpackDataMap( g_tDataMap, vPositionOs );
	}
	
	float3 HueShift( float3 color, float hue ) {
		const float3 k = float3(0.57735, 0.57735, 0.57735);
		float cosAngle = cos(hue);
		return float3(color * cosAngle + cross(k, color) * sin(hue) + k * dot(k, color) * (1.0 - cosAngle));
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 vAtlasDims = TextureDimensions2D( g_tAtlasColor, 0 ).xy;
		float2 vAltasMaxCells = vAtlasDims / g_flTextureAtlasCellSize;
		float2 vInvMaxCells = 1.0f / vAltasMaxCells;

		float2 vCellLocation =  float2( i.nTextureIndex % vAltasMaxCells.x, floor( i.nTextureIndex / vAltasMaxCells.x ) );

		// Normalized to Atlas Space
		float2 vTexCoordAtlas = frac((i.vTexCoord.xy % g_flVoxelSize) / g_flVoxelSize);
		vTexCoordAtlas = frac((vTexCoordAtlas / vAltasMaxCells) + (vCellLocation * vInvMaxCells));

		#if ( S_MODE_DEPTH )
		{
			#if (S_ALPHA_TEST || S_TRANSLUCENT)
				float4 vColor = Tex2DLevelS( g_tAtlasColor, g_sPointSampler, vTexCoordAtlas, 0 );
				#if (S_ALPHA_TEST)
					if(vColor.a < g_flAlphaTestReference) discard;
				#else
					#if S_DO_NOT_CAST_SHADOWS
						return 0.0f;
					#else
						return float4( 0.0, 0.0, 0.0, vColor.a );
					#endif
				#endif
			#endif
			return float4( 0.0, 0.0, 0.0, 1.0f );
		}
		#elif ( S_MODE_TOOLS_WIREFRAME )
		{
			return float4( g_vWireframeColor.rgb, 1.0f );
		}
		#else
		{
			DataMap data = FetchDataMap( i.vPositionOs - i.vNormalWs );
			float health = data.Health;
			
			float4 vColor = Tex2DLevelS( g_tAtlasColor, g_sPointSampler, vTexCoordAtlas, 0 );
			vColor.rgb = lerp(vColor.rgb, float3(1,0,0), health);
			
			bool hasTintColor = (i.vTintColor.r < 1.0f || i.vTintColor.g < 1.0f || i.vTintColor.b < 1.0f );
			
			if ( i.iHueShift > 0 || hasTintColor )
			{
				float4 maskColor = Tex2DLevelS( g_tAtlasHue, g_sPointSampler, vTexCoordAtlas, 0 );
				
				if ( maskColor.r > 0.1f && maskColor.g >= 0.1f && maskColor.b >= 0.1f )
				{
					if ( hasTintColor )
						vColor.rgb *= i.vTintColor.rgb;
				
					if ( i.iHueShift > 0 )
						vColor.rgb = HueShift( vColor.rgb, (3.14f / 128.0f) * i.iHueShift );
				}
			}

			#if S_ALPHA_TEST
				if(vColor.a < g_flAlphaTestReference) discard;
			#endif

			float3 vRma = Tex2DLevelS( g_tAtlasRma, g_sPointSampler, vTexCoordAtlas, 0 ).rgb;
			float3 vEmission = Tex2DLevelS( g_tAtlasEmission, g_sPointSampler, vTexCoordAtlas, 0 ).rgb;

			Material m = GatherMaterial( i );
			m.Albedo.rgb = vColor.rgb;
			m.Normal = Tex2DLevelS( g_tAtlasNormal, g_sPointSampler, vTexCoordAtlas, 0 ).rgb;
			m.Roughness = vRma.r;
			m.Metalness = vRma.g;
			m.AmbientOcclusion = vRma.b;
			m.Opacity = g_flVoxelOpacity;
			m.TintMask = 1.0f;
			#if (S_ALPHA_TEST || S_TRANSLUCENT)
				m.Opacity = vColor.a * g_flVoxelOpacity;
			#endif
			m.Emission.rgb = m.Albedo.rgb * vEmission;
			
			ShadingModelValveWithDiffuse sm;
			return FinalizePixelMaterial( i, m, sm );
		}
		#endif
	}
}