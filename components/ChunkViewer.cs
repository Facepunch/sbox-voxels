﻿using Sandbox;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Facepunch.Voxels
{
	public partial class ChunkViewer : EntityComponent, IValid
	{
		[Net] public bool IsCurrentChunkReady { get; private set; }

		[ClientRpc]
		public static void UnloadChunkForClient( int x, int y, int z )
		{
			var chunk = VoxelWorld.Current.GetChunk( new IntVector3( x, y, z ) );

			if ( chunk.IsValid() )
			{
				VoxelWorld.Current.RemoveChunk( chunk );
			}
		}

		[ClientRpc]
		public static void ResetViewerForClient()
		{
			var viewer = Game.LocalClient.GetChunkViewer();

			if ( viewer.IsValid() )
			{
				viewer.Reset();
			}
		}

		public HashSet<IntVector3> LoadedChunks { get; private set; }
		public HashSet<IntVector3> ChunksToRemove { get; private set; }
		public HashSet<Chunk> ChunksToSend { get; private set; }
		public Queue<IntVector3> ChunkSendQueue { get; private set; }
		public TimeSince TimeSinceLastReset { get; private set; }

		private TimeUntil NextUpdateTime { get; set; }

		public bool IsValid => Entity.IsValid();

		public void Reset()
		{
			ClearLoadedChunks();
			ChunksToRemove.Clear();
			ChunksToSend.Clear();
			ChunkSendQueue.Clear();
			IsCurrentChunkReady = false;
			TimeSinceLastReset = 0f;
			NextUpdateTime = 0f;
		}

		public bool IsBelowWorld()
		{
			if ( Entity is IClient client && client.Pawn.IsValid() )
			{
				var voxelPosition = VoxelWorld.Current.ToVoxelPosition( client.Pawn.Position );
				return VoxelWorld.Current.IsBelowBounds( voxelPosition );
			}

			return false;
		}

		public bool IsInWorld()
		{
			if ( Entity is IClient client && client.Pawn.IsValid() )
			{
				var voxelPosition = VoxelWorld.Current.ToVoxelPosition( client.Pawn.Position );
				return VoxelWorld.Current.IsInBounds( voxelPosition );
			}

			return false;
		}

		public bool HasLoadedMinimumChunks()
		{
			return LoadedChunks.Count >= VoxelWorld.Current.MinimumLoadedChunks;
		}

		public bool IsChunkLoaded( IntVector3 offset )
		{
			return LoadedChunks.Contains( offset );
		}

		public void AddLoadedChunk( IntVector3 offset )
		{
			if ( Game.IsServer )
			{
				ChunkSendQueue.Enqueue( offset );
				ChunksToRemove.Remove( offset );
			}
			else
			{
				LoadedChunks.Add( offset );
			}
		}

		public void RemoveLoadedChunk( IntVector3 offset )
		{
			if ( Game.IsServer )
				ChunksToRemove.Add( offset );
			else
				LoadedChunks.Remove( offset );
		}

		public void ClearLoadedChunks()
		{
			var world = VoxelWorld.Current;

			if ( Game.IsServer && world.IsValid() )
			{
				foreach ( var offset in LoadedChunks )
				{
					var chunk = world.GetChunk( offset );

					if ( chunk.IsValid() )
					{
						chunk.Viewers.Remove( this );
					}
				}
			}

			LoadedChunks.Clear();
		}

		public void Update()
		{
			if ( Entity is not IClient client ) return;
			if ( !NextUpdateTime ) return;

			var pawn = client.Pawn;
			if ( !pawn.IsValid() ) return;

			var world = VoxelWorld.Current;
			var position = new Vector2( pawn.Position );
			var chunkBounds = world.ChunkSize.Length;

			foreach ( var offset in LoadedChunks )
			{
				var chunk = VoxelWorld.Current.GetChunk( offset );

				if ( chunk.IsValid() )
				{
					var chunkPositionCenter = chunk.Offset + chunk.Center;
					var chunkPositionSource = new Vector2( world.ToSourcePosition( chunkPositionCenter ) );

					if ( position.Distance( chunkPositionSource ) >= chunkBounds * world.VoxelSize * world.ChunkUnloadDistance )
					{
						RemoveLoadedChunk( chunk.Offset );
					}
				}
			}

			var voxelPosition = world.ToVoxelPosition( position );
			var currentChunkOffset = world.ToChunkOffset( voxelPosition );
			var currentChunk = world.GetChunk( voxelPosition );

			IsCurrentChunkReady = currentChunk.IsValid() && currentChunk.HasDoneFirstFullUpdate;

			if ( VoxelWorld.Current.IsInBounds( currentChunkOffset ) )
			{
				AddLoadedChunk( currentChunkOffset );
			}

			var centerChunkPosition = new IntVector3( world.ChunkSize.x / 2, world.ChunkSize.y / 2, world.ChunkSize.z / 2 );

			while ( ChunkSendQueue.Count > 0 )
			{
				var offset = ChunkSendQueue.Dequeue();
				var chunkPositionCenter = offset + centerChunkPosition;
				var chunkPositionSource = new Vector2( world.ToSourcePosition( chunkPositionCenter ) );

				if ( position.Distance( chunkPositionSource ) <= chunkBounds * world.VoxelSize * world.ChunkRenderDistance )
				{
					var chunk = VoxelWorld.Current.GetOrCreateChunk( offset );
					if ( !chunk.IsValid() ) continue;

					if ( !chunk.Initialized )
					{
						_ = chunk.Initialize();
					}

					if ( !ChunksToSend.Contains( chunk ) )
					{
						ChunksToSend.Add( chunk );

						foreach ( var neighbour in chunk.GetNeighbourOffsets() )
						{
							if ( world.IsInBounds( neighbour ) )
								ChunkSendQueue.Enqueue( neighbour );
						}
					}
				}
			}

			if ( ChunksToSend.Count > 0 )
			{
				using ( var stream = new MemoryStream() )
				{
					using ( var writer = new BinaryWriter( stream ) )
					{
						var unloadedChunks = ChunksToSend.Where( c => !IsChunkLoaded( c.Offset ) && c.Initialized ).Take( 16 ).ToArray();
						var totalChunks = unloadedChunks.Count();

						if ( totalChunks > 0 )
						{
							writer.Write( totalChunks );

							foreach ( var chunk in unloadedChunks )
							{
								writer.Write( chunk.Offset.x );
								writer.Write( chunk.Offset.y );
								writer.Write( chunk.Offset.z );
								writer.Write( chunk.HasOnlyAirBlocks );

								if ( !chunk.HasOnlyAirBlocks )
									writer.Write( chunk.Blocks );

								chunk.SerializeBlockStates( writer );

								LoadedChunks.Add( chunk.Offset );
								ChunksToSend.Remove( chunk );
								chunk.Viewers.Add( this );
							}

							var compressed = CompressionHelper.Compress( stream.ToArray() );
							VoxelWorld.ReceiveChunks( To.Single( client ), compressed );
						}
					}
				}

				ChunksToSend.Clear();
			}

			if ( ChunksToRemove.Count > 0 )
			{
				foreach ( var offset in ChunksToRemove )
				{
					UnloadChunkForClient( To.Single( client ), offset.x, offset.y, offset.z );
					LoadedChunks.Remove( offset );

					var chunk = world.GetChunk( offset );

					if ( chunk.IsValid() )
					{
						chunk.Viewers.Remove( this );
					}
				}

				ChunksToRemove.Clear();
			}

			NextUpdateTime = 0.5f;
		}

		protected override void OnDeactivate()
		{
			ClearLoadedChunks();

			base.OnDeactivate();
		}

		protected override void OnActivate()
		{
			TimeSinceLastReset = 0;
			ChunksToRemove = new();
			ChunkSendQueue = new();
			ChunksToSend = new();
			LoadedChunks = new();

			base.OnActivate();
		}
	}
}
