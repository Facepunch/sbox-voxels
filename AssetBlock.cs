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
		public override bool IsTranslucent => Resource.IsTranslucent;
		public override bool UseTransparency => Resource.UseTransparency;
		public override bool HasTexture => true;
		public override bool IsPassable => Resource.IsPassable;
		public override bool AttenuatesSunLight => Resource.AttenuatesSunLight;
		public override string[] DetailModels => Resource.DetailModels;
		public override float DetailSpawnChance => Resource.DetailSpawnChance;
		public override float DetailScale => Resource.DetailScale;
		public override IntVector3 LightLevel => Resource.LightLevel;
		public override Vector3 LightFilter => Resource.LightFilter;
		public override string Icon => Resource.Icon;

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

		public override byte GetTextureId( BlockFace face, Chunk chunk, int x, int y, int z )
		{
			if ( Resource.FaceToTexture.TryGetValue( face, out var texture ) )
			{
				return World.BlockAtlas.GetTextureId( texture );
			}

			return base.GetTextureId( face, chunk, x, y, z );
		}
	}
}
