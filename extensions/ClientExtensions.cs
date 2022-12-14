using Sandbox;

namespace Facepunch.Voxels
{
	public static class ClientExtensions
	{
		public static ChunkViewer GetChunkViewer( this IClient client )
		{
			return VoxelWorld.Current?.GetViewer( client );
		}
	}
}
