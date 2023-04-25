using Facepunch.Voxels;
using Sandbox;
using System;
using System.IO;

namespace Facepunch.Voxels
{
	public static class BinaryHelper
	{
		public static byte[] Serialize( Action<BinaryWriter> action )
		{
			using ( var stream = new MemoryStream() )
			{
				using ( var writer = new BinaryWriter( stream ) )
				{
					action( writer );
				}

				return stream.ToArray();
			}
		}

		public static void Deserialize( byte[] data, Action<BinaryReader> action )
		{
			using ( var stream = new MemoryStream( data ) )
			{
				using ( var reader = new BinaryReader( stream ) )
				{
					action( reader );
				}
			}
		}
	}
}
