using Sandbox;
using System;

namespace Facepunch.Voxels
{
	public struct QueuedTick : IEquatable<QueuedTick>
	{
		public IntVector3 Position;
		public TimeUntil Delay;
		public byte BlockId;

		#region Equality
		public static bool operator ==( QueuedTick left, QueuedTick right ) => left.Equals( right );
		public static bool operator !=( QueuedTick left, QueuedTick right ) => !(left == right);
		public override bool Equals( object obj ) => obj is QueuedTick o && Equals( o );
		public bool Equals( QueuedTick o ) => Position == o.Position && BlockId == o.BlockId;
		public override int GetHashCode() => HashCode.Combine( Position, BlockId );
		#endregion
	}
}
