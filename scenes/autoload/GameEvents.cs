using Game.Component;
using Game.Resources.Building;
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
	[Signal]
	public delegate void BuildingStuckEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void BuildingUnStuckEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void RobotSelectedEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void NoMoreRobotSelectedEventHandler(BuildingComponent buildingComponent);
	[Signal]
	public delegate void PlaceBridgeButtonPressedEventHandler(BuildingComponent buildingComponent, BuildingResource buildingResource);
	[Signal]
	public delegate void AllRobotStoppedEventHandler();
	[Signal]
	public delegate void CarriedResourceCountChangedEventHandler(int carriedResourceCount);

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

	public static void EmitRobotSelected(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.RobotSelected, buildingComponent);
	}

	public static void EmitNoMoreRobotSelected(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.NoMoreRobotSelected, buildingComponent);
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

	public static void EmitBuildingStuck(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.BuildingStuck, buildingComponent);
	}

	public static void EmitBuildingUnStuck(BuildingComponent buildingComponent)
	{
		Instance.EmitSignal(SignalName.BuildingUnStuck, buildingComponent);
	}

	public static void EmitAllRobotStop()
	{
		Instance.EmitSignal(SignalName.AllRobotStopped);
	}

	public static void EmitCarriedResourceCountChanged(int carriedResourceCount)
	{
		Instance.EmitSignal(SignalName.CarriedResourceCountChanged, carriedResourceCount);
	}

	public static void EmitPlaceBridgeButtonPressed(BuildingComponent buildingComponent, BuildingResource buildingResource)
	{
		Instance.EmitSignal(SignalName.PlaceBridgeButtonPressed, buildingComponent, buildingResource);
	}
}
