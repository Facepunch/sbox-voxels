namespace Facepunch.Voxels
{
	public class Biome
	{
		public virtual string Name => "";

		public byte Id { get; set; }
		public Map Map { get; set; }
		public byte TopBlockId { get; protected set; }
		public byte BeachBlockId { get; protected set; }
		public byte GroundBlockId { get; protected set; }
		public byte LiquidBlockId { get; protected set; }
		public byte TreeLogBlockId { get; protected set; }
		public byte TreeLeafBlockId { get; protected set; }
		public byte UndergroundBlockId { get; protected set; }
		public float[] Parameters { get; private set; }

		public virtual void Initialize()
		{
			
		}

		protected void SetTemperature( float temperature )
		{
			Parameters[0] = temperature;
		}

		protected void SetPrecipitation( float precipitation )
		{
			Parameters[1] = precipitation;
		}

		protected void SetWeighting( float weighting )
		{
			Parameters[2] = weighting;
		}

		public Biome()
		{
			Parameters = new float[3];
		}
	}
}
