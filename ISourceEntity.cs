using System.IO;
using Sandbox;

namespace Facepunch.Voxels
{
	public interface ISourceEntity
	{
		string Name { get; }
		Vector3 Position { get; set; }
		Rotation Rotation { get; set; }
		void Serialize( BinaryWriter writer );
		void Deserialize( BinaryReader reader );
	}
}

