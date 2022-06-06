using Sandbox;
using SandboxEditor;
using System.Linq;

namespace Facepunch.Voxels
{
	[HammerEntity]
	[Title( "Day Cycle Controller" )]
	public partial class DayCycleController : Entity
	{
		[Net] public float Brightness { get; set; } = 1f;
		[Net] public float TimeOfDay { get; set; } = 12f;
		[Net] public float Speed { get; set; } = 0.02f;

		public Color DawnColor { get; set; }
		public Color DawnSkyColor { get; set; }
		public Color DayColor { get; set; }
		public Color DaySkyColor { get; set; }
		public Color DuskColor { get; set; }
		public Color DuskSkyColor { get; set; }
		public Color NightColor { get; set; }
		public Color NightSkyColor { get; set; }

		public DayCycleController()
		{
			Transmit = TransmitType.Always;
		}

		public EnvironmentLightEntity Environment
		{
			get
			{
				if ( CachedEnvironment == null )
					CachedEnvironment = All.OfType<EnvironmentLightEntity>().FirstOrDefault();

				return CachedEnvironment;
			}
		}

		private EnvironmentLightEntity CachedEnvironment;
		private DayCycleGradient BrightnessGradient;
		private DayCycleGradient SkyColorGradient;
		private DayCycleGradient ColorGradient;

		public void Initialize()
		{
			ColorGradient = new DayCycleGradient( DawnColor, DayColor, DuskColor, NightColor );
			SkyColorGradient = new DayCycleGradient( DawnSkyColor, DaySkyColor, DuskSkyColor, NightSkyColor );
			BrightnessGradient = new DayCycleGradient( Color.White.WithAlpha( 0.5f ), Color.White.WithAlpha( 1f ), Color.White.WithAlpha( 0.5f ), Color.White.WithAlpha( 0.15f ) );
		}

		[Event.Tick.Server]
		private void ServerTick()
		{
			TimeOfDay += Speed * Time.Delta;
			if ( TimeOfDay >= 24f ) TimeOfDay = 0f;

			var environment = Environment;
			if ( environment == null ) return;

			var sunAngle = ((TimeOfDay / 24f) * 360f);
			var radius = 20000f;

			environment.Color = ColorGradient.Evaluate( (1f / 24f) * TimeOfDay );
			environment.SkyColor = SkyColorGradient.Evaluate( (1f / 24f) * TimeOfDay );

			environment.Position = Vector3.Zero + Rotation.From( 0, 0, sunAngle + 60f ) * (radius * Vector3.Right);
			environment.Position += Rotation.From( 0, sunAngle, 0 ) * (radius * Vector3.Forward);

			var direction = (Vector3.Zero - environment.Position).Normal;
			environment.Rotation = Rotation.LookAt( direction, Vector3.Up );

			Brightness = BrightnessGradient.Evaluate( (1f / 24f) * TimeOfDay ).a;
		}
	}
}
