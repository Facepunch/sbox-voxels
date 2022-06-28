using Sandbox;

namespace Facepunch.Voxels
{
	public partial class BlockEntity : ModelEntity
	{
		[Net] public IntVector3 BlockPosition { get; set; }
		public IntVector3 LocalBlockPosition { get; set; }
		public BlockType BlockType { get; set; }
		public Chunk Chunk { get; set; }
		public VoxelWorld World { get; set; }

		public void CenterOnBlock( bool centerHorizontally = true, bool centerVertically = true )
		{
			var voxelSize = Chunk.VoxelSize;
			var centerBounds = Vector3.Zero;

			if ( centerHorizontally )
			{
				centerBounds.x = voxelSize;
				centerBounds.y = voxelSize;
			}

			if ( centerVertically )
			{
				centerBounds.z = voxelSize;
			}

			Position = World.ToSourcePosition( BlockPosition ) + centerBounds * 0.5f;
		}

		public void CenterOnSide( BlockFace face )
		{
			var voxelSize = Chunk.VoxelSize;
			var centerBounds = Vector3.Zero;

			if ( face == BlockFace.Top )
			{
				centerBounds.x = voxelSize * 0.5f;
				centerBounds.y = voxelSize * 0.5f;
				centerBounds.z = voxelSize;
			}
			else if ( face == BlockFace.Bottom )
			{
				centerBounds.x = voxelSize * 0.5f;
				centerBounds.y = voxelSize * 0.5f;
				centerBounds.z = 0f;
			}
			else if ( face == BlockFace.North )
			{
				centerBounds.x = voxelSize;
				centerBounds.y = voxelSize * 0.5f;
				centerBounds.z = voxelSize * 0.5f;
			}
			else if ( face == BlockFace.South )
			{
				centerBounds.x = 0f;
				centerBounds.y = voxelSize * 0.5f;
				centerBounds.z = voxelSize * 0.5f;
			}
			else if ( face == BlockFace.East )
			{
				centerBounds.x = voxelSize * 0.5f;
				centerBounds.y = voxelSize;
				centerBounds.z = voxelSize * 0.5f;
			}
			else if ( face == BlockFace.West )
			{
				centerBounds.x = voxelSize * 0.5f;
				centerBounds.y = 0f;
				centerBounds.z = voxelSize * 0.5f;
			}

			Position = World.ToSourcePosition( BlockPosition ) + centerBounds;
		}

		public virtual void Initialize()
		{
			
		}

		public override void ClientSpawn()
		{
			World = VoxelWorld.Current;
			LocalBlockPosition = World.ToLocalPosition( BlockPosition );
			Chunk = World.GetChunk( BlockPosition );

			base.ClientSpawn();
		}
	}
}
