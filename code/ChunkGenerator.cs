using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Facepunch.Voxels
{
	public class ChunkGenerator
	{
		protected Chunk Chunk { get; private set; }
		protected VoxelWorld VoxelWorld { get; private set; }

		public void Setup( VoxelWorld world, Chunk chunk )
		{
			VoxelWorld = world;
			Chunk = chunk;
		}

		public virtual void Initialize()
		{

		}

		public virtual void Generate()
		{

		}
	}
}
