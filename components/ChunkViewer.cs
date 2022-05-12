using Sandbox;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Facepunch.Voxels
{
	public partial class ChunkViewer : EntityComponent, IValid
	{
		[Net] public bool IsCurrentChunkReady { get; private set; }

		public bool IsServer => Host.IsServer;
		public bool IsClient => Host.IsClient;

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
			var viewer = Local.Client.GetChunkViewer();

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

		public bool IsValid => Entity.IsValid();

		public void Reset()
		{
			LoadedChunks.Clear();
			ChunksToRemove.Clear();
			ChunksToSend.Clear();
			ChunkSendQueue.Clear();
			IsCurrentChunkReady = false;
			TimeSinceLastReset = 0f;
		}

		public bool IsBelowWorld()
		{
			if ( Entity is Client client && client.Pawn.IsValid() )
			{
				var voxelPosition = VoxelWorld.Current.ToVoxelPosition( client.Pawn.Position );
				return VoxelWorld.Current.IsBelowBounds( voxelPosition );
			}

			return false;
		}

		public bool IsInWorld()
		{
			if ( Entity is Client client && client.Pawn.IsValid() )
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
			if ( IsServer )
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
			if ( IsServer )
				ChunksToRemove.Add( offset );
			else
				LoadedChunks.Remove( offset );
		}

		public void ClearLoadedChunks()
		{
			LoadedChunks.Clear();
		}

		public void Update()
		{
			if ( Entity is not Client client ) return;

			var pawn = client.Pawn;
			if ( !pawn.IsValid() ) return;

			var position = pawn.Position;
			var currentWorld = VoxelWorld.Current;
			var chunkBounds = currentWorld.ChunkSize.Length;

			foreach ( var offset in LoadedChunks )
			{
				var chunk = VoxelWorld.Current.GetChunk( offset );

				if ( chunk.IsValid() )
				{
					var chunkPositionCenter = chunk.Offset + chunk.Center;
					var chunkPositionSource = currentWorld.ToSourcePosition( chunkPositionCenter );

					if ( position.Distance( chunkPositionSource ) >= chunkBounds * currentWorld.VoxelSize * currentWorld.ChunkUnloadDistance )
					{
						RemoveLoadedChunk( chunk.Offset );
					}
				}
			}

			var voxelPosition = currentWorld.ToVoxelPosition( position );
			var currentChunkOffset = currentWorld.ToChunkOffset( voxelPosition );
			var currentChunk = currentWorld.GetChunk( voxelPosition );

			IsCurrentChunkReady = currentChunk.IsValid() && currentChunk.HasDoneFirstFullUpdate;

			if ( VoxelWorld.Current.IsInBounds( currentChunkOffset ) )
			{
				AddLoadedChunk( currentChunkOffset );
			}

			var centerChunkPosition = new IntVector3( currentWorld.ChunkSize.x / 2, currentWorld.ChunkSize.y / 2, currentWorld.ChunkSize.z / 2 );

			while ( ChunkSendQueue.Count > 0 )
			{
				var offset = ChunkSendQueue.Dequeue();
				var chunkPositionCenter = offset + centerChunkPosition;
				var chunkPositionSource = currentWorld.ToSourcePosition( chunkPositionCenter );

				if ( position.Distance( chunkPositionSource ) <= chunkBounds * currentWorld.VoxelSize * currentWorld.ChunkRenderDistance )
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
							if ( currentWorld.IsInBounds( neighbour ) )
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
						var unloadedChunks = ChunksToSend.Where( c => !IsChunkLoaded( c.Offset ) && c.Initialized && c.HasGenerated );
						writer.Write( unloadedChunks.Count() );

						foreach ( var chunk in unloadedChunks )
						{
							writer.Write( chunk.Offset.x );
							writer.Write( chunk.Offset.y );
							writer.Write( chunk.Offset.z );
							writer.Write( chunk.HasOnlyAirBlocks );

							if ( !chunk.HasOnlyAirBlocks )
								writer.Write( chunk.Blocks );

							chunk.LightMap.Serialize( writer );
							chunk.SerializeBlockStates( writer );

							LoadedChunks.Add( chunk.Offset );
						}

						var compressed = CompressionHelper.Compress( stream.ToArray() );
						VoxelWorld.ReceiveChunks( To.Single( client ), compressed );
					}
				}

				ChunksToSend.Clear();
			}

			if ( ChunksToRemove.Count > 0 )
			{
				foreach ( var chunk in ChunksToRemove )
				{
					UnloadChunkForClient( To.Single( client ), chunk.x, chunk.y, chunk.z );
					LoadedChunks.Remove( chunk );
				}

				ChunksToRemove.Clear();
			}
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
