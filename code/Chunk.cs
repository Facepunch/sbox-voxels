﻿using Sandbox;
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

			SceneObject = new SceneObject( Game.SceneWorld, Model, transform );
			SceneObject.Attributes.Set( "VoxelSize", Chunk.VoxelSize );
			SceneObject.Attributes.Set( "DataMap", Chunk.DataMap.Texture );
		}

		public void SetBrightness( float brightness )
		{
			SceneObject?.Attributes?.Set( "VoxelBrightness", brightness );
		}

		public void SetOpacity( float opacity )
		{
			SceneObject?.Attributes?.Set( "VoxelOpacity", opacity );
		}
	}

	public partial class Chunk : IValid
	{
		public event Action OnFullUpdate;

		public struct ChunkVertexData
		{
			public BlockVertex[][] Vertices;
			public Vector3[] CollisionVertices;
			public int[] CollisionIndices;
			public bool IsValid;
		}

		public struct ChunkBlockUpdate
		{
			public byte BlockId;
			public int Direction;
		}

		public Dictionary<IntVector3, ChunkBlockUpdate> OutgoingBlockUpdates { get; private set; } = new();
		public HashSet<IntVector3> BlockUpdatesToClear { get; private set; } = new();
		public HashSet<IntVector3> StateUpdatesToClear { get; private set; } = new();
		public Dictionary<IntVector3, BlockState> BlockStates { get; set; } = new();
		public ChunkVertexData UpdateVerticesResult { get; set; }
		public HashSet<IntVector3> DirtyBlockStates { get; set; } = new();
		public bool IsQueuedForFullUpdate { get; set; }
		public bool IsQueuedForNextUpdate { get; set; }
		public bool HasDoneFirstFullUpdate { get; set; }
		public HashSet<ChunkViewer> Viewers { get; set; } = new();
		public ChunkGenerator Generator { get; set; }
		public bool HasOnlyAirBlocks { get; set; }
		public bool HasGenerated { get; private set; }
		public bool Initialized { get; private set; }
		public bool IsDestroyed { get; private set; }
		public Vector3 Bounds { get; private set; }
		public Biome Biome { get; set; }

		public byte[] Blocks;

		public ChunkRenderLayer TranslucentLayer;
		public ChunkRenderLayer AlphaTestLayer;
		public ChunkRenderLayer OpaqueLayer;
		public List<ChunkRenderLayer> RenderLayers;
		public ChunkDataMap DataMap { get; set; }
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
		private Dictionary<int, List<Entity>> Details { get; set; }
		private bool IsFullUpdateEventPending { get; set; }
		private List<QueuedTick> QueuedTicks { get; set; } = new();
		private Queue<QueuedTick> TicksToRun { get; set; } = new();
		private TimeSince TimeSinceInitialized { get; set; }
		private bool IsInitializing { get; set; }
		private object Lock { get; set; } = new();

		public bool IsValid => Body.IsValid() && !IsDestroyed;

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
			Details = new();
			DataMap = new ChunkDataMap( this, world );
			Offset = new IntVector3( x, y, z );
			Body = Game.PhysicsWorld.Body;
			World = world;
		}

		public ChunkRenderLayer CreateRenderLayer( string materialName )
		{
			var material = Material.Load( materialName );
			var layer = new ChunkRenderLayer( this );
			var boundsMin = Vector3.Zero;
			var boundsMax = boundsMin + new Vector3( SizeX, SizeY, SizeZ ) * VoxelSize;
			layer.Mesh = new Mesh( material );
			layer.Mesh.CreateVertexBuffer<BlockVertex>( BlockVertex.Layout );
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

			if ( !IsValid )
			{
				// We might not be valid anymore.
				return;
			}

			InitializeBlocks();
			Initialized = true;
			IsInitializing = false;
			TimeSinceInitialized = 0f;

			if ( Game.IsClient )
			{
				TranslucentLayer = CreateRenderLayer( World.TranslucentMaterial );
				TranslucentLayer.Initialize();

				AlphaTestLayer = CreateRenderLayer( World.OpaqueMaterial );
				AlphaTestLayer.Initialize();

				OpaqueLayer = CreateRenderLayer( World.OpaqueMaterial );
				OpaqueLayer.Initialize();
			}

			Event.Register( this );

			World.AddToInitialUpdateList( this );
		}

		public void QueueTick( IntVector3 position, BlockType block, float delay )
		{
			QueuedTicks.Add( new QueuedTick
			{
				Position = position,
				BlockId = block.BlockId,
				Delay = delay
			} );
		}

		public void CreateBlockAtPosition( IntVector3 localPosition, byte blockId )
		{
			if ( !IsInside( localPosition ) ) return;

			var position = Offset + localPosition;
			var block = World.GetBlockType( blockId );

			SetBlock( localPosition, blockId );
			RemoveState( localPosition );

			block.OnBlockAdded( this, position, (int)BlockFace.Top );

			for ( var i = 0; i < 5; i++ )
			{
				var neighbourPosition = position + BlockDirections[i];

				if ( World.IsInBounds( neighbourPosition ) )
				{
					var neighbourId = World.GetBlock( neighbourPosition );
					var neighbourBlock = World.GetBlockType( neighbourId );
					var neighbourChunk = World.GetChunk( neighbourPosition );

					if ( neighbourChunk.IsValid() )
						neighbourBlock.OnNeighbourUpdated( neighbourChunk, neighbourPosition, position );
				}
			}

			var entityName = Game.IsServer ? block.ServerEntity : block.ClientEntity;

			if ( !string.IsNullOrEmpty( entityName ) )
			{
				var entity = TypeLibrary.Create<BlockEntity>( entityName );
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

		public void QueueFullUpdate()
		{
			if ( !HasDoneFirstFullUpdate ) return;

			if ( IsQueuedForFullUpdate )
			{
				IsQueuedForNextUpdate = true;
				return;
			}

			World.AddToFullUpdateList( this );
			IsQueuedForFullUpdate = true;
		}

		public void FullUpdate()
		{
			lock ( Lock )
			{
				DataMap.UpdateTexture();

				UpdateVerticesResult = StartUpdateVerticesTask();

				BuildCollision();

				if ( Game.IsClient )
				{
					SetMeshVertices( UpdateVerticesResult.Vertices );
				}

				IsQueuedForFullUpdate = false;
				IsFullUpdateEventPending = true;

				if ( IsQueuedForNextUpdate )
				{
					IsQueuedForNextUpdate = false;
					QueueFullUpdate();
				}
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
			lock ( Lock )
			{
				DataMap.UpdateTexture();

				UpdateVerticesResult = StartUpdateVerticesTask();

				BuildCollision();

				if ( Game.IsClient )
				{
					SetMeshVertices( UpdateVerticesResult.Vertices );
				}

				QueueNeighbourFullUpdate();

				HasDoneFirstFullUpdate = true;
				IsFullUpdateEventPending = true;
			}
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

		public void QueueBlockUpdate( IntVector3 position, byte blockId, int direction )
		{
			OutgoingBlockUpdates[position] = new ChunkBlockUpdate
			{
				BlockId = blockId,
				Direction = direction
			};
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

		public void DeserializeBlockState( BinaryReader reader )
		{
			var isValid = reader.ReadBoolean();
			var x = reader.ReadInt32();
			var y = reader.ReadInt32();
			var z = reader.ReadInt32();
			var blockId = reader.ReadByte();
			var position = new IntVector3( x, y, z );

			if ( !isValid )
			{
				RemoveState( position );
				return;
			}

			if ( !IsInside( x, y, z ) )
			{
				throw new Exception( $"Tried to deserialize a block state for a block outside of the chunk bounds ({x}, {y}, {z})!" );
			}

			var isNewState = false;
			var block = World.GetBlockType( blockId );
			var state = block.CreateState();

			if ( BlockStates.TryGetValue( position, out var oldState ) )
			{
				if ( oldState.GetType() != state.GetType() )
				{
					isNewState = true;
					oldState.OnRemoved();
				}
				else
				{
					state = oldState;
				}
			}
			else
			{
				isNewState = true;
			}

			state.Chunk = this;
			state.BlockId = blockId;
			state.LocalPosition = position;

			if ( isNewState )
			{
				state.OnCreated();
				BlockStates[position] = state;
			}

			try
			{
				state.Deserialize( reader );
			}
			catch ( Exception e )
			{
				BlockStates.Remove( position );
				Log.Error( e.StackTrace );
			}
		}

		public void DeserializeBlockStates( BinaryReader reader )
		{
			var count = reader.ReadInt32();

			for ( var i = 0; i < count; i++ )
			{
				DeserializeBlockState( reader );
			}
		}

		public void DeserializeBlockStates( byte[] data )
		{
			using ( var stream = new MemoryStream( data ) )
			{
				using ( var reader = new BinaryReader( stream ) )
				{
					DeserializeBlockStates( reader );
				}
			}
		}

		public void SerializeBlockState( IntVector3 position, BlockState state, BinaryWriter writer )
		{
			var blockId = GetLocalPositionBlock( position );
			writer.Write( true );
			writer.Write( position.x );
			writer.Write( position.y );
			writer.Write( position.z );
			writer.Write( blockId );
			state.Serialize( writer );
		}

		public void SerializeBlockStates( BinaryWriter writer )
		{
			writer.Write( BlockStates.Count );

			foreach ( var kv in BlockStates )
			{
				SerializeBlockState( kv.Key, kv.Value, writer );
			}
		}

		public byte[] SerializeBlockStates()
		{
			using ( var stream = new MemoryStream() )
			{
				using ( var writer = new BinaryWriter( stream ) )
				{
					SerializeBlockStates( writer );
					return stream.ToArray();
				}
			}
		}

		public T GetOrCreateState<T>( IntVector3 position ) where T : BlockState
		{
			if ( BlockStates.TryGetValue( position, out var state ) )
				return state as T;

			if ( !IsInside( position ) )
				return null;

			var blockId = GetLocalPositionBlock( position );
			var block = VoxelWorld.Current.GetBlockType( blockId );

			state = block.CreateState();
			state.Chunk = this;
			state.BlockId = blockId;
			state.LocalPosition = position;
			state.OnCreated();
			BlockStates.Add( position, state );

			state.IsDirty = true;

			return state as T;
		}

		public T GetState<T>( IntVector3 position ) where T : BlockState
		{
			if ( BlockStates.TryGetValue( position, out var data ) )
				return data as T;
			else
				return null;
		}

		public void RemoveState( IntVector3 position )
		{
			if ( BlockStates.ContainsKey( position ) )
			{
				if ( Game.IsServer )
				{
					DirtyBlockStates.Add( position );
				}

				var state = BlockStates[position];

				if ( state.IsValid() )
				{
					state.OnRemoved();
				}

				BlockStates.Remove( position );
			}
		}

		public void SetState<T>( IntVector3 position, T state ) where T : BlockState
		{
			if ( !IsInside( position ) )
				return;

			var shouldSendStateUpdate = false;
			var didHaveBlockState = false;

			if ( BlockStates.TryGetValue( position, out var oldState ) )
			{
				if ( oldState == state ) return;

				if ( oldState.IsValid() )
				{
					oldState.OnRemoved();
				}

				didHaveBlockState = true;
			}

			if ( state.IsValid() )
			{
				state = (T)state.Copy();
				state.LocalPosition = position;
				BlockStates[position] = state;
				shouldSendStateUpdate = true;
			}
			else if ( didHaveBlockState )
			{
				BlockStates.Remove( position );
				shouldSendStateUpdate = true;
			}

			if ( shouldSendStateUpdate )
			{
				DirtyBlockStates.Add( position );
			}
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

		public void InitializeBlocks()
		{
			var isServer = Game.IsServer;

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

						if ( !block.IsValid() )
						{
							throw new Exception( $"Unable to find a block with id #{blockId}" );
						}

						var localPosition = new IntVector3( x, y, z );
						var position = Offset + localPosition;

						if ( BlockStates.TryGetValue( localPosition, out var state ) )
						{
							state.OnCreated();
						}

						block.OnBlockAdded( this, position, 0 );

						var entityName = isServer ? block.ServerEntity : block.ClientEntity;

						if ( !string.IsNullOrEmpty( entityName ) )
						{
							var entity = TypeLibrary.Create<BlockEntity>( entityName );
							entity.BlockType = block;
							SetEntity( localPosition, entity );
						}
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

		public void Destroy()
		{
			if ( IsDestroyed )
			{
				throw new Exception( "Tried to destroy an already destroyed chunk!" );
			}

			if ( Game.IsClient )
			{
				var viewer = Game.LocalClient.GetChunkViewer();

				if ( viewer.IsValid() )
				{
					viewer.RemoveLoadedChunk( Offset );
				}
			}
			else
			{
				foreach ( var viewer in Viewers )
				{
					viewer.RemoveLoadedChunk( Offset );
				}
			}

			OutgoingBlockUpdates.Clear();
			BlockUpdatesToClear.Clear();
			StateUpdatesToClear.Clear();

			foreach ( var layer in RenderLayers )
			{
				if ( layer != null )
				{
					layer.SceneObject.Delete();
					layer.Model = null;
					layer.Mesh = null;
				}
			}

			RenderLayers.Clear();

			DataMap.Destroy();

			foreach ( var kv in Entities )
			{
				kv.Value.Delete();
			}

			foreach ( var kv in Details )
			{
				foreach ( var entity in kv.Value )
				{
					entity.Delete();
				}
			}

			Entities.Clear();
			Details.Clear();

			UpdateShapeDeleteQueue();

			if ( Body.IsValid() && Shape.IsValid() )
			{
				Shape.Remove();
				Shape = null;
			}

			IsDestroyed = true;

			Event.Unregister( this );
		}

		public List<Entity> GetDetails( IntVector3 position )
		{
			var index = GetLocalPositionIndex( position );
			if ( Details.TryGetValue( index, out var list ) )
				return list;
			else
				return null;
		}

		public Entity GetEntity( IntVector3 position )
		{
			var index = GetLocalPositionIndex( position );
			if ( Entities.TryGetValue( index, out var entity ) )
				return entity;
			else
				return null;
		}

		public void ClearDetails( IntVector3 position )
		{
			var index = GetLocalPositionIndex( position );

			if ( Details.TryGetValue( index, out var list ) )
			{
				foreach ( var entity in list )
				{
					entity.Delete();
				}

				Details.Remove( index );
			}
		}

		public void RemoveDetail( IntVector3 position, Entity entity )
		{
			var index = GetLocalPositionIndex( position );

			if ( Details.TryGetValue( index, out var list ) )
			{
				list.Remove( entity );
			}
		}

		public void AddDetail( IntVector3 position, Entity entity )
		{
			var index = GetLocalPositionIndex( position );

			if ( !Details.TryGetValue( index, out var list ) )
			{
				list = new List<Entity>();
				Details[index] = list;
			}

			list.Add( entity );
		}

		public void SetEntity( IntVector3 position, BlockEntity entity )
		{
			var mapPosition = Offset + position;

			entity.World = World;
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
			if ( !UpdateVerticesResult.IsValid ) return;
			if ( !Body.IsValid() ) return;

			var collisionVertices = UpdateVerticesResult.CollisionVertices;
			var collisionIndices = UpdateVerticesResult.CollisionIndices;
			var oldShape = Shape;

			if ( collisionVertices.Length > 0 && collisionIndices.Length > 0 )
			{
				Shape = Body.AddMeshShape( collisionVertices, collisionIndices );
				Shape.AddTag( "solid" );
			}

			if ( oldShape.IsValid() )
			{
				ShapesToDelete.Enqueue( oldShape );
			}
		}

		public void SetMeshVertices( BlockVertex[][] data )
		{
			Game.AssertClient();

			for ( int i = 0; i < RenderLayers.Count; i++ )
			{
				var layer = RenderLayers[i];
				if ( !layer.Mesh.IsValid ) continue;

				var vertices = data[i];
				var vertexCount = vertices.Length;

				if ( vertexCount > 0 )
				{
					layer.Mesh.SetVertexBufferSize( vertexCount );
					layer.Mesh.SetVertexBufferData( new Span<BlockVertex>( vertices ) );
				}

				layer.Mesh.SetVertexRange( 0, vertexCount );
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
			lock ( Lock )
			{
				var output = new ChunkVertexData
				{
					IsValid = false
				};

				for ( var i = 0; i < RenderLayers.Count; i++ )
				{
					var layer = RenderLayers[i];
					layer.Vertices.Clear();
				}

				var collisionVertices = new List<Vector3>();
				var collisionIndices = new List<int>();

				var faceWidth = 1;
				var faceHeight = 1;

				var chunkIndex = Offset.x * SizeY * SizeZ + Offset.y * SizeZ + Offset.z;
				var random = new Random( chunkIndex );
				var activeGame = Entity.All.OfType<BaseGameManager>().FirstOrDefault();

				for ( var x = 0; x < SizeX; x++ )
				{
					for ( var y = 0; y < SizeY; y++ )
					{
						for ( var z = 0; z < SizeZ; z++ )
						{
							// We need to check if the game is still running.
							if ( !activeGame.IsValid() ) break;

							var position = new IntVector3( x, y, z );
							var index = x * SizeY * SizeZ + y * SizeZ + z;
							var blockId = Blocks[index];
							if ( blockId == 0 ) continue;

							var block = World.GetBlockType( blockId );
							var hueShift = (byte)random.Int( Math.Clamp( block.MinHueShift, 0, 64 ), Math.Clamp( block.MaxHueShift, 0, 64 ) );

							for ( int faceSide = 0; faceSide < 6; faceSide++ )
							{
								var neighbourId = World.GetAdjacentBlock( Offset + position, faceSide );
								var neighbourBlock = World.GetBlockType( neighbourId );
								var collisionIndex = collisionIndices.Count;
								var textureId = block.GetTextureId( (BlockFace)faceSide, this, x, y, z );
								var sourceLighting = block.SourceLighting;
								var normal = (byte)faceSide;
								var chunkData = (uint)((hueShift & 0x3F) << 18);
								var blockData = (uint)((textureId & 0x1ff) << 18 | (normal & 0x7) << 27);
								var extraData = block.TintHex;
								var axis = BlockDirectionAxis[faceSide];
								var uAxis = (axis + 1) % 3;
								var vAxis = (axis + 2) % 3;

								var shouldGenerateVertices = Game.IsClient && !block.HideMesh && neighbourBlock.IsTranslucent && !block.ShouldCullFace( (BlockFace)faceSide, neighbourBlock );
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
										var vertex = new BlockVertex( (uint)(x + vOffset.x), (uint)(y + vOffset.y), (uint)(z + vOffset.z), (uint)x, (uint)y, (uint)z, blockData, chunkData, extraData );

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

				return output;
			}
		}

		private bool IsLoadedForClient( IClient client )
		{
			var viewer = client.GetChunkViewer();
			return viewer.IsValid() && viewer.IsChunkLoaded( Offset );
		}

		[Event.Tick.Server]
		private void ServerTick()
		{
			var clientsToSend = Game.Clients.Where( IsLoadedForClient );

			if ( OutgoingBlockUpdates.Count > 0 )
			{
				using ( var stream = new MemoryStream() )
				{
					using ( var writer = new BinaryWriter( stream ) )
					{
						var updatesPerTick = OutgoingBlockUpdates.Take( 8192 );
						writer.Write( updatesPerTick.Count() );

						foreach ( var kv in updatesPerTick )
						{
							var position = kv.Key;
							var data = kv.Value;
							writer.Write( position.x );
							writer.Write( position.y );
							writer.Write( position.z );
							writer.Write( data.BlockId );
							writer.Write( data.Direction );
							BlockUpdatesToClear.Add( position );
						}

						var compressed = CompressionHelper.Compress( stream.ToArray() );
						VoxelWorld.ReceiveBlockUpdate( To.Multiple( clientsToSend ), compressed.ToArray() );
					}
				}

				foreach ( var position in BlockUpdatesToClear )
				{
					OutgoingBlockUpdates.Remove( position );
				}

				BlockUpdatesToClear.Clear();
			}

			if ( DirtyBlockStates.Count > 0 )
			{
				using ( var stream = new MemoryStream() )
				{
					using ( var writer = new BinaryWriter( stream ) )
					{
						var updatesPerTick = DirtyBlockStates.Take( 4096 );
						writer.Write( updatesPerTick.Count() );

						foreach ( var position in updatesPerTick )
						{
							var state = GetState<BlockState>( position );

							if ( state.IsValid() )
							{
								SerializeBlockState( position, state, writer );
								state.IsDirty = false;
							}
							else
							{
								writer.Write( false );
								writer.Write( position.x );
								writer.Write( position.y );
								writer.Write( position.z );
								writer.Write( (byte)0 );
							}

							StateUpdatesToClear.Add( position );
						}

						VoxelWorld.ReceiveBlockStateUpdate( To.Multiple( clientsToSend ), Offset.x, Offset.y, Offset.z, stream.ToArray() );

						foreach ( var position in StateUpdatesToClear )
						{
							DirtyBlockStates.Remove( position );
						}

						StateUpdatesToClear.Clear();
					}
				}
			}

			DirtyBlockStates.Clear();

			for ( int i = QueuedTicks.Count - 1; i >= 0; i-- )
			{
				var tick = QueuedTicks[i];

				if ( tick.Delay )
				{
					TicksToRun.Enqueue( tick );
					QueuedTicks.RemoveAt( i );
				}
			}

			while ( TicksToRun.Count > 0 )
			{
				var queued = TicksToRun.Dequeue();
				var blockId = World.GetBlock( queued.Position );
				if ( queued.BlockId != blockId ) continue;

				var block = World.GetBlockType( queued.BlockId );
				block.Tick( queued.Position );
			}

			if ( World.ShouldServerUnloadChunks )
			{
				if ( Initialized && TimeSinceInitialized > 10f && Viewers.Count == 0 )
				{
					World.RemoveChunk( this );
				}
			}
		}

		[Event.Tick.Client]
		private void ClientTick()
		{
			var brightness = 1f;
			var game = VoxelWorld.GetGameManager();

			if ( game.IsValid() )
			{
				var component = game.Components.GetOrCreate<VoxelWorldComponent>();
				brightness = component.SkyBrightness;
			}

			for ( int i = 0; i < RenderLayers.Count; i++ )
			{
				var layer = RenderLayers[i];

				layer.SetBrightness( brightness );
				layer.SetOpacity( World.GlobalOpacity );
			}

			var viewer = Game.LocalClient.GetChunkViewer();

			if ( viewer.IsValid() )
			{
				viewer.AddLoadedChunk( Offset );
			}
		}

		[Event.Tick]
		private void Tick()
		{
			UpdateShapeDeleteQueue();

			var statesToTick = BlockStates.Where( kv =>
			{
				var state = kv.Value;
				return state.IsValid() && state.ShouldTick && state.LastTickTime >= state.TickRate;
			} );

			foreach ( var kv in statesToTick )
			{
				kv.Value.Tick();
			}

			if ( IsFullUpdateEventPending )
			{
				IsFullUpdateEventPending = false;
				OnFullUpdate?.Invoke();
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

		private void StartThreadedInitializeTask()
		{
			if ( Game.IsServer )
			{
				StartGeneratorTask();
			}
		}
	}
}
