using Sandbox;

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

				BlockId = block.BlockId;
			}

			base.Initialize();
		}

		public override void ClientSpawn()
		{
			BlockType = VoxelWorld.Current.GetBlockType( BlockId );
			base.ClientSpawn();
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
	}
}
