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
		public byte Health { get; set; }
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
			if ( IsClient )
			{
				Chunk.LightMap.SetHealth( LocalPosition, 100 );
			}
		}

		public virtual void OnRemoved()
		{
			if ( IsClient )
			{
				Chunk.LightMap.SetHealth( LocalPosition, 100 );
			}
		}

		public virtual void Serialize( BinaryWriter writer )
		{
			writer.Write( Health );
		}

		public virtual void Deserialize( BinaryReader reader )
		{
			Health = reader.ReadByte();

			if ( IsClient && Chunk.IsValid() )
			{
				Chunk.LightMap.SetHealth( LocalPosition, Health );
			}
		}

		public virtual BlockState Copy()
		{
			return (BlockState)MemberwiseClone();
		}
	}
}
