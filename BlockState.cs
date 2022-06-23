using Sandbox;
using System;
using System.IO;

namespace Facepunch.Voxels
{
	public class BlockState : IValid
	{
		public virtual bool ShouldTick => false;
		public virtual float TickRate => 1f;

		public TimeSince LastTickTime { get; set; }

		private byte InternalHealth { get; set; }
		public byte Health
		{
			get
			{
				return InternalHealth;
			}
			set
			{
				if ( InternalHealth != value )
				{
					Chunk.LightMap.SetBlockDamage( LocalPosition, (byte)(100 - value) );
					InternalHealth = value;
				}
			}
		}

		public BlockFace Direction { get; set; }
		public byte BlockId { get; set; }
		public Chunk Chunk { get; set; }
		public IntVector3 LocalPosition { get; set; }

		public bool IsClient => Host.IsClient;
		public bool IsServer => Host.IsServer;
		
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

		public virtual void Tick()
		{

		}

		public virtual void OnCreated()
		{
			Chunk.LightMap.SetBlockDamage( LocalPosition, 0 );
			Health = 100;
		}

		public virtual void OnRemoved()
		{
			Chunk.LightMap.SetBlockDamage( LocalPosition, 0 );
		}

		public virtual void Serialize( BinaryWriter writer )
		{
			writer.Write( Health );
			writer.Write( (byte)Direction );
		}

		public virtual void Deserialize( BinaryReader reader )
		{
			Health = reader.ReadByte();
			Direction = (BlockFace)reader.ReadByte();
		}

		public virtual BlockState Copy()
		{
			return (BlockState)MemberwiseClone();
		}
	}
}
