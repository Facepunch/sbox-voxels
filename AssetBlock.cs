using Facepunch.Voxels;
using Sandbox;

namespace Facepunch.Voxels
{
	public class AssetBlock : BlockType
	{
		public BlockResource Resource { get; private set; }

		public override string FriendlyName => Resource.FriendlyName;
		public override string Description => Resource.Description;
		public override string DefaultTexture => Resource.Textures.Default;
		public override bool HideMesh => !string.IsNullOrEmpty( ModelOverride.ModelName );
		public override bool IsTranslucent => HideMesh || Resource.IsTranslucent;
		public override bool UseTransparency => Resource.UseTransparency;
		public override bool IsPassable => Resource.IsPassable;
		public override bool AttenuatesSunLight => Resource.AttenuatesSunLight;
		public override string[] DetailModels => Resource.DetailModels;
		public override float DetailSpawnChance => Resource.DetailSpawnChance;
		public override float DetailScale => Resource.DetailScale;
		public override IntVector3 LightLevel => Resource.LightLevel;
		public override Vector3 LightFilter => Resource.LightFilter;
		public override string FootLeftSound => Resource.Sounds.FootLeft;
		public override string FootRightSound => Resource.Sounds.FootRight;
		public override string FootLaunchSound => Resource.Sounds.FootLaunch;
		public override string FootLandSound => Resource.Sounds.FootLand;
		public override string ImpactSound => Resource.Sounds.Impact;
		public override int MinHueShift => Resource.MinHueShift;
		public override int MaxHueShift => Resource.MaxHueShift;
		public override Color TintColor => Resource.TintColor;
		public override string Icon => GetIcon();
		public override string ServerEntity => GetServerEntity();
		public virtual BlockModelOverride ModelOverride => Resource.ModelOverride;

		public void SetResource( BlockResource resource )
		{
			Resource = resource;
		}

		public override string[] GetUniqueAliases()
		{
			return Resource.Aliases;
		}

		public override string GetUniqueName()
		{
			return Resource.ResourceName;
		}

		public override BlockState CreateState() => new BlockState();

		public override void OnBlockAdded( Chunk chunk, IntVector3 position, int direction )
		{
			if ( !string.IsNullOrEmpty( ModelOverride.ModelName ) )
			{
				var state = World.GetState<BlockState>( position );

				if ( !state.IsValid() )
				{
					state = World.GetOrCreateState<BlockState>( position );
					state.Direction = (BlockFace)direction;
					state.IsDirty = true;
				}
			}

			base.OnBlockAdded( chunk, position, direction );
		}

		public override byte GetTextureId( BlockFace face, Chunk chunk, int x, int y, int z )
		{
			if ( Resource.FaceToTexture.TryGetValue( face, out var texture ) )
			{
				return World.BlockAtlas.GetTextureId( texture );
			}

			return base.GetTextureId( face, chunk, x, y, z );
		}

		private string GetIcon()
		{
			if ( !string.IsNullOrEmpty( Resource.Icon ) )
			{
				return Resource.Icon.Replace( ".jpg", ".png" );
			}

			return null;
		}

		private string GetServerEntity()
		{
			if ( !string.IsNullOrEmpty( ModelOverride.ModelName ) )
			{
				return typeof( ModelBlockEntity ).Name;
			}

			return null;
		}
	}
}
