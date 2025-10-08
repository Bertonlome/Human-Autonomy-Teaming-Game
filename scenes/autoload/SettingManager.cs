using Game.Component;
using Godot;


namespace Game.Autoload;

public partial class SettingManager : Node
{
	public static SettingManager Instance { get; private set; }

	[Signal]
	public delegate void TrackingRobotEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void StopTrackingRobotEventHandler();
	public bool IsTrackingRobot { get; set; } = false;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		RenderingServer.SetDefaultClearColor(new Color("47aba9"));
		GetViewport().GetWindow().MinSize = new Vector2I(1280, 720);
	}

		public override void _Notification(int what)
	{
		if (what == NotificationSceneInstantiated)
		{
			Instance = this;
		}
	}

	public static void EmitTrackingRobot(BuildingComponent buildingComponent)
	{
		Instance.IsTrackingRobot = true;
		Instance.EmitSignal(SignalName.TrackingRobot, buildingComponent);
	}

	public static void EmitStopTrackingRobot()
	{
		Instance.IsTrackingRobot = false;
		Instance.EmitSignal(SignalName.StopTrackingRobot);
	}
}
