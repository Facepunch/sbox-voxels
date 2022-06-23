using Sandbox;

namespace Facepunch.Voxels
{
	[Library]
	public class ModelBlockEntity : BlockEntity
	{
		public override void Initialize()
		{
			var block = BlockType as AssetBlock;

			if ( block.IsValid() )
			{
				SetModel( block.ModelOverride );
				SetupPhysicsFromModel( PhysicsMotionType.Keyframed );

				if ( block.ModelFacesDirection )
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
			}

			base.Initialize();
		}
	}
}
