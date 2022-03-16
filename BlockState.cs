using Sandbox;
using System;
using System.IO;

namespace Facepunch.Voxels
{
	public class BlockState : IValid
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
					Chunk.DirtyBlockStates.Add( LocalPosition );
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

		public virtual BlockState Copy()
		{
			return (BlockState)MemberwiseClone();
		}
	}
}
