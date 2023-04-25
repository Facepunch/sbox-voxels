﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Facepunch.Voxels
{
	public class BiomeSampler
	{
		private FastNoiseLite[] Noises { get; set; }
		private int SampleCount { get; set; } = 3;
		private VoxelWorld VoxelWorld { get; set; }

		public BiomeSampler( VoxelWorld world )
		{
			Noises = new FastNoiseLite[SampleCount];

			for ( var i = 0; i < SampleCount; i++)
			{
				Noises[i] = new FastNoiseLite( world.Seed + i );
				Noises[i].SetNoiseType( FastNoiseLite.NoiseType.OpenSimplex2 );
				Noises[i].SetFractalType( FastNoiseLite.FractalType.FBm );
				Noises[i].SetFractalOctaves( 5 );
				Noises[i].SetFrequency( 1 / 800.0f );
			}

			VoxelWorld = world;
		}

		public Biome GetBiomeAt( int x, int y )
		{
			var currentBiome = (Biome)null;
			var currentDeviation = float.PositiveInfinity;

			for ( var b = 0; b < VoxelWorld.Biomes.Count; b++ )
			{
				var biome = VoxelWorld.Biomes[b];

				float deviation = 0f;

				for ( int i = 0; i < SampleCount; i++ )
				{
					var dp = Noises[i].GetNoise( x, y ) - biome.Parameters[i];
					deviation += dp * dp;
				}

				if ( deviation < currentDeviation )
				{
					currentDeviation = deviation;
					currentBiome = biome;
				}
			}

			return currentBiome;
		}
	}
}
