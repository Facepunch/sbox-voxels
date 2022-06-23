using System.IO;

namespace Facepunch.Voxels
{
	public class ModelBlockState : BlockState
	{
		public BlockFace Direction { get; set; }

		public override void Serialize( BinaryWriter writer )
		{
			base.Serialize( writer );
			writer.Write( (byte)Direction );
		}

		public override void Deserialize( BinaryReader reader )
		{
			base.Deserialize( reader );
			Direction = (BlockFace)reader.ReadByte();
		}
	}
}
