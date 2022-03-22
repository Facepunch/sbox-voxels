using System.IO;

namespace Facepunch.CoreWars.Voxels
{
	public interface ISourceEntity
	{
		string Name { get; set; }
		void Serialize( BinaryWriter writer );
		void Deserialize( BinaryReader reader );
	}
}

