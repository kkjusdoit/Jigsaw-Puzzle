# JigsawPuzzle Linear Walkthrough

## 讲解计划

这份讲解按程序真实运行路径展开，而不是按文件字母顺序罗列：

1. 先看项目入口和运行环境，明确 Unity 是怎么启动这套玩法的。
2. 再看 `PuzzleBootstrap`，理解为什么即使场景里没手工挂脚本，游戏也能跑起来。
3. 接着进入 `PuzzleGameController`，它是整个系统的总控器。
4. 在总控器的上下文里，再拆开棋盘、切图、状态模型、交换规则、自动合并规则。
5. 最后回到输入层和表现层，理解玩家的一次拖拽是如何变成一次合法交换的。

---

## 项目概述

这个代码库实现的是一个 2D 拼图游戏，目标不是传统的“自由拖动到正确位置自动吸附”，而是更接近你给我参考教程里的玩法：

- 拼图会在运行时按行列动态切图
- 每个拼图块都有“正确格子”和“当前格子”
- 玩家拖动的对象可能是一块，也可能是一整个已经拼对的组
- 松手后发生的是“整组交换格子”
- 如果两块在当前棋盘上的相对位置，刚好等于它们在正确答案里的相对位置，它们就会自动合并成同一组

所以，这套代码本质上是一个“格子状态机 + 规则求解器 + 轻量视图层”的组合，而不是一个靠物理拖拽、碰撞吸附完成的游戏。

---

## 核心架构

系统流转可以概括成下面这条主线：

`Unity 场景加载` -> `PuzzleBootstrap 自动注入运行时对象` -> `PuzzleGameController 构建棋盘和拼图块` -> `PuzzleInputController 采集鼠标/触摸输入` -> `PuzzleSwapResolver 计算一次交换是否合法` -> `PuzzleMergeResolver 判断是否需要自动合组` -> `PuzzlePieceView 刷新世界坐标和选中状态`

从职责上看，文件可以分成四层：

- 启动层：`PuzzleBootstrap`
- 编排层：`PuzzleGameController`
- 规则层：`PuzzleBoardController`、`PuzzleRuntimeModels`、`PuzzleSwapResolver`、`PuzzleMergeResolver`、`PuzzleLevelConfig`
- 表现层：`PuzzlePieceView`、`PuzzleInputController`、`PuzzleSpriteSlicer`

这是一种很典型的“状态优先、表现跟随”的做法。真正的真相源不是每个物体现在在屏幕哪里，而是：

- 哪个 `PieceId` 当前在哪个 `CellIndex`
- 哪个 `PieceId` 属于哪个 `GroupId`

只要这两个映射稳定，画面永远都能重建出来。

---

## 线性代码讲解

### 1. 项目入口：场景和运行方式

这个项目当前只启用了一个场景：

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1045 &1
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

这段来自 [EditorBuildSettings.asset](/Users/linkunkun/JigsawPuzzle/ProjectSettings/EditorBuildSettings.asset)。

这里最重要的点有两个：

- 只有 `SampleScene` 被加入构建
- 项目启用了 Input System 配置对象

这说明作者的目标不是先搭复杂场景，而是把玩法做成“进入任意基础场景也能自己启动”的形式。这个判断会在后面的 `PuzzleBootstrap` 里得到印证。

项目使用的 Unity 版本也很新：

```text
m_EditorVersion: 6000.0.60f1
m_EditorVersionWithRevision: 6000.0.60f1 (61dfb374e36f)
```

这段来自 [ProjectVersion.txt](/Users/linkunkun/JigsawPuzzle/ProjectSettings/ProjectVersion.txt)。

---

### 2. 真正的运行入口：`PuzzleBootstrap`

文件作用：
这个文件是整个玩法系统的“自动安装器”。它让场景不需要预先摆好对象，只要场景加载完成，就会自动创建一套运行时拼图系统。

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

这段来自 [PuzzleBootstrap.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleBootstrap.cs)。

解析：

