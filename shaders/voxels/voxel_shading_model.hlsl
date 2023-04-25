#ifndef VOXEL_SHADING_MODEL_H
#define VOXEL_SHADING_MODEL_H

// We're defining our own shading model on top of the valve shading model.
// We want to introduce more glinting to the specular in dark areas. Crystals & things look a lot nicer this way!
// Even though this isn't physically accurate, it looks cool

class ShadingModelVoxel : ShadingModelValveStandard
{
    float3 vPositionWs;
    float3 vViewRayWs;
    float3 vNormalWs;
    float3 vPositionSs;

    void Init( const PixelInput pixelInput, const Material material )
    {
        ShadingModelValveStandard::Init( pixelInput, material );

        vPositionWs = pixelInput.vPositionWithOffsetWs.xyz + g_vCameraPositionWs;
        vViewRayWs = normalize( CalculatePositionToCameraDirWs( vPositionWs ) );
        vNormalWs = pixelInput.vNormalWs;
    }

    //
    // Executed for every direct light
    //
    LightShade Direct( const LightData light )
    {
        LightShade shade = ShadingModelValveStandard::Direct( light );
        shade.Diffuse = 0;
        shade.Specular = 0;
        return shade;
    }
    
    //
    // Executed for indirect lighting, combine ambient occlusion, etc.
    //
    LightShade Indirect()
    {
        LightShade shade = ShadingModelValveStandard::Indirect();

        float3 vHalfAngleDirWs = normalize( Input.vNormalWs.xyz + vViewRayWs.xyz );

        float flNDotL = dot( Input.vNormalWs.xyz, Input.vNormalWs.xyz );
        float flNdotV = ClampToPositive( dot( Input.vNormalWs.xyz, vViewRayWs.xyz ) );
        float flVdotH = ClampToPositive( dot( vViewRayWs.xyz, vHalfAngleDirWs.xyz ) );
        float flNdotH = dot( vHalfAngleDirWs.xyz, Input.vNormalWs.xyz );
        float flXdotH = dot( vHalfAngleDirWs.xyz, Input.vPerPixelTangentUWs.xyz );
        float flYdotH = dot( vHalfAngleDirWs.xyz, Input.vPerPixelTangentVWs.xyz );

        float flSpecularTerm = ComputeGGXBRDF( Input.vRoughness.xx, flNDotL, flNdotV, flNdotH, Input.vPositionSs.xy ).x;

		float flFresnelEdge = 1.0;

		float flLDotH = ClampToPositive( dot( Input.vNormalWs.xyz, vHalfAngleDirWs.xyz ) );
		float3 vFresnel = Input.vSpecularColor + ( ( 1.0 - Input.vSpecularColor ) * flFresnelEdge * pow( 1.0 - flLDotH, Input.flFresnelExponent ) );


        //shade.Diffuse = saturate( flSpecularTerm );
        shade.Specular += (vFresnel * flSpecularTerm) * 0.0f;

        return shade;
    }

	//
	// Applying any post-processing effects after all lighting is complete
	//
	float4 PostProcess( float4 vColor )
	{
		return ShadingModelValveStandard::PostProcess( vColor );
	}
};

#endif