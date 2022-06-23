using System.Collections.Generic;
using System.Linq;

namespace Facepunch.Voxels
{
	public class BlockAtlas
	{
		public class FrameData
		{
			public string FileName { get; set; }
		}

		public List<FrameData> Frames { get; set; }
		public string Json { get; set; }

		private Dictionary<string, byte> TextureIds { get; set; }
		private string[] Blocks { get; set; }
		private bool Initialized { get; set; }

		public byte GetTextureId( string name )
		{
			if ( TextureIds.TryGetValue( name, out var textureId ) )
				return textureId;
			else
				return 0;
		}

		public void Initialize( string json )
		{
			if ( Initialized ) return;

			Initialized = true;
			TextureIds = new();
			Blocks = Frames.Select( f => f.FileName ).ToArray();
			Json = json;

			for ( var i = 0; i < Blocks.Length; i++ )
			{
				var block = Blocks[i].Replace( "_color", "" );
				TextureIds[block] = (byte)i;
			}
		}
	}
}
