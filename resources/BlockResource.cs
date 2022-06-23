using System;
using System.Collections.Generic;
using Sandbox;

namespace Facepunch.Voxels
{
	[GameResource( "Block Type", "block", "A simple voxel block type with no custom logic.", Icon = "star" )]
	public class BlockResource : GameResource
	{
		public static List<BlockResource> All { get; protected set; } = new();

		public string FriendlyName { get; set; }
		public string Description { get; set; }
		public string[] Aliases { get; set; }
		public string DefaultTexture { get; set; } = "";

		[ResourceType( "png" )]
		public string Icon { get; set; }

		public bool IsTranslucent { get; set; } = false;
		public bool UseTransparency { get; set; } = false;
		public bool IsPassable { get; set; } = false;
		public bool AttenuatesSunLight { get; set; } = false;

		[Category( "Light Emission" )] public int RedLight { get; set; } = 0;
		[Category( "Light Emission" )] public int GreenLight { get; set; } = 0;
		[Category( "Light Emission" )] public int BlueLight { get; set; } = 0;

		[Category( "Light Filtering" )] public float RedFilter { get; set; } = 1f;
		[Category( "Light Filtering" )] public float GreenFilter { get; set; } = 1f;
		[Category( "Light Filtering" )] public float BlueFilter { get; set; } = 1f;

		[Category( "Detail Meshes" )] public string[] DetailModels { get; set; }
		[Category( "Detail Meshes" )] public float DetailSpawnChance { get; set; } = 0f;
		[Category( "Detail Meshes" )] public float DetailScale { get; set; } = 1f;

		[HideInEditor]
		public IntVector3 LightLevel { get; protected set; }

		[HideInEditor]
		public Vector3 LightFilter { get; protected set; }

		protected override void PostLoad()
		{
			base.PostLoad();

			LightLevel = new IntVector3( RedLight, GreenLight, BlueLight );
			LightFilter = new Vector3( RedFilter, GreenFilter, BlueFilter );

			if ( !All.Contains( this ) )
			{
				All.Add( this );
			}
		}
	}
}