- `RuntimeInitializeOnLoadMethod` 表示这不是普通 MonoBehaviour 生命周期，而是 Unity 在场景加载后主动调用的静态入口。
- `AfterSceneLoad` 的选择很关键，说明作者要等场景对象都就位后再决定要不要注入系统。
- `FindFirstObjectByType<PuzzleGameController>()` 是防重逻辑。
  如果你以后手工在场景里放了一个 `PuzzleGameController`，这个自动安装器就不会重复创建。
- `PuzzleRuntime` 这个根对象只挂两个组件：
  - `PuzzleGameController`
  - `PuzzleInputController`

这体现了一种非常明确的架构意图：把“玩法编排”和“输入采集”分成两个组件，但让它们共享一个根节点。

---

### 3. 总控器：`PuzzleGameController`

文件作用：
这个文件是全系统的大脑。它负责启动、建棋盘、建拼图块、响应拖拽、提交交换、触发合并、刷新画面、判定通关。

先看它持有的依赖：

```csharp
public sealed class PuzzleGameController : MonoBehaviour
{
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

这段来自 [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs)。

解析：

- `levelConfig` 是唯一的外部静态输入。
- `slicer`、`swapResolver`、`mergeResolver` 都是纯逻辑或轻表现助手。
- `pieceViews` 是逻辑层到视图层的桥。
- `board` 和 `state` 是两个核心对象：
  - `board` 解决几何和坐标问题
  - `state` 解决当前局面的事实问题
- `activeDrag` 保存一次拖拽的临时上下文，而不是直接在拖动过程中修改真实状态。

这很重要：拖拽中只是“预览移动”，真正状态提交发生在松手后。

#### 3.1 启动阶段

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

解析：

- `Awake` 做环境准备
- `Start` 才真正搭玩法内容

这是一个合理分层：先保证相机存在，再生成棋盘和拼图。

#### 3.2 BuildGame：从配置到完整局面

```csharp
private void BuildGame()
{
    if (levelConfig == null)
    {
        levelConfig = Resources.Load<PuzzleLevelConfig>("DefaultPuzzleLevel");
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

解析：

- 先尝试从 Inspector 拿配置
- 没有配置就尝试从 `Resources/DefaultPuzzleLevel` 读取
- 再没有就创建 fallback 配置和 fallback 图片

这段设计体现了“可运行性优先”：

- 作为正式项目，可以走 `ScriptableObject` 配置
- 作为 demo 或空场景，也不会因为没配资源直接挂掉

#### 3.3 BuildBoard：只负责棋盘几何，不负责状态

```csharp
private void BuildBoard(PuzzleLevelConfig config, Sprite sprite)
{
    float aspect = sprite.bounds.size.x / sprite.bounds.size.y;
    float boardHeight = 6.4f;
    float boardWidth = Mathf.Min(8.4f, boardHeight * aspect);
    if (boardWidth >= 8.4f)
    {
        boardHeight = boardWidth / aspect;
    }

    board = new PuzzleBoardController(config.Rows, config.Columns, boardWidth, boardHeight, Vector3.zero);
    CreateBoardVisual();
}
```

解析：

- 这里的棋盘尺寸不是死写的，它会根据图片长宽比调整
- 目标是让拼图始终在一个合理的世界尺寸内显示
- `PuzzleBoardController` 只关心网格与坐标转换
- `CreateBoardVisual()` 则是纯显示层，用来画底板和网格线

换句话说，作者把“逻辑棋盘”和“视觉棋盘”分开了。

#### 3.4 BuildPieces：创建状态，再创建视图

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

解析：

这段是整个项目最关键的一段之一，因为它把“题目答案”和“当前局面”明确分开了。

- 第一轮循环定义的是每块的“正确答案”
  - `CorrectCellIndex`
  - `CorrectRow`
  - `CorrectColumn`
  - 初始 `GroupId = pieceId`
- 第二轮循环用 `arrangement` 写入“当前局面”
  - 某个格子现在装的是哪个 `pieceId`
  - 某个 `pieceId` 当前落在哪个 `cellIndex`
- 最后才创建每个块的视图

这是一种非常值得学习的顺序：

1. 先建逻辑状态
2. 再建视图对象
3. 最后用逻辑状态驱动画面

这样后续做交换、撤销、回放、自动测试都会轻松很多。

#### 3.5 拖拽不是直接改状态，而是先记录上下文

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

解析：

- 被拖拽的不是单块，而是这块所属的整个组
- `PuzzleDragContext` 保存了：
  - 当前拖的是哪个组
  - 锚点块是谁
  - 指针起始位置
  - 锚点世界坐标
  - 组内每一块拖拽开始时的世界坐标

这意味着拖拽期间不会立即污染 `PuzzleState`。

#### 3.6 松手才真正提交交换

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

解析：

这里体现了整个项目最核心的思想：

- 玩家释放的是一个世界坐标
- 先把世界坐标翻译成目标格子
- 再把“拖到了哪里”交给 `PuzzleSwapResolver` 变成一份 `PuzzleMovePlan`
- 如果计划不合法，就回弹
- 如果合法，才真正落状态

也就是说，这个项目的“操作语义”是：

> 玩家并不是在拖动物体本身，而是在提交一条“把这个组挪到这个目标格”的指令。

#### 3.7 ApplyMovePlan：状态更新的真正提交点

```csharp
private void ApplyMovePlan(PuzzleMovePlan movePlan)
{
    foreach (int displacedPieceId in movePlan.DisplacedPieceIds)
    {
        state.GetPiece(displacedPieceId).GroupId = displacedPieceId;
    }

    foreach ((int pieceId, int targetCellIndex) in movePlan.PieceToCellAssignments)
    {
        state.GetPiece(pieceId).CurrentCellIndex = targetCellIndex;
    }

    state.CellToPiece.Clear();
    foreach (PuzzlePieceState piece in state.Pieces.Values)
    {
        state.CellToPiece[piece.CurrentCellIndex] = piece.PieceId;
    }

    state.RebuildGroups();
    mergeResolver.ApplyAutoMerges(state, board);
    RefreshAllPieceWorldPositions();

    isSolved = state.IsSolved();
    if (isSolved)
    {
        statusText = "Puzzle complete!";
    }
}
```

解析：

这里的更新顺序非常有讲究：

1. 先把被顶开的块从原组里拆出来
2. 再把所有受影响块写到新的格子里
3. 然后重建 `CellToPiece`
4. 再重建组
5. 再尝试自动合并
6. 最后统一刷新画面

这个顺序的好处是，所有规则判断都始终基于完整一致的状态，不会出现“画面已经动了，但逻辑还没变”的半更新态。

---

### 4. 棋盘坐标系统：`PuzzleBoardController`

文件作用：
把抽象的格子索引和 Unity 世界坐标连接起来。

核心代码片段：

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
```

解析：

- `cellIndex` 是系统内部最重要的坐标表达
- `row * Columns + column` 是标准二维到一维映射
- `GetCellWorldPosition` 把格子中心点转换成世界坐标

注意它使用的是“左下角原点 + 顶部往下算行”的混合写法，这样能让图片切图的行列顺序与屏幕上的直觉更接近。

另一个重要方法是：

```csharp
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

解析：

这段把“玩家松手的位置”变成“目标格子”。所有交换逻辑都建立在这一步之上。

---

### 5. 切图器：`PuzzleSpriteSlicer`

文件作用：
把原图按行列切成多个子 `Sprite`。

核心代码片段：

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

解析：

- `source.rect` 而不是直接用整张 texture，说明它兼容 atlas 中的 sprite 子区域
- `rows - row - 1` 这一句很关键，它把常见的贴图坐标系翻转成符合“第 0 行在最上面”的逻辑行号
- `Sprite.Create` 让作者避免预先切图资源

这一层基本就是把教程里的“动态纹理分割”翻译成 Unity API。

---

### 6. 运行时状态模型：`PuzzleRuntimeModels`

文件作用：
定义整个玩法真正的“内存真相”。

核心代码片段：

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
```

解析：

这里有三张“表”：

- `Pieces`：通过 `pieceId` 找块状态
- `Groups`：通过 `groupId` 找组成员
- `CellToPiece`：通过格子找当前占用块

这三张表加在一起，几乎就描述了整个局面。

再看两个关键方法：

```csharp
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
```

解析：

- `IsSolved` 的判定非常干净：只看当前格子是否等于正确格子
- `RebuildGroups` 是一个“从 piece 反推 group”的重建方法

这说明作者没有把组维护成特别复杂的双向引用结构，而是选择在必要时重建，换可读性和稳定性。

---

### 7. 交换规则：`PuzzleSwapResolver`

文件作用：
把“玩家把这个组拖到某个目标格”翻译成一份“哪些块要搬去哪些格子”的交换计划。

核心代码片段：

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
```

解析：

它的第一步不是直接交换，而是先把拖拽抽象成一个位移向量 `delta`。

这样做的意义非常大：

- 拖的是整组时，每个成员都应用同一个格子偏移
- 算法天然支持“组拖动”

接着看核心求解部分：

```csharp
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
```

解析：

- 先计算移动组每个成员会去哪个新格子
- 如果任何一个成员越界，整个移动非法
- 然后找到这些目标格里原本占着的其他块

最后是回填策略：

```csharp
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
```

解析：

这里的实现不是“把另一整组整体平移到新位置”，而是更朴素一点：

- 找出移动组原本腾出来的格子
- 找出被顶开的块
- 按稳定顺序一一回填

这是一种很实用的首版策略，因为它能保证：

- 结果总是完整
- 不会出现格子重叠
- 不需要做更复杂的多组拓扑求解

如果以后你要更贴近原作的“整组整体挤压交换”，这里就是第一升级点。

---

### 8. 自动合并规则：`PuzzleMergeResolver`

文件作用：
在每次交换后检查是否有新的相邻块应当并成一个组。

核心代码片段：

```csharp
public void ApplyAutoMerges(PuzzleState state, PuzzleBoardController board)
{
    bool merged;
    do
    {
        merged = false;
        foreach (PuzzlePieceState piece in state.Pieces.Values)
        {
            int currentCell = piece.CurrentCellIndex;
            if (TryMergeNeighbor(state, board, piece, currentCell, 0, 1))
            {
                merged = true;
                break;
            }

            if (TryMergeNeighbor(state, board, piece, currentCell, 1, 0))
            {
                merged = true;
                break;
            }
        }
    }
    while (merged);
}
```

解析：

- 它只检查右邻和下邻
- 这样可以避免重复检查四个方向
- 用 `do/while` 循环是为了支持“合并触发新的合并”

真正的判定在这里：

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

解析：

这段很值得认真理解，因为它几乎就是教程里“自动合并”的精髓：

- 不是看两块图像是不是看起来接近
- 不是看世界坐标距离
- 而是看：
  - 当前棋盘上的相对方向
  - 是否等于正确答案中的相对方向

比如：

- A 在当前局面里位于 B 的左边
- 同时 A 在正确答案里也位于 B 的左边

那么它们就应该合并。

这就是为什么这个项目本质上是“离散格子逻辑游戏”，而不是连续坐标游戏。

---

### 9. 表现层：`PuzzlePieceView`

文件作用：
负责单个拼图块的显示、碰撞和选中反馈。

核心代码片段：

```csharp
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
```

解析：

- 每块是运行时动态创建的 GameObject
- `SpriteRenderer` 负责画图
- `BoxCollider2D` 负责点选
- `targetWorldSize / spriteSize` 用来把每块缩放到刚好填满一个棋盘格

这意味着棋盘逻辑尺寸和图片像素尺寸是解耦的。

再看选中反馈：

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

解析：

这是一套很轻量但很实用的反馈设计：

- 提高 sorting order
- 微微放大
- 轻微变色

因为它不依赖复杂动画，所以很适合玩法原型阶段。

---

### 10. 输入层：`PuzzleInputController`

文件作用：
把鼠标和触摸统一成“指针拖拽事件”，再转交给 `PuzzleGameController`。

核心代码片段：

```csharp
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

解析：

- 它优先处理触摸
- 没有触摸时回退到鼠标
- 最终统一调用 `HandlePointer`

这是一种典型的“多输入源收敛到单事件模型”的做法。

点选逻辑也很直接：

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

解析：

- `ScreenToWorldPoint` 把屏幕坐标翻译成玩法坐标
- `Physics2D.OverlapPoint` 用 collider 命中拼图块
- 命中后只把 `PieceId` 和世界坐标交给总控器

输入层本身不懂交换规则、不懂合并规则，它只是“把玩家操作变成统一指令”的适配器。

---

### 11. 关卡配置：`PuzzleLevelConfig`

文件作用：
定义玩法参数和初始局面策略。

核心代码片段：

```csharp
public enum PuzzleInitialLayoutMode
{
    Random,
    Preset
}

[CreateAssetMenu(fileName = "PuzzleLevelConfig", menuName = "Jigsaw Puzzle/Level Config")]
public sealed class PuzzleLevelConfig : ScriptableObject
{
    [SerializeField] private Sprite sourceSprite;
    [SerializeField] private int rows = 3;
    [SerializeField] private int columns = 3;
    [SerializeField] private PuzzleInitialLayoutMode initialLayoutMode = PuzzleInitialLayoutMode.Random;
    [SerializeField] private int minMisplacedCount = 6;
    [SerializeField] private bool showPieceIndex = true;
    [SerializeField] private int[] presetArrangement = Array.Empty<int>();
```

解析：

这个对象决定了玩法“题目长什么样”。

- 用哪张图
- 切成几行几列
- 初始布局随机还是预设
- 至少要乱到什么程度
- 要不要显示编号

最核心的方法是：

```csharp
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

解析：

- `Preset` 模式允许你做固定题目
- `Random` 模式会反复洗牌，直到乱序程度满足阈值

这正好对应了教程里提到的两个点：

- 随机不能太接近完成，否则不好玩
- 也可以通过预设初始序列来固定难度

---

## 总结与学习点

这个代码库最值得学的地方，不是某个花哨 API，而是它整体的思路很干净。

### 1. 先定义“真相源”，再做画面

这个项目真正的真相源是：

- `PieceId -> CurrentCellIndex`
- `PieceId -> GroupId`
- `CellIndex -> PieceId`

画面位置只是这套真相源的投影。

### 2. 拖拽只是预览，松手才提交

这是很成熟的玩法实现方式。拖拽过程中不改主状态，松手时通过求解器一次性判断合法性并提交，稳定性会高很多。

### 3. “自动合并”的本质是相对关系匹配

并组规则不是看距离，也不是看图案相似度，而是看：

- 当前棋盘上的相对方向
- 是否等于正确答案里的相对方向

这让规则清晰、可测试、可扩展。

### 4. 首版实现刻意保持了简单

当前实现有几个明显的“原型优先”选择：

- 运行时自动创建所有对象，而不是依赖复杂 prefab 场景
- 回退到内置示例图片，保证开箱即跑
- 交换时使用稳定回填策略，而不是更复杂的多组整体拓扑交换
- 表现层只做最必要的选中反馈

这说明作者在优先验证玩法闭环，而不是先追求生产级内容管线。

### 5. 如果你接下来想继续深入，最值得追的方向有三个

- 把 `PuzzleSwapResolver` 升级成更贴近原作的“整组整体换位”求解器
- 给 `PuzzleLevelConfig` 做正式资源化入口，比如多关卡和关卡选择
- 把当前 `OnGUI` 调试 UI 改成真正的 UGUI 或 UI Toolkit 面板

---

## 你现在应该怎么读这套代码

如果你准备真正把它吃透，我建议你按这个顺序重新读一遍源码：

1. [PuzzleBootstrap.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleBootstrap.cs)
2. [PuzzleGameController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleGameController.cs)
3. [PuzzleRuntimeModels.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleRuntimeModels.cs)
4. [PuzzleSwapResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleSwapResolver.cs)
5. [PuzzleMergeResolver.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleMergeResolver.cs)
6. [PuzzleInputController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzleInputController.cs)
7. [PuzzlePieceView.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Runtime/PuzzlePieceView.cs)
8. [PuzzleBoardController.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleBoardController.cs)
9. [PuzzleSpriteSlicer.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Core/PuzzleSpriteSlicer.cs)
10. [PuzzleLevelConfig.cs](/Users/linkunkun/JigsawPuzzle/Assets/Scripts/Puzzle/Data/PuzzleLevelConfig.cs)

这样你会先抓住系统主干，再回头理解各个工具类为什么存在。
