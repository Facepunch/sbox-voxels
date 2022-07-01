using Sandbox;

namespace Facepunch.Voxels
{
	[Library]
	public class PerlinChunkGenerator : ChunkGenerator
	{
		private int[] Heightmap;
		private FastNoiseLite Noise1;
		private FastNoiseLite Noise2;
		private FastNoiseLite Noise3;
		private FastNoiseLite Noise4;

		public override void Initialize()
		{
			Heightmap = new int[Chunk.SizeX * Chunk.SizeY];

			Noise1 = new FastNoiseLite( VoxelWorld.Seed );
			Noise1.SetNoiseType( FastNoiseLite.NoiseType.OpenSimplex2 );
			Noise1.SetFractalType( FastNoiseLite.FractalType.FBm );
			Noise1.SetFractalOctaves( 4 );
			Noise1.SetFrequency( 1 / 256.0f );

			Noise2 = new FastNoiseLite( VoxelWorld.Seed );
			Noise2.SetNoiseType( FastNoiseLite.NoiseType.OpenSimplex2 );
			Noise2.SetFractalType( FastNoiseLite.FractalType.FBm );
			Noise2.SetFractalOctaves( 4 );
			Noise2.SetFrequency( 1 / 256.0f );

			Noise3 = new FastNoiseLite( VoxelWorld.Seed );
			Noise3.SetNoiseType( FastNoiseLite.NoiseType.OpenSimplex2 );
			Noise3.SetFractalType( FastNoiseLite.FractalType.FBm );
			Noise3.SetFractalOctaves( 4 );
			Noise3.SetFrequency( 1 / 256.0f );

			Noise4 = new FastNoiseLite( VoxelWorld.Seed );
			Noise4.SetNoiseType( FastNoiseLite.NoiseType.OpenSimplex2 );
			Noise4.SetFrequency( 1 / 1024.0f );

			GenerateHeightmap();
		}

		public void GenerateHeightmap()
		{
			var offset = Chunk.Offset;

			for ( int y = 0; y < Chunk.SizeY; y++ )
			{
				for ( int x = 0; x < Chunk.SizeX; x++ )
				{
					var n1 = Noise1.GetNoise( x + offset.x, y + offset.y );
					var n2 = Noise2.GetNoise( x + offset.x, y + offset.y );
					var n3 = Noise3.GetNoise( x + offset.x, y + offset.y );
					var n4 = Noise4.GetNoise( x + offset.x, y + offset.y );
					Heightmap[x + y * Chunk.SizeX] = (int)((n1 + (n2 * n3 * (n4 * 2 - 1))) * 64 + 64);
				}
			}
		}

		public override void Generate()
		{
			var offset = Chunk.Offset;

			Rand.SetSeed( offset.x + offset.y + offset.z * Chunk.SizeZ + VoxelWorld.Seed );

			var topChunk = Chunk.GetNeighbour( BlockFace.Top );

			for ( var x = 0; x < Chunk.SizeX; x++ )
			{
				for ( var y = 0; y < Chunk.SizeY; y++ )
				{
					var biome = VoxelWorld.GetBiomeAt( x + offset.x, y + offset.y );
					var h = GetHeight( x, y );

					for ( var z = 0; z < Chunk.SizeZ; z++ )
					{
						var index = Chunk.GetLocalPositionIndex( x, y, z );
						var position = new IntVector3( x, y, z );

						if ( z + offset.z > h )
						{
							if ( z + offset.z < VoxelWorld.SeaLevel )
								Chunk.CreateBlockAtPosition( position, biome.LiquidBlockId );
							else if ( Chunk.Blocks[index] == 0 && z == Chunk.SizeZ - 1 )
								Chunk.LightMap.AddSunLight( position, 15 );
						}
						else
						{
							var isGeneratingTopBlock = z + offset.z == h && z + offset.z > VoxelWorld.SeaLevel - 1;

							if ( isGeneratingTopBlock )
								Chunk.CreateBlockAtPosition( position, biome.TopBlockId );
							else if ( z + offset.z <= VoxelWorld.SeaLevel - 1 && h < VoxelWorld.SeaLevel && z + offset.z > h - 3 )
								Chunk.CreateBlockAtPosition( position, biome.BeachBlockId );
							else if ( z + offset.z > h - 3 )
								Chunk.CreateBlockAtPosition( position, biome.GroundBlockId );
							else
								Chunk.CreateBlockAtPosition( position, biome.UndergroundBlockId );

							GenerateCaves( biome, x, y, z );

							if ( isGeneratingTopBlock && Chunk.Blocks[index] > 0 )
							{
								if ( Rand.Float() < 0.01f )
								{
									GenerateTree( biome, position.x, position.y, position.z );
								}
								else if ( VoxelWorld.Spawnpoints.Count == 0 || Rand.Float() <= 0.1f )
								{
									var spawnPositionSource = VoxelWorld.ToSourcePositionCenter( offset + position + new IntVector3( 0, 0, 1 ) );
									VoxelWorld.SpawnpointsQueue.Enqueue( spawnPositionSource );
								}
							}
						}

						if ( topChunk.IsValid() )
						{
							var sunlightLevel = topChunk.LightMap.GetSunLight( new IntVector3( x, y, 0 ) );

							if ( sunlightLevel > 0 )
								Chunk.LightMap.AddSunLight( new IntVector3( x, y, Chunk.SizeZ - 1 ), sunlightLevel );
						}
					}
				}
			}
		}

