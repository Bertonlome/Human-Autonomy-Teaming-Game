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
	SelectedRobotUI selectedRobotUI;

	public override void _Ready()
	{
		gridManager = GetNode<GridManager>("GridManager");
		monolith = GetNode<Monolith>("%Monolith");
		gameCamera = GetNode<GameCamera>("GameCamera");
		baseTerrainTilemapLayer = GetNode<TileMapLayer>("%BaseTerrainTileMapLayer");
		gameUI = GetNode<GameUI>("GameUI");
		buildingManager = GetNode<BuildingManager>("BuildingManager");

		buildingManager.SetStartingResourceCount(levelDefinitionResource.StartingResourceCount);
		buildingManager.BasePlaced += OnBasePlaced;

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
        if(evt.IsActionPressed(ESCAPE_ACTION))
		{
			var escapeMenu = escapeMenuScene.Instantiate<EscapeMenu>();
			AddChild(escapeMenu);
			GetViewport().SetInputAsHandled();
		}
    }

    private void ShowLevelComplete()
	{
		isComplete = true;
		SaveManager.SavelevelCompletion(levelDefinitionResource);
		var levelCompleteScreen = levelCompleteScreenScene.Instantiate<LevelCompleteScreen>();
		AddChild(levelCompleteScreen);
		monolith.SetActive();
		gameUI.HideUI();
		selectedRobotUI.HideUI();
	}

	private void OnCameraZoom()
	{
		if(baseBuilding != null)
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
		selectedRobotUI.selectedBuildingComponent = buildingComponent;
	}
}
