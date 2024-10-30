using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Game.Autoload;
using Game.Component;
using Game.Level.Util;
using Godot;

namespace Game.Manager;

public partial class GridManager : Node
{
	private const string IS_BUILDABLE = "is_buildable";
	private const string IS_WOOD = "is_wood";
	private const string IS_IGNORED = "is_ignored";
	private const string IS_ROUGH_TERRAIN = "is_rough_terrain";

	[Signal]
	public delegate void ResourceTilesUpdatedEventHandler(int collectedTiles);
	[Signal]
	public delegate void GridStateUpdatedEventHandler();

	private HashSet<Vector2I> validBuildableTiles = new();
	private HashSet<Vector2I> validBuildableAttackTiles = new();
	private HashSet<Vector2I> allTilesInBuildingRadius = new();
	private IEnumerable<Vector2I> listOfTilesInBuildingRadius;
	private HashSet<Vector2I> collectedResourceTiles = new();
	private HashSet<Vector2I> occupiedTiles = new();
	private HashSet<Vector2I> dangerOccupiedTiles = new();
	private HashSet<Vector2I> attackTiles = new();
	private HashSet<Vector2I> baseAntennaCoveredTiles = new();

	[Export]
	private TileMapLayer highlightTilemapLayer;
	[Export]
	private TileMapLayer baseTerrainTilemapLayer;

	private List<TileMapLayer> allTilemapLayers = new();
	private Dictionary<TileMapLayer, ElevationLayer> tileMapLayerToElevationLayer = new();
	private Dictionary<BuildingComponent, HashSet<Vector2I>> buildingToBuildableTiles = new();
	private Dictionary<BuildingComponent, HashSet<Vector2I>> dangerBuildingToTiles = new();
	private Dictionary<BuildingComponent, HashSet<Vector2I>> attackBuildingToTiles = new();

	private Vector2I goldMinePosition;

