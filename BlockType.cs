using Sandbox;
using System.Collections.Generic;

namespace Facepunch.Voxels
{
	public class BlockType
	{
		public VoxelWorld World { get; init; }
		public byte BlockId { get; set; }

		public virtual string Icon => "";
		public virtual string DefaultTexture => "";
		public virtual string FriendlyName => "";
		public virtual string Description => "";
		public virtual bool AttenuatesSunLight => false;
		public virtual float DetailSpawnChance => 0f;
		public virtual string[] DetailModels => null;
		public virtual bool HasTexture => true;
		public virtual bool IsPassable => false;
		public virtual bool IsTranslucent => false;
		public virtual bool UseTransparency => false;
		public virtual IntVector3 LightLevel => 0;
		public virtual Vector3 LightFilter => Vector3.One;
		public virtual string ServerEntity => "";
		public virtual string ClientEntity => "";

		public bool IsServer => Host.IsServer;
		public bool IsClient => Host.IsClient;

		public virtual byte GetTextureId( BlockFace face, Chunk chunk, int x, int y, int z )
		{
			if ( string.IsNullOrEmpty( DefaultTexture ) ) return 0;
			return World.BlockAtlas.GetTextureId( DefaultTexture );
		}

		public virtual BlockState CreateState() => new BlockState();

		public virtual bool ShouldCullFace( BlockFace face, BlockType neighbour )
		{
			return false;
		}

		public virtual void OnNeighbourUpdated( Chunk chunk, IntVector3 position, IntVector3 neighbourPosition )
		{
			if ( IsServer )
			{
				var blockAboveId = World.GetAdjacentBlock( position, (int)BlockFace.Top );

				if ( blockAboveId > 0 )
				{
					chunk.ClearDetails( position );
				}
			}
		}

		public virtual void OnBlockAdded( Chunk chunk, IntVector3 position, int direction )
		{
			if ( IsServer && DetailSpawnChance > 0f && Rand.Float() < DetailSpawnChance )
			{
				var blockAboveId = World.GetAdjacentBlock( position, (int)BlockFace.Top );
				
				if ( blockAboveId == 0 )
				{
					var detail = new ModelEntity();
					var sourcePosition = World.ToSourcePositionCenter( position, true, true, false);
					sourcePosition.z += World.VoxelSize;
					detail.SetModel( Rand.FromArray( DetailModels ) );
					detail.Position = sourcePosition;
					chunk.AddDetail( World.ToLocalPosition( position ), detail );
				}
			}
		}

		public virtual void OnBlockRemoved( Chunk chunk, IntVector3 position )
		{

		}

		public virtual void Tick( IntVector3 position )
		{

		}

		public virtual void Initialize()
		{

		}

		public BlockType()
		{
			World = VoxelWorld.Current;
		}
	}
}
