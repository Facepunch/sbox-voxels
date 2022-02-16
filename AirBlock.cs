using Facepunch.Voxels;
using Sandbox;

namespace Facepunch.Voxels
{
	public class AirBlock : BlockType
	{
		public AirBlock( Map map )
		{
			Map = map;
		}

		public override string FriendlyName => "Air";
		public override bool IsTranslucent => true;
		public override bool HasTexture => false;
		public override bool IsPassable => true;
	}
}
