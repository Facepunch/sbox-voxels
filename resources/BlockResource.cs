using System;
using System.Collections.Generic;
using Sandbox;

namespace Facepunch.Voxels
{
	[GameResource( "Block Type", "block", "A simple voxel block type with no custom logic.", Icon = "star" )]
	public class BlockResource : GameResource
	{
		public static List<BlockResource> All { get; protected set; } = new();

		[Category( "Meta" )] public string FriendlyName { get; set; }
		[Category( "Meta" )] public string Description { get; set; }
		[Category( "Meta" )] public string[] Aliases { get; set; }
		[Category( "Meta" )] public string DefaultTexture { get; set; } = "";

		[ResourceType( "png" ), Category( "Meta" )]
		public string Icon { get; set; }

		[Category( "Transparency" )] public bool IsTranslucent { get; set; } = false;
		[Category( "Transparency" )] public bool UseTransparency { get; set; } = false;

		[Category( "Collision" )] public bool IsPassable { get; set; } = false;

		[Category( "Lighting" )] public Vector3 LightLevel { get; set; } = Vector3.Zero;
		[Category( "Lighting" )] public Vector3 LightFilter { get; set; } = Vector3.One;
		[Category( "Lighting" )] public bool AttenuatesSunLight { get; set; } = false;

		[Category( "Detail" )] public string[] DetailModels { get; set; }
		[Category( "Detail" )] public float DetailSpawnChance { get; set; } = 0f;
		[Category( "Detail" )] public float DetailScale { get; set; } = 1f;

		protected override void PostLoad()
		{
			base.PostLoad();

			if ( !All.Contains( this ) )
			{
				All.Add( this );
			}
		}
	}
}
