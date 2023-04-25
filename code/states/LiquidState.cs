using Sandbox;
using System.IO;

namespace Facepunch.Voxels
{
	public class LiquidState : BlockState
	{
		public byte Depth { get; set; } = 8;
		
		public override void Serialize( BinaryWriter writer )
		{
			base.Serialize( writer );
			writer.Write( Depth );
		}

		public override void Deserialize( BinaryReader reader )
		{
			base.Deserialize( reader );
			Depth = reader.ReadByte();
		}
	}
}
