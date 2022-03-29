using System.IO;
using Sandbox;

namespace Facepunch.Voxels
{
	public interface ISourceEntity : IValid
	{
		string Name { get; }
		void Delete();
		Vector3 Position { get; set; }
		Rotation Rotation { get; set; }
		Transform Transform { get; set; }
		void Serialize( BinaryWriter writer );
		void Deserialize( BinaryReader reader );
	}
}

