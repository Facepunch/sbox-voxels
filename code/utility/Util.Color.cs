using Sandbox;

namespace Facepunch.Voxels
{
	public static partial class Util
	{
		public static uint ColorToInt( Color color )
		{
			var red = (color.r * 255f).FloorToInt();
			var green = (color.g * 255f).FloorToInt();
			var blue = (color.b * 255f).FloorToInt();
			return (uint)(((red & 0x0ff) << 16) | ((green & 0x0ff) << 8) | (blue & 0x0ff));
		}
	}
}
