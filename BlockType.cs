using Sandbox;
using System.Collections.Generic;

namespace Facepunch.Voxels
{
	public class BlockType
	{
		public VoxelWorld VoxelWorld { get; init; }
		public byte BlockId { get; set; }

		public virtual string DefaultTexture => "";
		public virtual string FriendlyName => "";
		public virtual bool AttenuatesSunLight => false;
		public virtual bool HasTexture => true;
		public virtual bool IsPassable => false;
		public virtual bool IsTranslucent => false;
		public virtual bool UseTransparency => false;
		public virtual IntVector3 LightLevel => 0;
		public virtual Vector3 LightFilter => Vector3.One;
		public virtual string ServerEntity => "";
		public virtual string ClientEntity => "";

		public virtual byte GetTextureId( BlockFace face, Chunk chunk, int x, int y, int z )
		{
			if ( string.IsNullOrEmpty( DefaultTexture ) ) return 0;

			return VoxelWorld.BlockAtlas.GetTextureId( DefaultTexture );
		}

		public virtual BlockState CreateState() => new BlockState();

		public virtual bool ShouldCullFace( BlockFace face, BlockType neighbour )
		{
			return false;
		}

		public virtual void OnBlockAdded( Chunk chunk, int x, int y, int z, int direction )
		{
			
		}

		public virtual void OnBlockRemoved( Chunk chunk, int x, int y, int z )
		{

		}

		public virtual void Initialize()
		{

		}

		public BlockType()
		{
			VoxelWorld = VoxelWorld.Current;
		}
	}
}
