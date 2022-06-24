using System;
using System.Collections.Generic;
using Sandbox;

namespace Facepunch.Voxels
{
	public struct BlockTextureList
	{
		public string Default { get; set; }
		public string Top { get; set; }
		public string Bottom { get; set; }
		public string North { get; set; }
		public string East { get; set; }
		public string South { get; set; }
		public string West { get; set; }
	}

	public struct BlockSoundData
	{
		[ResourceType( "sound" )] public string FootLeft { get; set; }
		[ResourceType( "sound" )] public string FootRight { get; set; }
		[ResourceType( "sound" )] public string FootLaunch { get; set; }
		[ResourceType( "sound" )] public string FootLand { get; set; }
		[ResourceType( "sound" )] public string Impact { get; set; }
	}

	public struct BlockModelOverride
	{
		[ResourceType( "vmdl" )]
		public string ModelName { get; set; }
		[ResourceType( "vmat" )]
		public string MaterialName { get; set; }
		public bool FaceDirection { get; set; }
	}

	[GameResource( "Block Type", "block", "A simple voxel block type with no custom logic.", Icon = "star" )]
	public class BlockResource : GameResource
	{
		public static List<BlockResource> All { get; protected set; } = new();

		public string FriendlyName { get; set; }
		public string Description { get; set; }

		[Description( "All textures should be the name of the sprite in the block atlas." )]
		public BlockTextureList Textures { get; set; }

		[Description( "You can use aliases to keep backwards compatibility with blocks that were previously classes." )]
		public string[] Aliases { get; set; }

		public BlockModelOverride ModelOverride { get; set; }

		[ResourceType( "png" )]
		public string Icon { get; set; }

		public bool IsTranslucent { get; set; } = false;
		public bool UseTransparency { get; set; } = false;
		public bool IsPassable { get; set; } = false;
		public bool AttenuatesSunLight { get; set; } = false;

		[MinMax( 0f, 16f )] public int MinHueShift { get; set; } = 0;
		[MinMax( 0f, 16f )] public int MaxHueShift { get; set; } = 0;

		public BlockSoundData Sounds { get; set; }

		[Category( "Light Emission" )] public int RedLight { get; set; } = 0;
		[Category( "Light Emission" )] public int GreenLight { get; set; } = 0;
		[Category( "Light Emission" )] public int BlueLight { get; set; } = 0;

		[Category( "Light Filtering" )] public float RedFilter { get; set; } = 1f;
		[Category( "Light Filtering" )] public float GreenFilter { get; set; } = 1f;
		[Category( "Light Filtering" )] public float BlueFilter { get; set; } = 1f;

		[Category( "Detail Meshes" ), ResourceType( "vmdl" )] public string[] DetailModels { get; set; }
		[Category( "Detail Meshes" )] public float DetailSpawnChance { get; set; } = 0f;
		[Category( "Detail Meshes" )] public float DetailScale { get; set; } = 1f;

		[HideInEditor]
		public Dictionary<BlockFace,string> FaceToTexture { get; set; }

		[HideInEditor]
		public IntVector3 LightLevel { get; protected set; }

		[HideInEditor]
		public Vector3 LightFilter { get; protected set; }

		protected override void PostLoad()
		{
			base.PostLoad();

			LightLevel = new IntVector3( RedLight, GreenLight, BlueLight );
			LightFilter = new Vector3( RedFilter, GreenFilter, BlueFilter );
			FaceToTexture = new();

			AddFaceTexture( BlockFace.Top, Textures.Top );
			AddFaceTexture( BlockFace.Bottom, Textures.Bottom );
			AddFaceTexture( BlockFace.North, Textures.North );
			AddFaceTexture( BlockFace.East, Textures.East );
			AddFaceTexture( BlockFace.South, Textures.South );
			AddFaceTexture( BlockFace.West, Textures.West );

			if ( !All.Contains( this ) )
			{
				All.Add( this );
			}
		}

		protected void AddFaceTexture( BlockFace face, string texture )
		{
			if ( !string.IsNullOrEmpty( texture ) )
			{
				FaceToTexture.Add( face, texture );
			}
		}
	}
}
