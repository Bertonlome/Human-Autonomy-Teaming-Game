using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Game.API;
using Game.Autoload;
using Game.Building;
using Game.Component;
using Game.Manager;
using Game.Resources.Building;
using Game.Services;
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
	[Signal]
	public delegate void SendPathToRobotButtonPressedEventHandler();
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
	private Button executePathButton;
	private Button previewPathButton;
	private Button configureApiKeyButton;
	private CheckButton displayTraceButton;
	private MarginContainer specialFunctionsContainer;
	private PanelContainer sendPathButtonPanelContainer;
	private Label adviceLabel;
	private Panel rakePanel;
	private ApiKeyDialog apiKeyDialog;
	private bool isTraceActive = false;
	private readonly StringName ACTION_SPACEBAR = "spacebar";
	private HashSet<Vector2I> _previouslyDiscoveredTiles = new(); // Track to calculate delta

	[Export]
	private GravitationalAnomalyMap gravitationalAnomalyMap;
	[Export]
	private BuildingManager buildingManager;
	[Export]
	private GeminiApiService geminiApiService;
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
		executePathButton = GetNode<Button>("%ExecutePathButton");
		adviceLabel = GetNode<Label>("%AdviceLabel");
		rakePanel = GetNode<Panel>("%RakePanel");
		sendPathToRobotButton = GetNode<Button>("%SendPathToRobotButton");
		
		// Try to get preview button (may not exist in older scenes)
		previewPathButton = GetNodeOrNull<Button>("%PreviewPathButton");
		
		// Create API key dialog
		apiKeyDialog = new ApiKeyDialog();
		AddChild(apiKeyDialog);
		if (geminiApiService != null)
		{
			apiKeyDialog.SetGeminiService(geminiApiService);
		}
		
		// Try to get configure API key button if it exists
		configureApiKeyButton = GetNodeOrNull<Button>("%ConfigureApiKeyButton");
		if (configureApiKeyButton != null)
		{
			configureApiKeyButton.Pressed += OnConfigureApiKeyButtonPressed;
		}
		
		CreateBuildingSections();

		stopRobotButton.Pressed += OnStopRobotButtonPressed;
		displayAnomalyMapButton.Pressed += OnDisplayAnomalyMapButtonPressed;
		buildingManager.AvailableResourceCountChanged += OnAvailableResourceCountChanged;
		buildingManager.AvailableMaterialCountChanged += OnAvailableMaterialCountChanged;
		buildingManager.NewMineralAnalyzed += OnNewMineralAnalyzed;
		buildingManager.ClockIsTicking += OnClockIsTicking;
		displayTraceButton.Toggled += OnDisplayTraceToggled;
		sendPathToRobotButton.Pressed += OnSendPathToRobotButtonPressed;
		executePathButton.Pressed += OnExecutePathButtonPressed;
		if (previewPathButton != null)
		{
			previewPathButton.Pressed += OnPreviewPathButtonPressed;
		}
		rakePanel.GuiInput += OnRakePanelGuiInput;

		buildingManager.BuildingPlaced += OnNewBuildingPlaced;
		buildingManager.BasePlaced += OnBasePlaced;
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingMoved, Callable.From<BuildingComponent>(OnRobotMoved));
		GameEvents.Instance.Connect(GameEvents.SignalName.RobotSelected, Callable.From<BuildingComponent>(OnRobotSelected));
		GameEvents.Instance.Connect(GameEvents.SignalName.RobotBackToIdle, Callable.From<BuildingComponent>(OnRobotBackToIdle));
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

	public void OnRobotBackToIdle(BuildingComponent buildingComponent)
	{
		adviceLabel.Text = $"{buildingComponent.BuildingResource.DisplayName} completed the path.";
	}
	
	public void OnRobotSelected(BuildingComponent buildingComponent)
	{
		adviceLabel.Text = "Press 'B' to enter painting path mode.";
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
		sendPathButtonPanelContainer.Visible = true;
		adviceLabel.Text = "ESC to quit painting path.\n 'N' to add annotation.";

	}

	public void HideSpecialFunctions()
	{
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
	
	private async void OnSendPathToRobotButtonPressed()
	{
		var paintedTiles = buildingManager.GetAllPaintedTiles();
		
		// Check if there are painted tiles before proceeding
		if (paintedTiles == null || paintedTiles.Count == 0)
		{
			adviceLabel.Text = "No path to optimize! Paint a path first.";
			GD.PrintErr("No painted tiles to send to API");
			await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
			return;
		}
		
		var contextTiles = buildingManager.GetContextualTilesForPaintedTiles(paintedTiles);
		
		// Export current path to JSON
		string jsonRequest = ExportPathToJson(paintedTiles);
		GD.Print("=== PATH API REQUEST ===");
		GD.Print(jsonRequest);
		GD.Print("========================");
		
		// Check if Gemini API is configured
		if (geminiApiService == null || !geminiApiService.IsConfigured())
		{
			GD.PrintErr("Gemini API service not configured. Using test path instead.");
			adviceLabel.Text = "Gemini API not configured. Set your API key first.";
			await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
			return;
		}
		
		// Show loading feedback
		adviceLabel.Text = "Request sent to Gemini API. Waiting for response...";
		
		// Detect if this is multi-robot request
		var robotCount = paintedTiles.GroupBy(t => t.AssociatedRobot).Count();
		bool isMultiRobot = robotCount > 1;
		
		if (isMultiRobot)
		{
			GD.Print($"Multi-robot request detected: {robotCount} robots");
		}
		
		try
		{
			// Call Gemini API with multi-robot flag
			string llmResponse = await geminiApiService.OptimizePathAsync(jsonRequest, isMultiRobot);
			GD.Print("=== LLM RESPONSE ===");
			GD.Print(llmResponse);
			GD.Print("====================");
			
			// Import the optimized path
			ImportPathFromJson(llmResponse);
			
			// Success feedback
			adviceLabel.Text = "Proposed path available!";
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error calling Gemini API: {ex.Message}");
			adviceLabel.Text = $"Error: {ex.Message}";
			await ToSignal(GetTree().CreateTimer(5.0), SceneTreeTimer.SignalName.Timeout);
		}
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
		
		if (robotPaths.Count == 0)
		{
			GD.PrintErr("Tiles have no associated robot");
			return "{}";
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
		};
		
		// If single robot, use legacy CurrentPath for backward compatibility
		if (robotPaths.Count == 1)
		{
			var kvp = robotPaths.First();
			var robot = kvp.Key;
			var robotTiles = kvp.Value;
			
			var pathData = new PathDataDto
			{
				RobotName = robot.BuildingResource.DisplayName,
				IsAerial = robot.BuildingResource.IsAerial,
				TotalTiles = robotTiles.Count
			};
			
			foreach (var tile in robotTiles)
			{
				pathData.Tiles.Add(new PaintedTileDto
				{
					TileNumber = tile.TileNumber,
					GridX = tile.GridPosition.X,
					GridY = tile.GridPosition.Y,
					Annotation = tile.Annotation ?? "",
					RobotName = robot.BuildingResource.DisplayName,
					IsAerial = robot.BuildingResource.IsAerial
				});
			}
			
			request.CurrentPath = pathData;
			request.RobotStartX = (int)(robot.GlobalPosition.X / 64);
			request.RobotStartY = (int)(robot.GlobalPosition.Y / 64);
			request.Context = "Optimize this exploration path for efficiency. Use contextTiles to understand terrain reachability and obstacles around the painted path.";
		}
		else // Multiple robots - use RobotPaths array
		{
			foreach (var kvp in robotPaths)
			{
				var robot = kvp.Key;
				var robotTiles = kvp.Value;
				
				var pathData = new PathDataDto
				{
					RobotName = robot.BuildingResource.DisplayName,
					IsAerial = robot.BuildingResource.IsAerial,
					TotalTiles = robotTiles.Count
				};
				
				foreach (var tile in robotTiles)
				{
					pathData.Tiles.Add(new PaintedTileDto
					{
						TileNumber = tile.TileNumber,
						GridX = tile.GridPosition.X,
						GridY = tile.GridPosition.Y,
						Annotation = tile.Annotation ?? "",
						RobotName = robot.BuildingResource.DisplayName,
						IsAerial = robot.BuildingResource.IsAerial
					});
				}
				
				request.RobotPaths.Add(pathData);
			}
			
			// Use first robot's position as reference
			var firstRobot = robotPaths.Keys.First();
			request.RobotStartX = (int)(firstRobot.GlobalPosition.X / 64);
			request.RobotStartY = (int)(firstRobot.GlobalPosition.Y / 64);
			request.Context = $"Optimize paths for {robotPaths.Count} robots. Consider inter-robot coordination to avoid collisions and maximize exploration efficiency. Use contextTiles to understand terrain reachability and obstacles.";
		}
		
		var options = new JsonSerializerOptions { WriteIndented = true };
		return JsonSerializer.Serialize(request, options);
	}
	
	/// <summary>
	/// Import LLM response and redraw the path(s)
	/// </summary>
	public void ImportPathFromJson(string jsonResponse)
	{
		try
		{
			var response = JsonSerializer.Deserialize<PathApiResponse>(jsonResponse);
			
			if (response == null || !response.Success)
			{
				GD.PrintErr($"Invalid response: {response?.Message ?? "Unknown error"}");
				return;
			}
			
			GD.Print($"LLM Reasoning: {response.Reasoning}");
			
			// Clear existing path
			buildingManager.ClearAllPaintedTiles();
			
			// Clear previous exclusion zones
			BuildingManager.ClearExclusionZones();
			
			// Get all available robots for mapping (handle duplicates by taking first match)
			var allRobots = BuildingComponent.GetValidBuildingComponents(this);
			var robotsByName = allRobots
				.GroupBy(r => r.BuildingResource.DisplayName)
				.ToDictionary(g => g.Key, g => g.First());
			
			// Collect all exclusion zones from all plans
			var allExclusionZones = new HashSet<Vector2I>();
			
			// Check for new strategic plan format (waypoints + exclusions)
			bool hasMultipleStrategicPlans = response.StrategicPlans != null && response.StrategicPlans.Count > 0;
			bool hasSingleStrategicPlan = response.StrategicPlan != null;
			
			if (hasMultipleStrategicPlans)
			{
				// Multi-robot strategic plan
				GD.Print($"Importing strategic plans for {response.StrategicPlans.Count} robots");
				
				int totalWaypoints = 0;
				foreach (var plan in response.StrategicPlans)
				{
					if (!robotsByName.TryGetValue(plan.RobotName, out var robot))
					{
						GD.PrintErr($"Robot '{plan.RobotName}' not found in scene!");
						continue;
					}
					
					GD.Print($"  - {plan.RobotName}: {plan.Waypoints.Count} waypoints, {plan.ExclusionZones.Count} exclusions");
					
					totalWaypoints += plan.Waypoints.Count;
					
					// Collect exclusion zones from this plan
					foreach (var exclusion in plan.ExclusionZones)
					{
						var gridPos = new Vector2I(exclusion.GridX, exclusion.GridY);
						allExclusionZones.Add(gridPos);
						GD.Print($"    Exclusion ({exclusion.GridX}, {exclusion.GridY}): {exclusion.Reason}");
					}
				}
				
				GD.Print($"Successfully imported {totalWaypoints} waypoints. Generating full path preview...");
				
				// Set global exclusion zones for A* pathfinding
				if (allExclusionZones.Count > 0)
				{
					BuildingManager.SetExclusionZones(allExclusionZones);
				}
				
				// Generate preview of the full path (this creates the actual painted tiles to execute)
				// Note: We don't create waypoint tiles separately since the preview generates the complete path
				GeneratePathPreview(response.StrategicPlans);
			}
			else if (hasSingleStrategicPlan)
			{
				// Single robot strategic plan
				var plan = response.StrategicPlan;
				
				if (robotsByName.TryGetValue(plan.RobotName, out var robot))
				{
					GD.Print($"Importing strategic plan for {plan.RobotName}: {plan.Waypoints.Count} waypoints, {plan.ExclusionZones.Count} exclusions");
					
					// Collect exclusion zones from this plan
					foreach (var exclusion in plan.ExclusionZones)
					{
						var gridPos = new Vector2I(exclusion.GridX, exclusion.GridY);
						allExclusionZones.Add(gridPos);
						GD.Print($"  Exclusion ({exclusion.GridX}, {exclusion.GridY}): {exclusion.Reason}");
					}
					
					GD.Print($"Successfully imported {plan.Waypoints.Count} waypoints. Generating full path preview...");
					
					// Set global exclusion zones for A* pathfinding
					if (allExclusionZones.Count > 0)
					{
						BuildingManager.SetExclusionZones(allExclusionZones);
					}
					
					// Generate preview of the full path (this creates the actual painted tiles to execute)
					// Note: We don't create waypoint tiles separately since the preview generates the complete path
					GeneratePathPreview(new List<Game.API.StrategicPlanDto> { plan });
				}
				else
				{
					GD.PrintErr($"Robot '{plan.RobotName}' not found in scene!");
				}
			}
			// Backward compatibility: Check for old full-path format
			else if (response.SuggestedPaths != null && response.SuggestedPaths.Count > 0)
			{
				// Multi-robot full path response (old format)
				GD.Print($"Importing FULL paths for {response.SuggestedPaths.Count} robots (old format)");
				
				int totalTiles = 0;
				foreach (var pathData in response.SuggestedPaths)
				{
					if (!robotsByName.TryGetValue(pathData.RobotName, out var robot))
					{
						GD.PrintErr($"Robot '{pathData.RobotName}' not found in scene!");
						continue;
					}
					
					buildingManager.SelectBuilding(robot);
					
					foreach (var tileDto in pathData.Tiles)
					{
						var gridPos = new Vector2I(tileDto.GridX, tileDto.GridY);
						buildingManager.CreatePaintedTileAt(gridPos, tileDto.Annotation);
						totalTiles++;
					}
				}
				
				GD.Print($"Successfully imported {totalTiles} tiles");
			}
			else if (response.SuggestedPath != null)
			{
				// Single robot full path (old format - backward compatibility)
				if (robotsByName.TryGetValue(response.SuggestedPath.RobotName, out var robot))
				{
					buildingManager.SelectBuilding(robot);
					
					foreach (var tileDto in response.SuggestedPath.Tiles)
					{
						var gridPos = new Vector2I(tileDto.GridX, tileDto.GridY);
						buildingManager.CreatePaintedTileAt(gridPos, tileDto.Annotation);
					}
					
					GD.Print($"Successfully imported {response.SuggestedPath.Tiles.Count} tiles (old format)");
				}
				else
				{
					GD.PrintErr($"Robot '{response.SuggestedPath.RobotName}' not found in scene!");
				}
			}
			else
			{
				GD.PrintErr("Response contains no recognized path or plan data");
			}
		}
		catch (JsonException ex)
		{
			GD.PrintErr($"Failed to parse JSON response: {ex.Message}");
		}
	}
	
	private void OnDisplayAnomalyMapButtonPressed()
	{
		gravitationalAnomalyMap.DisplayAnomalyMap();
	}
	
	private void OnConfigureApiKeyButtonPressed()
	{
		if (apiKeyDialog != null)
		{
			apiKeyDialog.PopupCentered();
		}
	}
	
	/// <summary>
	/// Preview the complete A* path through all waypoints before execution
	/// </summary>
	private void OnPreviewPathButtonPressed()
	{
		var paintedTiles = buildingManager.GetAllPaintedTiles();
		
		if (paintedTiles == null || paintedTiles.Count == 0)
		{
			adviceLabel.Text = "No waypoints to preview!";
			GD.PrintErr("No painted tiles to preview");
			return;
		}
		
		// Group waypoints by robot
		var robotWaypoints = new Dictionary<BuildingComponent, List<PaintedTile>>();
		foreach (var tile in paintedTiles)
		{
			if (tile.AssociatedRobot != null)
			{
				if (!robotWaypoints.ContainsKey(tile.AssociatedRobot))
				{
					robotWaypoints[tile.AssociatedRobot] = new List<PaintedTile>();
				}
				robotWaypoints[tile.AssociatedRobot].Add(tile);
			}
		}
		
		if (robotWaypoints.Count == 0)
		{
			adviceLabel.Text = "No robot associated with waypoints!";
			GD.PrintErr("Waypoints have no associated robot");
			return;
		}
		
		// Clear existing tiles first
		buildingManager.ClearAllPaintedTiles();
		
		GD.Print($"Generating preview for {robotWaypoints.Count} robot(s)...");
		
		// Generate full A* path for each robot
		int totalPathTiles = 0;
		foreach (var kvp in robotWaypoints)
		{
			var robot = kvp.Key;
			var waypoints = kvp.Value.OrderBy(t => t.TileNumber).ToList();
			
			buildingManager.SelectBuilding(robot);
			
			GD.Print($"  {robot.BuildingResource.DisplayName}: Connecting {waypoints.Count} waypoints");
			
			// Start from robot's current position
			var currentPos = robot.GetGridCellPosition();
			int segmentNum = 0;
			
			foreach (var waypoint in waypoints)
			{
				// Skip if waypoint is at current position (e.g., double-click on same tile)
				if (currentPos == waypoint.GridPosition)
				{
					GD.Print($"    Skipping waypoint at current position ({waypoint.GridPosition.X}, {waypoint.GridPosition.Y})");
					continue;
				}
				
				// Use A* to find path from current position to this waypoint
				var (moves, bridgeTiles) = robot.ComputeAStarPath(currentPos, waypoint.GridPosition);
				
				if (moves.Count == 0)
				{
					GD.Print($"    No path found to waypoint at ({waypoint.GridPosition.X}, {waypoint.GridPosition.Y}), skipping...");
					continue;
				}
				
				// Create painted tiles for this path segment
				var pathPos = currentPos;
				foreach (var move in moves)
				{
					pathPos = robot.ComputeNextPosition(pathPos, move);
					
					// Create painted tile at this position (no annotation to avoid clutter)
					buildingManager.CreatePaintedTileAt(pathPos, string.Empty);
					totalPathTiles++;
				}
				
				// Move to next segment
				currentPos = waypoint.GridPosition;
				segmentNum++;
			}
			
			GD.Print($"    Generated {totalPathTiles} path tiles");
		}
		
		adviceLabel.Text = $"Preview: {totalPathTiles} tiles. Press Execute to start.";
		GD.Print($"Preview complete: {totalPathTiles} total path tiles generated");
	}
	
	/// <summary>
	/// Generate A* path preview from strategic plans (waypoints + exclusions)
	/// </summary>
	private void GeneratePathPreview(List<Game.API.StrategicPlanDto> plans)
	{
		if (plans == null || plans.Count == 0)
		{
			return;
		}
		
		// Get all available robots for mapping (handle duplicates by taking first match)
		var allRobots = BuildingComponent.GetValidBuildingComponents(this);
		var robotsByName = allRobots
			.GroupBy(r => r.BuildingResource.DisplayName)
			.ToDictionary(g => g.Key, g => g.First());
		
		GD.Print($"Generating path preview for {plans.Count} robot(s)...");
		
		int totalPathTiles = 0;
		foreach (var plan in plans)
		{
			if (!robotsByName.TryGetValue(plan.RobotName, out var robot))
			{
				GD.PrintErr($"Cannot preview: Robot '{plan.RobotName}' not found!");
				continue;
			}
			
			if (plan.Waypoints.Count == 0)
			{
				GD.Print($"  {plan.RobotName}: No waypoints to connect");
				continue;
			}
			
			buildingManager.SelectBuilding(robot);
			
			// Sort waypoints by priority
			var sortedWaypoints = plan.Waypoints.OrderBy(w => w.Priority).ToList();
			GD.Print($"  {plan.RobotName}: Connecting {sortedWaypoints.Count} waypoints");
			
			// Start from robot's current position
			var currentPos = robot.GetGridCellPosition();
			int segmentNum = 0;
			bool isCarryingRobot = false; // Track if we're between LIFT and DROP
			
			foreach (var waypoint in sortedWaypoints)
			{
				var targetPos = new Vector2I(waypoint.GridX, waypoint.GridY);
				
				// Skip if waypoint is at current position (e.g., LLM generated duplicate)
				if (currentPos == targetPos)
				{
					GD.Print($"    Skipping waypoint P{waypoint.Priority} at current position ({targetPos.X}, {targetPos.Y})");
					continue;
				}
				
				// Use A* to find path from current position to this waypoint
				var (moves, bridgeTiles) = robot.ComputeAStarPath(currentPos, targetPos);
				
				if (moves.Count == 0)
				{
					GD.Print($"    No path found to waypoint P{waypoint.Priority} at ({targetPos.X}, {targetPos.Y}), skipping...");
					continue; // Skip this waypoint and try the next one
				}
				
				// Create painted tiles for this path segment
				var pathPos = currentPos;
				foreach (var move in moves)
				{
					pathPos = robot.ComputeNextPosition(pathPos, move);
					
					// Check if this is the waypoint position - if so, use waypoint's reason as annotation
					string annotation = string.Empty;
					if (pathPos == targetPos && !string.IsNullOrEmpty(waypoint.Reason))
					{
						// Transfer LIFT/DROP/LIFTING reasons to the tile annotation
						if (waypoint.Reason.Equals("LIFT", StringComparison.OrdinalIgnoreCase))
						{
							annotation = waypoint.Reason;
							isCarryingRobot = true; // Start carrying after LIFT
							GD.Print($"    Annotated waypoint at ({pathPos.X}, {pathPos.Y}) with '{annotation}'");
						}
						else if (waypoint.Reason.Equals("DROP", StringComparison.OrdinalIgnoreCase))
						{
							annotation = waypoint.Reason;
							isCarryingRobot = false; // Stop carrying at DROP
							GD.Print($"    Annotated waypoint at ({pathPos.X}, {pathPos.Y}) with '{annotation}'");
						}
						else if (waypoint.Reason.Equals("LIFTING", StringComparison.OrdinalIgnoreCase))
						{
							annotation = waypoint.Reason;
							GD.Print($"    Annotated waypoint at ({pathPos.X}, {pathPos.Y}) with '{annotation}'");
						}
					}
					else if (isCarryingRobot && string.IsNullOrEmpty(annotation))
					{
						// Intermediate tiles between LIFT and DROP get "LIFTING" annotation
						annotation = "LIFTING";
					}
					
					buildingManager.CreatePaintedTileAt(pathPos, annotation);
					totalPathTiles++;
				}
				
				// Move to next segment
				currentPos = targetPos;
				segmentNum++;
			}
			
			GD.Print($"    {robot.BuildingResource.DisplayName}: Generated {totalPathTiles} path tiles");
		}
		
		if (totalPathTiles > 0)
		{
			adviceLabel.Text = $"Path preview: {totalPathTiles} tiles through waypoints";
			GD.Print($"Path preview complete: {totalPathTiles} total tiles");
		}
		else
		{
			adviceLabel.Text = "Failed to generate path preview";
		}
	}
	
	private void OnExecutePathButtonPressed()
	{
		EmitSignal(nameof(SendPathToRobotButtonPressed));
		// Get all painted tiles
		var paintedTiles = buildingManager.GetAllPaintedTiles();
		
		if (paintedTiles == null || paintedTiles.Count == 0)
		{
			adviceLabel.Text = "No path to execute!";
			GD.PrintErr("No painted tiles to execute");
			return;
		}
		
		// Group tiles by robot
		var robotPaths = new Dictionary<BuildingComponent, List<PaintedTile>>();
		foreach (var tile in paintedTiles)
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
		
		if (robotPaths.Count == 0)
		{
			adviceLabel.Text = "No robot associated with path!";
			GD.PrintErr("Painted tiles have no associated robot");
			return;
		}
		
		// Keep painted tiles visible during execution to show the path
		// Note: Exclusion zones remain active during path execution
		// They will be cleared when a new strategic plan is imported
		
	// Execute path for each robot
	int robotCount = 0;
	foreach (var kvp in robotPaths)
	{
		var robot = kvp.Key;
		var tiles = kvp.Value;
		
		if (tiles.Count > 0)
		{
			// Sort tiles by tile number to follow correct order
			tiles.Sort((a, b) => a.TileNumber.CompareTo(b.TileNumber));
			
			GD.Print($"Executing custom path for robot {robot.BuildingResource.DisplayName} with {tiles.Count} waypoints");
			
			// Execute the path by visiting each waypoint in sequence
			ExecuteCustomPath(robot, tiles);
			robotCount++;
		}
	}
	
	adviceLabel.Text = $"Executing paths for {robotCount} robot(s)...";
}

/// <summary>
/// Execute a custom path by following each waypoint in sequence
/// </summary>
private void ExecuteCustomPath(BuildingComponent robot, List<PaintedTile> waypoints)
{
	// Extract grid positions and annotations from painted tiles
	var orderedWaypoints = waypoints.OrderBy(tile => tile.TileNumber).ToList();
	
	List<Vector2I> waypointPositions = orderedWaypoints
		.Select(tile => tile.GridPosition)
		.ToList();
	
	// Build annotation dictionary (only for tiles that have annotations)
	Dictionary<Vector2I, string> waypointAnnotations = new Dictionary<Vector2I, string>();
	foreach (var tile in orderedWaypoints)
	{
		if (!string.IsNullOrEmpty(tile.Annotation))
		{
			waypointAnnotations[tile.GridPosition] = tile.Annotation;
		}
	}
	
	GD.Print($"Executing path with {waypointPositions.Count} waypoints for {robot.BuildingResource.DisplayName}");
	if (waypointAnnotations.Count > 0)
	{
		GD.Print($"Found {waypointAnnotations.Count} annotated waypoints: {string.Join(", ", waypointAnnotations.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
	}
	
	// Let the robot handle the entire path smoothly with annotations
	robot.ExecuteWaypointPath(waypointPositions, waypointAnnotations);
	
	adviceLabel.Text = $"Executing path for {robot.BuildingResource.DisplayName}...";
}	private void OnAvailableResourceCountChanged(int availableResourceCount)
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
