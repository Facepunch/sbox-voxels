using Sandbox;

namespace Facepunch.Voxels
{
	public static class ClientExtensions
	{
		public static ChunkViewer GetChunkViewer( this Client client )
		{
			return VoxelWorld.Current?.GetViewer( client );
		}
	}
}
