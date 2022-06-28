using Sandbox;
using System;

namespace Facepunch.Voxels
{
	[Library]
	public partial class ModelBlockEntity : BlockEntity
	{
		[Net] public byte BlockId { get; set; }

		public override void Initialize()
		{
			var block = BlockType as AssetBlock;

			if ( block.IsValid() )
			{
				SetModel( block.ModelOverride.ModelName );
				SetupPhysicsFromModel( PhysicsMotionType.Keyframed );

				if ( block.ModelOverride.FaceDirection )
				{
					var state = World.GetState<BlockState>( BlockPosition );

					if ( state.IsValid() )
					{
						if ( state.Direction == BlockFace.North )
							Rotation = Rotation.FromAxis( Vector3.Up, 0f );
						else if ( state.Direction == BlockFace.South )
							Rotation = Rotation.FromAxis( Vector3.Up, 180f );
						else if ( state.Direction == BlockFace.East )
							Rotation = Rotation.FromAxis( Vector3.Up, 90f );
						else if ( state.Direction == BlockFace.West )
							Rotation = Rotation.FromAxis( Vector3.Up, 270f );
					}
				}

				RenderColor = block.TintColor;
				BlockId = block.BlockId;
			}

			base.Initialize();
		}

		public override void ClientSpawn()
		{
			base.ClientSpawn();
			BlockType = VoxelWorld.Current.GetBlockType( BlockId );
		}

		public override void OnNewModel( Model model )
		{
			if ( IsClient )
			{
				var block = BlockType as AssetBlock;

				if ( block.IsValid() && !string.IsNullOrEmpty( block.ModelOverride.MaterialName ) )
				{
					SetMaterialOverride( block.ModelOverride.MaterialName );
				}
			}

			base.OnNewModel( model );
		}

		[Event.Tick.Client]
		protected virtual void ClientTick()
		{
			if ( SceneObject.IsValid() && Chunk.IsValid() )
			{
				var r = Chunk.LightMap.GetRedTorchLight( LocalBlockPosition );
				var g = Chunk.LightMap.GetGreenTorchLight( LocalBlockPosition );
				var b = Chunk.LightMap.GetBlueTorchLight( LocalBlockPosition );
				var s = Chunk.LightMap.GetSunLight( LocalBlockPosition );
				SceneObject.Attributes.Set( "VoxelLight", new Vector4( r, g, b, s ) );
				SceneObject.Attributes.Set( "TintColor", BlockType.TintColor );

				var chunkIndex = Chunk.Offset.x * Chunk.SizeY * Chunk.SizeZ + Chunk.Offset.y * Chunk.SizeZ + Chunk.Offset.z;
				var random = new Random( chunkIndex );
				var hueShift = (byte)random.Int( Math.Clamp( BlockType.MinHueShift, 0, 64 ), Math.Clamp( BlockType.MaxHueShift, 0, 64 ) );
				SceneObject.Attributes.Set( "HueShift", hueShift );
			}
		}
	}
}
