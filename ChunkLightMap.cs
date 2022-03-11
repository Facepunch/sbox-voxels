using Sandbox;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Facepunch.Voxels
{
	public class ChunkLightMap
	{
		public Texture Texture { get; private set; }
		public Chunk Chunk { get; private set; }
		public bool IsDirty { get; private set; }
		public VoxelWorld VoxelWorld { get; private set; }
		public bool IsClient => Host.IsClient;
		public bool IsServer => Host.IsServer;
		public int ChunkSizeX;
		public int ChunkSizeY;
		public int ChunkSizeZ;
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
			Data = new byte[ChunkSizeX * ChunkSizeY * ChunkSizeZ * 4];

			if ( IsClient )
			{
				Texture = Texture.CreateVolume( ChunkSizeX, ChunkSizeY, ChunkSizeZ )
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
			writer.Write( Data );
		}

		public void Deserialize( BinaryReader reader )
		{
			Data = reader.ReadBytes( Data.Length );
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
			return (byte)((Data[index] >> 4) & 0xF);
		}

		public bool SetSunLight( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 1 );
			if ( !IsInBounds( index ) ) return false;
			if ( GetSunLight( position ) == value ) return false;
			IsDirty = true;
			Data[index] = (byte)((Data[index] & 0x0F) | ((value & 0xf) << 4));
			Data[ToIndex( position, 3 )] |= 0x40;
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

						SunLightRemoveQueue.Enqueue( new LightRemoveNode
						{
							Position = neighbourPosition,
							Value = lightLevel
						} );
					}
					else if ( lightLevel >= node.Value )
					{
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
							}
							else if ( lightLevel == 15 && neighbourPosition.z == node.z + 1 )
							{
								continue;
							}
							else
							{
								VoxelWorld.AddSunLight( neighbourPosition, (byte)(lightLevel - 1) );
							}
						}
					}
				}
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

						var chunk = VoxelWorld.GetChunk( neighbourPosition );
						if ( chunk.IsValid() && chunk != Chunk )
							affectedNeighbours.Add( chunk );

						removeQueue.Enqueue( new LightRemoveNode
						{
							Position = neighbourPosition,
							Value = node.Value
						} );
					}
					else if ( lightLevel >= node.Value )
					{
						var chunk = VoxelWorld.GetChunk( neighbourPosition );
						if ( chunk.IsValid() && chunk != Chunk )
							affectedNeighbours.Add( chunk );

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

							var chunk = VoxelWorld.GetChunk( neighbourPosition );
							if ( chunk.IsValid() && chunk != Chunk )
								affectedNeighbours.Add( chunk );
						}
					}
				}
			}

			foreach ( var neighbour in affectedNeighbours )
			{
				neighbour.LightMap.UpdateTorchLight( channel );
			}
		}

		public bool UpdateTexture( bool forceUpdate = false )
		{
			if ( IsClient && ( IsDirty || forceUpdate ) )
			{
				Texture.Update( Data );
				IsDirty = false;
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
			return (byte)(Data[index] & 0xF);
		}

		public bool SetRedTorchLight( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 0 );
			if ( !IsInBounds( index ) ) return false;
			if ( GetRedTorchLight( position ) == value ) return false;
			IsDirty = true;
			Data[index] = (byte)((Data[index] & 0xF0) | (value & 0xF));
			Data[ToIndex( position, 3 )] |= 0x40;
			return true;
		}

		public byte GetGreenTorchLight( IntVector3 position )
		{
			var index = ToIndex( position, 0 );
			if ( !IsInBounds( index ) ) return 0;
			return (byte)((Data[index] >> 4) & 0xF);
		}

		public bool SetGreenTorchLight( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 0 );
			if ( !IsInBounds( index ) ) return false;
			if ( GetGreenTorchLight( position ) == value ) return false;
			IsDirty = true;
			Data[index] = (byte)((Data[index] & 0x0F) | (value << 4));
			Data[ToIndex( position, 3 )] |= 0x40;
			return true;
		}

		public byte GetBlueTorchLight( IntVector3 position )
		{
			var index = ToIndex( position, 1 );
			if ( !IsInBounds( index ) ) return 0;
			return (byte)(Data[index] & 0xF);
		}

		public bool SetBlueTorchLight( IntVector3 position, byte value )
		{
			var index = ToIndex( position, 1 );
			if ( !IsInBounds( index ) ) return false;
			if ( GetBlueTorchLight( position ) == value ) return false;
			IsDirty = true;
			Data[index] = (byte)((Data[index] & 0xF0) | (value & 0xF));
			Data[ToIndex( position, 3 )] |= 0x40;
			return true;
		}
	}
}
