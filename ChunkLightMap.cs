using Sandbox;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Facepunch.Voxels
{
	public class ChunkLightMap
	{
		public event Action OnTextureUpdated;

		public Texture Texture { get; private set; }
		public Chunk Chunk { get; private set; }
		public bool IsDirty { get; private set; }
		public VoxelWorld VoxelWorld { get; private set; }
		public bool IsClient => Host.IsClient;
		public bool IsServer => Host.IsServer;
		public int ChunkSizeX;
		public int ChunkSizeY;
		public int ChunkSizeZ;
		public byte[] PendingData;
		public byte[] Data;

		public ConcurrentQueue<LightRemoveNode>[] TorchLightRemoveQueue { get; private set; }
		public ConcurrentQueue<LightAddNode>[] TorchLightAddQueue { get; private set; }
		public ConcurrentQueue<LightRemoveNode> SunLightRemoveQueue { get; private set; }
		public ConcurrentQueue<IntVector3> SunLightAddQueue { get; private set; }

		public ChunkLightMap( Chunk chunk, VoxelWorld world )
		{
			SunLightRemoveQueue = new();
			SunLightAddQueue = new();

			TorchLightRemoveQueue = new ConcurrentQueue<LightRemoveNode>[3];
			TorchLightAddQueue = new ConcurrentQueue<LightAddNode>[3];

			for ( var i = 0; i < 3; i++ )
				TorchLightRemoveQueue[i] = new();

			for ( var i = 0; i < 3; i++ )
				TorchLightAddQueue[i] = new();

			ChunkSizeX = chunk.SizeX;
			ChunkSizeY = chunk.SizeY;
			ChunkSizeZ = chunk.SizeZ;
			Chunk = chunk;
			VoxelWorld = world;
			IsDirty = true;
			PendingData = new byte[ChunkSizeX * ChunkSizeY * ChunkSizeZ * 4];
			Data = new byte[ChunkSizeX * ChunkSizeY * ChunkSizeZ * 4];

			if ( IsClient )
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
			if ( IsClient )
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

		public byte GetSunLight( IntVector3 position )
		{
			var index = ToIndex( position, 1 );
			if ( !IsInBounds( index ) ) return 0;
			return (byte)((PendingData[index] >> 4) & 0xF);
		}

		public bool IsOpaque( IntVector3 position )
		{
			var index = ToIndex( position, 2 );
			if ( !IsInBounds( index ) ) return false;
			return ((byte)((PendingData[index] >> 7) & 0x1) == 1);
		}

		public bool SetOpaque( IntVector3 position, bool isOpaque )
		{
			var index = ToIndex( position, 2 );
			var otherIndex = ToIndex( position, 3 );

			if ( !IsInBounds( index ) ) return false;
			if ( IsOpaque( position ) == isOpaque ) return false;

			var value = (byte)(isOpaque ? 1 : 0);

			PendingData[index] = (byte)(((value & 0x1) << 7) | PendingData[index] & 0x80);
			PendingData[otherIndex] |= 0x40;
			Data[index] = PendingData[index];
			Data[otherIndex] = PendingData[otherIndex];

			if ( IsClient )
			{
				var baseIndex = ToIndex( position, 0 );
				var data = new byte[4];
				data[0] = Data[baseIndex + 0];
				data[1] = Data[baseIndex + 1];
				data[2] = Data[baseIndex + 2];
				data[3] = Data[baseIndex + 3];
				Texture.Update3D( data, position.x, position.y, position.z, 1, 1, 1 );
			}

			return true;
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

			if ( IsClient )
			{
				var baseIndex = ToIndex( position, 0 );
				var data = new byte[4];
				data[0] = Data[baseIndex + 0];
				data[1] = Data[baseIndex + 1];
				data[2] = Data[baseIndex + 2];
				data[3] = Data[baseIndex + 3];
				Texture.Update3D( data, position.x, position.y, position.z, 1, 1, 1 );
			}

			return true;
		}

		public bool SetSunLight( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 1 );
			if ( !IsInBounds( index ) ) return false;
			if ( GetSunLight( position ) == value ) return false;
			IsDirty = true;
			PendingData[index] = (byte)((PendingData[index] & 0x0F) | ((value & 0xf) << 4));
			PendingData[ToIndex( position, 3 )] |= 0x40;
			return true;
		}

		public void AddRedTorchLight( IntVector3 position, byte value )
		{
			if ( SetRedTorchLight( position, value ) )
			{
				TorchLightAddQueue[0].Enqueue( new LightAddNode
				{
					Position = Chunk.Offset + position,
					Channel = 0
				} );
			}
		}

		public void AddGreenTorchLight( IntVector3 position, byte value )
		{
			if ( SetGreenTorchLight( position, value ) )
			{
				TorchLightAddQueue[1].Enqueue( new LightAddNode
				{
					Position = Chunk.Offset + position,
					Channel = 1
				} );
			}
		}

		public void AddBlueTorchLight( IntVector3 position, byte value )
		{
			if ( SetBlueTorchLight( position, value ) )
			{
				TorchLightAddQueue[2].Enqueue( new LightAddNode
				{
					Position = Chunk.Offset + position,
					Channel = 2
				} );
			}
		}

		public void AddSunLight( IntVector3 position, byte value )
		{
			if ( SetSunLight( position, value ) )
			{
				SunLightAddQueue.Enqueue( Chunk.Offset + position );
			}
		}

		public bool RemoveRedTorchLight( IntVector3 position )
		{
			TorchLightRemoveQueue[0].Enqueue( new LightRemoveNode
			{
				Position = Chunk.Offset + position,
				Channel = 0,
				Value = GetRedTorchLight( position )
			} );

			return SetRedTorchLight( position, 0 );
		}

		public bool RemoveGreenTorchLight( IntVector3 position )
		{
			TorchLightRemoveQueue[1].Enqueue( new LightRemoveNode
			{
				Position = Chunk.Offset + position,
				Channel = 1,
				Value = GetGreenTorchLight( position )
			} );

			return SetGreenTorchLight( position, 0 );
		}

		public bool RemoveBlueTorchLight( IntVector3 position )
		{
			TorchLightRemoveQueue[2].Enqueue( new LightRemoveNode
			{
				Position = Chunk.Offset + position,
				Channel = 2,
				Value = GetBlueTorchLight( position )
			} );

			return SetBlueTorchLight( position, 0 );
		}

		public void UpdateSunLight()
		{
			var affectedNeighbours = new HashSet<Chunk>();

			while ( SunLightRemoveQueue.Count > 0 )
			{
				if ( !SunLightRemoveQueue.TryDequeue( out var node ) )
					continue;

				for ( var i = 0; i < 6; i++ )
				{
					var neighbourPosition = VoxelWorld.GetAdjacentPosition( node.Position, i );
					var lightLevel = VoxelWorld.GetSunLight( neighbourPosition );

					if ( (lightLevel == 15 && neighbourPosition.z == node.Position.z - 1) || (lightLevel != 0 && lightLevel < node.Value) )
					{
						VoxelWorld.SetSunLight( neighbourPosition, 0 );

						AddAffectedNeighbour( affectedNeighbours, neighbourPosition );

						SunLightRemoveQueue.Enqueue( new LightRemoveNode
						{
							Position = neighbourPosition,
							Value = lightLevel
						} );
					}
					else if ( lightLevel >= node.Value )
					{
						AddAffectedNeighbour( affectedNeighbours, neighbourPosition );
						SunLightAddQueue.Enqueue( neighbourPosition );
					}
				}
			}

			while ( SunLightAddQueue.Count > 0 )
			{
				if ( !SunLightAddQueue.TryDequeue( out var node ) )
					continue;

				var blockId = VoxelWorld.GetBlock( node );
				var block = VoxelWorld.GetBlockType( blockId );

				if ( !block.IsTranslucent )
					continue;

				var lightLevel = VoxelWorld.GetSunLight( node );

				for ( var i = 0; i < 6; i++ )
				{
					var neighbourPosition = VoxelWorld.GetAdjacentPosition( node, i );
					var neighbourLightLevel = VoxelWorld.GetSunLight( neighbourPosition );

					if ( neighbourLightLevel + 2 <= lightLevel || (lightLevel == 15 && neighbourLightLevel != 15 && neighbourPosition.z == node.z - 1) )
					{
						var neighbourBlockId = VoxelWorld.GetBlock( neighbourPosition );
						var neighbourBlock = VoxelWorld.GetBlockType( neighbourBlockId );

						if ( neighbourBlock.IsTranslucent )
						{
							if ( lightLevel == 15 && neighbourPosition.z == node.z - 1 && !neighbourBlock.AttenuatesSunLight )
							{
								VoxelWorld.AddSunLight( neighbourPosition, lightLevel );
								AddAffectedNeighbour( affectedNeighbours, neighbourPosition );
							}
							else if ( lightLevel == 15 && neighbourPosition.z == node.z + 1 )
							{
								continue;
							}
							else
							{
								VoxelWorld.AddSunLight( neighbourPosition, (byte)(lightLevel - 1) );
								AddAffectedNeighbour( affectedNeighbours, neighbourPosition );
							}
						}
					}
				}
			}

			foreach ( var neighbour in affectedNeighbours )
			{
				neighbour.LightMap.UpdateSunLight();
			}

			foreach ( var neighbour in affectedNeighbours )
			{
				neighbour.QueueFullUpdate();
			}
		}

		public void UpdateTorchLight( int channel )
		{
			var affectedNeighbours = new HashSet<Chunk>();
			var removeQueue = TorchLightRemoveQueue[channel];
			var addQueue = TorchLightAddQueue[channel];

			while ( removeQueue.Count > 0 )
			{
				if ( !removeQueue.TryDequeue( out var node ) )
					continue;

				for ( var i = 0; i < 6; i++ )
				{
					var neighbourPosition = VoxelWorld.GetAdjacentPosition( node.Position, i );
					var lightLevel = VoxelWorld.GetTorchLight( neighbourPosition, channel );

					if ( lightLevel != 0 && lightLevel < node.Value )
					{
						VoxelWorld.SetTorchLight( neighbourPosition, channel, 0 );

						AddAffectedNeighbour( affectedNeighbours, neighbourPosition );

						removeQueue.Enqueue( new LightRemoveNode
						{
							Position = neighbourPosition,
							Value = node.Value
						} );
					}
					else if ( lightLevel >= node.Value )
					{
						AddAffectedNeighbour( affectedNeighbours, neighbourPosition );

						addQueue.Enqueue( new LightAddNode
						{
							Position = neighbourPosition,
							Channel = channel
						} );
					}
				}
			}

			while ( addQueue.Count > 0 )
			{
				if ( !addQueue.TryDequeue( out var node ) )
					continue;

				var lightLevel = VoxelWorld.GetTorchLight( node.Position, channel );
				var blockId = VoxelWorld.GetBlock( node.Position );
				var block = VoxelWorld.GetBlockType( blockId );

				if ( !block.IsTranslucent )
					continue;

				for ( var i = 0; i < 6; i++ )
				{
					var neighbourPosition = VoxelWorld.GetAdjacentPosition( node.Position, i );
					var neighbourBlockId = VoxelWorld.GetBlock( neighbourPosition );
					var neighbourBlock = VoxelWorld.GetBlockType( neighbourBlockId );

					if ( VoxelWorld.GetTorchLight( neighbourPosition, channel ) + 2 <= lightLevel )
					{
						if ( neighbourBlock.IsTranslucent )
						{
							VoxelWorld.AddTorchLight( neighbourPosition, channel, (byte)((lightLevel - 1) * neighbourBlock.LightFilter[channel]) );
							AddAffectedNeighbour( affectedNeighbours, neighbourPosition );
						}
					}
				}
			}

			foreach ( var neighbour in affectedNeighbours )
			{
				neighbour.LightMap.UpdateTorchLight( channel );
			}

			foreach ( var neighbour in affectedNeighbours )
			{
				neighbour.QueueFullUpdate();
			}
		}

		public bool UpdateTexture( bool forceUpdate = false )
		{
			if ( IsClient && (IsDirty || forceUpdate) )
			{
				Array.Copy( PendingData, Data, Data.Length );
				Texture.Update( Data );
				IsDirty = false;
				OnTextureUpdated?.Invoke();
				return true;
			}

			return false;
		}

		public void UpdateTorchLight()
		{
			UpdateTorchLight( 0 );
			UpdateTorchLight( 1 );
			UpdateTorchLight( 2 );
		}

		public bool RemoveSunLight( IntVector3 position )
		{
			SunLightRemoveQueue.Enqueue( new LightRemoveNode
			{
				Position = Chunk.Offset + position,
				Value = GetSunLight( position )
			} );

			return SetSunLight( position, 0 );
		}

		public byte GetTorchLight( IntVector3 position, int channel )
		{
			if ( channel == 0 ) return GetRedTorchLight( position );
			if ( channel == 1 ) return GetGreenTorchLight( position );
			return GetBlueTorchLight( position );
		}

		public void RemoveTorchLight( IntVector3 position, int channel )
		{
			if ( channel == 0 )
				RemoveRedTorchLight( position );
			else if ( channel == 1 )
				RemoveGreenTorchLight( position );
			else
				RemoveBlueTorchLight( position );
		}

		public void AddTorchLight( IntVector3 position, int channel, byte value )
		{
			if ( channel == 0 )
				AddRedTorchLight( position, value );
			else if ( channel == 1 )
				AddGreenTorchLight( position, value );
			else
				AddBlueTorchLight( position, value );
		}

		public bool SetTorchLight( IntVector3 position, int channel, byte value )
		{
			if ( channel == 0 )
				return SetRedTorchLight( position, value );
			else if ( channel == 1 )
				return SetGreenTorchLight( position, value );
			else
				return SetBlueTorchLight( position, value );
		}

		public byte GetRedTorchLight( IntVector3 position )
		{
			var index = ToIndex( position, 0 );
			if ( !IsInBounds( index ) ) return 0;
			return (byte)(PendingData[index] & 0xF);
		}

		public Vector4 GetLightAsVector( IntVector3 position )
		{
			var r = GetRedTorchLight( position );
			var g = GetGreenTorchLight( position );
			var b = GetBlueTorchLight( position );
			var s = GetSunLight( position );
			return new Vector4( r, g, b, s );
		}

		public bool SetRedTorchLight( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 0 );
			if ( !IsInBounds( index ) ) return false;
			if ( GetRedTorchLight( position ) == value ) return false;
			IsDirty = true;
			PendingData[index] = (byte)((PendingData[index] & 0xF0) | (value & 0xF));
			PendingData[ToIndex( position, 3 )] |= 0x40;
			return true;
		}

		public byte GetGreenTorchLight( IntVector3 position )
		{
			var index = ToIndex( position, 0 );
			if ( !IsInBounds( index ) ) return 0;
			return (byte)((PendingData[index] >> 4) & 0xF);
		}

		public bool SetGreenTorchLight( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 0 );
			if ( !IsInBounds( index ) ) return false;
			if ( GetGreenTorchLight( position ) == value ) return false;
			IsDirty = true;
			PendingData[index] = (byte)((PendingData[index] & 0x0F) | (value << 4));
			PendingData[ToIndex( position, 3 )] |= 0x40;
			return true;
		}

		public byte GetBlueTorchLight( IntVector3 position )
		{
			var index = ToIndex( position, 1 );
			if ( !IsInBounds( index ) ) return 0;
			return (byte)(PendingData[index] & 0xF);
		}

		public bool SetBlueTorchLight( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 1 );
			if ( !IsInBounds( index ) ) return false;
			if ( GetBlueTorchLight( position ) == value ) return false;
			IsDirty = true;
			PendingData[index] = (byte)((PendingData[index] & 0xF0) | (value & 0xF));
			PendingData[ToIndex( position, 3 )] |= 0x40;
			return true;
		}

		private void AddAffectedNeighbour( HashSet<Chunk> neighbours, IntVector3 position )
		{
			var chunk = VoxelWorld.GetChunk( position );
			if ( chunk.IsValid() && chunk != Chunk )
				neighbours.Add( chunk );
		}
	}
}
