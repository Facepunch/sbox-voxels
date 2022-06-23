using Facepunch.Voxels;
using Sandbox;

namespace Facepunch.Voxels
{
	public class AirBlock : BlockType
	{
		public AirBlock() { }

		public AirBlock( VoxelWorld world )
		{
			World = world;
		}

		public override string FriendlyName => "Air";
		public override bool ShowInEditor => false;
		public override bool IsTranslucent => true;
		public override bool HideMesh => true;
		public override bool IsPassable => true;
	}
}
