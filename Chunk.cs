using Sandbox;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Facepunch.Voxels
{
	public partial class ChunkRenderLayer
	{
		public List<BlockVertex> Vertices { get; set; }
		public SceneObject SceneObject { get; set; }
		public ModelBuilder ModelBuilder { get; set; }
		public Chunk Chunk { get; set; }
		public Model Model { get; set; }
		public Mesh Mesh { get; set; }

		public ChunkRenderLayer( Chunk chunk )
		{
			Vertices = new();
			Chunk = chunk;
		}

		public void Initialize()
		{
			ModelBuilder = new ModelBuilder();
			ModelBuilder.AddMesh( Mesh );

			Model = ModelBuilder.Create();

			var transform = new Transform( Chunk.Offset * (float)Chunk.VoxelSize );

			SceneObject = new SceneObject( Map.Scene, Model, transform );
			SceneObject.Attributes.Set( "VoxelSize", Chunk.VoxelSize );
			SceneObject.Attributes.Set( "LightMap", Chunk.LightMap.Texture );
		}
	}

	public partial class Chunk : IValid
	{
		public struct ChunkVertexData
		{
			public BlockVertex[][] Vertices;
			public Vector3[] CollisionVertices;
			public int[] CollisionIndices;
			public bool IsValid;
		}

		public Dictionary<IntVector3, BlockData> Data { get; set; } = new();
		public ChunkVertexData UpdateVerticesResult { get; set; }

		public HashSet<IntVector3> DirtyData { get; set; } = new();
		public bool HasDoneFirstFullUpdate { get; set; }
		public bool IsFullUpdateActive { get; set; }
		public ChunkGenerator Generator { get; set; }
		public bool QueueRebuild { get; set; }
		public bool HasOnlyAirBlocks { get; set; }
		public bool IsModelCreated { get; private set; }
		public bool HasGenerated { get; private set; }
		public bool Initialized { get; private set; }
		public Vector3 Bounds { get; private set; }
		public Biome Biome { get; set; }

		public bool IsServer => Host.IsServer;
		public bool IsClient => Host.IsClient;

		public byte[] Blocks;

		public ChunkRenderLayer TranslucentLayer;
		public ChunkRenderLayer AlphaTestLayer;
		public ChunkRenderLayer OpaqueLayer;
		public List<ChunkRenderLayer> RenderLayers;
		public ChunkLightMap LightMap { get; set; }
		public int VoxelSize;
		public int SizeX;
		public int SizeY;
		public int SizeZ;
		public IntVector3 Center;
		public IntVector3 Offset;
		public IntVector3 Size;
		public VoxelWorld World;

		public PhysicsBody Body;
		public PhysicsShape Shape;

		private ConcurrentQueue<PhysicsShape> ShapesToDelete { get; set; } = new();
		private Dictionary<int, BlockEntity> Entities { get; set; }
		private object VertexLock = new object();
		private bool QueuedFullUpdate { get; set; }
		private bool IsInitializing { get; set; }

		public bool IsValid => Body.IsValid();

		public Chunk()
		{

		}

		public Chunk( VoxelWorld world, int x, int y, int z )
		{
			HasOnlyAirBlocks = true;
			RenderLayers = new();
			VoxelSize = world.VoxelSize;
			SizeX = world.ChunkSize.x;
			SizeY = world.ChunkSize.y;
			SizeZ = world.ChunkSize.z;
			Size = new IntVector3( SizeX, SizeY, SizeZ );
			Center = new IntVector3( SizeX / 2, SizeY / 2, SizeZ / 2 );
			Bounds = new Vector3( SizeX, SizeY, SizeZ ) * VoxelSize;
			Blocks = new byte[SizeX * SizeY * SizeZ];
			Entities = new();
			LightMap = new ChunkLightMap( this, world );
			Offset = new IntVector3( x, y, z );
			Body = Map.Physics.Body;
			World = world;
		}

		public ChunkRenderLayer CreateRenderLayer( string materialName )
		{
			var material = Material.Load( materialName );
			var layer = new ChunkRenderLayer( this );
			var boundsMin = Vector3.Zero;
			var boundsMax = boundsMin + new Vector3( SizeX, SizeY, SizeZ ) * VoxelSize;
			layer.Mesh = new Mesh( material );
			layer.Mesh.SetBounds( boundsMin, boundsMax );
			RenderLayers.Add( layer );
			return layer;
		}

		public async Task Initialize()
		{
			if ( IsInitializing || Initialized )
				return;

			IsInitializing = true;

			await GameTask.RunInThreadAsync( StartThreadedInitializeTask );

			CreateEntities();
			Initialized = true;
			IsInitializing = false;

			if ( IsClient )
			{
				TranslucentLayer = CreateRenderLayer( World.TranslucentMaterial );
				AlphaTestLayer = CreateRenderLayer( World.OpaqueMaterial );
				OpaqueLayer = CreateRenderLayer( World.OpaqueMaterial );
			}

			Event.Register( this );

			World.AddToInitialUpdateList( this );

			if ( IsClient )
			{
				QueueNeighbourFullUpdate();
			}
		}

		public void CreateBlockAtPosition( IntVector3 localPosition, byte blockId )
		{
			if ( !IsInside( localPosition ) ) return;

			var position = Offset + localPosition;
			var block = World.GetBlockType( blockId );

			SetBlock( localPosition, blockId );
			block.OnBlockAdded( this, position.x, position.y, position.z, (int)BlockFace.Top );

			var entityName = IsServer ? block.ServerEntity : block.ClientEntity;

			if ( !string.IsNullOrEmpty( entityName ) )
			{
				var entity = Library.Create<BlockEntity>( entityName );
				entity.BlockType = block;
				SetEntity( localPosition, entity );
			}

			if ( blockId != 0 && HasOnlyAirBlocks )
				HasOnlyAirBlocks = false;
		}

		public bool IsEmpty( int lx, int ly, int lz )
		{
			if ( !IsInside( ly, ly, lz ) ) return true;
			var index = GetLocalPositionIndex( lx, ly, lz );
			return Blocks[index] == 0;
		}

		public bool IsInside( int lx, int ly, int lz )
		{
			if ( lx < 0 || ly < 0 || lz < 0 )
				return false;

			if ( lx >= SizeX || ly >= SizeY || lz >= SizeZ )
				return false;

			return true;
		}

		public bool IsInside( IntVector3 localPosition )
		{
			if ( localPosition.x < 0 || localPosition.y < 0 || localPosition.z < 0 )
				return false;

			if ( localPosition.x >= SizeX || localPosition.y >= SizeY || localPosition.z >= SizeZ )
				return false;

			return true;
		}

		public bool IsFullUpdateTaskRunning()
		{
			return IsFullUpdateActive;
		}

		public void QueueFullUpdate()
		{
			if ( !HasDoneFirstFullUpdate ) return;
			QueuedFullUpdate = true;
		}

		public async void FullUpdate()
		{
			if ( !IsValid || IsFullUpdateTaskRunning() )
				return;

			try
			{
				IsFullUpdateActive = true;
				QueuedFullUpdate = false;

				await GameTask.RunInThreadAsync( StartFullUpdateTask );

				IsFullUpdateActive = false;
				QueueRebuild = true;
			}
			catch ( TaskCanceledException )
			{

			}
		}

		public Voxel GetVoxel( IntVector3 position )
		{
			return GetVoxel( position.x, position.y, position.z );
		}

		public Voxel GetVoxel( int x, int y, int z )
		{
			var voxel = new Voxel();
			voxel.LocalPosition = new IntVector3( x, y, z );
			voxel.Position = Offset + voxel.LocalPosition;
			voxel.BlockIndex = GetLocalPositionIndex( x, y, z );
			voxel.BlockId = GetLocalIndexBlock( voxel.BlockIndex );
			voxel.IsValid = true;
			return voxel;
		}

		public void StartGeneratorTask()
		{
			if ( !HasGenerated && Generator != null )
			{
				Generator.Initialize();
				Generator.Generate();
				HasGenerated = true;
			}
		}

		public void StartFirstFullUpdateTask()
		{
			LightMap.UpdateTorchLight();
			LightMap.UpdateSunLight();

			UpdateVerticesResult = StartUpdateVerticesTask();

			if ( World.BuildCollisionInThread )
			{
				BuildCollision();
			}

			HasDoneFirstFullUpdate = true;
			QueueRebuild = true;
		}

		public void PerformFullTorchUpdate()
		{
			for ( var x = 0; x < SizeX; x++ )
			{
				for ( var y = 0; y < SizeY; y++ )
				{
					for ( var z = 0; z < SizeZ; z++ )
					{
						var position = new IntVector3( x, y, z );
						var blockIndex = GetLocalPositionIndex( position );
						var block = World.GetBlockType( Blocks[blockIndex] );

						if ( block.LightLevel.x > 0 || block.LightLevel.y > 0 || block.LightLevel.z > 0 )
						{
							LightMap.AddRedTorchLight( position, (byte)block.LightLevel.x );
							LightMap.AddGreenTorchLight( position, (byte)block.LightLevel.y );
							LightMap.AddBlueTorchLight( position, (byte)block.LightLevel.z );
						}
					}
				}
			}
		}

		public void UpdateAdjacents( bool recurseNeighbours = false )
		{
			UpdateNeighbourLightMap( "LightMapWest", BlockFace.West, recurseNeighbours );
			UpdateNeighbourLightMap( "LightMapEast", BlockFace.East, recurseNeighbours );
			UpdateNeighbourLightMap( "LightMapNorth", BlockFace.North, recurseNeighbours );
			UpdateNeighbourLightMap( "LightMapSouth", BlockFace.South, recurseNeighbours );
			UpdateNeighbourLightMap( "LightMapTop", BlockFace.Top, recurseNeighbours );
			UpdateNeighbourLightMap( "LightMapBottom", BlockFace.Bottom, recurseNeighbours );
		}

		public IEnumerable<IntVector3> GetNeighbourOffsets()
		{
			yield return GetAdjacentChunkOffset( BlockFace.Top );
			yield return GetAdjacentChunkOffset( BlockFace.Bottom );
			yield return GetAdjacentChunkOffset( BlockFace.North );
			yield return GetAdjacentChunkOffset( BlockFace.East );
			yield return GetAdjacentChunkOffset( BlockFace.South );
			yield return GetAdjacentChunkOffset( BlockFace.West );
		}

		public IEnumerable<Chunk> GetNeighbours()
		{
			var chunk = World.GetChunk( GetAdjacentChunkOffset( BlockFace.Top ) );
			if ( chunk.IsValid() ) yield return chunk;

			chunk = World.GetChunk( GetAdjacentChunkOffset( BlockFace.Bottom ) );
			if ( chunk.IsValid() ) yield return chunk;

			chunk = World.GetChunk( GetAdjacentChunkOffset( BlockFace.North ) );
			if ( chunk.IsValid() ) yield return chunk;

			chunk = World.GetChunk( GetAdjacentChunkOffset( BlockFace.East ) );
			if ( chunk.IsValid() ) yield return chunk;

			chunk = World.GetChunk( GetAdjacentChunkOffset( BlockFace.South ) );
			if ( chunk.IsValid() ) yield return chunk;

			chunk = World.GetChunk( GetAdjacentChunkOffset( BlockFace.West ) );
			if ( chunk.IsValid() ) yield return chunk;
		}

		public void QueueNeighbourFullUpdate()
		{
			QueueNeighbourFullUpdate( BlockFace.Top );
			QueueNeighbourFullUpdate( BlockFace.Bottom );
			QueueNeighbourFullUpdate( BlockFace.North );
			QueueNeighbourFullUpdate( BlockFace.East );
			QueueNeighbourFullUpdate( BlockFace.South );
			QueueNeighbourFullUpdate( BlockFace.West );
		}

		public IntVector3 GetAdjacentChunkOffset( BlockFace direction )
		{
			IntVector3 position;

			if ( direction == BlockFace.Top )
				position = new IntVector3( 0, 0, SizeZ );
			else if ( direction == BlockFace.Bottom )
				position = new IntVector3( 0, 0, -SizeZ );
			else if ( direction == BlockFace.North )
				position = new IntVector3( 0, SizeY, 0 );
			else if ( direction == BlockFace.South )
				position = new IntVector3( 0, -SizeY, 0 );
			else if ( direction == BlockFace.East )
				position = new IntVector3( SizeX, 0, 0 );
			else
				position = new IntVector3( -SizeX, 0, 0 );

			return Offset + position;
		}

		public void QueueNeighbourFullUpdate( BlockFace direction )
		{
			var neighbour = GetNeighbour( direction );

			if ( neighbour.IsValid() && neighbour.Initialized )
			{
				neighbour.QueueFullUpdate();
			}
		}

		public void BuildMeshAndCollision()
		{
			BuildMesh();

			if ( !IsModelCreated )
			{
				foreach ( var layer in RenderLayers )
				{
					layer.Initialize();
				}

				IsModelCreated = true;
			}

			if ( !World.BuildCollisionInThread )
			{
				BuildCollision();
			}

			UpdateAdjacents( true );

			QueueRebuild = false;
		}

		public void DeserializeData( BinaryReader reader )
		{
			var count = reader.ReadInt32();

			for ( var i = 0; i < count; i++ )
			{
				var x = reader.ReadByte();
				var y = reader.ReadByte();
				var z = reader.ReadByte();
				var blockIndex = GetLocalPositionIndex( x, y, z );
				var blockId = Blocks[blockIndex];
				var block = World.GetBlockType( blockId );
				var position = new IntVector3( x, y, z );

				if ( !Data.TryGetValue( position, out var blockData ) )
				{
					blockData = block.CreateDataInstance();
					blockData.Chunk = this;
					blockData.LocalPosition = position;
					Data.Add( position, blockData );
				}

				blockData.Deserialize( reader );
			}
		}

		public void DeserializeData( byte[] data )
		{
			using ( var stream = new MemoryStream( data ) )
			{
				using ( var reader = new BinaryReader( stream ) )
				{
					DeserializeData( reader );
				}
			}
		}

		public void SerializeData( BinaryWriter writer )
		{
			writer.Write( Data.Count );

			foreach ( var kv in Data )
			{
				var position = kv.Key;
				writer.Write( (byte)position.x );
				writer.Write( (byte)position.y );
				writer.Write( (byte)position.z );
				kv.Value.Serialize( writer );
			}
		}

		public byte[] SerializeData()
		{
			using ( var stream = new MemoryStream() )
			{
				using ( var writer = new BinaryWriter( stream ) )
				{
					SerializeData( writer );
					return stream.ToArray();
				}
			}
		}

		public T GetOrCreateData<T>( IntVector3 position ) where T : BlockData
		{
			if ( Data.TryGetValue( position, out var data ) )
				return data as T;

			var blockId = GetLocalPositionBlock( position );
			var block = VoxelWorld.Current.GetBlockType( blockId );

			data = block.CreateDataInstance();
			data.Chunk = this;
			data.LocalPosition = position;
			Data.Add( position, data );

			data.IsDirty = true;

			return data as T;
		}

		public T GetData<T>( IntVector3 position ) where T : BlockData
		{
			if ( Data.TryGetValue( position, out var data ) )
				return data as T;
			else
				return null;
		}

		public int GetLocalPositionIndex( int x, int y, int z )
		{
			return x * SizeY * SizeZ + y * SizeZ + z;
		}

		public int GetLocalPositionIndex( IntVector3 position )
		{
			return position.x * SizeY * SizeZ + position.y * SizeZ + position.z;
		}

		public byte GetMapPositionBlock( IntVector3 position )
		{
			var x = position.x % SizeX;
			var y = position.y % SizeY;
			var z = position.z % SizeZ;
			var index = x * SizeY * SizeZ + y * SizeZ + z;
			return Blocks[index];
		}

		public byte GetLocalPositionBlock( int x, int y, int z )
		{
			return Blocks[GetLocalPositionIndex( x, y, z )];
		}

		public byte GetLocalPositionBlock( IntVector3 position )
		{
			return Blocks[GetLocalPositionIndex( position )];
		}

		public byte GetLocalIndexBlock( int index )
		{
			return Blocks[index];
		}

		public IntVector3 ToMapPosition( IntVector3 position )
		{
			return Offset + position;
		}

		public void CreateEntities()
		{
			var isServer = IsServer;

			for ( var x = 0; x < SizeX; x++ )
			{
				for ( var y = 0; y < SizeY; y++ )
				{
					for ( var z = 0; z < SizeZ; z++ )
					{
						var index = x * SizeY * SizeZ + y * SizeZ + z;
						var blockId = Blocks[index];
						if ( blockId == 0 ) continue;

						var block = World.GetBlockType( blockId );
						var entityName = isServer ? block.ServerEntity : block.ClientEntity;

						if ( !string.IsNullOrEmpty( entityName ) )
						{
							var entity = Library.Create<BlockEntity>( entityName );
							var position = new IntVector3( x, y, z );
							entity.BlockType = block;
							SetEntity( position, entity );
						}
					}
				}
			}
		}

		public void PropagateSunlight()
		{
			var z = SizeZ - 1;

			for ( var x = 0; x < SizeX; x++ )
			{
				for ( var y = 0; y < SizeY; y++ )
				{
					var position = new IntVector3( x, y, z );
					var blockId = GetLocalPositionBlock( position );
					var block = World.GetBlockType( blockId );

					if ( block.IsTranslucent )
					{
						LightMap.AddSunLight( position, 15 );
					}
				}
			}

			var chunkAbove = GetNeighbour( BlockFace.Top );
			if ( !chunkAbove.IsValid() ) return;
			if ( !chunkAbove.Initialized ) return;

			for ( var x = 0; x < SizeX; x++ )
			{
				for ( var y = 0; y < SizeY; y++ )
				{
					var lightLevel = World.GetSunLight( chunkAbove.Offset + new IntVector3( x, y, 0 ) );

					if ( lightLevel > 0 )
					{
						LightMap.AddSunLight( new IntVector3( x, y, SizeZ - 1 ), lightLevel );
					}
				}
			}
		}

		public int GetSizeInDirection( BlockFace direction )
		{
			if ( direction == BlockFace.Top || direction == BlockFace.Bottom )
				return SizeZ;
			else if ( direction == BlockFace.North || direction == BlockFace.South )
				return SizeY;
			else
				return SizeX;
		}

		public Chunk GetNeighbour( BlockFace direction )
		{
			var directionIndex = (int)direction;
			var neighbourPosition = Offset + (BlockDirections[directionIndex] * GetSizeInDirection( direction ));
			return World.GetChunk( neighbourPosition );
		}

		public void SetBlock( IntVector3 position, byte blockId )
		{
			var index = GetLocalPositionIndex( position );
			Blocks[index] = blockId;

			if ( blockId != 0 && HasOnlyAirBlocks )
				HasOnlyAirBlocks = false;
		}

		public void SetBlock( int index, byte blockId )
		{
			Blocks[index] = blockId;

			if ( blockId != 0 && HasOnlyAirBlocks )
				HasOnlyAirBlocks = false;
		}

		public void Destroy()
		{
			if ( IsClient )
			{
				var viewer = Local.Client.GetChunkViewer();

				if ( viewer.IsValid() )
				{
					viewer.RemoveLoadedChunk( Offset );
				}
			}

			foreach ( var layer in RenderLayers )
			{
				layer?.SceneObject.Delete();
			}

			RenderLayers.Clear();

			LightMap.Destroy();

			foreach ( var kv in Entities )
			{
				kv.Value.Delete();
			}

			Entities.Clear();

			UpdateShapeDeleteQueue();

			if ( Body.IsValid() && Shape.IsValid() )
			{
				Shape.Remove();
				Shape = null;
			}

			Body = null;

			Event.Unregister( this );
		}

		public Entity GetEntity( IntVector3 position )
		{
			var index = GetLocalPositionIndex( position );
			if ( Entities.TryGetValue( index, out var entity ) )
				return entity;
			else
				return null;
		}

		public void SetEntity( IntVector3 position, BlockEntity entity )
		{
			var mapPosition = Offset + position;

			entity.VoxelWorld = World;
			entity.Chunk = this;
			entity.BlockPosition = mapPosition;
			entity.LocalBlockPosition = position;
			entity.CenterOnBlock( true, false );
			entity.Initialize();

			var index = GetLocalPositionIndex( position );
			RemoveEntity( position );
			Entities.Add( index, entity );
		}

		public void RemoveEntity( IntVector3 position )
		{
			var index = GetLocalPositionIndex( position );
			if ( Entities.TryGetValue( index, out var entity ) )
			{
				entity.Delete();
				Entities.Remove( index );
			}
		}

		public void BuildCollision()
		{
			lock ( VertexLock )
			{
				if ( !UpdateVerticesResult.IsValid ) return;
				if ( !Body.IsValid() ) return;

				var collisionVertices = UpdateVerticesResult.CollisionVertices;
				var collisionIndices = UpdateVerticesResult.CollisionIndices;
				var oldShape = Shape;

				if ( collisionVertices.Length > 0 && collisionIndices.Length > 0 )
				{
					Shape = Body.AddMeshShape( collisionVertices, collisionIndices );
				}

				if ( oldShape.IsValid() )
				{
					ShapesToDelete.Enqueue( oldShape );
				}
			}
		}

		public void BuildMesh()
		{
			Host.AssertClient();

			lock ( VertexLock )
			{
				if ( !UpdateVerticesResult.IsValid ) return;

				try
				{
					for ( int i = 0; i < RenderLayers.Count; i++ )
					{
						var layer = RenderLayers[i];
						if ( !layer.Mesh.IsValid ) continue;

						var vertices = UpdateVerticesResult.Vertices[i];
						var vertexCount = vertices.Length;

						if ( layer.Mesh.HasVertexBuffer )
							layer.Mesh.SetVertexBufferSize( vertexCount );
						else
							layer.Mesh.CreateVertexBuffer<BlockVertex>( Math.Max( 1, vertexCount ), BlockVertex.Layout );

						vertexCount = 0;

						if ( vertices.Length > 0 )
						{
							layer.Mesh.SetVertexBufferData( new Span<BlockVertex>( vertices ), vertexCount );
							vertexCount += vertices.Length;
						}

						layer.Mesh.SetVertexRange( 0, vertexCount );
					}
				}
				catch ( Exception e )
				{
					Log.Error( e );
				}
			}
		}

		private void UpdateNeighbourLightMap( string name, BlockFace direction, bool recurseNeighbours = false )
		{
			var neighbour = World.GetChunk( GetAdjacentChunkOffset( direction ) );

			if ( neighbour.IsValid() )
			{
				for ( int i = 0; i < RenderLayers.Count; i++ )
				{
					var layer = RenderLayers[i];
					layer.SceneObject?.Attributes.Set( name, neighbour.LightMap.Texture );
				}

				if ( recurseNeighbours ) neighbour.UpdateAdjacents();
			}
		}

		static readonly IntVector3[] BlockVertices = new[]
		{
			new IntVector3( 0, 0, 1 ),
			new IntVector3( 0, 1, 1 ),
			new IntVector3( 1, 1, 1 ),
			new IntVector3( 1, 0, 1 ),
			new IntVector3( 0, 0, 0 ),
			new IntVector3( 0, 1, 0 ),
			new IntVector3( 1, 1, 0 ),
			new IntVector3( 1, 0, 0 ),
		};

		static readonly int[] BlockIndices = new[]
		{
			2, 1, 0, 0, 3, 2,
			5, 6, 7, 7, 4, 5,
			4, 7, 3, 3, 0, 4,
			6, 5, 1, 1, 2, 6,
			5, 4, 0, 0, 1, 5,
			7, 6, 2, 2, 3, 7,
		};

		public static readonly IntVector3[] BlockDirections = new[]
		{
			new IntVector3( 0, 0, 1 ),
			new IntVector3( 0, 0, -1 ),
			new IntVector3( 0, -1, 0 ),
			new IntVector3( 0, 1, 0 ),
			new IntVector3( -1, 0, 0 ),
			new IntVector3( 1, 0, 0 ),
		};

		static readonly int[] BlockDirectionAxis = new[]
		{
			2, 2, 1, 1, 0, 0
		};

		public ChunkVertexData StartUpdateVerticesTask()
		{
			var output = new ChunkVertexData
			{
				IsValid = false
			};

			lock ( VertexLock )
			{
				for ( var i = 0; i < RenderLayers.Count; i++ )
				{
					var layer = RenderLayers[i];
					layer.Vertices.Clear();
				}

				var collisionVertices = new List<Vector3>();
				var collisionIndices = new List<int>();

				var faceWidth = 1;
				var faceHeight = 1;

				for ( var x = 0; x < SizeX; x++ )
				{
					for ( var y = 0; y < SizeY; y++ )
					{
						for ( var z = 0; z < SizeZ; z++ )
						{
							// We need to check if the game is still running.
							if ( !Game.Current.IsValid() ) break;

							var position = new IntVector3( x, y, z );
							var index = x * SizeY * SizeZ + y * SizeZ + z;
							var blockId = Blocks[index];
							if ( blockId == 0 ) continue;

							var block = World.GetBlockType( blockId );

							for ( int faceSide = 0; faceSide < 6; faceSide++ )
							{
								var neighbourId = World.GetAdjacentBlock( Offset + position, faceSide );
								var neighbourBlock = World.GetBlockType( neighbourId );
								var collisionIndex = collisionIndices.Count;
								var textureId = block.GetTextureId( (BlockFace)faceSide, this, x, y, z );
								var normal = (byte)faceSide;
								var faceData = (uint)((textureId & 31) << 18 | (0 & 15) << 23 | (normal & 7) << 27);
								var axis = BlockDirectionAxis[faceSide];
								var uAxis = (axis + 1) % 3;
								var vAxis = (axis + 2) % 3;

								var shouldGenerateVertices = IsClient && block.HasTexture && neighbourBlock.IsTranslucent && !block.ShouldCullFace( (BlockFace)faceSide, neighbourBlock );
								var shouldGenerateCollision = !block.IsPassable && neighbourBlock.IsPassable;

								if ( !shouldGenerateCollision && !shouldGenerateVertices )
									continue;

								for ( int i = 0; i < 6; ++i )
								{
									var vi = BlockIndices[(faceSide * 6) + i];
									var vOffset = BlockVertices[vi];

									vOffset[uAxis] *= faceWidth;
									vOffset[vAxis] *= faceHeight;

									if ( shouldGenerateVertices )
									{
										var vertex = new BlockVertex( (uint)(x + vOffset.x), (uint)(y + vOffset.y), (uint)(z + vOffset.z), (uint)x, (uint)y, (uint)z, faceData );

										if ( block.IsTranslucent )
										{ 
											if ( block.UseTransparency )
												TranslucentLayer.Vertices.Add( vertex );
											else
												AlphaTestLayer.Vertices.Add( vertex );
										}
										else
										{
											OpaqueLayer.Vertices.Add( vertex );
										}
									}

									if ( shouldGenerateCollision )
									{
										collisionVertices.Add( new Vector3( (x + vOffset.x) + Offset.x, (y + vOffset.y) + Offset.y, (z + vOffset.z) + Offset.z ) * VoxelSize );
										collisionIndices.Add( collisionIndex + i );
									}
								}
							}
						}
					}
				}

				output.Vertices = new BlockVertex[RenderLayers.Count][];

				for ( var i = 0; i < RenderLayers.Count; i++ )
				{
					output.Vertices[i] = RenderLayers[i].Vertices.ToArray();
				}

				output.CollisionVertices = collisionVertices.ToArray();
				output.CollisionIndices = collisionIndices.ToArray();
				output.IsValid = true;
			}

			return output;
		}

		[Event.Tick.Server]
		private void ServerTick()
		{
			if ( DirtyData.Count > 0 )
			{
				using ( var stream = new MemoryStream() )
				{
					using ( var writer = new BinaryWriter( stream ) )
					{
						writer.Write( DirtyData.Count );

						foreach ( var position in DirtyData )
						{
							var data = GetData<BlockData>( position );
							if ( data == null ) continue;
							writer.Write( (byte)position.x );
							writer.Write( (byte)position.y );
							writer.Write( (byte)position.z );
							data.Serialize( writer );
							data.IsDirty = false;
						}

						VoxelWorld.ReceiveDataUpdate( To.Everyone, Offset.x, Offset.y, Offset.z, stream.ToArray() );
					}
				}
			}

			DirtyData.Clear();

			if ( IsFullUpdateTaskRunning() ) return;

			if ( !World.BuildCollisionInThread && QueueRebuild )
			{
				BuildCollision();
				QueueRebuild = false;
			}
		}

		[Event.Tick.Client]
		private void ClientTick()
		{
			if ( IsFullUpdateTaskRunning() ) return;

			if ( QueueRebuild && !AreAdjacentChunksUpdating() )
			{
				BuildMeshAndCollision();

				var viewer = Local.Client.GetChunkViewer();

				if ( viewer.IsValid() )
				{
					viewer.AddLoadedChunk( Offset );
				}
			}

			if ( !QueueRebuild && HasDoneFirstFullUpdate )
			{
				LightMap.UpdateTorchLight();
				LightMap.UpdateSunLight();

				if ( LightMap.UpdateTexture() )
				{
					QueueFullUpdate();
				}
			}
		}

		[Event.Tick]
		private void Tick()
		{
			UpdateShapeDeleteQueue();

			if ( QueuedFullUpdate )
			{
				FullUpdate();
			}
		}

		private void UpdateShapeDeleteQueue()
		{
			while ( ShapesToDelete.Count > 0 )
			{
				if ( ShapesToDelete.TryDequeue( out var shape ) )
				{
					shape.Remove();
				}
			}
		}

		private async Task StartThreadedInitializeTask()
		{
			if ( IsServer )
			{
				StartGeneratorTask();
				PropagateSunlight();
				PerformFullTorchUpdate();
			}

			LightMap.UpdateTorchLight();
			LightMap.UpdateSunLight();

			await GameTask.Delay( 1 );
		}

		private bool AreAdjacentChunksUpdating()
		{
			return GetNeighbours().Any( c => c.IsFullUpdateTaskRunning() );
		}

		private void StartFullUpdateTask()
		{
			try
			{
				LightMap.UpdateTorchLight();
				LightMap.UpdateSunLight();

				UpdateVerticesResult = StartUpdateVerticesTask();

				if ( World.BuildCollisionInThread )
				{
					BuildCollision();
				}
			}
			catch ( Exception e )
			{
				Log.Error( e );
			}
		}
	}
}
