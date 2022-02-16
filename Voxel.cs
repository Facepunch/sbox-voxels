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

		public Chunk Chunk => Map.Current.GetChunk( Position );

		public BlockData GetData<T>() where T : BlockData => Chunk.GetData<T>( LocalPosition );
		public BlockData GetOrCreateData<T>() where T : BlockData => Chunk.GetOrCreateData<T>( LocalPosition );
		public BlockType GetBlockType() => Map.Current.GetBlockType( BlockId );

		public byte GetSunLight() => Chunk.LightMap.GetSunLight( LocalPosition );
		public byte GetRedTorchLight() => Chunk.LightMap.GetRedTorchLight( LocalPosition );
		public byte GetGreenTorchLight() => Chunk.LightMap.GetGreenTorchLight( LocalPosition );
		public byte GetBlueTorchLight() => Chunk.LightMap.GetBlueTorchLight( LocalPosition );
	}
}
