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

		public override BlockState CreateState()
		{
			return new LiquidState();
		}

		public override bool ShouldCullFace( BlockFace face, BlockType neighbour )
		{
			if ( neighbour == this )
				return true;

			return false;
		}

		public override void OnNeighbourUpdated( Chunk chunk, IntVector3 position, IntVector3 neighbourPosition )
		{
			if ( IsServer && ShouldSpread( position ) )
			{
				World.QueueBlockTick( position, this, 0.15f );
			}
		}

		public override void OnBlockAdded( Chunk chunk, IntVector3 position, int direction )
		{
			if ( IsServer && ShouldSpread( position ) )
			{
				World.QueueBlockTick( position, this, 0.15f );
			}
		}

		public override void Tick( IntVector3 position )
		{
			var blockBelowPosition = position + Chunk.BlockDirections[1];
			var blockBelowId = World.GetBlock( blockBelowPosition );
			var state = World.GetOrCreateState<LiquidState>( position );

			if ( !state.IsValid() || state.Depth == 0 ) return;

			if ( blockBelowId == 0 || blockBelowId == BlockId )
			{
				if ( blockBelowId == 0 )
				{
					World.SetBlockOnServer( blockBelowPosition, BlockId );
					World.QueueBlockTick( blockBelowPosition, this, 0.15f );

					var belowState = World.GetOrCreateState<LiquidState>( blockBelowPosition );

					if ( belowState.IsValid() )
					{
						belowState.Depth = 8;
						belowState.IsDirty = true;
					}
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
					SpreadToSides( state, position, true, true );
				else
					SpreadToSides( state, position, true, false );
			}
			else
			{
				SpreadToSides( state, position, false, true );
			}
		}

		public override void Initialize()
		{

		}

		private void SpreadToSides( LiquidState state, IntVector3 position, bool withGroundBelow, bool withWaterBelow )
		{
			for ( var i = 0; i < 6; i++ )
			{
				var face = (BlockFace)i;
				if ( face == BlockFace.Top || face == BlockFace.Bottom ) continue;

				var neighbourBlockPosition = position + Chunk.BlockDirections[i];
				if ( !World.IsInBounds( neighbourBlockPosition ) ) continue;

				var neighbourBlockId = World.GetBlock( neighbourBlockPosition );

				var blockBelowPosition = neighbourBlockPosition + Chunk.BlockDirections[1];
				var blockBelowId = World.GetBlock( blockBelowPosition );

				if ( withGroundBelow && blockBelowId == 0 ) continue;
				if ( !withWaterBelow && blockBelowId == BlockId ) continue;

				if ( neighbourBlockId == 0 )
				{
					World.SetBlockOnServer( neighbourBlockPosition, BlockId );
					World.QueueBlockTick( neighbourBlockPosition, this, 0.15f );

					var neighbourState = World.GetOrCreateState<LiquidState>( neighbourBlockPosition );

					if ( neighbourState.IsValid() )
					{
						neighbourState.Depth = (byte)(state.Depth - 1);
						neighbourState.IsDirty = true;
					}
				}
			}
		}
	}
}
