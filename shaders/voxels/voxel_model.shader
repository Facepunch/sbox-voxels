HEADER
{
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Voxel Model";
	DebugInfo = false;
}

FEATURES
{
	#include "common/features.hlsl"

	Feature( F_ALPHA_TEST, 0..1, "Translucent" );
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
	FeatureRule( Allow1( F_TRANSLUCENT, F_ALPHA_TEST ), "Translucent and Alpha Test are not compatible" );
	Feature( F_PREPASS_ALPHA_TEST, 0..1, "Translucent" );
}

MODES
{
	VrForward();
	Depth( S_MODE_DEPTH );
	ToolsVis( S_MODE_TOOLS_VIS );
	ToolsWireframe( S_MODE_TOOLS_WIREFRAME );
	ToolsShadingComplexity( "vr_tools_shading_complexity.vfx" );
}

COMMON
{
	#include "system.fxc"
	#include "sbox_shared.fxc"
	
	#define VS_INPUT_HAS_TANGENT_BASIS 1
	#define PS_INPUT_HAS_TANGENT_BASIS 1

	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"
	
	PixelInput MainVs( INSTANCED_SHADER_PARAMS( VS_INPUT i ) )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
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
	
	CreateInputTexture2D( ModelTextureColor, Srgb, 8, "", "_color", "Voxel Pixel,10/10", Default4( 1.0f, 1.0f, 1.0f, 1.0f ) );

	SamplerState g_sPointSampler < Filter( POINT ); AddressU( MIRROR ); AddressV( MIRROR ); >;

	CreateTexture2DWithoutSampler( g_tModelColor )  < Channel( RGBA,  None( ModelTextureColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); Filter( POINT ); AddressU( MIRROR ); AddressV( MIRROR ); >;

	CreateInputTexture2D( ModelTextureNormal,           Linear, 8, "NormalizeNormals", "_normal", 		"Model Material,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( ModelTextureRoughness,        Linear, 8, "",                 "_rough",  		"Model Material,10/30", Default( 0.5 ) );
	CreateInputTexture2D( ModelTextureMetalness,        Linear, 8, "",                 "_metal",  		"Model Material,10/40", Default( 1.0 ) );
	CreateInputTexture2D( ModelTextureAmbientOcclusion, Linear, 8, "",                 "_ao",     		"Model Material,10/50", Default( 1.0 ) );
	CreateInputTexture2D( ModelTextureEmission, 		Linear, 8, "",                 "_emission",     "Model Material,10/60", Default3( 0.0, 0.0, 0.0 ) );
	CreateInputTexture2D( ModelTextureHue, 				Linear, 8, "",                 "_hue",     		"Model Material,10/70", Default3( 1.0f, 1.0f, 1.0f ) );

	CreateTexture2DWithoutSampler( g_tModelNormal )   < Channel( RGBA, HemiOctNormal( ModelTextureNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTexture2DWithoutSampler( g_tModelRma )      < Channel( R,    None( ModelTextureRoughness ), Linear ); Channel( G, None( ModelTextureMetalness ), Linear ); Channel( B, None( ModelTextureAmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTexture2DWithoutSampler( g_tModelEmission ) < Channel( RGB, None( ModelTextureEmission ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTexture2DWithoutSampler( g_tModelHue )  	  < Channel( RGB,  None( ModelTextureHue ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); Filter( POINT ); AddressU( MIRROR ); AddressV( MIRROR ); >;
	
	float3 g_vTintColor< Range3(0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f); Default3(1.0f, 1.0f, 1.0f); >;
	float g_flDiffuseBoost< Range(0.0f, 1.0f); Default(0.0f); >;
	float g_flSpecularBoost< Range(0.1f, 32.0f); Default(1.0f); >;
	float g_flLightDirectionBias< Range(0.0f, 1.0f); Default(0.0f); >;
	int g_HueShift< Range(0, 64); Default(0); >;
	
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

	float3 HueShift( float3 color, float hue )
	{
		const float3 k = float3(0.57735, 0.57735, 0.57735);
		float cosAngle = cos(hue);
		return float3(color * cosAngle + cross(k, color) * sin(hue) + k * dot(k, color) * (1.0 - cosAngle));
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 vColor = Tex2DLevelS( g_tColor, g_sPointSampler, i.vTextureCoords.xy, 0 );
		Material m = GatherMaterial( i );
		
		bool hasTintColor = (g_vTintColor.r < 1.0f || g_vTintColor.g < 1.0f || g_vTintColor.b < 1.0f );
		
		if ( g_HueShift > 0 || hasTintColor )
		{
			if ( hasTintColor )
				vColor.rgb *= g_vTintColor.rgb;
		
			if ( g_HueShift > 0 )
				vColor.rgb = HueShift( vColor.rgb, (3.14f / 128.0f) * g_HueShift );
		}
		
		float3 vRma = Tex2DLevelS( g_tModelRma, g_sPointSampler, i.vTextureCoords.xy, 0 ).rgb;
		float3 vEmission = Tex2DLevelS( g_tModelEmission, g_sPointSampler, i.vTextureCoords.xy, 0 ).rgb;
		
		m.TintMask = 1.0f;
		m.Opacity = 1.0f;
		m.Albedo.rgb = vColor.rgb;
		m.Normal = Tex2DLevelS( g_tModelNormal, g_sPointSampler, i.vTextureCoords.xy, 0 ).rgb;
		m.Roughness = vRma.r;
		m.Metalness = vRma.g;
		m.AmbientOcclusion = vRma.b;
		m.Emission.rgb = m.Albedo.rgb * vEmission;
		
		ShadingModelValveWithDiffuse sm;
		return FinalizePixelMaterial( i, m, sm );
	}
}
