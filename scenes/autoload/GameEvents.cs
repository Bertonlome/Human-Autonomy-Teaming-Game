using Game.Component;
using Godot;

namespace Game.Autoload;

public partial class GameEvents : Node
{
	public static GameEvents Instance { get; private set; }

	[Signal]
	public delegate void BuildingPlacedEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void BuildingDestroyedEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void BuildingDisabledEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void BuildingEnabledEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void BuildingMovedEventHandler(BuildingComponent buildingComponent);

	public override void _Notification(int what)
	{
		if (what == NotificationSceneInstantiated)
		{
			Instance = this;
		}
	}

	public static void EmitBuildingPlaced(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.BuildingPlaced, buildingComponent);
	}

	public static void EmitBuildingMoved(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.BuildingMoved, buildingComponent);
	}

	public static void EmitBuildingDestroyed(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.BuildingDestroyed, buildingComponent);
	}

	public static void EmitBuildingDisabled(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.BuildingDisabled, buildingComponent);
	}

	public static void EmitBuildingEnabled(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.BuildingEnabled, buildingComponent);
	}
}
