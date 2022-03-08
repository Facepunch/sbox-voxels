using Facepunch.Voxels;
using Sandbox;

namespace Facepunch.Voxels
{
	public class AirBlock : BlockType
	{
		public AirBlock( VoxelWorld world )
		{
			World = world;
		}

		public override string FriendlyName => "Air";
		public override bool IsTranslucent => true;
		public override bool HasTexture => false;
		public override bool IsPassable => true;
	}
}
