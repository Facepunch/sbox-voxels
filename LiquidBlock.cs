using Sandbox;
using System.Collections.Generic;

namespace Facepunch.Voxels
{
	public class LiquidBlock : BlockType
	{
		public virtual IntVector3[] FlowDirections => new IntVector3[]
		{
			new IntVector3( 0, 0, -1 ),
			new IntVector3( 1, 0, 0 ),
			new IntVector3( -1, 0, 0 ),
			new IntVector3( 0, 1, 0 ),
			new IntVector3( 0, -1, 0 )
		};

		public override bool IsPassable => false;

		public virtual bool ShouldSpread( IntVector3 position )
		{
			return true;
		}

		public override bool ShouldCullFace( BlockFace face, BlockType neighbour )
		{
			if ( neighbour == this )
				return true;

			return false;
		}

		public override void OnBlockAdded( Chunk chunk, int x, int y, int z, int direction )
		{
			
		}

		public override void OnBlockRemoved( Chunk chunk, int x, int y, int z )
		{

		}

		public override void Initialize()
		{

		}
	}
}
