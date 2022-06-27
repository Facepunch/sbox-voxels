using Sandbox;
using System.Runtime.InteropServices;

namespace Facepunch.Voxels
{
	[StructLayout( LayoutKind.Sequential )]
	public struct BlockVertex
	{
		private readonly uint BlockData;
		private readonly uint ChunkData;
		private readonly uint ExtraData;

		public BlockVertex( uint vertexX, uint vertexY, uint vertexZ, uint chunkX, uint chunkY, uint chunkZ, uint blockData, uint chunkData, uint extraData )
		{
			BlockData = (blockData | (vertexX & 0x3F) | (vertexY & 0x3F) << 6 | (vertexZ & 0x3F) << 12);
			ChunkData = (chunkData | (chunkX & 0x3F) | (chunkY & 0x3F) << 6 | (chunkZ & 0x3F) << 12);
			ExtraData = extraData;
		}

		public static readonly VertexAttribute[] Layout =
		{
			new VertexAttribute( VertexAttributeType.TexCoord, VertexAttributeFormat.UInt32, 3, 10 )
		};
	}
}
