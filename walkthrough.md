# JigsawPuzzle Linear Walkthrough

## 讲解计划

这份文档按“程序真实跑起来的顺序”来讲，而不是按文件名顺序平铺：

1. 先确认项目是什么、场景怎么启动。
2. 再看运行时入口 `PuzzleBootstrap`。
3. 然后进入总控 `PuzzleGameController`，因为它串起了几乎全部核心逻辑。
4. 在总控器内部，再拆出配置、棋盘、状态模型、交换规则、自动合并规则。
5. 最后回到输入层和表现层，把玩家的一次拖拽完整走一遍。

文中的代码片段都来自当前仓库实际文件；我先用终端读取，再嵌入到这里，没有手写代码。

## 项目概述

这个项目是一个 Unity 6 的 2D 拼图原型。它的玩法不是“自由拖动拼图块到正确位置自动吸附”，而是：

- 程序启动后，把一张完整图片按网格切成很多小块。
- 每个拼图块都知道自己“正确应该在哪个格子”。
- 开局时，拼图块会被打乱到别的格子里。
- 玩家拖动时，拖动的不是单块的最终位置，而是某个块所在的整组。
- 松手后，系统会尝试把这整组平移到目标格子，并把被占住的其他块交换到腾出来的格子。
- 如果若干块在棋盘上的相对位置，恰好等于它们在正确答案里的相对位置，它们会自动合并为同一组。

一句话概括：这是一套“基于格子状态和分组规则的拼图系统”，画面只是状态的投影。

先看仓库自述：

```md
# Jigsaw-Puzzle

A Unity 6 jigsaw puzzle prototype with runtime image slicing, group merging, and group swapping.
```

