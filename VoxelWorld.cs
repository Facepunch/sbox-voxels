﻿using Sandbox;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Facepunch.Voxels
{
	public partial class VoxelWorld : IValid
	{
		public delegate void OnInitializedCallback();
		public event OnInitializedCallback OnInitialized;

		private static HashSet<ModelEntity> VoxelModelsEntities { get; set; } = new();
		public static VoxelWorld Current { get; private set; }

		public static void RegisterVoxelModel( ModelEntity entity )
		{
			Host.AssertClient();
			VoxelModelsEntities.Add( entity );
			Current?.UpdateVoxelModel( entity );
		}

		public static void UnregisterVoxelModel( ModelEntity entity )
		{
			Host.AssertClient();
			VoxelModelsEntities.Remove( entity );
		}

		public static VoxelWorld Create( int seed )
		{
			return new VoxelWorld( seed );
		}

		[ClientRpc]
		public static void DestroyWorldOnClient()
		{
			var viewer = Local.Client.GetChunkViewer();

			if ( viewer.IsValid() )
			{
				viewer.Reset();
			}

			Current?.Destroy();
			Current = null;
		}

		[ClientRpc]
		public static void Receive( byte[] data )
		{
			Current?.Destroy();
			Current = null;

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
						OpaqueMaterial = opaqueMaterial,
						TranslucentMaterial = translucentMaterial
					};

					Current.LoadBlockAtlasFromJson( reader.ReadString(), reader.ReadString() );

					var types = reader.ReadInt32();

					for ( var i = 0; i < types; i++ )
					{
						var id = reader.ReadByte();
						var name = reader.ReadString();
						var isResource = reader.ReadBoolean();
						var aliasCount = reader.ReadInt32();

						BlockType type;

						if ( isResource )
						{
							var resource = BlockResource.All.First( r => r.ResourceName == name );
							var block = new AssetBlock();
							block.SetResource( resource );
							type = block;
						}
						else
						{
							type = TypeLibrary.Create<BlockType>( name );
						}

						type.BlockId = id;
						type.Initialize();

						Log.Info( $"[Client] Initializing block type {type.FriendlyName} with id #{id}" );

						Current.BlockTypes.TryAdd( name, id );
						Current.BlockData.TryAdd( id, type );

						for ( var j = 0; j < aliasCount; j++ )
						{
							var alias = reader.ReadString();
							Current.BlockTypes.TryAdd( alias, id );
						}
					}

					var biomeCount = reader.ReadInt32();

					for ( var i = 0; i < biomeCount; i++ )
					{
						var biomeId = reader.ReadByte();
						var biomeLibraryId = reader.ReadInt32();
						var biome = TypeLibrary.Create<Biome>( biomeLibraryId );
						biome.Id = biomeId;
						biome.VoxelWorld = Current;
						biome.Initialize();
						Current.BiomeLookup.TryAdd( biomeId, biome );
						Current.Biomes.Add( biome );

						if ( !string.IsNullOrEmpty( biome.Name ) )
							Log.Info( $"[Client] Initializing biome type {biome.Name}" );
					}
				}
			}

			Current.Initialize();
		}

		[ClientRpc]
		public static void ReceiveBlockUpdate( byte[] data )
		{
			var decompressed = CompressionHelper.Decompress( data );

			if ( Current.IsValid() )
			{
				Current.BlockUpdateQueue.Enqueue( decompressed );
			}
		}

		[ClientRpc]
		public static void ReceiveBlockStateUpdate( int x, int y, int z, byte[] data )
		{
			if ( Current.IsValid() )
			{
				Current.StateUpdateQueue.Enqueue( new StateUpdate
				{
					x = x,
					y = y,
					z = z,
					Data = data.ToArray()
				} );
			}
		}

		public BBox ToSourceBBox( IntVector3 position )
		{
			var sourcePosition = ToSourcePosition( position );
			var sourceMins = sourcePosition;
			var sourceMaxs = sourcePosition + Vector3.One * VoxelSize;

			return new BBox( sourceMins, sourceMaxs );
		}

		public Vector3 ToSourcePositionCenter( IntVector3 position, bool centerX = true, bool centerY = true, bool centerZ = true )
		{
			var halfVoxelSize = VoxelSize * 0.5f;

			return new Vector3(
				centerX ? position.x * VoxelSize + halfVoxelSize : position.x * VoxelSize,
				centerY ? position.y * VoxelSize + halfVoxelSize : position.y * VoxelSize,
				centerZ ? position.z * VoxelSize + halfVoxelSize : position.z * VoxelSize
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

		public ConcurrentQueue<Vector3> SpawnpointsQueue { get; private set; } = new();
		public Dictionary<byte, BlockType> BlockData { get; private set; } = new();
		public Dictionary<string, byte> BlockTypes { get; private set; } = new();
		public Dictionary<byte, Biome> BiomeLookup { get; private set; } = new();
		public Dictionary<IntVector3, Chunk> Chunks { get; private set; } = new();
		public List<Vector3> Spawnpoints { get; private set; } = new();
		public bool HasNoDayCycleController { get; private set; } = false;
		public float GlobalOpacity { get; set; } = 1f;
		public List<Biome> Biomes { get; private set; } = new();

		public DayCycleController DayCycle
		{
			get
			{
				if ( !HasNoDayCycleController && !CachedDayCycle.IsValid() )
				{
					var controller = Entity.All.OfType<DayCycleController>().FirstOrDefault();
					HasNoDayCycleController = !controller.IsValid();
					CachedDayCycle = controller;
				}

				return CachedDayCycle;
			}
		}

		public IBlockAtlasProvider BlockAtlas { get; private set; }
		public IntVector3 MaxSize { get; private set; } = new IntVector3( 0, 0, 128 );
		public string OpaqueMaterial { get; private set; }
		public string TranslucentMaterial { get; private set; }
		public bool ShouldServerUnloadChunks { get; private set; }
		public int MinimumLoadedChunks { get; private set; }
		public int ChunkRenderDistance { get; private set; }
		public int ChunkUnloadDistance { get; private set; }
		public bool IsLoadingFromFile { get; private set; }
		public IntVector3 ChunkSize = new IntVector3( 32, 32, 32 );
		public int VoxelSize = 48;
		public bool Initialized { get; private set; }
		public int SeaLevel { get; private set; }
		public int Seed { get; private set; }

		public bool IsServer => Host.IsServer;
		public bool IsClient => Host.IsClient;

		public int SizeX;
		public int SizeY;
		public int SizeZ;
		public FastNoiseLite CaveNoise;

		private ConcurrentQueue<Chunk> ChunkInitialUpdateQueue = new ConcurrentQueue<Chunk>();
		private ConcurrentQueue<Chunk> ChunkFullUpdateQueue = new ConcurrentQueue<Chunk>();

		private struct BlockUpdate
		{
			public byte BlockId;
			public int Direction;
		}

		private struct StateUpdate
		{
			public byte[] Data;
			public int x;
			public int y;
			public int z;
		}

		private Queue<byte[]> BlockUpdateQueue { get; set; } = new();
		private Queue<byte[]> ChunkUpdateQueue { get; set; } = new();
		private Queue<StateUpdate> StateUpdateQueue { get; set; } = new();

		private Dictionary<IntVector3, BlockUpdate> BlockUpdateMap { get; set; } = new();
		private string BlockAtlasType { get; set; }
		private byte NextAvailableBlockId { get; set; }
		private byte NextAvailableBiomeId { get; set; }
		private Type ChunkGeneratorType { get; set; }

		private DayCycleController CachedDayCycle;
		private BiomeSampler BiomeSampler;

		public bool IsDestroyed { get; private set; }
		public bool IsInfinite => MaxSize.x == 0 && MaxSize.y == 0;
		public bool IsValid => !IsDestroyed;

		private VoxelWorld() { }

		private VoxelWorld( int seed )
		{
			BlockTypes[typeof( AirBlock ).Name] = NextAvailableBlockId;

			CaveNoise = new( seed );
			CaveNoise.SetNoiseType( FastNoiseLite.NoiseType.OpenSimplex2 );
			CaveNoise.SetFractalType( FastNoiseLite.FractalType.FBm );
			CaveNoise.SetFractalOctaves( 2 );
			CaveNoise.SetFrequency( 1f / 128f );

			Current = this;

			var airBlock = new AirBlock();
			airBlock.Initialize();
			BlockData[NextAvailableBlockId] = airBlock;

			NextAvailableBlockId++;
			Seed = seed;

			BiomeSampler = new BiomeSampler( this );
		}

		public bool IsBelowBounds( IntVector3 position )
		{
			return position.z < 0;
		}

		public bool IsInBounds( IntVector3 position )
		{
			if ( position.x >= 0 && position.y >= 0 && position.z >= 0 )
			{
				if ( IsInfinite )
				{
					return position.z < MaxSize.z;
				}

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

		public void QueueBlockTick( IntVector3 position, BlockType block, float delay )
		{
			var chunk = GetChunk( position );

			if ( chunk.IsValid() )
			{
				chunk.QueueTick( position, block, delay );
			}
		}

		public void AddToFullUpdateList( Chunk chunk )
		{
			if ( !ChunkFullUpdateQueue.Contains( chunk ) )
			{
				ChunkFullUpdateQueue.Enqueue( chunk );
			}
		}

		public void AddToInitialUpdateList( Chunk chunk )
		{
			if ( !ChunkInitialUpdateQueue.Contains( chunk ) )
			{
				ChunkInitialUpdateQueue.Enqueue( chunk );
			}
		}

		public Chunk GetOrCreateChunk( IntVector3 offset )
		{
			if ( Chunks.TryGetValue( offset, out var chunk ) )
			{
				return chunk;
			}

			chunk = new Chunk( this, offset.x, offset.y, offset.z );
			chunk.OnFullUpdate += () => OnChunkUpdated( chunk );
			
			if ( IsServer && ChunkGeneratorType != null )
			{
				var generator = TypeLibrary.Create<ChunkGenerator>( ChunkGeneratorType );
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

			return null;
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
			var biome = TypeLibrary.Create<T>( typeof( T ) );
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
					writer.Write( OpaqueMaterial );
					writer.Write( TranslucentMaterial );
					writer.Write( BlockAtlas.Json );
					writer.Write( BlockAtlasType );
					writer.Write( BlockData.Count - 1 );

					foreach ( var kv in BlockData )
					{
						if ( kv.Key == 0 )
							continue;

						writer.Write( kv.Key );
						writer.Write( kv.Value.GetUniqueName() );
						writer.Write( kv.Value is AssetBlock );

						var aliases = kv.Value.GetUniqueAliases();

						if ( aliases == null )
						{
							writer.Write( 0 );
						}
						else
						{
							writer.Write( aliases.Length );

							foreach ( var alias in aliases )
							{
								writer.Write( alias );
							}
						}
					}

					writer.Write( BiomeLookup.Count );

					foreach ( var kv in BiomeLookup )
					{
						var description = TypeLibrary.GetDescription( kv.Value.GetType() );
						writer.Write( kv.Key );
						writer.Write( description.Identity );
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

		public void SetServerUnloadsChunks( bool value )
		{
			ShouldServerUnloadChunks = value;
		}

		public void SetMinimumLoadedChunks( int minimum )
		{
			MinimumLoadedChunks = minimum;
		}

		public async Task<bool> LoadFromBytes( byte[] bytes )
		{
			var blockIdRemap = new Dictionary<byte, byte>();

			try
			{
				IsLoadingFromFile = true;

				foreach ( var client in Client.All )
				{
					var viewer = client.GetChunkViewer();

					if ( viewer.IsValid() )
					{
						viewer.Reset();
					}
				}

				DestroyWorldOnClient( To.Everyone );

				if ( Initialized )
				{
					Reset();
				}

				using ( var stream = new MemoryStream( bytes ) )
				{
					using ( var reader = new BinaryReader( stream ) )
					{
						var voxelSize = reader.ReadInt32();
						var maxSizeX = reader.ReadInt32();
						var maxSizeY = reader.ReadInt32();
						var maxSizeZ = reader.ReadInt32();
						var chunkSizeX = reader.ReadInt32();
						var chunkSizeY = reader.ReadInt32();
						var chunkSizeZ = reader.ReadInt32();

						SetVoxelSize( voxelSize );
						SetMaxSize( maxSizeX, maxSizeY, maxSizeZ );
						SetChunkSize( chunkSizeX, chunkSizeY, chunkSizeZ );

						var blockCount = reader.ReadInt32();

						for ( var i = 0; i < blockCount; i++ )
						{
							var blockId = reader.ReadByte();
							var blockType = reader.ReadString();

							if ( BlockTypes.TryGetValue( blockType, out var realBlockId ) )
								blockIdRemap[blockId] = realBlockId;
							else
								blockIdRemap[blockId] = 0;
						}

						var chunkCount = reader.ReadInt32();

						for ( var i = 0; i < chunkCount; i++ )
						{
							var chunkX = reader.ReadInt32();
							var chunkY = reader.ReadInt32();
							var chunkZ = reader.ReadInt32();
							var chunk = GetOrCreateChunk( chunkX, chunkY, chunkZ );

							chunk.HasOnlyAirBlocks = reader.ReadBoolean();

							if ( !chunk.HasOnlyAirBlocks )
							{
								chunk.Blocks = reader.ReadBytes( ChunkSize.x * ChunkSize.y * ChunkSize.z );
							}

							for ( var j = 0; j < chunk.Blocks.Length; j++ )
							{
								var currentBlockId = chunk.Blocks[j];

								if ( blockIdRemap.TryGetValue( currentBlockId, out var remappedBlockId ) )
								{
									chunk.Blocks[j] = remappedBlockId;
								}
							}

							var blockStateCount = reader.ReadInt32();

							if ( blockStateCount > 0 )
							{
								for ( var j = 0; j < blockStateCount; j++ )
								{
									var blockStateDataLength = reader.ReadInt32();
									var blockStateData = reader.ReadBytes( blockStateDataLength );

									try
									{
										BinaryHelper.Deserialize( blockStateData, r =>
										{
											chunk.DeserializeBlockState( r );
										} );
									}
									catch ( Exception e )
									{
										Log.Error( e );
									}
								}
							}

							await GameTask.Delay( 5 );
						}

						var entityCount = reader.ReadInt32();

						if ( entityCount > 0 )
						{
							for ( var i = 0; i < entityCount; i++ )
							{
								var entityDataLength = reader.ReadInt32();
								
								if ( entityDataLength > 0 )
								{
									var entityData = reader.ReadBytes( entityDataLength );

									try
									{
										BinaryHelper.Deserialize( entityData, r =>
										{
											var libraryName = r.ReadString();
											var entity = TypeLibrary.Create<ISourceEntity>( libraryName );
											entity.Position = r.ReadVector3();
											entity.Rotation = r.ReadRotation();
											entity.Deserialize( r );
										} );
									}
									catch ( Exception e )
									{
										Log.Error( e );
									}
								}
							}
						}
					}
				}

				if ( Initialized )
				{
					foreach ( var client in Client.All )
					{
						Send( client );
					}
				}

				IsLoadingFromFile = false;

				return true;
			}
			catch ( TaskCanceledException )
			{
				return false;
			}
			catch ( Exception e )
			{
				Log.Error( e );
				return false;
			}
		}

		public async Task<bool> LoadFromFile( BaseFileSystem fs, string fileName )
		{
			if ( !fs.FileExists( fileName ) )
			{
				return false;
			}

			var bytes = await fs.ReadAllBytesAsync( fileName );

			return await LoadFromBytes( bytes );
		}

		public void SaveToFile( BaseFileSystem fs, string fileName )
		{
			try
			{
				using ( var stream = fs.OpenWrite( fileName, FileMode.Create ) )
				{
					using ( var writer = new BinaryWriter( stream ) )
					{
						writer.Write( VoxelSize );
						writer.Write( MaxSize.x );
						writer.Write( MaxSize.y );
						writer.Write( MaxSize.z );
						writer.Write( ChunkSize.x );
						writer.Write( ChunkSize.y );
						writer.Write( ChunkSize.z );

						writer.Write( BlockTypes.Count );

						foreach ( var kv in BlockTypes )
						{
							writer.Write( kv.Value );
							writer.Write( kv.Key );
						}

						writer.Write( Chunks.Count );

						foreach ( var kv in Chunks )
						{
							var chunk = kv.Value;

							writer.Write( chunk.Offset.x );
							writer.Write( chunk.Offset.y );
							writer.Write( chunk.Offset.z );
							writer.Write( chunk.HasOnlyAirBlocks );

							if ( !chunk.HasOnlyAirBlocks )
								writer.Write( chunk.Blocks );

							var blockStateCount = chunk.BlockStates.Count;

							writer.Write( blockStateCount );

							if ( blockStateCount > 0 )
							{
								foreach ( var k2v2 in chunk.BlockStates )
								{
									var blockStateData = BinaryHelper.Serialize( w =>
									{
										chunk.SerializeBlockState( k2v2.Key, k2v2.Value, w );
									} );

									writer.Write( blockStateData.Length );

									if ( blockStateData.Length > 0 )
									{
										writer.Write( blockStateData );
									}
								}
							}
						}

						var entities = Entity.All.OfType<ISourceEntity>().ToList();
						var entityCount = entities.Count;

						writer.Write( entityCount );

						for ( int i = 0; i < entities.Count; i++ )
						{
							var entityData = BinaryHelper.Serialize( w =>
							{
								var entity = entities[i];
								var className = TypeLibrary.GetDescription( entity.GetType() ).ClassName;

								w.Write( className );
								w.Write( entity.Position );
								w.Write( entity.Rotation );

								entity.Serialize( w );
							} );

							writer.Write( entityData.Length );

							if ( entityData.Length > 0 )
							{
								writer.Write( entityData );
							}
						}
					}
				}
			}
			catch ( Exception e )
			{
				Log.Error( e );
			}
		}

		public IntVector3 GetPositionMaxs( IntVector3 mins, IntVector3 maxs )
		{
			var maxX = Math.Max( mins.x, maxs.x );
			var maxY = Math.Max( mins.y, maxs.y );
			var maxZ = Math.Max( mins.z, maxs.z );

			return new IntVector3( maxX, maxY, maxZ );
		}

		public IntVector3 GetPositionMins( IntVector3 mins, IntVector3 maxs )
		{
			var minX = Math.Min( mins.x, maxs.x );
			var minY = Math.Min( mins.y, maxs.y );
			var minZ = Math.Min( mins.z, maxs.z );

			return new IntVector3( minX, minY, minZ );
		}

		public IEnumerable<IntVector3> GetPositionsInBox( IntVector3 mins, IntVector3 maxs )
		{
			var minX = Math.Min( mins.x, maxs.x );
			var minY = Math.Min( mins.y, maxs.y );
			var minZ = Math.Min( mins.z, maxs.z );
			var maxX = Math.Max( mins.x, maxs.x );
			var maxY = Math.Max( mins.y, maxs.y );
			var maxZ = Math.Max( mins.z, maxs.z );

			for ( var x = minX; x <= maxX; x++ )
			{
				for ( var y = minY; y <= maxY; y++ )
				{
					for ( var z = minZ; z <= maxZ; z++ )
					{
						yield return new IntVector3( x, y, z );
					}
				}
			}
		}

		public void SetMaterials( string opaqueMaterialName, string translucentMaterialName )
		{
			OpaqueMaterial = opaqueMaterialName;
			TranslucentMaterial = translucentMaterialName;
		}

		public bool SetBlockInDirection( Vector3 origin, Vector3 direction, byte blockId, out IntVector3 position, bool checkSourceCollision = false, float distance = 10000f, Func<IntVector3, bool> condition = null )
		{
			var face = Trace( origin * (1.0f / VoxelSize), direction.Normal, distance, out var endPosition, out _ );

			if ( face == BlockFace.Invalid )
			{
				position = IntVector3.Zero;
				return false;
			}

			position = blockId != 0 ? GetAdjacentPosition( endPosition, (int)face ) : endPosition;

			if ( checkSourceCollision )
			{
				var bbox = ToSourceBBox( position );

				if ( Entity.FindInBox( bbox ).Any() )
					return false;
			}

			if ( condition != null && !condition.Invoke( position ) )
			{
				return false;
			}

			SetBlockOnServer( position, blockId, (int)face );
			return true;
		}

		public bool SetBlockInDirection( Vector3 origin, Vector3 direction, byte blockId, bool checkSourceCollision = false, float distance = 10000f, Func<IntVector3, bool> condition = null )
		{
			return SetBlockInDirection( origin, direction, blockId, out _, checkSourceCollision, distance, condition );
		}

		public bool GetBlockInDirection( Vector3 origin, Vector3 direction, out IntVector3 position, float distance = 10000f )
		{
			var face = Trace( origin * (1.0f / VoxelSize), direction.Normal, distance, out position, out _ );
			return (face != BlockFace.Invalid);
		}

		public IEnumerable<IntVector3> GetBlocksInRadius( IntVector3 position, float radius )
		{
			var voxelBlastRadius = (int)(radius / VoxelWorld.Current.VoxelSize);

			for ( var x = -voxelBlastRadius; x <= voxelBlastRadius; ++x )
			{
				for ( var y = -voxelBlastRadius; y <= voxelBlastRadius; ++y )
				{
					for ( var z = -voxelBlastRadius; z <= voxelBlastRadius; ++z )
					{
						var blockPosition = position + new IntVector3( x, y, z );

						if ( position.Distance( blockPosition ) <= voxelBlastRadius )
						{
							yield return blockPosition;
						}
					}
				}
			}
		}

		public void SetBlockOnServerKeepState( IntVector3 position, byte blockId )
		{
			SetBlockOnServer( position, blockId, -1, false );
		}

		public void SetBlockOnServer( IntVector3 position, byte blockId, int direction = 0, bool clearState = true )
		{
			Host.AssertServer();

			if ( SetBlockAndUpdate( position, blockId, direction, false, clearState ) )
			{
				var chunk = GetChunk( position );
				var state = GetState<BlockState>( position );

				if ( state.IsValid() )
				{
					direction = (int)state.Direction;
				}

				if ( chunk.IsValid() )
				{
					chunk.QueueBlockUpdate( position, blockId, direction );
				}
			}
		}

		public byte FindBlockId<T>() where T : BlockType
		{
			if ( BlockTypes.TryGetValue( typeof( T ).Name, out var id ) )
				return id;
			else
				return 0;
		}

		public void LoadBlockAtlasFromJson( string json, string provider )
		{
			if ( BlockAtlas != null )
				throw new Exception( "Unable to load a block atlas as one is already loaded for this world!" );

			var type = TypeLibrary.GetDescription( provider );

			BlockAtlas = (IBlockAtlasProvider)JsonSerializer.Deserialize( json, type.TargetType );
			BlockAtlas.Initialize( json );
		}

		public void LoadBlockAtlas( string fileName )
		{
			if ( BlockAtlas != null )
				throw new Exception( "Unable to load a block atlas as one is already loaded for this world!" );

			string jsonString;

			BlockAtlas = FileSystem.Mounted.ReadJsonOrDefault<BlockAtlas>( fileName );
			jsonString = JsonSerializer.Serialize( (BlockAtlas)BlockAtlas );

			BlockAtlasType = BlockAtlas.GetType().Name;
			BlockAtlas.Initialize( jsonString );
		}

		public void AddBlockType( BlockType type )
		{
			Host.AssertServer();

			if ( BlockAtlas == null )
				throw new Exception( "Unable to add any block types with no loaded block atlas!" );

			var name = type.GetUniqueName();

			type.BlockId = NextAvailableBlockId;
			type.Initialize();

			Log.Info( $"[Server] Initializing block type {type.FriendlyName} with id #{NextAvailableBlockId}" );

			var aliases = type.GetUniqueAliases();

			if ( aliases != null )
			{
				foreach ( var alias in aliases )
				{
					BlockTypes[alias] = NextAvailableBlockId;
					Log.Info( $"[Server] Adding {alias} as an alias for #{NextAvailableBlockId}" );
				}
			}

			BlockTypes[name] = NextAvailableBlockId;
			BlockData[NextAvailableBlockId] = type;
			NextAvailableBlockId++;
		}

		[ClientRpc]
		public static void ReceiveChunks( byte[] data )
		{
			var decompressed = CompressionHelper.Decompress( data );

			if ( Current.IsValid() )
			{
				Current.ChunkUpdateQueue.Enqueue( decompressed );
			}
		}

		public void AddAllBlockTypes()
		{
			Host.AssertServer();

			if ( BlockAtlas == null )
				throw new Exception( "Unable to add any block types with no loaded block atlas!" );

			foreach ( var type in TypeLibrary.GetDescriptions<BlockType>() )
			{
				if ( type.IsAbstract || type.IsGenericType )
					continue;

				if ( type.TargetType == typeof( AirBlock ) || type.TargetType == typeof( AssetBlock ) )
					continue;

				AddBlockType( type.Create<BlockType>() );
			}

			foreach ( var resource in BlockResource.All )
			{
				var type = new AssetBlock();
				type.SetResource( resource );
				AddBlockType( type );
			}
		}

		public void Reset()
		{
			if ( IsServer )
			{
				var entities = Entity.All.OfType<ISourceEntity>();

				foreach ( var entity in entities )
				{
					entity.Delete();
				}
			}

			ChunkInitialUpdateQueue.Clear();
			ChunkFullUpdateQueue.Clear();

			foreach ( var kv in Chunks )
			{
				kv.Value.Destroy();
			}

			Chunks.Clear();
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

		public void SetState<T>( IntVector3 position, T state ) where T : BlockState
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			var localPosition = ToLocalPosition( position );
			chunk.SetState( localPosition, state );
		}

		public T GetState<T>( IntVector3 position ) where T : BlockState
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return null;

			var localPosition = ToLocalPosition( position );
			return chunk.GetState<T>( localPosition );
		}

		public void Destroy()
		{
			Event.Unregister( this );
			IsDestroyed = true;
			Reset();
		}

		public void Initialize()
		{
			if ( Initialized ) return;

			Initialized = true;
			OnInitialized?.Invoke();

			Event.Register( this );

			GameTask.RunInThreadAsync( ChunkInitialUpdateTask );
			GameTask.RunInThreadAsync( ChunkFullUpdateTask );
		}

		public bool SetBlockAndUpdate( IntVector3 position, byte blockId, int direction, bool forceUpdate = false, bool clearState = true )
		{
			var currentChunk = GetChunk( position );
			if ( !currentChunk.IsValid() ) return false;

			var shouldBuild = false;
			var chunksToUpdate = new HashSet<Chunk>();

			if ( SetBlock( position, blockId, direction, clearState ) || forceUpdate )
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

		public bool SetBlock( IntVector3 position, byte blockId, int direction, bool clearState = true )
		{
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return false;

			var localPosition = ToLocalPosition( position );
			var blockIndex = chunk.GetLocalPositionIndex( localPosition );
			var currentBlockId = chunk.GetLocalIndexBlock( blockIndex );

			if ( blockId == currentBlockId ) return false;

			var currentBlock = GetBlockType( currentBlockId );
			var block = GetBlockType( blockId );

			currentBlock.OnBlockRemoved( chunk, position );

			chunk.ClearDetails( localPosition );

			if ( !clearState )
			{
				var state = chunk.GetState<BlockState>( localPosition );

				if ( state.IsValid() )
				{
					if ( direction == -1 )
						direction = (int)state.Direction;

					state.Direction = (BlockFace)direction;
					state.BlockId = blockId;
				}
			}
			else
			{
				chunk.RemoveState( localPosition );
			}

			chunk.SetBlock( localPosition, blockId );

			block.OnBlockAdded( chunk, position, direction );

			var entityName = IsServer ? block.ServerEntity : block.ClientEntity;   

			if ( !string.IsNullOrEmpty( entityName ) )
			{
				var entity = TypeLibrary.Create<BlockEntity>( entityName );
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
					{
						neighbourBlock.OnNeighbourUpdated( neighbourChunk, neighbourPosition, position );
					}
				}
			}

			return true;
		}

		public static IntVector3 GetAdjacentPosition( IntVector3 position, int side )
		{
			return position + Chunk.BlockDirections[side];
		}

		public BlockType GetBlockType( IntVector3 position )
		{
			return GetBlockType( GetBlock( position ) );
		}

		public BlockType GetBlockType<T>() where T : BlockType
		{
			return GetBlockType( FindBlockId<T>() );
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
			distance = 0f;

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
			distance = 0f;
			Ray ray = new( position, direction );

			var currentIterations = 0;

			while ( currentIterations < 10000 )
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

			if ( traceHitPos.HasValue )
				distanceHit = Vector3.DistanceBetween( position, traceHitPos.Value );

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

		private void OnChunkUpdated( Chunk chunk )
		{
			UpdateVoxelModels();
		}

		private void UpdateVoxelModels()
		{
			var entitiesToRemove = new HashSet<ModelEntity>();

			foreach ( var entity in VoxelModelsEntities )
			{
				if ( !entity.IsValid() )
				{
					entitiesToRemove.Add( entity );
					continue;
				}

				UpdateVoxelModel( entity );
			}

			foreach ( var entity in entitiesToRemove )
			{
				VoxelModelsEntities.Remove( entity );
			}
		}

		private void UpdateVoxelModel( ModelEntity entity )
		{
			var position = ToVoxelPosition( entity.WorldSpaceBounds.Center );
			var chunk = GetChunk( position );
			if ( !chunk.IsValid() ) return;

			if ( entity.SceneObject.IsValid() )
			{
				
			}
		}

		[Event.Tick.Client]
		private void ClientTick()
		{
			while ( ChunkUpdateQueue.Count > 0 )
			{
				var update = ChunkUpdateQueue.Dequeue();

				using ( var stream = new MemoryStream( update ) )
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

							chunk.DeserializeBlockStates( reader );

							_ = chunk.Initialize();
						}
					}
				}
			}

			while ( BlockUpdateQueue.Count > 0 )
			{
				var update = BlockUpdateQueue.Dequeue();

				using ( var stream = new MemoryStream( update ) )
				{
					using ( var reader = new BinaryReader( stream ) )
					{
						var count = reader.ReadInt32();

						for ( var i = 0; i < count; i++ )
						{
							var x = reader.ReadInt32();
							var y = reader.ReadInt32();
							var z = reader.ReadInt32();
							var blockId = reader.ReadByte();
							var direction = reader.ReadInt32();
							var position = new IntVector3( x, y, z );

							BlockUpdateMap[position] = new BlockUpdate
							{
								BlockId = blockId,
								Direction = direction
							};
						}
					}
				}
			}

			if ( BlockUpdateMap.Count > 0 )
			{
				var chunksToUpdate = new HashSet<Chunk>();

				foreach ( var kv in BlockUpdateMap )
				{
					var position = kv.Key;
					var update = kv.Value;

					if ( Current.SetBlock( position, update.BlockId, update.Direction ) )
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

				BlockUpdateMap.Clear();
			}

			while ( StateUpdateQueue.Count > 0 )
			{
				var update = StateUpdateQueue.Dequeue();
				var position = new IntVector3( update.x, update.y, update.z );
				var chunk = Current.GetChunk( position );

				if ( chunk.IsValid() )
				{
					chunk.DeserializeBlockStates( update.Data );
				}
			}
		}

		[Event.Tick.Server]
		private void ServerTick()
		{
			if ( IsLoadingFromFile ) return;

			foreach ( var client in Client.All )
			{
				if ( client.Components.TryGet<ChunkViewer>( out var viewer ) )
				{
					viewer.Update();
				}
			}

			while ( SpawnpointsQueue.Count > 0 )
			{
				if ( SpawnpointsQueue.TryDequeue( out var position ) )
				{
					Spawnpoints.Add( position );
				}
			}
		}

		private async void ChunkFullUpdateTask()
		{
			var queue = ChunkFullUpdateQueue;

			while ( !IsDestroyed )
			{
				try
				{
					if ( !GameManager.Current.IsValid() ) break;

					await GameTask.Delay( 1 );

					while ( queue.Count > 0 )
					{
						if ( queue.TryDequeue( out var chunk ) )
						{
							if ( chunk.IsValid() )
							{
								chunk.FullUpdate();
							}
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

		private async void ChunkInitialUpdateTask()
		{
			var chunksToUpdate = new List<Chunk>();
			var queue = ChunkInitialUpdateQueue;

			while ( !IsDestroyed )
			{
				try
				{
					if ( !GameManager.Current.IsValid() ) break;

					while ( queue.Count > 0 )
					{
						if ( queue.TryDequeue( out var queuedChunk ) )
						{
							chunksToUpdate.Add( queuedChunk );
						}
					}

					if ( chunksToUpdate.Count == 0 )
					{
						await GameTask.Delay( 1 );
						continue;
					}

					if ( IsClient && !Local.Pawn.IsValid() )
					{
						await GameTask.Delay( 1 );
						continue;
					}

					var currentChunkIndex = chunksToUpdate.Count - 1;
					var currentChunk = chunksToUpdate[currentChunkIndex];

					if ( IsClient )
					{
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
							if ( client.IsValid() && client.Pawn.IsValid() )
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
