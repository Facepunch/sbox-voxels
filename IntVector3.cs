using System;
using System.IO;

namespace Facepunch.Voxels
{
	public struct IntVector3 : IEquatable<IntVector3>
	{
		public int x;
		public int y;
		public int z;

		public IntVector3( int x, int y, int z )
		{
			this.x = x;
			this.y = y;
			this.z = z;
		}

		public IntVector3( int all = 0 ) : this( all, all, all )
		{
		}

		public static IntVector3 operator +( IntVector3 c1, IntVector3 c2 )
		{
			return new IntVector3( c1.x + c2.x, c1.y + c2.y, c1.z + c2.z );
		}

		public static IntVector3 operator -( IntVector3 c1, IntVector3 c2 )
		{
			return new IntVector3( c1.x - c2.x, c1.y - c2.y, c1.z - c2.z );
		}

		public static IntVector3 operator +( IntVector3 c1, int c2 )
		{
			return new IntVector3( c1.x * c2, c1.y * c2, c1.z * c2 );
		}

		public static IntVector3 operator *( IntVector3 c1, int c2 )
		{
			return new IntVector3( c1.x * c2, c1.y * c2, c1.z * c2 );
		}

		public static Vector3 operator *( IntVector3 c1, float c2 )
		{
			return new Vector3( c1.x * c2, c1.y * c2, c1.z * c2 );
		}

		static public implicit operator IntVector3( int value )
		{
			return new IntVector3( value, value, value );
		}

		public int this[int index]
		{
			get
			{
				return index switch
				{
					0 => x,
					1 => y,
					2 => z,
					_ => throw new IndexOutOfRangeException(),
				};
			}

			set
			{
				switch ( index )
				{
					case 0: x = value; break;
					case 1: y = value; break;
					case 2: z = value; break;
				}
			}
		}

		public readonly int Length => (int)Math.Sqrt( x * x + y * y + z * z );

		public readonly int LengthSquared => x * x + y * y + z * z;

		public override string ToString()
		{
			return $"{x},{y},{z}";
		}

		public void Write( BinaryWriter writer )
		{
			writer.Write( x );
			writer.Write( y );
			writer.Write( z );
		}

		public readonly IntVector3 Read( BinaryReader reader )
		{
			return new IntVector3( reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() );
		}

		public readonly IntVector3 ComponentMin( IntVector3 other )
		{
			return new IntVector3( Math.Min( x, other.x ), Math.Min( y, other.y ), Math.Min( z, other.z ) );
		}

		public readonly IntVector3 ComponentMax( IntVector3 other )
		{
			return new IntVector3( Math.Max( x, other.x ), Math.Max( y, other.y ), Math.Max( z, other.z ) );
		}

		public int Distance( IntVector3 other )
		{
			var dx = other.x - x;
			var dy = other.y - y;
			var dz = other.z - z;
			return (int)Math.Sqrt( dx * dx + dy * dy + dz * dz );
		}

		public static IntVector3 Min( IntVector3 a, IntVector3 b ) => a.ComponentMin( b );
		public static IntVector3 Max( IntVector3 a, IntVector3 b ) => a.ComponentMax( b );

		public readonly IntVector3 WithX( int x ) => new( x, y, z );
		public readonly IntVector3 WithY( int y ) => new( x, y, z );
		public readonly IntVector3 WithZ( int z ) => new( x, y, z );

		public static readonly IntVector3 One = new( 1 );
		public static readonly IntVector3 Zero = new( 0 );

		public static readonly IntVector3 Forward = new( 1, 0, 0 );
		public static readonly IntVector3 Backward = new( -1, 0, 0 );
		public static readonly IntVector3 Up = new( 0, 0, 1 );
		public static readonly IntVector3 Down = new( 0, 0, -1 );
		public static readonly IntVector3 Right = new( 0, -1, 0 );
		public static readonly IntVector3 Left = new( 0, 1, 0 );

		public static readonly IntVector3 OneX = new( 1, 0, 0 );
		public static readonly IntVector3 OneY = new( 0, 1, 0 );
		public static readonly IntVector3 OneZ = new( 0, 0, 1 );

		#region Equality
		public static bool operator ==( IntVector3 left, IntVector3 right ) => left.Equals( right );
		public static bool operator !=( IntVector3 left, IntVector3 right ) => !(left == right);
		public override bool Equals( object obj ) => obj is IntVector3 o && Equals( o );
		public bool Equals( IntVector3 o ) => x == o.x && y == o.y && z == o.z;
		public override int GetHashCode() => HashCode.Combine( x, y, z );
		#endregion
	}
}