	public override void _Ready()
	{
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingPlaced, Callable.From<BuildingComponent>(OnBuildingPlaced));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingDestroyed, Callable.From<BuildingComponent>(OnBuildingDestroyed));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingEnabled, Callable.From<BuildingComponent>(OnBuildingEnabled));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingDisabled, Callable.From<BuildingComponent>(OnBuildingDisabled));
		GameEvents.Instance.Connect(GameEvents.SignalName.BuildingMoved, Callable.From<BuildingComponent>(OnBuildingMoved));

		allTilemapLayers = GetAllTilemapLayers(baseTerrainTilemapLayer);
		MapTileMapLayersToElevationLayers();
	}

	public void SetGoldMinePosition(Vector2I position)
	{
		goldMinePosition = position;
	}

	public (TileMapLayer, bool) GetTileCustomData(Vector2I tilePosition, string dataName)
	{
		foreach (var layer in allTilemapLayers)
		{
			var customData = layer.GetCellTileData(tilePosition);
			if (customData == null || (bool)customData.GetCustomData(IS_IGNORED)) continue;
			return (layer, (bool)customData.GetCustomData(dataName));
		}
		return (null, false);
	}

	public bool IsTilePositionInAnyBuildingRadius(Vector2I tilePosition)
	{
		return allTilesInBuildingRadius.Contains(tilePosition);
	}

	public bool IsTileAreaBuildable(Rect2I tileArea, bool isAttackTiles = false)
	{
		IEnumerable<Vector2I> tileSetToCheck;
		var tiles = tileArea.ToTiles();
		if (tiles.Count == 0) return false;

		(TileMapLayer firstTileMapLayer, _) = GetTileCustomData(tiles[0], IS_BUILDABLE);
		var targetElevationLayer = firstTileMapLayer != null ? tileMapLayerToElevationLayer[firstTileMapLayer] : null;

		if(BuildingManager.selectedBuildingComponent != null)
		{
			tileSetToCheck = GetBuildableTileSet(isAttackTiles).Except(BuildingManager.selectedBuildingComponent.GetOccupiedCellPositions());
		}
		else
		{
			tileSetToCheck = GetBuildableTileSet(isAttackTiles);
		}
		if (isAttackTiles)
		{
			tileSetToCheck = tileSetToCheck.Except(occupiedTiles).ToHashSet();
		}

		return tiles.All((tilePosition) =>
		{
			(TileMapLayer tileMapLayer, bool isBuildable) = GetTileCustomData(tilePosition, IS_BUILDABLE);
			var elevationLayer = tileMapLayer != null ? tileMapLayerToElevationLayer[tileMapLayer] : null;
			return isBuildable && tileSetToCheck.Contains(tilePosition) && elevationLayer == targetElevationLayer;
		});
	}

	public bool IsBuildingMovable(BuildingComponent buildingComponent, Rect2I originArea, Rect2I destinationArea)
	{
		IEnumerable<Vector2I> tileSetToCheckGround;
		IEnumerable<Vector2I> tileSetToCheckAerial;

		var tilesDestination = destinationArea.ToTiles();
		var tilesOrigin = originArea.ToTiles();

		if (tilesDestination.Count == 0) return false;

		(TileMapLayer firstTileMapLayer, _) = GetTileCustomData(tilesDestination[0], IS_BUILDABLE);
		var targetElevationLayer = firstTileMapLayer != null ? tileMapLayerToElevationLayer[firstTileMapLayer] : null;

		(firstTileMapLayer, _) = GetTileCustomData(tilesOrigin[0], IS_BUILDABLE);
		var OriginElevationLayer = firstTileMapLayer != null ? tileMapLayerToElevationLayer[firstTileMapLayer] : null;

		var transitionTile = originArea.ToTiles().Intersect(destinationArea.ToTiles()).ToHashSet();

		tileSetToCheckGround = GetBuildableTileSet().Union(transitionTile).ToHashSet(); //Buildable takes into account rocks and plants
		
		tileSetToCheckAerial = occupiedTiles.ToHashSet(); //UAV can fly over rocks and plants

		return tilesDestination.All((tilePosition) =>
		{
			(TileMapLayer tileMapLayer, bool isBuildable) = GetTileCustomData(tilePosition, IS_BUILDABLE);
			var elevationLayer = tileMapLayer != null ? tileMapLayerToElevationLayer[tileMapLayer] : null;
			(tileMapLayer, bool isWood) = GetTileCustomData(tilePosition, IS_WOOD);
			(tileMapLayer, bool isRoulable) = GetTileCustomData(tilePosition, IS_ROUGH_TERRAIN);
			
			//Check for ground vehicle
			var check1 = tileSetToCheckGround.Contains(tilePosition) ? true: false;
			var check2 = elevationLayer == targetElevationLayer ? true: false;
			var check3 = OriginElevationLayer == targetElevationLayer ? true: false;
			var check7 = !isRoulable;
			//Check for aerial vehicle
			var check4 = buildingComponent.BuildingResource.IsAerial;
			var check5 = tileSetToCheckAerial.Contains(tilePosition) ? false: true;
			var check6 = !isWood;
			return (check1 && check2 && check3 && check7) || (check4 && check5 && check6);
		});
	}

	public bool IsGettingOutOfACoverage(BuildingComponent buildingComponent, Rect2I destinationArea)
	{
		var tilesInRadiusofRobotArrival = GetValidTilesInRadius(destinationArea, buildingComponent.BuildingResource.BuildableRadius);
		
		buildingToBuildableTiles.Remove(buildingComponent);

		var allTilesFromDictionary = new HashSet<Vector2I>();
		foreach(var tileSet in buildingToBuildableTiles.Values)
		{
			allTilesFromDictionary.UnionWith(tileSet);
		}

		var anyTilesInRadius = tilesInRadiusofRobotArrival		
			.Any((tilePosition) => 
			{
				return allTilesFromDictionary.Contains(tilePosition);
			});
	
		//GD.Print("Nmbr of intersect with base : " +tilesInRadiusofRobotArrival.Intersect(baseAntennaCoveredTiles).Count());
		//GD.Print("anytiles in radius ? " + anyTilesInRadius);
		bool testfinal = (tilesInRadiusofRobotArrival.Intersect(baseAntennaCoveredTiles).Count() > 0) || anyTilesInRadius == true;
		//GD.Print("Conjunction just to be sure : " + testfinal);

		if((tilesInRadiusofRobotArrival.Intersect(baseAntennaCoveredTiles).Count() > 0) || anyTilesInRadius) return true;
		else return true;
	}

	public void HighlightDangerOccupiedTiles()
	{
		var atlasCoords = new Vector2I(2, 0);
		foreach (var tilePosition in dangerOccupiedTiles)
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, atlasCoords);
		}
	}

	public void HighlightBuildableTiles(bool isAttackTiles = false)
	{
		foreach (var tilePosition in GetBuildableTileSet(isAttackTiles))
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, Vector2I.Zero);
		}
	}

	public void HighlightExpandedBuildableTiles(Rect2I tileArea, int radius)
	{
		var validTiles = GetValidTilesInRadius(tileArea, radius).ToHashSet();
		var expandedTiles = validTiles.Except(validBuildableTiles).Except(occupiedTiles);
		var atlasCoords = new Vector2I(1, 0);
		foreach (var tilePosition in expandedTiles)
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, atlasCoords);
		}
	}

	public void HighlightAttackTiles(Rect2I tileArea, int radius)
	{
		var buildingAreaTiles = tileArea.ToTiles();
		var validTiles = GetValidTilesInRadius(tileArea, radius).ToHashSet()
			.Except(validBuildableAttackTiles)
			.Except(buildingAreaTiles);

		var atlasCoords = new Vector2I(1, 0);
		foreach (var tilePosition in validTiles)
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, atlasCoords);
		}
	}

	public void HighlightResourceTiles(Rect2I tileArea, int radius)
	{
		var resourceTiles = GetResourceTilesInRadius(tileArea, radius);
		var atlasCoords = new Vector2I(1, 0);
		foreach (var tilePosition in resourceTiles)
		{
			highlightTilemapLayer.SetCell(tilePosition, 0, atlasCoords);
		}
	}

	public void ClearHighlightedTiles()
	{
		highlightTilemapLayer.Clear();
	}

	public Vector2I GetMouseGridCellPositionWithDimensionOffset(Vector2 dimensions)
	{
		var mouseGridPosition = highlightTilemapLayer.GetGlobalMousePosition() / 64;
		mouseGridPosition -= dimensions / 2;
		mouseGridPosition = mouseGridPosition.Round();
		return new Vector2I((int)mouseGridPosition.X, (int)mouseGridPosition.Y);
	}

	public Vector2I GetMouseGridCellPosition()
	{
		var mousePosition = highlightTilemapLayer.GetGlobalMousePosition();
		return ConvertWorldPositionToTilePosition(mousePosition);
	}

	public Vector2I ConvertWorldPositionToTilePosition(Vector2 worldPosition)
	{
		var tilePosition = worldPosition / 64;
		tilePosition = tilePosition.Floor();
		return new Vector2I((int)tilePosition.X, (int)tilePosition.Y);
	}

	public bool CanMoveBuilding(BuildingComponent toMoveBuildingComponent, Rect2I destinationArea = new Rect2I())
	{
		if(destinationArea.Area == 0)
		{
			destinationArea = toMoveBuildingComponent.GetAreaOccupied(ConvertWorldPositionToTilePosition(toMoveBuildingComponent.GlobalPosition));
		}
		
		var tilesInRadiusofRobotArrival = GetValidTilesInRadius(destinationArea, toMoveBuildingComponent.BuildingResource.BuildableRadius);

		if(toMoveBuildingComponent.BuildingResource.BuildableRadius > 0)
		{
			return IsRobotNetworkConnected(toMoveBuildingComponent, tilesInRadiusofRobotArrival) && IsGettingOutOfACoverage(toMoveBuildingComponent, destinationArea);
		}
		return false;
	}

	public bool CanDestroyBuilding(BuildingComponent toDestroyBuildingComponent)
	{
		if (toDestroyBuildingComponent.BuildingResource.BuildableRadius > 0)
		{
			return !WillBuildingDestructionCreateOrphanBuildings(toDestroyBuildingComponent) &&
				IsBuildingNetworkConnected(toDestroyBuildingComponent);
		}
		else if (toDestroyBuildingComponent.BuildingResource.IsAttackBuilding())
		{
			return CanDestroyBarracks(toDestroyBuildingComponent);
		}
		return true;
	}

	private bool CanDestroyBarracks(BuildingComponent toDestroyBuildingComponent)
	{
		var disabledDangerBuildings = BuildingComponent.GetDangerBuildingComponents(this)
			.Where((buildingComponent) => buildingComponent.GetOccupiedCellPositions().Any((tilePosition) =>
			{
				return attackBuildingToTiles[toDestroyBuildingComponent].Contains(tilePosition);
			}));

		if (!disabledDangerBuildings.Any()) return true;

		var allDangerBuildingsStillDisabled = disabledDangerBuildings.All((dangerBuilding) =>
		{
			return dangerBuilding.GetOccupiedCellPositions().Any((tilePosition) =>
			{
				return attackBuildingToTiles.Keys.Where((attackBuilding) => attackBuilding != toDestroyBuildingComponent)
					.Any((attackBuilding) => attackBuildingToTiles[attackBuilding].Contains(tilePosition));
			});
		});

		if (allDangerBuildingsStillDisabled) return true;

		var nonDangerBuildings = BuildingComponent.GetNonDangerBuildingComponents(this).Where((nonDangerBuilding) =>
		{
			return nonDangerBuilding != toDestroyBuildingComponent;
		});
		var anyDangerBuildingContainsPlayerBuilding = disabledDangerBuildings.Any((dangerBuilding) =>
		{
			var dangerTiles = dangerBuildingToTiles[dangerBuilding];
			return nonDangerBuildings.Any((nonDangerBuilding) =>
			{
				return nonDangerBuilding.GetOccupiedCellPositions().Any((tilePosition) => dangerTiles.Contains(tilePosition));
			});
		});

		return !anyDangerBuildingContainsPlayerBuilding;
	}

	public HashSet<Vector2I> GetCollectedResourcetiles()
	{
		return collectedResourceTiles.ToHashSet();
	}

	private bool WillBuildingDestructionCreateOrphanBuildings(BuildingComponent toDestroyBuildingComponent)
	{
		var dependentBuildings = BuildingComponent.GetNonDangerBuildingComponents(this)
			.Where((buildingComponent) =>
			{
				if (buildingComponent == toDestroyBuildingComponent) return false;
				if (buildingComponent.BuildingResource.IsBase) return false;

				var anyTilesInRadius = buildingComponent.GetOccupiedCellPositions()
					.Any((tilePosition) => buildingToBuildableTiles[toDestroyBuildingComponent].Contains(tilePosition));
				return anyTilesInRadius;
			});

		var allBuildingsStillValid = dependentBuildings.All((dependentBuilding) =>
		{
			var tilesForBuilding = dependentBuilding.GetOccupiedCellPositions();
			var buildingsToCheck = buildingToBuildableTiles.Keys
				.Where((key) => key != toDestroyBuildingComponent && key != dependentBuilding);

			return tilesForBuilding.All((tilePosition) =>
			{
				var tileIsInSet = buildingsToCheck
					.Any((buildingComponent) => buildingToBuildableTiles[buildingComponent].Contains(tilePosition));
				return tileIsInSet;
			});
		});

		if (!allBuildingsStillValid)
		{
			return true;
		}

		return false;
	}

	private bool IsBuildingNetworkConnected(BuildingComponent toMoveBuildingComponent)
	{
		var baseBuilding = BuildingComponent.GetValidBuildingComponents(this)
			.First((buildingComponent) => buildingComponent.BuildingResource.IsBase);

		var visitedBuildings = new HashSet<BuildingComponent>();
		VisitAllConnectedBuildings(baseBuilding, toMoveBuildingComponent, visitedBuildings);

		var totalBuildingsToVisit = BuildingComponent.GetValidBuildingComponents(this)
			.Count((buildingComponent) =>
			{
				return buildingComponent != toMoveBuildingComponent && buildingComponent.BuildingResource.BuildableRadius > 0;
			});

		return totalBuildingsToVisit == visitedBuildings.Count;
	}

	private bool IsRobotNetworkConnected(BuildingComponent toMoveBuildingComponent, List<Vector2I> robotCoverageAtDestination)
	{
		var baseBuilding = BuildingComponent.GetValidBuildingComponents(this)
			.First((buildingComponent) => buildingComponent.BuildingResource.IsBase);

		var visitedBuildings = new HashSet<BuildingComponent>();
		VisitAllConnectedRobots(baseBuilding, toMoveBuildingComponent, robotCoverageAtDestination, visitedBuildings);

		if(visitedBuildings.Contains(baseBuilding))
		{
			return true;
		}
		else
		{
			return false;
		}
	}

	private void VisitAllConnectedRobots(BuildingComponent rootBuilding, BuildingComponent toMoveRobot ,List<Vector2I> robotCoverageAtDestination, HashSet<BuildingComponent> visitedBuildings)
	{
		var connectedRobots = BuildingComponent.GetNonDangerBuildingComponents(this)
			.Where((buildingComponent) =>
			{
				if (buildingComponent.BuildingResource.BuildableRadius == 0) return false;
				if (visitedBuildings.Contains(buildingComponent)) return false;

				var anyTilesInRadius = GetValidTilesInRadius(buildingComponent.GetTileArea(), buildingComponent.BuildingResource.BuildableRadius)
					.Any((tilePosition) => robotCoverageAtDestination.Contains(tilePosition));
				return buildingComponent != toMoveRobot && anyTilesInRadius;
			}).ToList();


		visitedBuildings.UnionWith(connectedRobots);
		if (visitedBuildings.Contains(rootBuilding)) return;

		foreach (var connectedRobot in connectedRobots)
		{
			VisitAllConnectedRobots(rootBuilding, connectedRobot, GetValidTilesInRadius(connectedRobot.GetTileArea(), connectedRobot.BuildingResource.BuildableRadius), visitedBuildings);
		}
	}

	private void VisitAllConnectedBuildings(BuildingComponent rootBuilding,	BuildingComponent excludeBuilding, HashSet<BuildingComponent> visitedBuildings
	)
	{
		var dependentBuildings = BuildingComponent.GetNonDangerBuildingComponents(this)
			.Where((buildingComponent) =>
			{
				if (buildingComponent.BuildingResource.BuildableRadius == 0) return false;
				if (visitedBuildings.Contains(buildingComponent)) return false;

				var anyTilesInRadius = buildingComponent.GetOccupiedCellPositions()
					.All((tilePosition) => buildingToBuildableTiles[rootBuilding].Contains(tilePosition));
				return buildingComponent != excludeBuilding && anyTilesInRadius;
			}).ToList();

		visitedBuildings.UnionWith(dependentBuildings);
		foreach (var dependentBuilding in dependentBuildings)
		{
			VisitAllConnectedBuildings(dependentBuilding, excludeBuilding, visitedBuildings);
		}
	}

	private HashSet<Vector2I> GetBuildableTileSet(bool isAttackTiles = false)
	{
		return isAttackTiles ? validBuildableAttackTiles : validBuildableTiles;
	}

	private List<TileMapLayer> GetAllTilemapLayers(Node2D rootNode)
	{
		var result = new List<TileMapLayer>();
		var children = rootNode.GetChildren();
		children.Reverse();
		foreach (var child in children)
		{
			if (child is Node2D childNode)
			{
				result.AddRange(GetAllTilemapLayers(childNode));
			}
		}

		if (rootNode is TileMapLayer tileMapLayer)
		{
			result.Add(tileMapLayer);
		}
		return result;
	}
	private void MapTileMapLayersToElevationLayers()
	{
		foreach (var layer in allTilemapLayers)
		{
			ElevationLayer elevationLayer;
			Node startNode = layer;
			do
			{
				var parent = startNode.GetParent();
				elevationLayer = parent as ElevationLayer;
				startNode = parent;
			} while (elevationLayer == null && startNode != null);

			tileMapLayerToElevationLayer[layer] = elevationLayer;
		}
	}

	private void UpdateDangerOccupiedTiles(BuildingComponent buildingComponent)
	{
		occupiedTiles.UnionWith(buildingComponent.GetOccupiedCellPositions());

		if (buildingComponent.BuildingResource.IsDangerBuilding())
		{
			var tileArea = buildingComponent.GetTileArea();
			var tilesInRadius = GetValidTilesInRadius(tileArea, buildingComponent.BuildingResource.DangerRadius).ToHashSet();

			dangerBuildingToTiles[buildingComponent] = tilesInRadius.ToHashSet();

			if (!buildingComponent.IsDisabled)
			{
				tilesInRadius.ExceptWith(occupiedTiles);
				dangerOccupiedTiles.UnionWith(tilesInRadius);
			}
		}
	}

	private void UpdateValidBuildableTiles(BuildingComponent buildingComponent)
	{
		occupiedTiles.UnionWith(buildingComponent.GetOccupiedCellPositions());
		var tileArea = buildingComponent.GetTileArea();

		if (buildingComponent.BuildingResource.BuildableRadius > 0)
		{
			var allTiles = GetTilesInRadius(tileArea, buildingComponent.BuildingResource.BuildableRadius, (_) => true);
			allTilesInBuildingRadius.UnionWith(allTiles);

			var validTiles = GetValidTilesInRadius(tileArea, buildingComponent.BuildingResource.BuildableRadius);
			var validTilesPlusOne = GetValidTilesInRadius(tileArea, buildingComponent.BuildingResource.BuildableRadius + 1);
			buildingToBuildableTiles[buildingComponent] = validTiles.ToHashSet();
			validBuildableTiles.UnionWith(validTiles);
		}

		validBuildableTiles.ExceptWith(occupiedTiles);
		validBuildableAttackTiles.UnionWith(validBuildableTiles);

		validBuildableTiles.ExceptWith(dangerOccupiedTiles);
		EmitSignal(SignalName.GridStateUpdated);
	}

	private void SetBaseAntennaCoverage()
	{
		
		var buildingComponents = BuildingComponent.GetBaseBuilding(this);
		foreach(var buildingComponent in buildingComponents)
		{
			var baseOccupiedTiles = buildingComponent.GetOccupiedCellPositions();
			var tileArea = buildingComponent.GetTileArea();
			var allTiles = GetTilesInRadius(tileArea, buildingComponent.BuildingResource.BuildableRadius, (_) => true);
			baseAntennaCoveredTiles = allTiles.ToHashSet();
		}
	}

	private void UpdateCollectedResourceTiles(BuildingComponent buildingComponent)
	{
		var tileArea = buildingComponent.GetTileArea();
		var resourceTiles = GetResourceTilesInRadius(tileArea, buildingComponent.BuildingResource.ResourceRadius);

		var oldResourceTileCount = collectedResourceTiles.Count;
		collectedResourceTiles.UnionWith(resourceTiles);

		if (oldResourceTileCount != collectedResourceTiles.Count)
		{
			EmitSignal(SignalName.ResourceTilesUpdated, collectedResourceTiles.Count);
		}
		EmitSignal(SignalName.GridStateUpdated);
	}

	private void UpdateAttackTiles(BuildingComponent buildingComponent)
	{
		if (!buildingComponent.BuildingResource.IsAttackBuilding()) return;

		var tileArea = buildingComponent.GetTileArea();
		var newAttackTiles = GetTilesInRadius(tileArea, buildingComponent.BuildingResource.AttackRadius, (_) => true)
			.ToHashSet();
		attackBuildingToTiles[buildingComponent] = newAttackTiles;
		attackTiles.UnionWith(newAttackTiles);
	}

	private void RecalculateGrid()
	{
		occupiedTiles.Clear();
		validBuildableTiles.Clear();
		validBuildableAttackTiles.Clear();
		allTilesInBuildingRadius.Clear();
		collectedResourceTiles.Clear();
		dangerOccupiedTiles.Clear();
		attackTiles.Clear();
		buildingToBuildableTiles.Clear();
		dangerBuildingToTiles.Clear();
		attackBuildingToTiles.Clear();

		var buildingComponents = BuildingComponent.GetValidBuildingComponents(this);

		foreach (var buildingComponent in buildingComponents)
		{
			UpdateBuildingComponentGridState(buildingComponent);
		}

		CheckDangerBuildingDestruction();

		EmitSignal(SignalName.ResourceTilesUpdated, collectedResourceTiles.Count);
		EmitSignal(SignalName.GridStateUpdated);
	}

	private void RecalculateDangerOccupiedTiles()
	{
		dangerOccupiedTiles.Clear();
		var dangerBuildings = BuildingComponent.GetDangerBuildingComponents(this);
		foreach (var building in dangerBuildings)
		{
			UpdateDangerOccupiedTiles(building);
		}
	}

	private void CheckDangerBuildingDestruction()
	{
		var dangerBuildings = BuildingComponent.GetDangerBuildingComponents(this);
		foreach (var building in dangerBuildings)
		{
			var tileArea = building.GetTileArea();
			var isInsideAttackTile = tileArea.ToTiles().Any((tilePosition) => attackTiles.Contains(tilePosition));
			if (isInsideAttackTile)
			{
				building.Disable();
			}
			else
			{
				building.Enable();
			}
		}
	}

	private bool IsTileInsideCircle(Vector2 centerPosition, Vector2 tilePosition, float radius)
	{
		var distanceX = centerPosition.X - (tilePosition.X + .5);
		var distanceY = centerPosition.Y - (tilePosition.Y + .5);
		var distanceSquared = (distanceX * distanceX) + (distanceY * distanceY);
		return distanceSquared <= radius * radius;
	}

	private List<Vector2I> GetTilesInRadius(Rect2I tileArea, int radius, Func<Vector2I, bool> filterFn)
	{
		var result = new List<Vector2I>();
		var tileAreaF = tileArea.ToRect2F();
		var tileAreaCenter = tileAreaF.GetCenter();
		var radiusMod = Mathf.Max(tileAreaF.Size.X, tileAreaF.Size.Y) / 2;

		for (var x = tileArea.Position.X - radius; x < tileArea.End.X + radius; x++)
		{
			for (var y = tileArea.Position.Y - radius; y < tileArea.End.Y + radius; y++)
			{
				var tilePosition = new Vector2I(x, y);
				if (!IsTileInsideCircle(tileAreaCenter, tilePosition, radius + radiusMod) || !filterFn(tilePosition)) continue;
				result.Add(tilePosition);
			}
		}
		return result;
	}

	private List<Vector2I> GetValidTilesInRadius(Rect2I tileArea, int radius)
	{
		return GetTilesInRadius(tileArea, radius, (tilePosition) =>
		{
			return GetTileCustomData(tilePosition, IS_BUILDABLE).Item2 || tilePosition == goldMinePosition;
		});
	}

	private List<Vector2I> GetResourceTilesInRadius(Rect2I tileArea, int radius)
	{
		return GetTilesInRadius(tileArea, radius, (tilePosition) =>
		{
			return GetTileCustomData(tilePosition, IS_WOOD).Item2;
		});
	}

	public void UpdateBuildingComponentGridState(BuildingComponent buildingComponent)
	{
		var buildingOccupiedTiles = buildingComponent.GetOccupiedCellPositions();
		UpdateDangerOccupiedTiles(buildingComponent);
		UpdateValidBuildableTiles(buildingComponent);
		UpdateCollectedResourceTiles(buildingComponent);
		UpdateAttackTiles(buildingComponent);
	}

	private void OnBuildingPlaced(BuildingComponent buildingComponent)
	{
		UpdateBuildingComponentGridState(buildingComponent);
		if(baseAntennaCoveredTiles.Count() == 0)
		{
			SetBaseAntennaCoverage();
		}
		CheckDangerBuildingDestruction();
	}

	private void OnBuildingMoved(BuildingComponent buildingComponent)
	{
		ClearHighlightedTiles();
		RecalculateGrid();
		//UpdateBuildingComponentGridState(buildingComponent);
		HighlightBuildableTiles();
	}

	private void OnBuildingDestroyed(BuildingComponent buildingComponent)
	{
		RecalculateGrid();
	}

	private void OnBuildingEnabled(BuildingComponent buildingComponent)
	{
		UpdateBuildingComponentGridState(buildingComponent);
	}

	private void OnBuildingDisabled(BuildingComponent buildingComponent)
	{
		RecalculateGrid();
	}
}
