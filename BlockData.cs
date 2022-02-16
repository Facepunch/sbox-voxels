using Sandbox;
using System.IO;

namespace Facepunch.Voxels
{
	public class BlockData : IValid
	{
		public byte Health { get; set; }
		public Chunk Chunk { get; set; }
		public IntVector3 LocalPosition { get; set; }

		private bool InternalIsDirty;
		public bool IsDirty
		{
			set
			{
				if ( InternalIsDirty != value )
				{
					InternalIsDirty = value;
					Chunk.DirtyData.Add( LocalPosition );
				}
			}
			get
			{
				return InternalIsDirty;
			}
		}

		public bool IsValid => true;

		public virtual void Serialize( BinaryWriter writer )
		{
			writer.Write( Health );
		}

		public virtual void Deserialize( BinaryReader reader )
		{
			Health = reader.ReadByte();
		}
	}
}
