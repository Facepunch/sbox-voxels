using Sandbox;
using System;

namespace Facepunch.Voxels
{
	public partial class VoxelWorldComponent : EntityComponent
	{
		[Net] public float SkyBrightness { get; set; } = 1f;
	}
}