这段来自 [README.md](/Users/linkunkun/JigsawPuzzle/README.md#L1)。

再看 Unity 版本：

```text
m_EditorVersion: 6000.0.60f1
m_EditorVersionWithRevision: 6000.0.60f1 (61dfb374e36f)
```

这段来自 [ProjectVersion.txt](/Users/linkunkun/JigsawPuzzle/ProjectSettings/ProjectVersion.txt#L1)。

## 核心架构

从职责上，这个项目可以分成五层：

- 启动层：`PuzzleBootstrap`
- 编排层：`PuzzleGameController`
- 配置层：`PuzzleLevelConfig`
- 规则层：`PuzzleBoardController`、`PuzzleRuntimeModels`、`PuzzleSwapResolver`、`PuzzleMergeResolver`
- 表现与输入层：`PuzzleSpriteSlicer`、`PuzzlePieceView`、`PuzzleInputController`

系统主流程可以画成这样：

`场景加载` -> `PuzzleBootstrap 安装运行时对象` -> `PuzzleGameController 构建棋盘与状态` -> `PuzzleInputController 采集输入` -> `PuzzleGameController 记录拖拽上下文` -> `PuzzleSwapResolver 计算交换方案` -> `PuzzleMergeResolver 自动重算分组` -> `PuzzlePieceView 刷新画面`

这套设计里最重要的思想是：真相不在屏幕坐标，而在状态映射。

- `PieceId -> CurrentCellIndex` 表示某块当前在棋盘哪个格子。
- `PieceId -> CorrectCellIndex` 表示某块正确答案应该在哪。
- `PieceId -> GroupId` 表示当前哪些块已经被系统认定为一个整体。
- `CellIndex -> PieceId` 表示每个格子当前被谁占据。

只要这些映射对，视图随时都能重建。

## 线性代码讲解

### 1. 项目是怎么启动的

Unity 构建设置里只有一个场景被启用：

```yaml
EditorBuildSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Scenes:
  - enabled: 1
    path: Assets/Scenes/SampleScene.unity
    guid: 99c9720ab356a0642a771bea13969a05
  m_configObjects:
    com.unity.input.settings.actions: {fileID: -944628639613478452, guid: 052faaac586de48259a63d0c4782560b, type: 3}
  m_UseUCBPForAssetBundles: 0
```

这段来自 [EditorBuildSettings.asset](/Users/linkunkun/JigsawPuzzle/ProjectSettings/EditorBuildSettings.asset#L4)。

这里能读出两个关键信息：

- 当前项目只依赖 `Assets/Scenes/SampleScene.unity` 这个场景。
- 项目启用了新版 Input System 的动作配置对象。

包依赖里也能看到这一点：

```json
{
  "dependencies": {
    "com.unity.2d.sprite": "1.0.0",
    "com.unity.ai.navigation": "2.0.9",
    "com.unity.collab-proxy": "2.9.3",
    "com.unity.ide.rider": "3.0.38",
    "com.unity.ide.visualstudio": "2.0.23",
    "com.unity.inputsystem": "1.14.2",
    "com.unity.multiplayer.center": "1.0.0",
    "com.unity.render-pipelines.universal": "17.0.4",
    "com.unity.test-framework": "1.6.0",
    "com.unity.timeline": "1.8.9",
    "com.unity.ugui": "2.0.0",
    "com.unity.visualscripting": "1.9.7"
  }
}
```

这段来自 [manifest.json](/Users/linkunkun/JigsawPuzzle/Packages/manifest.json#L1) 的连续片段。

虽然场景里有一个主相机，但核心玩法对象并不是手工摆在场景里的预制体；真正入口在运行时代码里。

### 2. 运行时入口：`PuzzleBootstrap`

文件作用：
它是“自动安装器”。只要场景加载完成，如果还没有 `PuzzleGameController`，它就动态创建一套运行时系统。

核心代码片段：

```csharp
public static class PuzzleBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Object.FindFirstObjectByType<PuzzleGameController>() != null)
        {
            return;
        }

        GameObject root = new GameObject("PuzzleRuntime");
        root.AddComponent<PuzzleGameController>();
        root.AddComponent<PuzzleInputController>();
    }
}
```

这段来自 [PuzzleBootstrap.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleBootstrap.cs#L6)。

怎么理解它：

- `RuntimeInitializeOnLoadMethod(...AfterSceneLoad)` 让这个入口不依赖场景里手工挂脚本。
- `FindFirstObjectByType<PuzzleGameController>()` 是防重复安装。
- 运行时根对象叫 `PuzzleRuntime`，上面只挂两个组件：
  - `PuzzleGameController`，负责游戏编排。
  - `PuzzleInputController`，负责输入采集。

这是一种很典型的“空场景也能自举”的做法，适合原型阶段快速迭代。

### 3. 总控器的依赖图：`PuzzleGameController`

文件作用：
它是整个玩法系统的大脑，负责启动、建局、拖拽、换位、合并、通关和简单 HUD。

先看字段：

```csharp
public sealed class PuzzleGameController : MonoBehaviour
{
    private const string DefaultLevelResourcePath = "DefaultPuzzleLevel";
    private const string DefaultSpriteResourcePath = "Puzzle/demo_puzzle_source";
    private const string DefaultSpriteAssetPath = "Assets/Puzzle/demo_puzzle_source.png";

    [SerializeField] private PuzzleLevelConfig levelConfig;

    private readonly PuzzleSpriteSlicer slicer = new PuzzleSpriteSlicer();
    private readonly PuzzleSwapResolver swapResolver = new PuzzleSwapResolver();
    private readonly PuzzleMergeResolver mergeResolver = new PuzzleMergeResolver();
    private readonly Dictionary<int, PuzzlePieceView> pieceViews = new Dictionary<int, PuzzlePieceView>();

    private PuzzleBoardController board;
    private PuzzleState state;
    private PuzzleDragContext? activeDrag;
    private System.Random random;
    private Sprite sourceSprite;
    private bool isSolved;
    private string statusText;

    public Camera WorldCamera { get; private set; }
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L13)。

这一组字段已经把架构讲明白了：

- `levelConfig` 是静态配置入口。
- `slicer` 负责切图。
- `swapResolver` 负责一次移动是否合法，以及交换怎么排。
- `mergeResolver` 负责自动合组。
- `pieceViews` 是逻辑状态到 GameObject 的映射。
- `board` 是几何规则。
- `state` 是当前对局的事实源。
- `activeDrag` 是拖拽过程中的临时上下文。

也就是说，`PuzzleGameController` 自己并不深度承载所有规则，而是做编排器。

### 4. 启动阶段：先准备环境，再建局

```csharp
private void Awake()
{
    random = new System.Random();
    WorldCamera = EnsureCamera();
    EnsureBackgroundColor();
}

private void Start()
{
    BuildGame();
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L36)。

这两个生命周期分工很清楚：

- `Awake` 只做运行环境准备。
- `Start` 才真正开始构造棋盘和拼图。

这能避免对象初始化顺序太乱，也让“相机是否存在”这种问题在建局之前就解决掉。

### 5. `BuildGame`：从资源和配置构造一局游戏

```csharp
private void BuildGame()
{
    if (levelConfig == null)
    {
        levelConfig = Resources.Load<PuzzleLevelConfig>(DefaultLevelResourcePath);
    }

    sourceSprite = ResolveSourceSprite();
    PuzzleLevelConfig config = levelConfig != null ? levelConfig : PuzzleLevelConfig.CreateFallback(sourceSprite);

    BuildBoard(config, sourceSprite);
    BuildPieces(config, sourceSprite);
    RefreshAllPieceWorldPositions();
    statusText = "Swap groups to solve the board";
    isSolved = state.IsSolved();
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L118)。

这段很值得学，因为它把“正式资源路径”和“兜底路径”都照顾到了：

- 优先使用 Inspector 配好的 `levelConfig`。
- 如果没配，就尝试从 `Resources/DefaultPuzzleLevel` 载入。
- 如果配置还是没有，就临时创建 fallback 配置。
- 图片资源也走同样的兜底思路。

这意味着这个项目非常强调“无论怎样都尽量能跑起来”。

### 6. 棋盘几何：`BuildBoard` 和 `PuzzleBoardController`

先看总控里如何创建棋盘：

```csharp
private void BuildBoard(PuzzleLevelConfig config, Sprite sprite)
{
    float aspect = sprite.bounds.size.x / sprite.bounds.size.y;
    float maxBoardHeight = 7.0f;
    float maxBoardWidth = 4.6f;
    float boardHeight = maxBoardHeight;
    float boardWidth = boardHeight * aspect;
    if (boardWidth > maxBoardWidth)
    {
        boardWidth = maxBoardWidth;
        boardHeight = boardWidth / aspect;
    }

    Vector3 boardCenter = new Vector3(0f, -0.45f, 0f);
    board = new PuzzleBoardController(config.Rows, config.Columns, boardWidth, boardHeight, boardCenter);
    FitCameraToBoard();
    CreateBoardVisual();
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L135)。

这里做了三件事：

- 根据原图宽高比决定棋盘宽高。
- 限制棋盘的最大高度和宽度，避免超出屏幕。
- 把几何计算委托给 `PuzzleBoardController`。

`PuzzleBoardController` 本身只关心“格子数学”：

```csharp
public sealed class PuzzleBoardController
{
    public PuzzleBoardController(int rows, int columns, float boardWidth, float boardHeight, Vector3 center)
    {
        Rows = rows;
        Columns = columns;
        CellWidth = boardWidth / columns;
        CellHeight = boardHeight / rows;
        BoardWidth = boardWidth;
        BoardHeight = boardHeight;
        Center = center;
        BottomLeft = center - new Vector3(boardWidth * 0.5f, boardHeight * 0.5f, 0f);
    }

    public int GetCellIndex(int row, int column) => row * Columns + column;

    public Vector2Int GetCellCoords(int cellIndex)
    {
        return new Vector2Int(cellIndex / Columns, cellIndex % Columns);
    }

    public Vector3 GetCellWorldPosition(int cellIndex)
    {
        Vector2Int coords = GetCellCoords(cellIndex);
        float x = BottomLeft.x + (coords.y + 0.5f) * CellWidth;
        float y = BottomLeft.y + BoardHeight - (coords.x + 0.5f) * CellHeight;
        return new Vector3(x, y, 0f);
    }

    public bool TryGetCellIndex(Vector3 worldPosition, out int cellIndex)
    {
        float localX = worldPosition.x - BottomLeft.x;
        float localY = (BottomLeft.y + BoardHeight) - worldPosition.y;

        int column = Mathf.FloorToInt(localX / CellWidth);
        int row = Mathf.FloorToInt(localY / CellHeight);

        if (row < 0 || row >= Rows || column < 0 || column >= Columns)
        {
            cellIndex = -1;
            return false;
        }

        cellIndex = GetCellIndex(row, column);
        return true;
    }
```

这段来自 [PuzzleBoardController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleBoardController.cs#L5)。

为什么这个类重要：

- 它把“第几行第几列”和“世界坐标”之间的映射集中管理。
- 它不依赖 MonoBehaviour，也不依赖视图对象，所以很好测试。
- 拖拽时，世界坐标要转成格子；刷新画面时，格子又要转回世界坐标，这个类就是中间桥梁。

你可以把它看成“棋盘坐标系服务”。

### 7. 开局状态是怎么生成的：`PuzzleLevelConfig`

`PuzzleGameController.BuildPieces` 会先向配置要一个初始排列，所以我们先看配置类。

```csharp
public sealed class PuzzleLevelConfig : ScriptableObject
{
    [SerializeField] private Sprite sourceSprite;
    [SerializeField] private int rows = 3;
    [SerializeField] private int columns = 3;
    [SerializeField] private PuzzleInitialLayoutMode initialLayoutMode = PuzzleInitialLayoutMode.Random;
    [SerializeField] private int minMisplacedCount = 6;
    [SerializeField] private bool showPieceIndex = true;
    [SerializeField] private int[] presetArrangement = Array.Empty<int>();

    public Sprite SourceSprite => sourceSprite;
    public int Rows => Mathf.Max(2, rows);
    public int Columns => Mathf.Max(2, columns);
    public PuzzleInitialLayoutMode InitialLayoutMode => initialLayoutMode;
    public int MinMisplacedCount => Mathf.Max(1, minMisplacedCount);
    public bool ShowPieceIndex => showPieceIndex;
    public int PieceCount => Rows * Columns;

    public int[] CreateInitialArrangement(System.Random random)
    {
        int pieceCount = PieceCount;

        if (initialLayoutMode == PuzzleInitialLayoutMode.Preset &&
            presetArrangement != null &&
            presetArrangement.Length == pieceCount &&
            IsValidPermutation(presetArrangement, pieceCount))
        {
            int[] arrangement = new int[pieceCount];
            Array.Copy(presetArrangement, arrangement, pieceCount);
            return arrangement;
        }

        int[] shuffled = CreateIdentity(pieceCount);
        int targetMisplaced = Mathf.Min(MinMisplacedCount, pieceCount);

        for (int attempt = 0; attempt < 64; attempt++)
        {
            Shuffle(shuffled, random);
            if (CountMisplaced(shuffled) >= targetMisplaced)
            {
                return shuffled;
            }
        }

        Array.Reverse(shuffled);
        return shuffled;
    }
```

这段来自 [PuzzleLevelConfig.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Data/PuzzleLevelConfig.cs#L14)。

这个配置类做的不只是“存参数”，它还承担了“生成开局排列”的职责：

- 如果选 `Preset`，并且 `presetArrangement` 是合法排列，就直接使用它。
- 否则就随机洗牌。
- 洗牌不是盲目随机，而是要求至少有 `minMisplacedCount` 个拼图块放错。
- 连试 64 次还不满足，就直接反转数组兜底。

这设计得很务实：既允许关卡设计，也保证随机局不会太接近已解状态。

### 8. 真实状态模型：`PuzzleRuntimeModels`

开局前后，系统真正依赖的是这些运行时模型：

```csharp
public sealed class PuzzlePieceState
{
    public int PieceId;
    public int CorrectCellIndex;
    public int CurrentCellIndex;
    public int GroupId;
    public int CorrectRow;
    public int CorrectColumn;
}

public sealed class PuzzleGroupState
{
    public int GroupId;
    public readonly List<int> PieceIds = new List<int>();
}

public sealed class PuzzleState
{
    public readonly Dictionary<int, PuzzlePieceState> Pieces = new Dictionary<int, PuzzlePieceState>();
    public readonly Dictionary<int, PuzzleGroupState> Groups = new Dictionary<int, PuzzleGroupState>();
    public readonly Dictionary<int, int> CellToPiece = new Dictionary<int, int>();

    public PuzzlePieceState GetPiece(int pieceId) => Pieces[pieceId];

    public PuzzleGroupState GetGroup(int groupId) => Groups[groupId];

    public IEnumerable<int> GetGroupPieceIds(int groupId) => Groups[groupId].PieceIds;

    public bool IsSolved()
    {
        foreach (PuzzlePieceState piece in Pieces.Values)
        {
            if (piece.CurrentCellIndex != piece.CorrectCellIndex)
            {
                return false;
            }
        }

        return true;
    }

    public void RebuildGroups()
    {
        Groups.Clear();
        foreach (PuzzlePieceState piece in Pieces.Values)
        {
            if (!Groups.TryGetValue(piece.GroupId, out PuzzleGroupState group))
            {
                group = new PuzzleGroupState { GroupId = piece.GroupId };
                Groups.Add(group.GroupId, group);
            }

            group.PieceIds.Add(piece.PieceId);
        }
    }
}
```

这段来自 [PuzzleRuntimeModels.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleRuntimeModels.cs#L6)。

这里最值得注意的是三个字典：

- `Pieces`：按 `PieceId` 查某块的完整状态。
- `Groups`：按 `GroupId` 查这个组包含哪些块。
- `CellToPiece`：按格子查当前是谁占着。

这三个映射组合起来，能支持：

- 判断某块在哪里。
- 判断某组有哪些成员。
- 判断目标格子被谁占着。
- 判断是否已经通关。

另外两个只读结构体也很关键：

```csharp
public readonly struct PuzzleMovePlan
{
    public PuzzleMovePlan(
        int movingGroupId,
        int anchorPieceId,
        Dictionary<int, int> pieceToCellAssignments,
        HashSet<int> displacedPieceIds)
    {
        MovingGroupId = movingGroupId;
        AnchorPieceId = anchorPieceId;
        PieceToCellAssignments = pieceToCellAssignments;
        DisplacedPieceIds = displacedPieceIds;
    }

    public int MovingGroupId { get; }
    public int AnchorPieceId { get; }
    public Dictionary<int, int> PieceToCellAssignments { get; }
    public HashSet<int> DisplacedPieceIds { get; }
}

public readonly struct PuzzleDragContext
{
    public PuzzleDragContext(
        int groupId,
        int anchorPieceId,
        Vector3 pointerWorldStart,
        Vector3 anchorWorldStart,
        Dictionary<int, Vector3> pieceWorldStarts)
    {
        GroupId = groupId;
        AnchorPieceId = anchorPieceId;
        PointerWorldStart = pointerWorldStart;
        AnchorWorldStart = anchorWorldStart;
        PieceWorldStarts = pieceWorldStarts;
    }

    public int GroupId { get; }
    public int AnchorPieceId { get; }
    public Vector3 PointerWorldStart { get; }
    public Vector3 AnchorWorldStart { get; }
    public Dictionary<int, Vector3> PieceWorldStarts { get; }
}
```

这段来自 [PuzzleRuntimeModels.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleRuntimeModels.cs#L63) 的连续片段。

它们分别表示：

- `PuzzleDragContext`：拖拽过程中的临时信息。
- `PuzzleMovePlan`：松手后，一次合法交换应该怎样改状态。

这就是“预览拖拽”和“正式提交”分离的关键。

### 9. `BuildPieces`：把配置、状态和视图接起来

```csharp
private void BuildPieces(PuzzleLevelConfig config, Sprite sprite)
{
    state = new PuzzleState();
    pieceViews.Clear();

    int[] arrangement = config.CreateInitialArrangement(random);
    Transform root = new GameObject("PuzzlePieces").transform;
    root.SetParent(transform, false);

    Vector2 cellSize = new Vector2(board.CellWidth, board.CellHeight);
    for (int pieceId = 0; pieceId < config.PieceCount; pieceId++)
    {
        int correctRow = pieceId / config.Columns;
        int correctColumn = pieceId % config.Columns;
        PuzzlePieceState pieceState = new PuzzlePieceState
        {
            PieceId = pieceId,
            CorrectCellIndex = pieceId,
            GroupId = pieceId,
            CorrectRow = correctRow,
            CorrectColumn = correctColumn
        };
        state.Pieces.Add(pieceId, pieceState);
    }

    for (int cellIndex = 0; cellIndex < arrangement.Length; cellIndex++)
    {
        int pieceId = arrangement[cellIndex];
        PuzzlePieceState piece = state.GetPiece(pieceId);
        piece.CurrentCellIndex = cellIndex;
        state.CellToPiece[cellIndex] = pieceId;
    }

    state.RebuildGroups();
    mergeResolver.ApplyAutoMerges(state, board);

    for (int pieceId = 0; pieceId < config.PieceCount; pieceId++)
    {
        PuzzlePieceState piece = state.GetPiece(pieceId);
        Sprite slice = slicer.CreateSlice(sprite, config.Rows, config.Columns, piece.CorrectRow, piece.CorrectColumn);
        PuzzlePieceView view = PuzzlePieceView.Create(root, $"Piece_{pieceId:00}");
        view.Initialize(pieceId, slice, cellSize, config.ShowPieceIndex);
        pieceViews.Add(pieceId, view);
    }
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L154)。

把它拆开看，逻辑非常顺：

1. 新建 `PuzzleState`。
2. 向配置拿初始排列 `arrangement`。
3. 先为每个 `PieceId` 填好“正确答案信息”。
4. 再根据 `arrangement` 写入“当前在哪个格子”。
5. 重建组信息，并立刻做一次自动合并。
6. 最后才创建视图对象并给每块分配切片图片。

这里一个很好的设计点是：视图是在逻辑状态之后才创建的。也就是说，画面完全是状态的结果，不是状态的来源。

### 10. 运行时切图：`PuzzleSpriteSlicer`

```csharp
public sealed class PuzzleSpriteSlicer
{
    public Sprite CreateSlice(Sprite source, int rows, int columns, int row, int column)
    {
        Rect sourceRect = source.rect;
        float sliceWidth = sourceRect.width / columns;
        float sliceHeight = sourceRect.height / rows;

        Rect sliceRect = new Rect(
            sourceRect.x + column * sliceWidth,
            sourceRect.y + (rows - row - 1) * sliceHeight,
            sliceWidth,
            sliceHeight);

        Vector2 pivot = new Vector2(0.5f, 0.5f);
        return Sprite.Create(source.texture, sliceRect, pivot, source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
    }
}
```

这段来自 [PuzzleSpriteSlicer.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleSpriteSlicer.cs#L5)。

这段的关键点在第 15 行这一类坐标换算：

- 配置里的 `row` 是从上往下理解的。
- 纹理坐标通常是从下往上算的。
- 所以这里用了 `rows - row - 1` 来把行号翻过来。

如果你以后自己改这里，最容易出错的就是 Y 轴方向。

### 11. 拼图块视图：`PuzzlePieceView`

```csharp
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public sealed class PuzzlePieceView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private BoxCollider2D boxCollider;

    private TextMesh indexText;
    private Vector3 defaultScale;

    public int PieceId { get; private set; }

    public static PuzzlePieceView Create(Transform parent, string name)
    {
        GameObject pieceObject = new GameObject(name);
        pieceObject.transform.SetParent(parent, false);
        PuzzlePieceView view = pieceObject.AddComponent<PuzzlePieceView>();
        view.spriteRenderer = pieceObject.GetComponent<SpriteRenderer>();
        view.boxCollider = pieceObject.GetComponent<BoxCollider2D>();
        return view;
    }

    public void Initialize(int pieceId, Sprite sprite, Vector2 targetWorldSize, bool showIndex)
    {
        PieceId = pieceId;
        spriteRenderer.sprite = sprite;
        spriteRenderer.color = Color.white;
        spriteRenderer.sortingOrder = 10;

        Vector2 spriteSize = sprite.bounds.size;
        transform.localScale = new Vector3(
            targetWorldSize.x / spriteSize.x,
            targetWorldSize.y / spriteSize.y,
            1f);
        defaultScale = transform.localScale;

        boxCollider.size = sprite.bounds.size;
        boxCollider.offset = sprite.bounds.center;

        if (showIndex)
        {
            indexText = CreateIndexText();
            indexText.text = (pieceId + 1).ToString();
        }
    }
```

这段来自 [PuzzlePieceView.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzlePieceView.cs#L5)。

这个类的职责很纯：

- 拿到切好的 `Sprite` 后负责把它显示出来。
- 调整缩放，让切片正好铺满一个棋盘格。
- 挂上 `BoxCollider2D` 供点击/触摸命中检测。
- 可选显示编号文字。

选中效果也集中在这里：

```csharp
public void SetSelected(bool isSelected)
{
    spriteRenderer.sortingOrder = isSelected ? 100 : 10;
    transform.localScale = isSelected ? defaultScale * 1.03f : defaultScale;
    spriteRenderer.color = isSelected ? new Color(1f, 0.97f, 0.85f, 1f) : Color.white;

    if (indexText != null)
    {
        MeshRenderer textRenderer = indexText.GetComponent<MeshRenderer>();
        textRenderer.sortingOrder = isSelected ? 101 : 11;
    }
}
```

这段来自 [PuzzlePieceView.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzlePieceView.cs#L56)。

所以“高亮一组”并不是加特效系统，而是直接改排序层级、颜色和缩放，简单但有效。

### 12. 自动合并规则：`PuzzleMergeResolver`

这是整个玩法最有意思的规则之一。它决定哪些块应该变成一个整体。

```csharp
public sealed class PuzzleMergeResolver
{
    public void ApplyAutoMerges(PuzzleState state, PuzzleBoardController board)
    {
        HashSet<int> visitedPieceIds = new HashSet<int>();
        int fallbackGroupId = 0;

        foreach (PuzzlePieceState piece in state.Pieces.Values)
        {
            if (visitedPieceIds.Contains(piece.PieceId))
            {
                continue;
            }

            List<int> component = CollectConnectedComponent(state, board, piece.PieceId, visitedPieceIds);
            int componentGroupId = component.Count > 0 ? GetStableGroupId(component) : fallbackGroupId++;
            foreach (int pieceId in component)
            {
                state.GetPiece(pieceId).GroupId = componentGroupId;
            }
        }

        state.RebuildGroups();
    }
```

这段来自 [PuzzleMergeResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleMergeResolver.cs#L7)。

这段做的不是简单“两块相邻就合并”，而是：

- 从每个未访问的块出发。
- 收集一个连通分量。
- 分量中的块共享同一个 `GroupId`。
- 最后统一 `RebuildGroups()`。

“连通”的判定在这里：

```csharp
private static bool ArePiecesCorrectNeighbors(PuzzlePieceState a, PuzzlePieceState b, PuzzleBoardController board)
{
    Vector2Int currentA = board.GetCellCoords(a.CurrentCellIndex);
    Vector2Int currentB = board.GetCellCoords(b.CurrentCellIndex);
    Vector2Int currentDelta = currentA - currentB;
    Vector2Int correctDelta = new Vector2Int(a.CorrectRow - b.CorrectRow, a.CorrectColumn - b.CorrectColumn);

    if (Mathf.Abs(currentDelta.x) + Mathf.Abs(currentDelta.y) != 1)
    {
        return false;
    }

    if (Mathf.Abs(correctDelta.x) + Mathf.Abs(correctDelta.y) != 1)
    {
        return false;
    }

    return currentDelta == correctDelta;
}
```

这段来自 [PuzzleMergeResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleMergeResolver.cs#L32)。

这段非常关键。它要求同时满足两件事：

- 当前棋盘上必须是正交相邻。
- 正确答案里也必须是正交相邻。
- 而且两者的方向差必须完全一致。

比如 A 在正确答案里应该在 B 左边，那么它们当前也必须保持这个左-右关系，才会被视为“拼对了”。

收集连通块时用的是 BFS：

```csharp
private static List<int> CollectConnectedComponent(
    PuzzleState state,
    PuzzleBoardController board,
    int rootPieceId,
    HashSet<int> visitedPieceIds)
{
    List<int> component = new List<int>();
    Queue<int> queue = new Queue<int>();
    queue.Enqueue(rootPieceId);
    visitedPieceIds.Add(rootPieceId);

    while (queue.Count > 0)
    {
        int pieceId = queue.Dequeue();
        component.Add(pieceId);

        PuzzlePieceState piece = state.GetPiece(pieceId);
        Vector2Int coords = board.GetCellCoords(piece.CurrentCellIndex);
        EnqueueNeighbor(state, board, piece, coords.x - 1, coords.y, visitedPieceIds, queue);
        EnqueueNeighbor(state, board, piece, coords.x + 1, coords.y, visitedPieceIds, queue);
        EnqueueNeighbor(state, board, piece, coords.x, coords.y - 1, visitedPieceIds, queue);
        EnqueueNeighbor(state, board, piece, coords.x, coords.y + 1, visitedPieceIds, queue);
    }

    return component;
}
```

这段来自 [PuzzleMergeResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleMergeResolver.cs#L52)。

所以本质上，自动合并是在“当前局面图”上找满足正确相对关系的连通区域。

### 13. 拖拽开始：只记录上下文，不改真实状态

```csharp
public bool TryBeginDrag(int pieceId, Vector3 pointerWorldPosition)
{
    if (!CanInteract || !pieceViews.TryGetValue(pieceId, out PuzzlePieceView view))
    {
        return false;
    }

    PuzzlePieceState piece = state.GetPiece(pieceId);
    List<int> groupPieceIds = state.GetGroup(piece.GroupId).PieceIds;
    Dictionary<int, Vector3> pieceWorldStarts = new Dictionary<int, Vector3>();
    foreach (int groupPieceId in groupPieceIds)
    {
        PuzzlePieceView pieceView = pieceViews[groupPieceId];
        pieceWorldStarts[groupPieceId] = pieceView.transform.position;
        pieceView.SetSelected(true);
    }

    activeDrag = new PuzzleDragContext(
        piece.GroupId,
        pieceId,
        pointerWorldPosition,
        pieceViews[pieceId].transform.position,
        pieceWorldStarts);

    statusText = $"Dragging group #{piece.GroupId + 1}";
    return true;
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L50)。

这里做得很稳：

- 先通过被点中的块拿到它所在组。
- 把整组所有成员的起始世界坐标保存下来。
- 把整组都设为选中状态。
- 记录一个 `PuzzleDragContext`。

注意：这一步并没有改 `CurrentCellIndex`，说明此时仍然只是视觉拖拽。

拖动过程中也只是位移视图：

```csharp
public void UpdateDrag(Vector3 pointerWorldPosition)
{
    if (!activeDrag.HasValue)
    {
        return;
    }

    PuzzleDragContext drag = activeDrag.Value;
    Vector3 delta = pointerWorldPosition - drag.PointerWorldStart;

    foreach ((int pieceId, Vector3 pieceStart) in drag.PieceWorldStarts)
    {
        pieceViews[pieceId].SetWorldPosition(pieceStart + delta);
    }
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L78)。

### 14. 松手时怎么判定交换：`PuzzleSwapResolver`

松手后，控制器会把目标位置交给交换求解器：

```csharp
public void EndDrag(Vector3 pointerWorldPosition)
{
    if (!activeDrag.HasValue)
    {
        return;
    }

    PuzzleDragContext drag = activeDrag.Value;
    Vector3 delta = pointerWorldPosition - drag.PointerWorldStart;
    Vector3 anchorTargetWorld = drag.AnchorWorldStart + delta;

    if (!board.TryGetCellIndex(anchorTargetWorld, out int targetAnchorCell) ||
        !swapResolver.TryCreateMovePlan(state, board, drag.GroupId, drag.AnchorPieceId, targetAnchorCell, out PuzzleMovePlan movePlan))
    {
        CancelActiveDrag();
        statusText = "Invalid move";
        return;
    }

    ApplyMovePlan(movePlan);
    activeDrag = null;
    statusText = "Move resolved";
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L94)。

这里的关键转换是：

- 指针的世界坐标 -> 锚点目标世界坐标
- 锚点目标世界坐标 -> 目标格子 `targetAnchorCell`
- 当前状态 + 目标格子 -> 一份 `PuzzleMovePlan`

真正的交换算法在这里：

```csharp
public bool TryCreateMovePlan(
    PuzzleState state,
    PuzzleBoardController board,
    int movingGroupId,
    int anchorPieceId,
    int targetAnchorCellIndex,
    out PuzzleMovePlan movePlan)
{
    PuzzlePieceState anchorPiece = state.GetPiece(anchorPieceId);
    Vector2Int anchorCoords = board.GetCellCoords(anchorPiece.CurrentCellIndex);
    Vector2Int targetCoords = board.GetCellCoords(targetAnchorCellIndex);
    Vector2Int delta = targetCoords - anchorCoords;

    if (delta == Vector2Int.zero)
    {
        movePlan = default;
        return false;
    }

    List<int> movingPieceIds = state.GetGroup(movingGroupId).PieceIds;
    Dictionary<int, int> assignments = new Dictionary<int, int>();
    HashSet<int> movingPieceSet = new HashSet<int>(movingPieceIds);
    HashSet<int> targetCells = new HashSet<int>();
    HashSet<int> oldCells = new HashSet<int>();

    foreach (int pieceId in movingPieceIds)
    {
        PuzzlePieceState piece = state.GetPiece(pieceId);
        oldCells.Add(piece.CurrentCellIndex);

        if (!board.TryGetTranslatedCell(piece.CurrentCellIndex, delta, out int translatedCell))
        {
            movePlan = default;
            return false;
        }

        assignments[pieceId] = translatedCell;
        targetCells.Add(translatedCell);
    }

    List<int> displacedPieceIds = new List<int>();
    foreach (int targetCell in targetCells)
    {
        int occupantPieceId = state.CellToPiece[targetCell];
        if (!movingPieceSet.Contains(occupantPieceId))
        {
            displacedPieceIds.Add(occupantPieceId);
        }
    }

    List<int> vacatedCells = oldCells.Where(cell => !targetCells.Contains(cell)).OrderBy(cell => cell).ToList();
    displacedPieceIds = displacedPieceIds.Distinct().OrderBy(pieceId => state.GetPiece(pieceId).CurrentCellIndex).ToList();

    if (displacedPieceIds.Count != vacatedCells.Count)
    {
        movePlan = default;
        return false;
    }

    for (int index = 0; index < displacedPieceIds.Count; index++)
    {
        assignments[displacedPieceIds[index]] = vacatedCells[index];
    }

    movePlan = new PuzzleMovePlan(
        movingGroupId,
        anchorPieceId,
        assignments,
        new HashSet<int>(displacedPieceIds));
    return true;
}
```

这段来自 [PuzzleSwapResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleSwapResolver.cs#L10)。

这段算法的核心思想是“整组平移 + 占位交换”：

- 先根据锚点移动量 `delta`，尝试把整组所有块平移到对应新格子。
- 只要有任意一块平移后越界，整个操作直接非法。
- 统计整组移动后将占据哪些 `targetCells`。
- 找出这些目标格子里原本有哪些“外部块”被占到了。
- 再把这些被挤开的块，按顺序塞回移动组腾出来的 `vacatedCells`。

这不是传统消消乐那种逐格交换，而是一个“组平移置换”。

### 15. 提交移动：更新状态，再重算合组

```csharp
private void ApplyMovePlan(PuzzleMovePlan movePlan)
{
    foreach ((int pieceId, int targetCellIndex) in movePlan.PieceToCellAssignments)
    {
        state.GetPiece(pieceId).CurrentCellIndex = targetCellIndex;
    }

    state.CellToPiece.Clear();
    foreach (PuzzlePieceState piece in state.Pieces.Values)
    {
        state.CellToPiece[piece.CurrentCellIndex] = piece.PieceId;
    }

    mergeResolver.ApplyAutoMerges(state, board);
    RefreshAllPieceWorldPositions();

    isSolved = state.IsSolved();
    if (isSolved)
    {
        statusText = "Puzzle complete!";
    }
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L200)。

一次提交后的顺序也很漂亮：

1. 先批量更新每块的 `CurrentCellIndex`。
2. 重建 `CellToPiece`。
3. 自动重算哪些块现在应该合并成组。
4. 再刷新全部视图坐标。
5. 最后判断是否通关。

注意第 3 步放在第 4 步前面，这意味着刷新画面时，新的组关系已经生效了。

### 15.1 本次会话问答：group 交换与破坏

这里把这次讨论里几个紧挨着的问题一起记下来，避免以后只记住一句结论，却忘了它背后的语义。

问题 1：一个散块，能不能顶掉某个已有 group 里的一块？

当前实现的答案是：能。

问题 2：如果这次移动成功后，会破坏原有的 group，这种情况允许吗？

当前实现的答案也是：允许。

问题 3：为什么会允许？

核心原因有两点：

- `PuzzleSwapResolver.TryCreateMovePlan()` 只关心整组平移后占到哪些格子，以及这些格子里原本有哪些块需要被挤走。它不会检查“被挤走的块是否属于某个已有 group”，也不会保护那个 group 不被拆散。
- `PuzzleGameController.ApplyMovePlan()` 在真正落子后，会立即调用 `PuzzleMergeResolver.ApplyAutoMerges()`，基于新的棋盘局面重新计算连通块和 `GroupId`。所以如果原来的 group 因为这次交换失去了正确邻接关系，它就会被自动打散并重分组。

问题 4：这说明当前代码里的 group 本质上是什么？

它不是“不可破坏的刚体”，而是“当前局面下，由正确邻接关系自动推导出来的一个整体”。也就是说，group 是重算结果，不是受绝对保护的实体。

问题 5：从交互设计上看，这样做推荐吗？

如果只看当前代码行为，这个规则是成立的；但如果从玩家理解成本看，这个规则未必理想。因为玩家已经被训练成“拖的是一个组”，那直觉上通常也会期待“组本身不应被外部单块轻易打散”。否则 group 既像整体，又不像整体，规则感会有一点摇摆。

这也是为什么后续如果要继续迭代玩法，一个很自然的改进方向就是：

- 把任何会打散已有 group 的移动直接判为 `Invalid move`。

对应代码依据：

- [PuzzleSwapResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleSwapResolver.cs#L50) 到 [PuzzleSwapResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleSwapResolver.cs#L78) 会收集 `displacedPieceIds` 并安排它们回填 `vacatedCells`，但没有判断这些块原先是否属于同一组。
- [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L200) 到 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L214) 在应用移动后直接调用 `mergeResolver.ApplyAutoMerges(...)`。
- [PuzzleMergeResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleMergeResolver.cs#L9) 到 [PuzzleMergeResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleMergeResolver.cs#L29) 会按新局面重新扫描连通分量并写回新的 `GroupId`。

### 16. 输入层：鼠标和触摸如何接进总控

```csharp
public sealed class PuzzleInputController : MonoBehaviour
{
    [SerializeField] private PuzzleGameController gameController;

    private int activePointerId = int.MinValue;
    private bool isDragging;

    private void Awake()
    {
        if (gameController == null)
        {
            gameController = GetComponent<PuzzleGameController>();
        }
    }

    private void Update()
    {
        if (gameController == null || !gameController.CanInteract)
        {
            return;
        }

        if (TryGetTouchPointer(out int touchId, out Vector2 touchPosition, out bool touchDown, out bool touchHeld, out bool touchUp))
        {
            HandlePointer(touchId, touchPosition, touchDown, touchHeld, touchUp);
            return;
        }

        if (Mouse.current == null)
        {
            return;
        }

        HandlePointer(
            -1,
            Mouse.current.position.ReadValue(),
            Mouse.current.leftButton.wasPressedThisFrame,
            Mouse.current.leftButton.isPressed,
            Mouse.current.leftButton.wasReleasedThisFrame);
    }
```

这段来自 [PuzzleInputController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleInputController.cs#L8)。

这个控制器的风格很直接：

- 优先处理触摸。
- 没有触摸时退回鼠标。
- 它不自己做玩法判断，只把输入转成 `TryBeginDrag / UpdateDrag / EndDrag` 调用。

命中拼图块的方式也很朴素：

```csharp
private void HandlePointer(int pointerId, Vector2 screenPosition, bool pointerDown, bool pointerHeld, bool pointerUp)
{
    Vector3 worldPosition = gameController.WorldCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -gameController.WorldCamera.transform.position.z));

    if (pointerDown)
    {
        Collider2D hit = Physics2D.OverlapPoint(worldPosition);
        if (hit != null && hit.TryGetComponent(out PuzzlePieceView pieceView))
        {
            isDragging = gameController.TryBeginDrag(pieceView.PieceId, worldPosition);
            activePointerId = isDragging ? pointerId : int.MinValue;
        }
    }

    if (pointerHeld && isDragging && activePointerId == pointerId)
    {
        gameController.UpdateDrag(worldPosition);
    }

    if (pointerUp && isDragging && activePointerId == pointerId)
    {
        gameController.EndDrag(worldPosition);
        isDragging = false;
        activePointerId = int.MinValue;
    }
}
```

这段来自 [PuzzleInputController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleInputController.cs#L49)。

几个值得注意的实现点：

- 使用 `Physics2D.OverlapPoint`，所以每个块必须有 2D Collider。
- `activePointerId` 让触摸场景下只跟踪当前那根手指。
- 输入层并不知道“怎么移动才合法”，它只是把命中的块 ID 和坐标传给总控。

### 17. 相机、棋盘背景和 HUD 都是运行时创建的

相机兜底逻辑：

```csharp
private Camera EnsureCamera()
{
    Camera camera = Camera.main;
    if (camera == null)
    {
        GameObject cameraObject = new GameObject("Main Camera");
        camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
    }

    camera.orthographic = true;
    camera.orthographicSize = 4.7f;
    camera.transform.position = new Vector3(0f, 0f, -10f);
    return camera;
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L274)。

棋盘背景和网格线也不是美术资源，而是动态生成的白块精灵：

```csharp
private void CreateBoardVisual()
{
    Transform boardRoot = new GameObject("PuzzleBoard").transform;
    boardRoot.SetParent(transform, false);

    Sprite whiteSprite = CreateSolidSprite();
    CreateQuad(boardRoot, "BoardBackground", board.Center, new Vector2(board.BoardWidth + 0.12f, board.BoardHeight + 0.12f), new Color(0.15f, 0.18f, 0.24f, 1f), 0, whiteSprite);

    for (int row = 1; row < board.Rows; row++)
    {
        float y = board.BottomLeft.y + board.BoardHeight - row * board.CellHeight;
        CreateQuad(boardRoot, $"Row_{row}", new Vector3(board.Center.x, y, 0f), new Vector2(board.BoardWidth + 0.02f, 0.03f), new Color(0.28f, 0.33f, 0.40f, 1f), 1, whiteSprite);
    }

    for (int column = 1; column < board.Columns; column++)
    {
        float x = board.BottomLeft.x + column * board.CellWidth;
        CreateQuad(boardRoot, $"Column_{column}", new Vector3(x, board.Center.y, 0f), new Vector2(0.03f, board.BoardHeight + 0.02f), new Color(0.28f, 0.33f, 0.40f, 1f), 1, whiteSprite);
    }
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L316)。

HUD 也直接用 `OnGUI`：

```csharp
private void OnGUI()
{
    GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
    {
        fontSize = 16,
        alignment = TextAnchor.MiddleLeft
    };

    GUI.Box(new Rect(16f, 16f, 340f, 84f), $"Auto-Merge Puzzle\n{statusText}", boxStyle);
    if (state != null)
    {
        int solvedCount = state.Pieces.Values.Count(piece => piece.CurrentCellIndex == piece.CorrectCellIndex);
        GUI.Box(new Rect(16f, 108f, 340f, 42f), $"Solved pieces: {solvedCount}/{state.Pieces.Count}", boxStyle);
    }

    if (isSolved)
    {
        GUIStyle solvedStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter
        };
        GUI.Box(new Rect(Screen.width * 0.5f - 180f, 20f, 360f, 56f), "Puzzle Complete", solvedStyle);
    }
}
```

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs#L396)。

这说明作者在原型阶段更偏向“先把玩法说明白”，而不是先搭完整 UI 框架。

## 一次完整拖拽的线性闭环

把所有文件串起来后，玩家的一次拖拽实际经历的是这条链：

1. 场景加载完成后，`PuzzleBootstrap.Install()` 创建 `PuzzleRuntime`。
2. `PuzzleGameController.Start()` 调用 `BuildGame()`。
3. `BuildGame()` 用 `PuzzleLevelConfig` 决定行列、图片和初始排列。
4. `BuildBoard()` 创建 `PuzzleBoardController`，建立格子坐标系。
5. `BuildPieces()` 初始化 `PuzzleState`，记录每块的正确位置和当前位置。
6. `PuzzleSpriteSlicer` 把原图切成若干 `Sprite`。
7. `PuzzlePieceView` 为每个块创建可见对象和碰撞器。
8. `PuzzleMergeResolver` 在开局时先做一次自动合组。
9. 玩家按下时，`PuzzleInputController` 用 `Physics2D.OverlapPoint` 命中某块。
10. `PuzzleGameController.TryBeginDrag()` 找到该块所属组，保存整组起始位置。
11. 拖动过程中，`UpdateDrag()` 只改视图坐标，不改真实状态。
12. 松手时，`EndDrag()` 把锚点目标世界坐标换算成目标格子。
13. `PuzzleSwapResolver.TryCreateMovePlan()` 计算整组平移和被挤出块的交换方案。
14. 如果方案非法，`CancelActiveDrag()` 把整组视图弹回原位。
15. 如果方案合法，`ApplyMovePlan()` 更新 `CurrentCellIndex` 和 `CellToPiece`。
16. `PuzzleMergeResolver.ApplyAutoMerges()` 根据新局面重算组关系。
17. `RefreshAllPieceWorldPositions()` 让视图重新贴回格子中心。
18. `IsSolved()` 检查是否所有块都回到 `CorrectCellIndex`。

这就是这个项目最核心的“线性心智模型”。

## 总结与学习点

这个仓库最值得学的，不是某一个花哨算法，而是它的结构取舍：

- 它把“状态事实”和“画面表现”分开了。拖拽中可以先只动视图，松手后再提交状态。
- 它把复杂逻辑拆成了小而专的类：棋盘几何、切图、交换、合并、输入、视图，各司其职。
- 它的玩法核心不是自由坐标，而是格子映射和组关系，这让规则变得很稳定。
- 自动合并用“当前相对位置是否等于正确相对位置”来判定，这个规则简洁而且可解释。
- 整个项目有很强的原型思维：空场景自举、资源兜底、运行时生成棋盘和 HUD，优先保证“马上能玩”。

如果你要继续深入，我建议下一步重点盯三件事：

- 在脑中始终区分 `CurrentCellIndex`、`CorrectCellIndex`、`GroupId` 这三类信息。
- 用一个 3x3 的小例子手推一次 `PuzzleSwapResolver` 的 `delta / targetCells / vacatedCells`。
- 用一组已经拼对的相邻块，手推一次 `PuzzleMergeResolver` 的 BFS 连通分量过程。

只要这三件事吃透，这个仓库你就不只是“看懂了”，而是真的能自己改。
