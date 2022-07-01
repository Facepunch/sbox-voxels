using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace Facepunch.Voxels
{
	public interface IBlockAtlasProvider
	{
		public byte GetTextureId( string name );
		public void Initialize( string json );
		public string Json { get; }
	}

	[Library]
	public class BlockAtlas : IBlockAtlasProvider
	{
		public class Sprite
		{
			public string Name { get; set; }
			public string FilePath { get; set; }
		}

		public List<Sprite> Sprites { get; set; }
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
			Blocks = Sprites.Select( f => f.Name ).ToArray();
			Json = json;

			for ( var i = 0; i < Blocks.Length; i++ )
			{
				var block = Blocks[i].Replace( "_color", "" );
				TextureIds[block] = (byte)i;
			}
		}
	}

	[Library]
	public class BlockAtlasTexturePacker : IBlockAtlasProvider
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