		public bool GenerateCaves( Biome biome, int x, int y, int z )
		{
			var localPosition = new IntVector3( x, y, z );
			var offset = Chunk.Offset;

			if ( !Chunk.IsInside( localPosition ) ) return false;

			var position = offset + new IntVector3( x, y, z );
			int rx = localPosition.x + offset.x;
			int ry = localPosition.y + offset.y;
			int rz = localPosition.z + offset.z;

			double n1 = VoxelWorld.CaveNoise.GetNoise( rx, ry, rz );
			double n2 = VoxelWorld.CaveNoise.GetNoise( rx, ry + 88f, rz );
			double finalNoise = n1 * n1 + n2 * n2;

			if ( finalNoise < 0.02f )
			{
				Chunk.CreateBlockAtPosition( position, 0 );
				return true;
			}

			return false;
		}

		public void GenerateTree( Biome biome, int x, int y, int z )
		{
			var minTrunkHeight = 3;
			var maxTrunkHeight = 6;
			var minLeavesRadius = 1;
			var maxLeavesRadius = 2;
			int trunkHeight = Rand.Int( minTrunkHeight, maxTrunkHeight );
			int trunkTop = z + trunkHeight;
			int leavesRadius = Rand.Int( minLeavesRadius, maxLeavesRadius );

			// Would we be trying to generate a tree in another chunk?
			if ( z + trunkHeight + leavesRadius >= Chunk.SizeZ
				|| x <= leavesRadius || x >= Chunk.SizeX - leavesRadius
				|| y <= leavesRadius || y >= Chunk.SizeY - leavesRadius )
			{
				return;
			}

			for ( int trunkZ = z + 1; trunkZ < trunkTop; trunkZ++ )
			{
				if ( Chunk.IsInside( x, y, trunkZ ) )
				{
					Chunk.CreateBlockAtPosition( new IntVector3( x, y, trunkZ ), biome.TreeLogBlockId );
				}
			}

			for ( int leavesX = x - leavesRadius; leavesX <= x + leavesRadius; leavesX++ )
			{
				for ( int leavesY = y - leavesRadius; leavesY <= y + leavesRadius; leavesY++ )
				{
					for ( int leavesZ = trunkTop; leavesZ <= trunkTop + leavesRadius; leavesZ++ )
					{
						if ( Chunk.IsInside( leavesX, leavesY, leavesZ ) )
						{
							if (
								Chunk.IsEmpty( leavesX, leavesY, leavesZ ) &&
								(leavesX != x - leavesRadius || leavesY != y - leavesRadius) &&
								(leavesX != x + leavesRadius || leavesY != y + leavesRadius) &&
								(leavesX != x + leavesRadius || leavesY != y - leavesRadius) &&
								(leavesX != x - leavesRadius || leavesY != y + leavesRadius)
							)
							{
								var position = new IntVector3( leavesX, leavesY, leavesZ );
								Chunk.CreateBlockAtPosition( position, biome.TreeLeafBlockId );
							}
						}
					}
				}
			}

			for ( int leavesX = x - (leavesRadius - 1); leavesX <= x + (leavesRadius - 1); leavesX++ )
			{
				for ( int leavesY = y - (leavesRadius - 1); leavesY <= y + (leavesRadius - 1); leavesY++ )
				{
					var position = new IntVector3( leavesX, leavesY, trunkTop + leavesRadius + 1 );

					if ( VoxelWorld.IsEmpty( position ) )
					{
						Chunk.CreateBlockAtPosition( position, biome.TreeLeafBlockId );
					}
				}
			}
		}

		public int GetHeight( int x, int y )
		{
			return Heightmap[x + y * Chunk.SizeX];
		}

		public void SetHeight( int x, int y, int height )
		{
			Heightmap[x + y * Chunk.SizeX] = height;
		}
	}
}
