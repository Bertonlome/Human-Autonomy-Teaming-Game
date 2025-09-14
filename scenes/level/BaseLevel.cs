using System;
using System.Linq;
using Game.Autoload;
using Game.Component;
using Game.Manager;
using Game.Resources.Level;
using Game.Ui;
using Game.UI;
using Godot;

namespace Game;

public partial class BaseLevel : Node
{
	private readonly StringName ESCAPE_ACTION = "escape";
	[Export]
	private PackedScene levelCompleteScreenScene;
	[Export]
	private PackedScene levelFailedScreenScene;
	[Export]
	private PackedScene selectedRobotUIScene;
	[Export]
	private LevelDefinitionResource levelDefinitionResource;
	[Export]
	private PackedScene escapeMenuScene;

	private GridManager gridManager;
	private Monolith monolith;
	private GameCamera gameCamera;
	private Node2D baseBuilding;
	private TileMapLayer baseTerrainTilemapLayer;
	private GameUI gameUI;
	private BuildingManager buildingManager;
	private bool isComplete;
	private bool isFailed;
	private int currentTimeElapsed = 0;
	SelectedRobotUI selectedRobotUI;

	public override void _Ready()
	{
		isComplete = false;
		isFailed = false;
		gridManager = GetNode<GridManager>("GridManager");
		monolith = GetNode<Monolith>("%Monolith");
		gameCamera = GetNode<GameCamera>("GameCamera");
		baseTerrainTilemapLayer = GetNode<TileMapLayer>("%BaseTerrainTileMapLayer");
		gameUI = GetNode<GameUI>("GameUI");
		buildingManager = GetNode<BuildingManager>("BuildingManager");

		buildingManager.SetStartingResourceCount(levelDefinitionResource.StartingWoodCount);
		buildingManager.SetStartingMaterialCount(levelDefinitionResource.StartingMaterialCount);
		gameUI.SetTimeToCompleteLevel(levelDefinitionResource.LevelDuration);
		//gameUI.TimeIsUp += ShowLevelFailed;
		buildingManager.BasePlaced += OnBasePlaced;
		buildingManager.ClockIsTicking += OnClockisTicking;

		gameCamera.SetBoundingRect(baseTerrainTilemapLayer.GetUsedRect());
		gameCamera.Zoom = new Vector2((float)0.2, (float)0.2);
		gameCamera.CameraZoom += OnCameraZoom;
		gridManager.AerialRobotHasVisionOfMonolith += OnAerialRobotHasVisionOfMonolith;
		gridManager.GroundRobotTouchingMonolith += OnGroundRobotTouchingMonolith;

		GameEvents.Instance.Connect(GameEvents.SignalName.RobotSelected, Callable.From<BuildingComponent>(OnRobotSelected));
	}

	public void OnBasePlaced()
	{
		baseBuilding = BuildingComponent.GetValidBuildingComponents(this)
			.First((buildingComponent) => buildingComponent.BuildingResource.IsBase);
	}

	public override void _UnhandledInput(InputEvent evt)
	{
		if (evt.IsActionPressed(ESCAPE_ACTION))
		{
			var escapeMenu = escapeMenuScene.Instantiate<EscapeMenu>();
			AddChild(escapeMenu);
			GetViewport().SetInputAsHandled();
		}
	}

	private void ShowLevelComplete()
	{
		if (!isComplete)
		{
			isComplete = true;
			SaveManager.SavelevelCompletion(levelDefinitionResource, currentTimeElapsed, buildingManager.mineralAnalyzedCount);
			var levelCompleteScreen = levelCompleteScreenScene.Instantiate<LevelCompleteScreen>();
			AddChild(levelCompleteScreen);
			levelCompleteScreen.SetTimeElapsed(currentTimeElapsed);
			monolith.SetActive();
			gameUI.HideUI();
			selectedRobotUI.HideUI();
		}
	}

	public void ShowLevelFailed()
	{
		if (!isFailed && !isComplete)
		{
			isFailed = true;
			var levelFailedScreen = levelFailedScreenScene.Instantiate<LevelFailedScreen>();
			AddChild(levelFailedScreen);

			gameUI.HideUI();
			if (selectedRobotUI != null)
			{
				selectedRobotUI.HideUI();
			}
		}
	}

	private void OnCameraZoom()
	{
		if (baseBuilding != null)
		{
			gameCamera.CenterOnPosition(baseBuilding.GlobalPosition);
		}
	}

	private void OnAerialRobotHasVisionOfMonolith()
	{
		monolith.SetVisible();
	}

	private void OnGroundRobotTouchingMonolith()
	{
		if (isComplete) return;
		ShowLevelComplete();
	}

	private void OnRobotSelected(BuildingComponent buildingComponent)
	{
		selectedRobotUI = selectedRobotUIScene.Instantiate<SelectedRobotUI>();
		AddChild(selectedRobotUI);
		//selectedRobotUI.selectedBuildingComponent = buildingComponent;
		selectedRobotUI.SetupUI(buildingComponent); // Call setup after adding to tree
	}

	private void OnClockisTicking()
	{
		currentTimeElapsed++;
		if (currentTimeElapsed >= levelDefinitionResource.LevelDuration)
		{
			ShowLevelFailed();
		}
	}
}
