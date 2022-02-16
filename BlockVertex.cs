using Sandbox;
using System.Runtime.InteropServices;

namespace Facepunch.Voxels
{
	[StructLayout( LayoutKind.Sequential )]
	public struct BlockVertex
	{
		private readonly uint Data;
		private readonly uint Data2;

		public BlockVertex( uint vertexX, uint vertexY, uint vertexZ, uint chunkX, uint chunkY, uint chunkZ, uint faceData )
		{
			Data = (faceData | (vertexX & 63) | (vertexY & 63) << 6 | (vertexZ & 63) << 12);
			Data2 = (chunkX & 63) | (chunkY & 63) << 6 | (chunkZ & 63) << 12;
		}

		public static readonly VertexAttribute[] Layout =
		{
			new VertexAttribute( VertexAttributeType.TexCoord, VertexAttributeFormat.UInt32, 2, 10 )
		};
	}
}
