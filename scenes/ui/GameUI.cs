using Game.Autoload;
using Game.Component;
using Game.Manager;
using Game.Resources.Building;
using Godot;

namespace Game.UI;

public partial class GameUI : CanvasLayer
{
	[Signal]
	public delegate void BuildingResourceSelectedEventHandler(BuildingResource buildingResource);

	private VBoxContainer buildingSectionContainer;
	private Label resourceLabel;
	private Button stopRobotButton;
	private Button displayAnomalyMapButton;
	private readonly StringName ACTION_SPACEBAR = "spacebar";

	[Export]
	private GravitationalAnomalyMap gravitationalAnomalyMap;
	[Export]
	private BuildingManager buildingManager;
	[Export]
	private BuildingResource[] buildingResources;
	[Export]
	private PackedScene buildingSectionScene;

	public override void _Ready()
	{
		buildingSectionContainer = GetNode<VBoxContainer>("%BuildingSectionContainer");
		resourceLabel = GetNode<Label>("%ResourceLabel");
		stopRobotButton = GetNode<Button>("%StopRobotButton");
		displayAnomalyMapButton = GetNode<Button>("%DisplayAnomalyMapButton");
		CreateBuildingSections();

		stopRobotButton.Pressed += OnStopRobotButtonPressed;
		displayAnomalyMapButton.Pressed += OnDisplayAnomalyMapButtonPressed;
		buildingManager.AvailableResourceCountChanged += OnAvailableResourceCountChanged;
	}

		public override void _UnhandledInput(InputEvent evt)
	{
		if(evt.IsActionPressed(ACTION_SPACEBAR))
		{
			GetViewport().SetInputAsHandled();
			return;
		}
	}

	public void HideUI()
	{
		Visible = false;
	}

	private void CreateBuildingSections()
	{
		foreach (var buildingResource in buildingResources)
		{
			var buildingSection = buildingSectionScene.Instantiate<BuildingSection>();
			buildingSectionContainer.AddChild(buildingSection);
			buildingSection.SetBuildingResource(buildingResource);

			buildingSection.SelectButtonPressed += () =>
			{
				EmitSignal(SignalName.BuildingResourceSelected, buildingResource);
			};
		}
	}

	private void OnStopRobotButtonPressed()
	{
		var allRobots = BuildingComponent.GetValidBuildingComponents(this);
		foreach(var robot in allRobots)
		{
			robot.StopAnyAutomatedMovementMode();
		}
		GameEvents.EmitAllRobotStop();
	}

	private void OnDisplayAnomalyMapButtonPressed()
	{
		gravitationalAnomalyMap.DisplayAnomalyMap();
	}

	private void OnAvailableResourceCountChanged(int availableResourceCount)
	{
		resourceLabel.Text = availableResourceCount.ToString();
	}
}
