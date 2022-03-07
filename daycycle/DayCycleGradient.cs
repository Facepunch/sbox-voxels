namespace Facepunch.Voxels
{
	public class DayCycleGradient
	{
		private struct GradientNode
		{
			public Color Color;
			public float Time;

			public GradientNode( Color color, float time )
			{
				Color = color;
				Time = time;
			}
		}

		private GradientNode[] Nodes;

		public DayCycleGradient( Color dawnColor, Color dayColor, Color duskColor, Color nightColor )
		{
			Nodes = new GradientNode[7];
			Nodes[0] = new GradientNode( nightColor, 0f );
			Nodes[1] = new GradientNode( nightColor, 0.2f );
			Nodes[2] = new GradientNode( dawnColor, 0.3f );
			Nodes[3] = new GradientNode( dayColor, 0.5f );
			Nodes[4] = new GradientNode( dayColor, 0.7f );
			Nodes[5] = new GradientNode( duskColor, 0.85f );
			Nodes[6] = new GradientNode( nightColor, 1f );
		}

		public Color Evaluate( float fraction )
		{
			for ( var i = 0; i < Nodes.Length; i++ )
			{
				var node = Nodes[i];
				var nextIndex = i + 1;

				if ( Nodes.Length <= nextIndex )
					nextIndex = 0;

				var nextNode = Nodes[nextIndex];

				if ( fraction >= node.Time && fraction <= nextNode.Time )
				{
					var duration = (nextNode.Time - node.Time);
					var interpolate = (1f / duration) * (fraction - node.Time);

					return Color.Lerp( node.Color, nextNode.Color, interpolate );
				}
			}

			return Nodes[0].Color;
		}
	}
}
