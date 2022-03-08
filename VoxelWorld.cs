using Sandbox;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Facepunch.Voxels
{
	public partial class VoxelWorld : IValid
	{
		public struct ChunkBlockUpdate
		{
			public int x;
			public int y;
			public int z;
			public byte blockId;
			public int direction;
		}

		public delegate void OnInitializedCallback();
		public event OnInitializedCallback OnInitialized;

		public static VoxelWorld Current { get; private set; }

		public static VoxelWorld Create( int seed )
		{
			return new VoxelWorld( seed );
		}

		[ClientRpc]
		public static void Receive( byte[] data )
		{
			if ( Current != null )
			{
				Current.Destroy();
			}

			using ( var stream = new MemoryStream( data ) )
			{
				using ( var reader = new BinaryReader( stream ) )
				{
					var seaLevel = reader.ReadInt32();
					var seed = reader.ReadInt32();
					var maxSizeX = reader.ReadInt32();
					var maxSizeY = reader.ReadInt32();
					var maxSizeZ = reader.ReadInt32();
					var chunkSizeX = reader.ReadInt32();
					var chunkSizeY = reader.ReadInt32();
					var chunkSizeZ = reader.ReadInt32();
					var voxelSize = reader.ReadInt32();
					var chunkRenderDistance = reader.ReadInt32();
					var chunkUnloadDistance = reader.ReadInt32();
					var minimumLoadedChunks = reader.ReadInt32();
					var buildCollisionInThread = reader.ReadBoolean();
					var opaqueMaterial = reader.ReadString();
					var translucentMaterial = reader.ReadString();

					Current = new VoxelWorld( seed )
					{
						SeaLevel = seaLevel,
						MaxSize = new IntVector3( maxSizeX, maxSizeY, maxSizeZ ),
						ChunkSize = new IntVector3( chunkSizeX, chunkSizeY, chunkSizeZ ),
						VoxelSize = voxelSize,
						ChunkRenderDistance = chunkRenderDistance,
						ChunkUnloadDistance = chunkUnloadDistance,
						MinimumLoadedChunks = minimumLoadedChunks,
						BuildCollisionInThread = buildCollisionInThread,
						OpaqueMaterial = opaqueMaterial,
						TranslucentMaterial = translucentMaterial
					};

					Current.LoadBlockAtlas( reader.ReadString() );

					var types = reader.ReadInt32();

					for ( var i = 0; i < types; i++ )
					{
						var id = reader.ReadByte();
						var name = reader.ReadString();
						var type = Library.Create<BlockType>( name );

						type.Initialize();

						Log.Info( $"[Client] Initializing block type {name} with id #{id}" );

						Current.BlockTypes.TryAdd( name, id );
						Current.BlockData.TryAdd( id, type );
					}

					var biomeCount = reader.ReadInt32();

					for ( var i = 0; i < biomeCount; i++ )
					{
						var biomeId = reader.ReadByte();
						var biomeLibraryId = reader.ReadInt32();
						var biome = Library.TryCreate<Biome>( biomeLibraryId );
						biome.Id = biomeId;
						biome.VoxelWorld = Current;
						biome.Initialize();
						Current.BiomeLookup.TryAdd( biomeId, biome );
						Current.Biomes.Add( biome );
						Log.Info( $"[Client] Initializing biome type {biome.Name}" );
					}
				}
			}

			Current.Init();
		}

		[ClientRpc]
		public static void ReceiveBlockUpdate( byte[] data )
		{
			var decompressed = CompressionHelper.Decompress( data );

			using ( var stream = new MemoryStream( decompressed ) )
			{
				using ( var reader = new BinaryReader( stream ) )
				{
					var count = reader.ReadInt32();
					var chunksToUpdate = new HashSet<Chunk>();

					for ( var i = 0; i < count; i++ )
					{
						var x = reader.ReadInt32();
						var y = reader.ReadInt32();
						var z = reader.ReadInt32();
						var blockId = reader.ReadByte();
						var direction = reader.ReadInt32();
						var position = new IntVector3( x, y, z );

						if ( Current.SetBlock( position, blockId, direction ) )
						{
							var chunk = Current.GetChunk( position );
							chunksToUpdate.Add( chunk );

							for ( int j = 0; j < 6; j++ )
							{
								var adjacentPosition = GetAdjacentPosition( position, j );
								var adjacentChunk = Current.GetChunk( adjacentPosition );

								if ( adjacentChunk.IsValid() )
								{
									chunksToUpdate.Add( adjacentChunk );
								}
							}
						}
					}

					foreach ( var chunk in chunksToUpdate )
					{
						chunk.QueueFullUpdate();
					}
				}
			}
		}

		[ClientRpc]
		public static void ReceiveBlockStateUpdate( int x, int y, int z, byte[] data )
		{
			if ( Current == null ) return;

			var position = new IntVector3( x, y, z );
			var chunk = Current.GetChunk( position );

			if ( chunk.IsValid() )
			{
				chunk.DeserializeBlockStates( data );
			}
		}

		[ClientRpc]
		public static void SetBlockOnClient( int x, int y, int z, byte blockId, int direction )
		{
			Host.AssertClient();
			Current?.SetBlockAndUpdate( new IntVector3( x, y, z ), blockId, direction, true );
		}

		public BBox ToSourceBBox( IntVector3 position )
		{
			var sourcePosition = ToSourcePosition( position );
			var sourceMins = sourcePosition;
			var sourceMaxs = sourcePosition + new Vector3( ChunkSize.x, ChunkSize.y, ChunkSize.z );

			return new BBox( sourceMins, sourceMaxs );
		}

		public Vector3 ToSourcePositionCenter( IntVector3 position )
		{
			var halfVoxelSize = VoxelSize * 0.5f;

			return new Vector3(
				position.x * VoxelSize + halfVoxelSize,
				position.y * VoxelSize + halfVoxelSize,
				position.z * VoxelSize + halfVoxelSize
			);
		}

		public Vector3 ToSourcePosition( IntVector3 position )
		{
			return new Vector3( position.x * VoxelSize, position.y * VoxelSize, position.z * VoxelSize );
		}

		public IntVector3 ToVoxelPosition( Vector3 position )
		{
			var fPosition = position * (1.0f / VoxelSize);
			return new IntVector3( (int)fPosition.x, (int)fPosition.y, (int)fPosition.z );
		}

		public List<Vector3> SuitableSpawnPositions { get; private set; } = new();
		public Dictionary<byte, BlockType> BlockData { get; private set; } = new();
		public Dictionary<string, byte> BlockTypes { get; private set; } = new();
		public List<ChunkBlockUpdate> OutgoingBlockUpdates { get; private set; } = new();
		public Dictionary<byte, Biome> BiomeLookup { get; private set; } = new();
		public Dictionary<IntVector3, Chunk> Chunks { get; private set; } = new();
		public List<Biome> Biomes { get; private set; } = new();

		public DayCycleController DayCycle
		{
			get
			{
				if ( !CachedDayCycle.IsValid() )
				{
					CachedDayCycle = Entity.All.OfType<DayCycleController>().FirstOrDefault();
				}

				return CachedDayCycle;
			}
		}

		public BlockAtlas BlockAtlas { get; private set; }
		public IntVector3 MaxSize { get; private set; }
		public bool BuildCollisionInThread { get; private set; }
		public string OpaqueMaterial { get; private set; }
		public string TranslucentMaterial { get; private set; }
		public int MinimumLoadedChunks { get; private set; }
		public int ChunkRenderDistance { get; private set; }
		public int ChunkUnloadDistance { get; private set; }
		public IntVector3 ChunkSize { get; private set; } = new IntVector3( 32, 32, 32 );
		public int VoxelSize { get; private set; } = 48;
		public bool Initialized { get; private set; }
		public int SeaLevel { get; private set; }
		public int Seed { get; private set; }

		public bool IsServer => Host.IsServer;
		public bool IsClient => Host.IsClient;

		public int SizeX;
		public int SizeY;
		public int SizeZ;
		public FastNoiseLite CaveNoise;

		private ConcurrentQueue<Chunk>[] ChunkInitialUpdateQueues = new ConcurrentQueue<Chunk>[1];

		private string BlockAtlasFileName { get; set; }
		private byte NextAvailableBlockId { get; set; }
		private byte NextAvailableBiomeId { get; set; }
		private Type ChunkGeneratorType { get; set; }

		private DayCycleController CachedDayCycle;
		private BiomeSampler BiomeSampler;

		public bool IsInfinite => MaxSize == 0;
		public bool IsValid => true;

		private VoxelWorld() { }

		private VoxelWorld( int seed )
		{
			BlockTypes[typeof( AirBlock ).Name] = NextAvailableBlockId;
			BlockData[NextAvailableBlockId] = new AirBlock( this );

			for ( var i = 0; i < ChunkInitialUpdateQueues.Length; i++ )
			{
				ChunkInitialUpdateQueues[i] = new();
			}

			CaveNoise = new( seed );
			CaveNoise.SetNoiseType( FastNoiseLite.NoiseType.OpenSimplex2 );
			CaveNoise.SetFractalType( FastNoiseLite.FractalType.FBm );
			CaveNoise.SetFractalOctaves( 2 );
			CaveNoise.SetFrequency( 1f / 128f );

			if ( IsServer )
			{
				var cycle = new DayCycleController
				{
					DawnSkyColor = Color.Orange,
					DaySkyColor = Color.White,
					NightSkyColor = Color.Black,
					DuskSkyColor = Color.Orange,
					DawnColor = Color.Orange,
					DuskColor = Color.Orange,
					DayColor = Color.White,
					NightColor = Color.Black
				};
				cycle.Initialize();
				CachedDayCycle = cycle;
			}

			NextAvailableBlockId++;
			Current = this;
			Seed = seed;

			BiomeSampler = new BiomeSampler( this );
		}

		public bool IsInBounds( IntVector3 position )
		{
			if ( position.x >= 0 && position.y >= 0 && position.z >= 0 )
			{
				if ( IsInfinite ) return true;
				return position.x < MaxSize.x && position.y < MaxSize.y && position.z < MaxSize.z;
			}

			return false;
		}

		public void SetMaxSize( int x, int y, int z )
		{
			MaxSize = new IntVector3( x, y, z );
		}

		public Chunk GetOrCreateChunk( int x, int y, int z )
		{
			return GetOrCreateChunk( new IntVector3( x, y, z ) );
		}

		public void QueueTick( IntVector3 position, BlockType block, float delay )
		{
			var chunk = GetChunk( position );

			if ( chunk.IsValid() )
			{
				chunk.QueueTick( position, block, delay );
			}
		}

		public void AddToInitialUpdateList( Chunk chunk )
		{
			var smallestIndex = 0;
			var smallestValue = int.MaxValue;

			for ( var i = 0; i < ChunkInitialUpdateQueues.Length; i++ )
			{
				var count = ChunkInitialUpdateQueues[i].Count;

				if ( count < smallestValue )
				{
					smallestIndex = i;
					smallestValue = count;

					if ( count == 0 ) break;
				}
			}

			ChunkInitialUpdateQueues[smallestIndex].Enqueue( chunk );
		}

		public Chunk GetOrCreateChunk( IntVector3 offset )
		{
			if ( Chunks.TryGetValue( offset, out var chunk ) )
			{
				return chunk;
			}

			chunk = new Chunk( this, offset.x, offset.y, offset.z );
			
			if ( IsServer && ChunkGeneratorType != null )
			{
				var generator = Library.Create<ChunkGenerator>( ChunkGeneratorType );
				generator.Setup( this, chunk );
				chunk.Generator = generator;
			}

			Chunks.TryAdd( offset, chunk );

			SizeX = Math.Max( SizeX, offset.x + ChunkSize.x );
			SizeY = Math.Max( SizeY, offset.y + ChunkSize.y );
			SizeZ = Math.Max( SizeZ, offset.z + ChunkSize.z );

			return chunk;
		}

		public IntVector3 ToChunkOffset( IntVector3 position )
		{
			position.x = Math.Max( (position.x / ChunkSize.x) * ChunkSize.x, 0 );
			position.y = Math.Max( (position.y / ChunkSize.y) * ChunkSize.y, 0 );
			position.z = Math.Max( (position.z / ChunkSize.z) * ChunkSize.z, 0 );
			return position;
		}

		public ChunkViewer GetViewer( Client client )
		{
			if ( client.Components.TryGet<ChunkViewer>( out var viewer ) )
			{
				return viewer;
			}

			return null; ;
		}

		public ChunkViewer AddViewer( Client client )
		{
			return client.Components.GetOrCreate<ChunkViewer>();
		}

		public Chunk GetChunk( IntVector3 position )
		{
			if ( position.x < 0 || position.y < 0 || position.z < 0 ) return null;

			position.x = (position.x / ChunkSize.x) * ChunkSize.x;
			position.y = (position.y / ChunkSize.y) * ChunkSize.y;
			position.z = (position.z / ChunkSize.z) * ChunkSize.z;

			if ( Chunks.TryGetValue( position, out var chunk ) )
			{
				return chunk;
			}

			return null;
		}

		public void SetChunkGenerator<T>() where T : ChunkGenerator
		{
			ChunkGeneratorType = typeof( T );
		}

		public T AddBiome<T>() where T : Biome
		{
			var biome = Library.Create<T>( typeof( T ) );
			biome.Id = NextAvailableBiomeId++;
			biome.VoxelWorld = this;
			biome.Initialize();
			BiomeLookup.TryAdd( biome.Id, biome );
			Biomes.Add( biome );
			return biome;
		}

		public void SetSeaLevel( int seaLevel )
		{
			SeaLevel = seaLevel;
		}

		public void RemoveChunk( Chunk chunk )
		{
			if ( chunk.IsValid() )
			{
				Chunks.Remove( chunk.Offset );
				chunk.Destroy();
			}
		}

		public void Send( Client client )
		{
			using ( var stream = new MemoryStream() )
			{
				using ( var writer = new BinaryWriter( stream ) )
				{
					writer.Write( SeaLevel );
					writer.Write( Seed );
					writer.Write( MaxSize.x );
					writer.Write( MaxSize.y );
					writer.Write( MaxSize.z );
					writer.Write( ChunkSize.x );
					writer.Write( ChunkSize.y );
					writer.Write( ChunkSize.z );
					writer.Write( VoxelSize );
					writer.Write( ChunkRenderDistance );
					writer.Write( ChunkUnloadDistance );
					writer.Write( MinimumLoadedChunks );
					writer.Write( BuildCollisionInThread );
					writer.Write( OpaqueMaterial );
					writer.Write( TranslucentMaterial );
					writer.Write( BlockAtlasFileName );
					writer.Write( BlockData.Count - 1 );

					foreach ( var kv in BlockData )
					{
						if ( kv.Key == 0 ) continue;

						writer.Write( kv.Key );
						writer.Write( kv.Value.GetType().Name );
					}

					writer.Write( BiomeLookup.Count );

					foreach ( var kv in BiomeLookup )
					{
						var attribute = Library.GetAttribute( kv.Value.GetType() );
						writer.Write( kv.Key );
						writer.Write( attribute.Identifier );
					}
				}

				Receive( To.Single( client ), stream.GetBuffer() );
			}
		}

		public void SetChunkRenderDistance( int distance )
		{
			ChunkRenderDistance = distance;
		}

		public void SetChunkUnloadDistance( int distance )
		{
			ChunkUnloadDistance = distance;
		}

		public void SetChunkSize( int x, int y, int z )
		{
			ChunkSize = new IntVector3( x, y, z );
		}

		public void SetVoxelSize( int voxelSize )
		{
			VoxelSize = voxelSize;
		}

		public void SetMinimumLoadedChunks( int minimum )
		{
			MinimumLoadedChunks = minimum;
		}

		public void SetMaterials( string opaqueMaterialName, string translucentMaterialName )
		{
			OpaqueMaterial = opaqueMaterialName;
			TranslucentMaterial = translucentMaterialName;
		}

		public void SetBuildCollisionInThread( bool value )
		{
			BuildCollisionInThread = value;
		}

		public bool SetBlockInDirection( Vector3 origin, Vector3 direction, byte blockId, bool checkSourceCollision = false )
		{
			var face = Trace( origin * (1.0f / VoxelSize), direction.Normal, 10000f, out var endPosition, out _ );

			if ( face == BlockFace.Invalid )
				return false;

			var position = blockId != 0 ? GetAdjacentPosition( endPosition, (int)face ) : endPosition;

			if ( checkSourceCollision )
			{
				var bbox = ToSourceBBox( position );

				if ( Entity.FindInBox( bbox ).Any() )
					return false;
			}

			SetBlockOnServer( position.x, position.y, position.z, blockId, (int)face );
			return true;
		}

		public bool GetBlockInDirection( Vector3 origin, Vector3 direction, out IntVector3 position )
		{
			var face = Trace( origin * (1.0f / VoxelSize), direction.Normal, 10000f, out position, out _ );
			return (face != BlockFace.Invalid);
		}

		public void SetBlockOnServer( IntVector3 position, byte blockId, int direction = 0 )
		{
			Host.AssertServer();

			if ( SetBlockAndUpdate( position, blockId, direction ) )
			{
				OutgoingBlockUpdates.Add( new ChunkBlockUpdate
				{
					x = position.x,
					y = position.y,
					z = position.z,
					blockId = blockId,
					direction = direction
				} );
			}
		}

		public void SetBlockOnServer( int x, int y, int z, byte blockId, int direction = 0 )
		{
			SetBlockOnServer( new IntVector3( x, y, z ), blockId, direction );
		}

		public byte FindBlockId<T>() where T : BlockType
		{
			if ( BlockTypes.TryGetValue( typeof( T ).Name, out var id ) )
				return id;
			else
				return 0;
		}

		public void LoadBlockAtlas( string fileName )
		{
			if ( BlockAtlas != null )
				throw new Exception( "Unable to load a block atlas as one is already loaded for this world!" );

			BlockAtlasFileName = fileName;
			BlockAtlas = FileSystem.Mounted.ReadJsonOrDefault<BlockAtlas>( fileName );
			BlockAtlas.Initialize();
		}

		public void AddBlockType( BlockType type )
		{
			Host.AssertServer();

			if ( BlockAtlas == null )
				throw new Exception( "Unable to add any block types with no loaded block atlas!" );

			Log.Info( $"[Server] Initializing block type {type.GetType().Name} with id #{NextAvailableBlockId}" );

			type.BlockId = NextAvailableBlockId;
			type.Initialize();

			BlockTypes[type.GetType().Name] = NextAvailableBlockId;
			BlockData[NextAvailableBlockId] = type;
			NextAvailableBlockId++;
		}

		[ClientRpc]
		public static async void ReceiveChunks( byte[] data )
		{
			var decompressed = CompressionHelper.Decompress( data );
			var viewer = Local.Client.GetChunkViewer();

			using ( var stream = new MemoryStream( decompressed ) )
			{
				using ( var reader = new BinaryReader( stream ) )
				{
					var count = reader.ReadInt32();

					for ( var i = 0; i < count; i++ )
					{
						var x = reader.ReadInt32();
						var y = reader.ReadInt32();
						var z = reader.ReadInt32();

						var chunk = Current.GetOrCreateChunk( new IntVector3( x, y, z ) );
						chunk.HasOnlyAirBlocks = reader.ReadBoolean();

						if ( !chunk.HasOnlyAirBlocks )
							chunk.Blocks = reader.ReadBytes( chunk.Blocks.Length );

						chunk.LightMap.Deserialize( reader );
						chunk.DeserializeBlockStates( reader );

						_ = chunk.Initialize();

						if ( i % 32 == 0 )
						{
							await GameTask.Delay( 5 );
						}
					}
				}
			}
		}

		public void AddAllBlockTypes()
		{
			Host.AssertServer();

			if ( BlockAtlas == null )
				throw new Exception( "Unable to add any block types with no loaded block atlas!" );

			foreach ( var type in Library.GetAll<BlockType>() )
			{
				AddBlockType( Library.Create<BlockType>( type ) );
			}
		}

		public Voxel GetVoxel( IntVector3 position )
		{
			return GetVoxel( position.x, position.y, position.z );
		}

		public Voxel GetVoxel( int x, int y, int z )
		{
			var chunk = GetChunk( new IntVector3( x, y, z ) ) ;
			if ( !chunk.IsValid() ) return Voxel.Empty;
			return chunk.GetVoxel( x % ChunkSize.x, y % ChunkSize.y, z % ChunkSize.z );
		}

		public T GetOrCreateState<T>( IntVector3 position ) where T : BlockState
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return null;

			var localPosition = ToLocalPosition( position );
			return chunk.GetOrCreateState<T>( localPosition );
		}

		public T GetState<T>( IntVector3 position ) where T : BlockState
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return null;

			var localPosition = ToLocalPosition( position );
			return chunk.GetState<T>( localPosition );
		}

		public byte GetSunLight( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return 0;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.GetSunLight( localPosition );
		}

		public bool SetSunLight( IntVector3 position, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return false;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.SetSunLight( localPosition, value );
		}

		public byte GetTorchLight( IntVector3 position, int channel )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return 0;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.GetTorchLight( localPosition, channel );
		}

		public bool SetTorchLight( IntVector3 position, int channel, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return false;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.SetTorchLight( localPosition, channel, value );
		}

		public byte GetRedTorchLight( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return 0;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.GetRedTorchLight( localPosition );
		}

		public bool SetRedTorchLight( IntVector3 position, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return false;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.SetRedTorchLight( localPosition, value );
		}

		public byte GetGreenTorchLight( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return 0;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.GetGreenTorchLight( localPosition );
		}

		public bool SetGreenTorchLight( IntVector3 position, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return false;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.SetGreenTorchLight( localPosition, value );
		}

		public byte GetBlueTorchLight( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return 0;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.GetGreenTorchLight( localPosition );
		}

		public bool SetBlueTorchLight( IntVector3 position, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return false;

			var localPosition = ToLocalPosition( position );
			return chunk.LightMap.SetGreenTorchLight( localPosition, value );
		}

		public void RemoveSunLight( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.RemoveSunLight( localPosition );
		}

		public void RemoveTorchLight( IntVector3 position, int channel )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.RemoveTorchLight( localPosition, channel );
		}

		public void RemoveRedTorchLight( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.RemoveRedTorchLight( localPosition );
		}

		public void RemoveGreenTorchLight( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.RemoveGreenTorchLight( localPosition );
		}

		public void RemoveBlueTorchLight( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.RemoveBlueTorchLight( localPosition );
		}

		public void AddSunLight( IntVector3 position, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.AddSunLight( localPosition, value );
		}

		public void AddTorchLight( IntVector3 position, int channel, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.AddTorchLight( localPosition, channel, value );
		}

		public void AddRedTorchLight( IntVector3 position, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.AddRedTorchLight( localPosition, value );
		}

		public void AddGreenTorchLight( IntVector3 position, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.AddGreenTorchLight( localPosition, value );
		}

		public void AddBlueTorchLight( IntVector3 position, byte value )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.LightMap.AddBlueTorchLight( localPosition, value );
		}

		public void Destroy()
		{
			foreach ( var kv in Chunks )
			{
				kv.Value.Destroy();
			}

			Event.Unregister( this );

			Chunks.Clear();
		}

		public void Init()
		{
			if ( Initialized ) return;

			Initialized = true;
			OnInitialized?.Invoke();

			Event.Register( this );

			for ( var i = 0; i < ChunkInitialUpdateQueues.Length; i++ )
			{
				var index = i;
				_ = GameTask.RunInThreadAsync( () => ChunkFullUpdateTask( index ) );
			}
		}

		public bool SetBlockAndUpdate( IntVector3 position, byte blockId, int direction, bool forceUpdate = false )
		{
			var currentChunk = GetChunk( position );
			if ( !currentChunk.IsValid() ) return false;

			var shouldBuild = false;
			var chunksToUpdate = new HashSet<Chunk>();

			if ( SetBlock( position, blockId, direction ) || forceUpdate )
			{
				shouldBuild = true;
				chunksToUpdate.Add( currentChunk );

				for ( int i = 0; i < 6; i++ )
				{
					var adjacentPosition = GetAdjacentPosition( position, i );
					var adjacentChunk = GetChunk( adjacentPosition );

					if ( adjacentChunk.IsValid() )
					{
						chunksToUpdate.Add( adjacentChunk );
					}
				}
			}

			foreach ( var chunk in chunksToUpdate )
			{
				chunk.QueueFullUpdate();
			}

			return shouldBuild;
		}

		public IntVector3 ToLocalPosition( IntVector3 position )
		{
			return new IntVector3( position.x % ChunkSize.x, position.y % ChunkSize.y, position.z % ChunkSize.z );
		}

		public static BlockFace GetOppositeDirection( BlockFace direction )
		{
			return (BlockFace)GetOppositeDirection( (int)direction );
		}

		public static int GetOppositeDirection( int direction )
		{
			return direction + ((direction % 2 != 0) ? -1 : 1);
		}

		public Biome GetBiomeAt( int x, int y )
		{
			return BiomeSampler.GetBiomeAt( x, y );
		}

		public byte GetBlock( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return 0;
			return chunk.GetMapPositionBlock( position );
		}

		public bool SetBlock( IntVector3 position, byte blockId, int direction )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return false;

			var localPosition = ToLocalPosition( position );
			var blockIndex = chunk.GetLocalPositionIndex( localPosition );
			var currentBlockId = chunk.GetLocalIndexBlock( blockIndex );

			if ( blockId == currentBlockId ) return false;

			if ( (blockId != 0 && currentBlockId == 0) || (blockId == 0 && currentBlockId != 0) )
			{
				var block = GetBlockType( blockId );

				RemoveRedTorchLight( position );
				RemoveGreenTorchLight( position );
				RemoveBlueTorchLight( position );
				RemoveSunLight( position );

				if ( block.LightLevel.x > 0 || block.LightLevel.y > 0 || block.LightLevel.z > 0 )
				{
					AddRedTorchLight( position, (byte)block.LightLevel.x );
					AddGreenTorchLight( position, (byte)block.LightLevel.y );
					AddBlueTorchLight( position, (byte)block.LightLevel.z );
				}

				var currentBlock = GetBlockType( currentBlockId );
				currentBlock.OnBlockRemoved( chunk, position );

				chunk.BlockStates.Remove( localPosition );
				chunk.SetBlock( blockIndex, blockId );

				block.OnBlockAdded( chunk, position, direction );

				var entityName = IsServer ? block.ServerEntity : block.ClientEntity;

				if ( !string.IsNullOrEmpty( entityName ) )
				{
					var entity = Library.Create<BlockEntity>( entityName );
					entity.BlockType = block;
					chunk.SetEntity( localPosition, entity );
				}
				else
				{
					chunk.RemoveEntity( localPosition );
				}

				for ( var i = 0; i < 5; i++ )
				{
					var neighbourPosition = position + Chunk.BlockDirections[i];

					if ( IsInBounds( neighbourPosition ) )
					{
						var neighbourId = GetBlock( neighbourPosition );
						var neighbourBlock = GetBlockType( neighbourId );
						var neighbourChunk = GetChunk( neighbourPosition );

						if ( neighbourChunk.IsValid() )
							neighbourBlock.OnNeighbourUpdated( neighbourChunk, neighbourPosition, position );
					}
				}

				return true;

			}

			return false;
		}

		public static IntVector3 GetAdjacentPosition( IntVector3 position, int side )
		{
			return position + Chunk.BlockDirections[side];
		}

		public BlockType GetBlockType( byte blockId )
		{
			if ( BlockData.TryGetValue( blockId, out var type ) )
				return type;
			else
				return null;
		}

		public byte GetAdjacentBlock( IntVector3 position, int side )
		{
			return GetBlock( GetAdjacentPosition( position, side ) );
		}

		public bool IsAdjacentEmpty( IntVector3 position, int side )
		{
			return IsEmpty( GetAdjacentPosition( position, side ) );
		}

		public bool IsEmpty( IntVector3 position )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return true;
			return chunk.GetMapPositionBlock( position ) == 0;
		}

		public BlockFace Trace( Vector3 position, Vector3 direction, float length, out IntVector3 hitPosition, out float distance )
		{
			hitPosition = new IntVector3( 0, 0, 0 );
			distance = 0;

			if ( direction.Length <= 0.0f )
			{
				return BlockFace.Invalid;
			}

			IntVector3 edgeOffset = new( direction.x < 0 ? 0 : 1,
				direction.y < 0 ? 0 : 1,
				direction.z < 0 ? 0 : 1 );

			IntVector3 stepAmount = new( direction.x < 0 ? -1 : 1,
				direction.y < 0 ? -1 : 1,
				direction.z < 0 ? -1 : 1 );

			IntVector3 faceDirection = new( direction.x < 0 ? (int)BlockFace.North : (int)BlockFace.South,
				direction.y < 0 ? (int)BlockFace.East : (int)BlockFace.West,
				direction.z < 0 ? (int)BlockFace.Top : (int)BlockFace.Bottom );

			Vector3 position3f = position;
			distance = 0;
			Ray ray = new( position, direction );

			var currentIterations = 0;

			while ( currentIterations < 1000 )
			{
				currentIterations++;

				IntVector3 position3i = new( (int)position3f.x, (int)position3f.y, (int)position3f.z );

				Vector3 distanceToNearestEdge = new( position3i.x - position3f.x + edgeOffset.x,
					position3i.y - position3f.y + edgeOffset.y,
					position3i.z - position3f.z + edgeOffset.z );

				for ( int i = 0; i < 3; ++i )
				{
					if ( MathF.Abs( distanceToNearestEdge[i] ) == 0.0f )
					{
						distanceToNearestEdge[i] = stepAmount[i];
					}
				}

				Vector3 lengthToNearestEdge = new( MathF.Abs( distanceToNearestEdge.x / direction.x ),
					MathF.Abs( distanceToNearestEdge.y / direction.y ),
					MathF.Abs( distanceToNearestEdge.z / direction.z ) );

				int axis;

				if ( lengthToNearestEdge.x < lengthToNearestEdge.y && lengthToNearestEdge.x < lengthToNearestEdge.z )
					axis = 0;
				else if ( lengthToNearestEdge.y < lengthToNearestEdge.x && lengthToNearestEdge.y < lengthToNearestEdge.z )
					axis = 1;
				else
					axis = 2;

				distance += lengthToNearestEdge[axis];
				position3f = position + direction * distance;
				position3f[axis] = MathF.Floor( position3f[axis] + 0.5f * stepAmount[axis] );

				if ( position3f.x < 0.0f || position3f.y < 0.0f || position3f.z < 0.0f ||
					 position3f.x >= SizeX || position3f.y >= SizeY || position3f.z >= SizeZ )
				{
					break;
				}

				BlockFace lastFace = (BlockFace)faceDirection[axis];

				if ( distance > length )
				{
					distance = length;
					return BlockFace.Invalid;
				}

				position3i = new( (int)position3f.x, (int)position3f.y, (int)position3f.z );

				byte blockId = GetBlock( position3i );

				if ( blockId != 0 )
				{
					hitPosition = position3i;
					return lastFace;
				}
			}

			Plane plane = new( new Vector3( 0.0f, 0.0f, 0.0f ), new Vector3( 0.0f, 0.0f, 1.0f ) );
			float distanceHit = 0;
			var traceHitPos = plane.Trace( ray, true );

			if ( traceHitPos.HasValue ) distanceHit = Vector3.DistanceBetween( position, traceHitPos.Value );

			if ( distanceHit >= 0.0f && distanceHit <= length )
			{
				Vector3 hitPosition3f = position + direction * distanceHit;

				if ( hitPosition3f.x < 0.0f || hitPosition3f.y < 0.0f || hitPosition3f.z < 0.0f ||
					 hitPosition3f.x > SizeX || hitPosition3f.y > SizeY || hitPosition3f.z > SizeZ )
				{
					distance = length;

					return BlockFace.Invalid;
				}

				hitPosition3f.z = 0.0f;
				IntVector3 blockHitPosition = new( (int)hitPosition3f.x, (int)hitPosition3f.y, (int)hitPosition3f.z );

				byte blockId = GetBlock( blockHitPosition );

				if ( blockId == 0 )
				{
					distance = distanceHit;
					hitPosition = blockHitPosition;
					hitPosition.z = -1;

					return BlockFace.Top;
				}
			}

			distance = length;

			return BlockFace.Invalid;
		}

		[Event.Tick.Server]
		private void ServerTick()
		{
			if ( OutgoingBlockUpdates.Count > 0 )
			{
				using ( var stream = new MemoryStream() )
				{
					using ( var writer = new BinaryWriter( stream ) )
					{
						writer.Write( OutgoingBlockUpdates.Count );
						
						foreach ( var update in OutgoingBlockUpdates )
						{
							writer.Write( update.x );
							writer.Write( update.y );
							writer.Write( update.z );
							writer.Write( update.blockId );
							writer.Write( update.direction );
						}

						var compressed = CompressionHelper.Compress( stream.ToArray() );
						ReceiveBlockUpdate( compressed.ToArray() );
					}
				}

				OutgoingBlockUpdates.Clear();
			}

			foreach ( var client in Client.All )
			{
				if ( client.Components.TryGet<ChunkViewer>( out var viewer ) )
				{
					viewer.Update();
				}
			}
		}

		private async void ChunkFullUpdateTask( int index )
		{
			var chunksToUpdate = new List<Chunk>();
			var queue = ChunkInitialUpdateQueues[index];

			while ( true )
			{
				try
				{
					while ( queue.Count > 0 )
					{
						if ( queue.TryDequeue( out var queuedChunk ) )
						{
							chunksToUpdate.Add( queuedChunk );
						}
					}

					if ( chunksToUpdate.Count == 0 )
					{
						await GameTask.Delay( 1000 / 30 );
						continue;
					}

					var currentChunkIndex = chunksToUpdate.Count - 1;
					var currentChunk = chunksToUpdate[currentChunkIndex];

					if ( IsClient )
					{
						if ( !Local.Pawn.IsValid() )
						{
							await GameTask.Delay( 1000 / 30 );
							continue;
						}

						var currentDistance = float.PositiveInfinity;
						var localPawnPosition = Local.Pawn.Position;

						for ( var i = 0; i < chunksToUpdate.Count; i++ )
						{
							var chunk = chunksToUpdate[i];
							var distance = ToSourcePosition( chunk.Offset + chunk.Center ).Distance( localPawnPosition );

							if ( distance < currentDistance )
							{
								currentChunk = chunk;
								currentDistance = distance;
								currentChunkIndex = i;
							}
						}
					}
					else if ( IsServer )
					{
						var clients = Client.All;

						foreach ( var client in clients )
						{
							if ( client.Pawn.IsValid() )
							{
								var chunk = GetChunk( ToVoxelPosition( client.Pawn.Position ) );
								var chunkIndex = chunksToUpdate.IndexOf( chunk );

								if ( chunkIndex >= 0 )
								{
									currentChunk = chunksToUpdate[chunkIndex];
									currentChunkIndex = chunkIndex;
									break;
								}
							}
						}
					}

					chunksToUpdate.RemoveAt( currentChunkIndex );

					if ( currentChunk.IsValid() )
					{
						if ( !currentChunk.HasDoneFirstFullUpdate )
						{
							currentChunk.StartFirstFullUpdateTask();
						}
					}
				}
				catch ( TaskCanceledException )
				{
					break;
				}
				catch ( Exception e )
				{
					Log.Error( e );
					break;
				}
			}
		}
	}
}
