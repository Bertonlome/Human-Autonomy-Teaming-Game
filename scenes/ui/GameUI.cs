using System;
using System.Collections.Generic;
using System.Text.Json;
using Game.API;
using Game.Autoload;
using Game.Building;
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
	[Signal]
	public delegate void TouchedRakePanelEventHandler();
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
	private Button sendPathToRobotButton;
	private CheckButton displayTraceButton;
	private MarginContainer specialFunctionsContainer;
	private PanelContainer sendPathButtonPanelContainer;
	private Label adviceLabel;
	private Panel rakePanel;
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
		specialFunctionsContainer = GetNode<MarginContainer>("%SpecialFunctionsContainer");
		sendPathButtonPanelContainer = GetNode<PanelContainer>("%SendPathButtonPanelContainer");
		adviceLabel = GetNode<Label>("%AdviceLabel");
		rakePanel = GetNode<Panel>("%RakePanel");
		sendPathToRobotButton = GetNode<Button>("%SendPathToRobotButton");
		CreateBuildingSections();

		stopRobotButton.Pressed += OnStopRobotButtonPressed;
		displayAnomalyMapButton.Pressed += OnDisplayAnomalyMapButtonPressed;
		buildingManager.AvailableResourceCountChanged += OnAvailableResourceCountChanged;
		buildingManager.AvailableMaterialCountChanged += OnAvailableMaterialCountChanged;
		buildingManager.NewMineralAnalyzed += OnNewMineralAnalyzed;
		buildingManager.ClockIsTicking += OnClockIsTicking;
		displayTraceButton.Toggled += OnDisplayTraceToggled;
		sendPathToRobotButton.Pressed += OnSendPathToRobotButtonPressed;
		rakePanel.GuiInput += OnRakePanelGuiInput;

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

	public void DisplaySpecialFunctions()
	{
		//specialFunctionsContainer.Visible = true;
		sendPathButtonPanelContainer.Visible = true;
		adviceLabel.Text = "ESC to quit painting path.\n 'N' to add annotation.";

	}

	public void HideSpecialFunctions()
	{
		//specialFunctionsContainer.Visible = false;
		sendPathButtonPanelContainer.Visible = false;
		adviceLabel.Text = "Press 'B' to enter painting path mode.";
	}

	private void CreateBuildingSections()
	{
		// Clear existing building sections first
		foreach (Node child in buildingSectionContainer.GetChildren())
		{
			child.QueueFree();
		}
		
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

	private void OnRakePanelGuiInput(InputEvent evt)
	{
		if (evt is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.ButtonIndex == MouseButton.Left)
			{
				if (mouseEvent.Pressed)
				{
					// Start dragging
					EmitSignal(nameof(TouchedRakePanel));
					GetViewport().SetInputAsHandled();
				}
			}
		}
	}
	
	private void OnSendPathToRobotButtonPressed()
	{
		var paintedTiles = buildingManager.GetAllPaintedTiles();
		var contextTiles = buildingManager.GetContextualTilesForPaintedTiles(paintedTiles);
		
		// Export current path to JSON
		string jsonRequest = ExportPathToJson(paintedTiles);
		GD.Print("=== PATH API REQUEST ===");
		GD.Print(jsonRequest);
		GD.Print("========================");
		
		// TODO: Send jsonRequest to your LLM API endpoint
		// Example: await HttpClient.PostAsync("https://your-api.com/optimize-path", jsonRequest);
		
		// For now, copy the JSON from console and test with your LLM
		// Then use ImportPathFromJson() to apply the response
		TestImportSamplePath();
	}
	
	/// <summary>
	/// Export painted tiles to JSON format for LLM API
	/// </summary>
	private string ExportPathToJson(List<PaintedTile> tiles)
	{
		if (tiles == null || tiles.Count == 0)
		{
			GD.PrintErr("No painted tiles to export");
			return "{}";
		}
		
		// Group tiles by robot
		var robotPaths = new Dictionary<BuildingComponent, List<PaintedTile>>();
		foreach (var tile in tiles)
		{
			if (tile.AssociatedRobot != null)
			{
				if (!robotPaths.ContainsKey(tile.AssociatedRobot))
				{
					robotPaths[tile.AssociatedRobot] = new List<PaintedTile>();
				}
				robotPaths[tile.AssociatedRobot].Add(tile);
			}
		}
		
		// For now, export the first robot's path (you can extend to handle multiple)
		var firstRobot = tiles[0].AssociatedRobot;
		if (firstRobot == null)
		{
			GD.PrintErr("Tiles have no associated robot");
			return "{}";
		}
		
		// Create request DTO
		var pathData = new PathDataDto
		{
			RobotName = firstRobot.BuildingResource.DisplayName,
			IsAerial = firstRobot.BuildingResource.IsAerial,
			TotalTiles = tiles.Count
		};
		
		// Convert each painted tile
		foreach (var tile in tiles)
		{
			pathData.Tiles.Add(new PaintedTileDto
			{
				TileNumber = tile.TileNumber,
				GridX = tile.GridPosition.X,
				GridY = tile.GridPosition.Y,
				Annotation = tile.Annotation ?? "",
				RobotName = tile.AssociatedRobot?.BuildingResource.DisplayName ?? "Unknown",
				IsAerial = tile.AssociatedRobot?.BuildingResource.IsAerial ?? false
			});
		}
		
		// Get contextual tiles for surrounding area
		var contextTiles = buildingManager.GetContextualTilesForPaintedTiles(tiles);
		var contextTileDtos = new List<ContextTileDto>();
		
		// Calculate bounding box from context tiles
		int minX = int.MaxValue, minY = int.MaxValue;
		int maxX = int.MinValue, maxY = int.MinValue;
		
		foreach (var contextTile in contextTiles)
		{
			minX = Math.Min(minX, contextTile.GridPosition.X);
			minY = Math.Min(minY, contextTile.GridPosition.Y);
			maxX = Math.Max(maxX, contextTile.GridPosition.X);
			maxY = Math.Max(maxY, contextTile.GridPosition.Y);
			
			contextTileDtos.Add(new ContextTileDto
			{
				GridX = contextTile.GridPosition.X,
				GridY = contextTile.GridPosition.Y,
				IsReachable = contextTile.IsReachable,
				IsPaintedTile = contextTile.IsPaintedTile,
				TileNumber = contextTile.PaintedTileReference?.TileNumber
			});
		}
		
		var request = new PathApiRequest
		{
			CurrentPath = pathData,
			ContextTiles = contextTileDtos,
			BoundingBox = new BoundingBoxDto
			{
				MinX = minX,
				MinY = minY,
				MaxX = maxX,
				MaxY = maxY,
				Width = maxX - minX + 1,
				Height = maxY - minY + 1
			},
			MapWidth = 100, // TODO: Get from GridManager
			MapHeight = 100, // TODO: Get from GridManager
			RobotStartX = (int)(firstRobot.GlobalPosition.X / 64),
			RobotStartY = (int)(firstRobot.GlobalPosition.Y / 64),
			Context = "Optimize this exploration path for efficiency. Use contextTiles to understand terrain reachability and obstacles around the painted path."
		};
		
		var options = new JsonSerializerOptions { WriteIndented = true };
		return JsonSerializer.Serialize(request, options);
	}
	
	/// <summary>
	/// Import LLM response and redraw the path
	/// </summary>
	public void ImportPathFromJson(string jsonResponse)
	{
		try
		{
			var response = JsonSerializer.Deserialize<PathApiResponse>(jsonResponse);
			
			if (response == null || !response.Success || response.SuggestedPath == null)
			{
				GD.PrintErr($"Invalid response: {response?.Message ?? "Unknown error"}");
				return;
			}
			
			GD.Print($"LLM Reasoning: {response.Reasoning}");
			
			// Clear existing path
			buildingManager.ClearAllPaintedTiles();
			
			// Redraw the new path from LLM
			foreach (var tileDto in response.SuggestedPath.Tiles)
			{
				var gridPos = new Vector2I(tileDto.GridX, tileDto.GridY);
				buildingManager.CreatePaintedTileAt(gridPos, tileDto.Annotation);
			}
			
			GD.Print($"Successfully imported {response.SuggestedPath.Tiles.Count} tiles from LLM");
		}
		catch (JsonException ex)
		{
			GD.PrintErr($"Failed to parse JSON response: {ex.Message}");
		}
	}
	
	/// <summary>
	/// TEST METHOD: Generate a sample LLM response for testing
	/// Call this from console to test the import functionality
	/// </summary>
	public void TestImportSamplePath()
	{
		GD.Print("Testing import with sample path...");
		string llmResponse = @"{
			""success"": true,
			""message"": ""Path optimized successfully"",
			""suggestedPath"": {
				""robotName"": ""Rover"",
				""isAerial"": false,
				""tiles"": [
					{
						""tileNumber"": 1,
						""gridX"": 33,
						""gridY"": 4,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 2,
						""gridX"": 32,
						""gridY"": 3,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 3,
						""gridX"": 31,
						""gridY"": 2,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 4,
						""gridX"": 30,
						""gridY"": 1,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 5,
						""gridX"": 29,
						""gridY"": 0,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 6,
						""gridX"": 28,
						""gridY"": 0,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 7,
						""gridX"": 27,
						""gridY"": 0,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 8,
						""gridX"": 26,
						""gridY"": 0,
						""annotation"": ""explore around"",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 9,
						""gridX"": 25,
						""gridY"": 0,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 10,
						""gridX"": 25,
						""gridY"": 1,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 11,
						""gridX"": 26,
						""gridY"": 1,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 12,
						""gridX"": 27,
						""gridY"": 1,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 13,
						""gridX"": 27,
						""gridY"": 0,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 14,
						""gridX"": 26,
						""gridY"": 0,
						""annotation"": ""explore around"",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 15,
						""gridX"": 25,
						""gridY"": 1,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 16,
						""gridX"": 25,
						""gridY"": 2,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 17,
						""gridX"": 25,
						""gridY"": 3,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 18,
						""gridX"": 25,
						""gridY"": 4,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 19,
						""gridX"": 25,
						""gridY"": 5,
						""annotation"": """",
						""robotName"": ""Rover"",
						""isAerial"": false
					},
					{
						""tileNumber"": 20,
						""gridX"": 24,
						""gridY"": 6,
						""annotation"": ""collect"",
						""robotName"": ""Rover"",
						""isAerial"": false
					}
				],
				""totalTiles"": 20
			},
			""reasoning"": ""Optimized path based on reachability analysis from context tiles""
		}";
		
		ImportPathFromJson(llmResponse);
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
