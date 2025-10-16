using System;
using System.Collections.Generic;
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
	[Signal]
	public delegate void StopRobotButtonPressedEventHandler(Node2D robot);
	[Signal]
	public delegate void SelectRobotButtonPressedEventHandler(Node2D robot);
	[Signal]
	public delegate void TimeIsUpEventHandler();
	private bool isTimeIsUp = false;
	public int TimeToCompleteLevel;

	private VBoxContainer buildingSectionContainer;
	private VBoxContainer unitsSectionContainer;
	private Label resourceLabel;
	private Label materialLabel;
	private Label mineralLabel;
	private Label timeLeftLabel;
	private Button stopRobotButton;
	private Button displayAnomalyMapButton;
	private CheckButton displayTraceButton;
	private bool isTraceActive = false;
	private readonly StringName ACTION_SPACEBAR = "spacebar";
	private HashSet<Vector2I> _previouslyDiscoveredTiles = new(); // Track to calculate delta

	[Export]
	private GravitationalAnomalyMap gravitationalAnomalyMap;
	[Export]
	private BuildingManager buildingManager;
	[Export]
	private BuildingResource[] buildingResources;
	[Export]
	private PackedScene buildingSectionScene;
	[Export]
	private PackedScene UnitSectionScene;

	public override void _Ready()
	{
		buildingSectionContainer = GetNode<VBoxContainer>("%BuildingSectionContainer");
		unitsSectionContainer = GetNode<VBoxContainer>("%UnitsContainer");
		resourceLabel = GetNode<Label>("%ResourceLabel");
		materialLabel = GetNode<Label>("%MaterialLabel");
		mineralLabel = GetNode<Label>("%MineralLabel");
		timeLeftLabel = GetNode<Label>("%TimeLeftLabel");
		stopRobotButton = GetNode<Button>("%StopRobotButton");
		displayAnomalyMapButton = GetNode<Button>("%DisplayAnomalyMapButton");
		displayTraceButton = GetNode<CheckButton>("%DisplayTraceButton");
		CreateBuildingSections();

		stopRobotButton.Pressed += OnStopRobotButtonPressed;
		displayAnomalyMapButton.Pressed += OnDisplayAnomalyMapButtonPressed;
		buildingManager.AvailableResourceCountChanged += OnAvailableResourceCountChanged;
		buildingManager.AvailableMaterialCountChanged += OnAvailableMaterialCountChanged;
		buildingManager.NewMineralAnalyzed += OnNewMineralAnalyzed;
		buildingManager.ClockIsTicking += OnClockIsTicking;
		displayTraceButton.Toggled += OnDisplayTraceToggled;

		buildingManager.BuildingPlaced += OnNewBuildingPlaced;
		buildingManager.BasePlaced += OnBasePlaced;
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingMoved, Callable.From<BuildingComponent>(OnRobotMoved));
	}

	public void OnRobotMoved(BuildingComponent buildingComponent)
	{
		if (isTraceActive)
		{
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();
			
			var allRobots = BuildingComponent.GetValidBuildingComponents(this);
			HashSet<Vector2I> tileDiscoveredByAllRobots = new();
			foreach (var robot in allRobots)
			{
				var tileDiscovered = robot.GetTileDiscovered();
				foreach (var tile in tileDiscovered)
				{
					tileDiscoveredByAllRobots.Add(tile);
				}
			}
			
			var gatherTime = stopwatch.Elapsed.TotalMilliseconds;
			
			// OPTIMIZATION: Only send newly discovered tiles to DisplayTrace
			var newTiles = new HashSet<Vector2I>(tileDiscoveredByAllRobots);
			newTiles.ExceptWith(_previouslyDiscoveredTiles);
			
			var deltaTime = stopwatch.Elapsed.TotalMilliseconds;
			
			if (newTiles.Count > 0)
			{
				gravitationalAnomalyMap.DisplayTrace(newTiles);
			}
			
			stopwatch.Stop();
			
			GD.Print($"OnRobotMoved: Total tiles={tileDiscoveredByAllRobots.Count}, New tiles={newTiles.Count}, Gather={gatherTime:F2}ms, Delta={deltaTime-gatherTime:F2}ms, Total={stopwatch.Elapsed.TotalMilliseconds:F2}ms");
			
			// Remember all discovered tiles for next frame
			_previouslyDiscoveredTiles = tileDiscoveredByAllRobots;
		}
	}

	public void SetTimeToCompleteLevel(int timeResource)
	{
		TimeToCompleteLevel = timeResource;
		var timeSpan = TimeSpan.FromSeconds(timeResource);
		timeLeftLabel.Text = timeSpan.ToString(@"mm\:ss");
		isTimeIsUp = false;
	}

	private void OnClockIsTicking()
	{
		if (isTimeIsUp)
		{
			return;
		}
		var currentTimeLeft = timeLeftLabel.Text;
		TimeSpan timeLeft;
		if (!TimeSpan.TryParseExact(currentTimeLeft, @"mm\:ss", null, out timeLeft))
		{
			// If parsing fails, reset to 00:00 and end the level
			timeLeftLabel.Text = "00:00";
			//isTimeIsUp = true;
			//EmitSignal(SignalName.TimeIsUp);
			GetViewport().SetInputAsHandled();
			return;
		}
		timeLeft = timeLeft.Subtract(TimeSpan.FromSeconds(1));
		if (timeLeft.TotalSeconds <= 0)
		{
			//isTimeIsUp = true;
			timeLeftLabel.Text = "00:00";
			//EmitSignal(SignalName.TimeIsUp);
			GetViewport().SetInputAsHandled();
			return;
		}
		else
		{
			timeLeftLabel.Text = timeLeft.ToString(@"mm\:ss");
		}

	}

	public override void _UnhandledInput(InputEvent evt)
	{
		if (evt.IsActionPressed(ACTION_SPACEBAR))
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
		if (buildingManager.IsBasePlaced)
		{
			foreach (var buildingResource in buildingResources)
			{
				if (buildingResource.DisplayName == "Base") continue; // Skip the Base section if already placed
				var buildingSection = buildingSectionScene.Instantiate<BuildingSection>();
				buildingSectionContainer.AddChild(buildingSection);
				buildingSection.SetBuildingResource(buildingResource);
	
				buildingSection.SelectButtonPressed += () =>
				{
					EmitSignal(SignalName.BuildingResourceSelected, buildingResource);
				};
			}
		}
		else if (!buildingManager.IsBasePlaced)
		{
			// Only show the Base building section
			foreach (var buildingResource in buildingResources)
			{
				if (buildingResource.DisplayName == "Base")
				{
					var buildingSection = buildingSectionScene.Instantiate<BuildingSection>();
					buildingSectionContainer.AddChild(buildingSection);
					buildingSection.SetBuildingResource(buildingResource);
	
					buildingSection.SelectButtonPressed += () =>
					{
						EmitSignal(SignalName.BuildingResourceSelected, buildingResource);
					};
					break; // Exit the loop after adding the Base section
				}
			}
		}
	}

	private void OnBasePlaced()
	{
		CreateBuildingSections();
	}


	private void OnNewBuildingPlaced(BuildingComponent buildingComponent, BuildingResource buildingResource)
	{
		if (buildingResource.DisplayName == "Base" || buildingResource.DisplayName == "Bridge" || buildingResource.DisplayName == "Antenna") return;


		var unitSection = UnitSectionScene.Instantiate<UnitSection>();
		unitsSectionContainer.AddChild(unitSection);

		if (buildingResource.DisplayName == "Rover")
		{
			unitSection.SetRobotType(buildingComponent, buildingResource, UnitSection.RobotType.GroundRobot);
		}
		else if (buildingResource.DisplayName == "Drone")
		{
			unitSection.SetRobotType(buildingComponent, buildingResource, UnitSection.RobotType.AerialRobot);
		}
		else
		{
			GD.PrintErr($"Unknown robot type: {buildingResource.DisplayName}");
			return;
		}
		unitSection.StopButtonPressed += () =>
		{
			buildingComponent.StopAnyAutomatedMovementMode();
		};
		unitSection.SelectButtonPressed += () =>
		{
			buildingManager.SelectBuilding(buildingComponent); // <-- Select robot via BuildingManager
		};
		buildingComponent.BatteryChange += unitSection.OnBatteryChange;
		buildingComponent.NewAnomalyReading += unitSection.OnNewAnomalyReading;
		buildingComponent.ModeChanged += unitSection.OnModeChanged;
		buildingComponent.robotStuck += unitSection.OnRobotStuck;
		buildingComponent.robotUnStuck += unitSection.OnRobotUnStuck;
		buildingComponent.StartCharging += unitSection.OnStartCharging;
		buildingComponent.StopCharging += unitSection.OnStopCharging;
		buildingManager.NewRobotSelected += unitSection.OnNewRobotSelected;
		buildingManager.NoMoreRobotSelected += unitSection.OnNoMoreRobotSelected;
	}

	private void OnStopRobotButtonPressed()
	{
		var allRobots = BuildingComponent.GetValidBuildingComponents(this);
		foreach (var robot in allRobots)
		{
			robot.StopAnyAutomatedMovementMode();
		}
		GameEvents.EmitAllRobotStop();
	}

	private void OnDisplayTraceToggled(bool buttonPressed)
	{
		if (!buttonPressed)
		{
			gravitationalAnomalyMap.HideTrace();
			isTraceActive = false;
			_previouslyDiscoveredTiles.Clear(); // Reset tracking when disabled
			return;
		}
		else
		{
			var allRobots = BuildingComponent.GetValidBuildingComponents(this);
			HashSet<Vector2I> tileDiscoveredByAllRobots = new();
			foreach (var robot in allRobots)
			{
				var tileDiscovered = robot.GetTileDiscovered();
				foreach (var tile in tileDiscovered)
				{
					tileDiscoveredByAllRobots.Add(tile);
				}
			}
			
			// First time enabling: send ALL discovered tiles
			gravitationalAnomalyMap.DisplayTrace(tileDiscoveredByAllRobots);
			
			// Remember for next update
			_previouslyDiscoveredTiles = tileDiscoveredByAllRobots;
		}
		isTraceActive = buttonPressed;
	}

	private void OnDisplayAnomalyMapButtonPressed()
	{
		gravitationalAnomalyMap.DisplayAnomalyMap();
	}

	private void OnAvailableResourceCountChanged(int availableResourceCount)
	{
		resourceLabel.Text = availableResourceCount.ToString();
	}

	private void OnAvailableMaterialCountChanged(int availableMaterialCount)
	{
		materialLabel.Text = availableMaterialCount.ToString();
	}

	private void OnNewMineralAnalyzed(int mineralAnalyzedCount)
	{
		mineralLabel.Text = mineralAnalyzedCount.ToString();
	}


}
