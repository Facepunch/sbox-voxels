using Sandbox;
using System;
using System.IO;

namespace Facepunch.Voxels
{
	public class ChunkDataMap
	{
		public event Action OnTextureUpdated;

		public Texture Texture { get; private set; }
		public Chunk Chunk { get; private set; }
		public bool IsDirty { get; private set; }
		public VoxelWorld VoxelWorld { get; private set; }
		public int ChunkSizeX;
		public int ChunkSizeY;
		public int ChunkSizeZ;
		public byte[] PendingData;
		public byte[] Data;

		public ChunkDataMap( Chunk chunk, VoxelWorld world )
		{
			ChunkSizeX = chunk.SizeX;
			ChunkSizeY = chunk.SizeY;
			ChunkSizeZ = chunk.SizeZ;
			Chunk = chunk;
			VoxelWorld = world;
			IsDirty = true;
			PendingData = new byte[ChunkSizeX * ChunkSizeY * ChunkSizeZ * 4];
			Data = new byte[ChunkSizeX * ChunkSizeY * ChunkSizeZ * 4];

			if ( Game.IsClient )
			{
				Texture = Texture.CreateVolume( ChunkSizeX, ChunkSizeY, ChunkSizeZ )
					.WithMips( 0 )
					.WithFormat( ImageFormat.R32F )
					.WithData( Data )
					.Finish();
			}
		}

		public void Destroy()
		{
			if ( Game.IsClient )
			{
				Texture.Dispose();
				Texture = null;
			}
		}

		public void Serialize( BinaryWriter writer )
		{
			writer.Write( PendingData.Length );
			writer.Write( PendingData );
		}

		public void Deserialize( BinaryReader reader )
		{
			var length = reader.ReadInt32();
			PendingData = reader.ReadBytes( length );
			IsDirty = true;
		}

		public int ToIndex( IntVector3 position, int component )
		{
			return (((position.z * ChunkSizeY * ChunkSizeX) + (position.y * ChunkSizeX) + position.x) * 4) + component;
		}

		public bool IsInBounds( int index )
		{
			return (index >= 0 && index < Data.Length);
		}

		public byte GetBlockDamage( IntVector3 position )
		{
			var index = ToIndex( position, 2 );
			if ( !IsInBounds( index ) ) return 0;
			return (byte)(PendingData[index] & 0x7f);
		}

		public bool SetBlockDamage( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 2 );
			var otherIndex = ToIndex( position, 3 );

			if ( !IsInBounds( index ) ) return false;
			if ( GetBlockDamage( position ) == value ) return false;

			PendingData[index] = (byte)((value & 0x7f) | PendingData[index] & 0x80);
			PendingData[otherIndex] |= 0x40;
			Data[index] = PendingData[index];
			Data[otherIndex] = PendingData[otherIndex];

			/*
			if ( Game.IsClient )
			{
				var baseIndex = ToIndex( position, 0 );
				var data = new byte[4];
				data[0] = Data[baseIndex + 0];
				data[1] = Data[baseIndex + 1];
				data[2] = Data[baseIndex + 2];
				data[3] = Data[baseIndex + 3];
				Texture.Update3D( data, position.x, position.y, position.z, 1, 1, 1 );
			}
			*/

			if ( Game.IsClient )
			{
				// TODO: Fix the above code instead, this sucks.
				UpdateTexture( true );
			}

			return true;
		}

		public bool UpdateTexture( bool forceUpdate = false )
		{
			if ( Game.IsClient && (IsDirty || forceUpdate) )
			{
				Array.Copy( PendingData, Data, Data.Length );
				Texture.Update( Data );
				IsDirty = false;
				OnTextureUpdated?.Invoke();
				return true;
			}

			return false;
		}
	}
}
