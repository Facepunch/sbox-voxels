namespace Facepunch.Voxels
{
	public struct Voxel
	{
		public static readonly Voxel Empty = new Voxel();

		public bool IsValid;
		public byte BlockId;
		public int BlockIndex;
		public IntVector3 Position;
		public IntVector3 LocalPosition;

		public Chunk Chunk => VoxelWorld.Current.GetChunk( Position );

		public BlockState GetData<T>() where T : BlockState => Chunk.GetState<T>( LocalPosition );
		public BlockState GetOrCreateData<T>() where T : BlockState => Chunk.GetOrCreateState<T>( LocalPosition );
		public BlockType GetBlockType() => VoxelWorld.Current.GetBlockType( BlockId );
	}
}
