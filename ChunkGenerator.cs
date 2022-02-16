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
		protected Map Map { get; private set; }

		public void Setup( Map map, Chunk chunk )
		{
			Map = map;
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
