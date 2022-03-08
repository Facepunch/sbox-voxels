using Sandbox;
using System.Collections.Generic;

namespace Facepunch.Voxels
{
	public class LiquidBlock : BlockType
	{
		public override bool IsPassable => true;

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

		public override void OnNeighbourUpdated( Chunk chunk, IntVector3 position, IntVector3 neighbourPosition )
		{
			if ( ShouldSpread( position ) )
			{
				World.QueueTick( position, this );
			}
		}

		public override void OnBlockAdded( Chunk chunk, IntVector3 position, int direction )
		{
			if ( ShouldSpread( position ) )
			{
				World.QueueTick( position, this );
			}
		}

		public override void Tick( IntVector3 position )
		{
			/*
			var blockBelowPosition = position + Chunk.BlockDirections[1];
			var blockBelowId = World.GetBlock( blockBelowPosition );

			if ( blockBelowId == 0 || blockBelowId == BlockId )
			{
				if ( blockBelowId == 0 )
				{
					World.SetBlockOnServer( blockBelowPosition, BlockId );
					World.QueueTick( blockBelowPosition, this );
				}

				var neighbourCount = 0;

				for ( var j = 0; j < 6; j++ )
				{
					var face = (BlockFace)j;
					if ( face == BlockFace.Top || face == BlockFace.Bottom ) continue;

					var adjacentBlockId = World.GetBlock( position + Chunk.BlockDirections[j] );
					if ( adjacentBlockId == BlockId )
						neighbourCount++;
				}

				if ( neighbourCount >= 3 )
				{
					SpreadToSides( position );
				}
			}
			else
			{
				SpreadToSides( position );
			}
			*/
		}

		public override void Initialize()
		{

		}

		private void SpreadToSides( IntVector3 position )
		{
			for ( var i = 0; i < 6; i++ )
			{
				var face = (BlockFace)i;
				if ( face == BlockFace.Top || face == BlockFace.Bottom ) continue;

				var neighbourBlockPosition = position + Chunk.BlockDirections[i];
				var neighbourBlockId = World.GetBlock( neighbourBlockPosition );

				if ( neighbourBlockId == 0 )
				{
					World.SetBlockOnServer( neighbourBlockPosition, BlockId );
					World.QueueTick( neighbourBlockPosition, this );
				}
			}
		}
	}
}
