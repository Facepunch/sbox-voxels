using Sandbox;
using System.Runtime.InteropServices;

namespace Facepunch.Voxels
{
	[StructLayout( LayoutKind.Sequential )]
	public struct BlockVertex
	{
		private readonly uint FaceData;
		private readonly uint ExtraData;

		public BlockVertex( uint vertexX, uint vertexY, uint vertexZ, uint chunkX, uint chunkY, uint chunkZ, uint faceData, uint extraData )
		{
			FaceData = (faceData | (vertexX & 0x3F) | (vertexY & 0x3F) << 6 | (vertexZ & 0x3F) << 12);
			ExtraData = (extraData | (chunkX & 0x3F) | (chunkY & 0x3F) << 6 | (chunkZ & 0x3F) << 12);
		}

		public static readonly VertexAttribute[] Layout =
		{
			new VertexAttribute( VertexAttributeType.TexCoord, VertexAttributeFormat.UInt32, 2, 10 )
		};
	}
}
