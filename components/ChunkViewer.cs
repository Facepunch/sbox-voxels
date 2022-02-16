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
			var chunk = Map.Current.GetChunk( new IntVector3( x, y, z ) );

			if ( chunk.IsValid() )
			{
				Map.Current.RemoveChunk( chunk );
			}
		}

		public HashSet<IntVector3> LoadedChunks { get; private set; }
		public HashSet<IntVector3> ChunksToRemove { get; private set; }
		public HashSet<Chunk> ChunksToSend { get; private set; }
		public Queue<IntVector3> ChunkSendQueue { get; private set; }

		public bool IsValid => Entity.IsValid();

		public bool IsInMapBounds()
		{
			if ( Entity is Client client && client.Pawn.IsValid() )
			{
				var voxelPosition = Map.Current.ToVoxelPosition( client.Pawn.Position );
				return Map.Current.IsInBounds( voxelPosition );
			}

			return false;
		}

		public bool HasLoadedMinimumChunks()
		{
			return LoadedChunks.Count >= Map.Current.MinimumLoadedChunks;
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
			var currentMap = Map.Current;
			var chunkBounds = currentMap.ChunkSize.Length;

			foreach ( var offset in LoadedChunks )
			{
				var chunk = Map.Current.GetChunk( offset );

				if ( chunk.IsValid() )
				{
					var chunkPositionCenter = chunk.Offset + chunk.Center;
					var chunkPositionSource = currentMap.ToSourcePosition( chunkPositionCenter );

					if ( position.Distance( chunkPositionSource ) >= chunkBounds * currentMap.VoxelSize * currentMap.ChunkUnloadDistance )
					{
						RemoveLoadedChunk( chunk.Offset );
					}
				}
			}

			var voxelPosition = currentMap.ToVoxelPosition( position );
			var currentChunkOffset = currentMap.ToChunkOffset( voxelPosition );
			var currentChunk = currentMap.GetChunk( voxelPosition );

			IsCurrentChunkReady = currentChunk.IsValid() && currentChunk.HasDoneFirstFullUpdate;

			if ( Map.Current.IsInBounds( currentChunkOffset ) )
			{
				AddLoadedChunk( currentChunkOffset );
			}

			var centerChunkPosition = new IntVector3( currentMap.ChunkSize.x / 2, currentMap.ChunkSize.y / 2, currentMap.ChunkSize.z / 2 );

			while ( ChunkSendQueue.Count > 0 )
			{
				var offset = ChunkSendQueue.Dequeue();
				var chunkPositionCenter = offset + centerChunkPosition;
				var chunkPositionSource = currentMap.ToSourcePosition( chunkPositionCenter );

				if ( position.Distance( chunkPositionSource ) <= chunkBounds * currentMap.VoxelSize * currentMap.ChunkRenderDistance )
				{
					var chunk = Map.Current.GetOrCreateChunk( offset );
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
							if ( currentMap.IsInBounds( neighbour ) )
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
							writer.Write( chunk.Blocks );

							chunk.LightMap.Serialize( writer );
							chunk.SerializeData( writer );

							LoadedChunks.Add( chunk.Offset );
						}

						var compressed = CompressionHelper.Compress( stream.ToArray() );
						Map.ReceiveChunks( To.Single( client ), compressed );
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
			ChunksToRemove = new();
			ChunkSendQueue = new();
			ChunksToSend = new();
			LoadedChunks = new();

			base.OnActivate();
		}
	}
}
