using Sandbox;
using System;

namespace Facepunch.Voxels
{
	public struct BlockChangeSignal : IEquatable<BlockChangeSignal>
	{
		public IntVector3 Position;
		public byte OldBlockId;
		public byte NewBlockId;

		#region Equality
		public static bool operator ==( BlockChangeSignal left, BlockChangeSignal right ) => left.Equals( right );
		public static bool operator !=( BlockChangeSignal left, BlockChangeSignal right ) => !(left == right);
		public override bool Equals( object obj ) => obj is BlockChangeSignal o && Equals( o );
		public bool Equals( BlockChangeSignal o ) => Position == o.Position;
		public override int GetHashCode() => HashCode.Combine( Position );
		#endregion
	}
}
