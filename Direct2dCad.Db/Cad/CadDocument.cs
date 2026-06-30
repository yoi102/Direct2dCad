using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.Db.Cad;

/// <summary>
/// 一张图纸的内存模型。
/// Document 是 CAD DB 的聚合根，负责统一创建和维护 Layer / Block / Entity / Style / HatchPattern 的一致性。
/// </summary>
public sealed class CadDocument : IEquatable<CadDocument>
{
    private readonly CadIdGenerator _ids;
    private readonly Dictionary<LayerId, CadLayer> _layers = [];
    private readonly Dictionary<BlockId, CadBlockDefinition> _blocks = [];
    private readonly Dictionary<EntityId, CadEntity> _entities = [];
    private readonly Dictionary<StyleId, CadStyle> _styles = [];
    private readonly Dictionary<HatchPatternId, CadHatchPatternDefinition> _hatchPatterns = [];
    public CadDocumentSettings DocumentSettings { get; }
    public CadViewSettings ViewSettings { get; } = new();

    public DocumentId Id { get; }
    public string Name { get; private set; }

    public IReadOnlyDictionary<LayerId, CadLayer> Layers => _layers;
    public IReadOnlyDictionary<BlockId, CadBlockDefinition> Blocks => _blocks;
    public IReadOnlyDictionary<EntityId, CadEntity> Entities => _entities;
    public IReadOnlyDictionary<StyleId, CadStyle> Styles => _styles;
    public IReadOnlyDictionary<HatchPatternId, CadHatchPatternDefinition> HatchPatterns => _hatchPatterns;

    public static CadDocument Create(string name)
    {
        var ids = new CadIdGenerator();
        return new CadDocument(ids.NewDocumentId(), name, ids);
    }

    public CadDocument(DocumentId id, string name, CadIdGenerator? idGenerator = null)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();
        _ids = idGenerator ?? new CadIdGenerator();
        _ids.RegisterExisting(id);
        DocumentSettings = CadDocumentSettings.Default();
        InitializeDefaults();
    }

    private void InitializeDefaults()
    {
        var defaultGraphicStyle = new CadGraphicStyle(
            StyleId.DefaultGraphic,
            "Default Graphic",
            CadColor.White,
            CadLineWeight.Default,
            LineTypeId.Continuous);

        AddStyleCore(defaultGraphicStyle);

        var defaultLayer = new CadLayer(
            LayerId.Default,
            "0",
            CadColor.White,
            CadLineWeight.Default,
            StyleId.DefaultGraphic);

        AddLayerCore(defaultLayer);

        AddBlockCore(new CadBlockDefinition(
            BlockId.ModelSpace,
            "*ModelSpace",
            CadPointD.Origin));

        AddBlockCore(new CadBlockDefinition(
            BlockId.PaperSpace,
            "*PaperSpace",
            CadPointD.Origin));
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        Name = name.Trim();
    }

    #region Layer

    public LayerId CreateLayer(
        string name,
        CadColor color,
        CadLineWeight lineWeight,
        StyleId? defaultGraphicStyleId = null)
    {
        ValidateGraphicStyle(defaultGraphicStyleId, allowNull: true);

        var layer = new CadLayer(
            _ids.NewLayerId(),
            name,
            color,
            lineWeight,
            defaultGraphicStyleId);

        AddLayerCore(layer);
        return layer.Id;
    }

    public void SetLayerDefaultGraphicStyle(LayerId layerId, StyleId? styleId)
    {
        ValidateGraphicStyle(styleId, allowNull: true);
        GetLayer(layerId).SetDefaultGraphicStyleInternal(styleId);
    }

    public bool RemoveLayer(LayerId layerId)
    {
        if (!_layers.ContainsKey(layerId))
            return false;

        EnsureLayerCanBeRemoved(layerId);

        if (HasEntitiesOnLayer(layerId))
            throw new InvalidOperationException(
                $"Layer cannot be removed because it is used by one or more entities: {layerId}");

        _layers.Remove(layerId);
        return true;
    }

    /// <summary>
    /// 强制删除图层，并删除该图层上的所有实体。
    /// 默认图层不能删除；Document 至少必须保留一个图层。
    /// </summary>
    public bool RemoveLayerAndDeleteEntities(LayerId layerId)
    {
        if (!_layers.ContainsKey(layerId))
            return false;

        EnsureLayerCanBeRemoved(layerId);

        var entityIds = _entities.Values
            .Where(x => x.LayerId.Equals(layerId))
            .Select(x => x.Id)
            .ToArray();

        foreach (var entityId in entityIds)
        {
            RemoveEntity(entityId);
        }

        _layers.Remove(layerId);
        return true;
    }

    /// <summary>
    /// 删除图层，并把该图层上的实体移动到目标图层。
    /// </summary>
    public bool RemoveLayerAndMoveEntities(LayerId layerId, LayerId targetLayerId)
    {
        if (!_layers.ContainsKey(layerId))
            return false;

        EnsureLayerCanBeRemoved(layerId);

        if (layerId.Equals(targetLayerId))
            throw new InvalidOperationException("Target layer cannot be the same as the layer being removed.");

        ValidateLayer(targetLayerId);

        foreach (var entity in _entities.Values.Where(x => x.LayerId.Equals(layerId)).ToArray())
        {
            entity.ChangeLayerInternal(targetLayerId);
        }

        _layers.Remove(layerId);
        return true;
    }

    public bool HasEntitiesOnLayer(LayerId layerId)
    {
        return _entities.Values.Any(x => x.LayerId.Equals(layerId));
    }

    public IReadOnlyList<EntityId> GetEntityIdsOnLayer(LayerId layerId)
    {
        return _entities.Values
            .Where(x => x.LayerId.Equals(layerId))
            .Select(x => x.Id)
            .ToArray();
    }

    private void EnsureLayerCanBeRemoved(LayerId layerId)
    {
        if (layerId.Equals(LayerId.Default))
            throw new InvalidOperationException("Default layer cannot be removed.");

        if (_layers.Count <= 1)
            throw new InvalidOperationException("Document must contain at least one layer.");
    }

    #endregion Layer

    #region Block

    public BlockId CreateBlockDefinition(string name, CadPointD basePoint)
    {
        var block = new CadBlockDefinition(_ids.NewBlockId(), name, basePoint);
        AddBlockCore(block);
        return block.Id;
    }

    public bool RemoveBlockDefinition(BlockId blockId)
    {
        if (!_blocks.TryGetValue(blockId, out var block))
            return false;

        EnsureBlockCanBeRemoved(blockId);

        if (IsBlockReferenced(blockId))
            throw new InvalidOperationException(
                $"Block definition cannot be removed because it is referenced by one or more block references: {blockId}");

        // 删除块定义时，块内部拥有的实体也一起从 Document.Entities 删除。
        foreach (var entityId in block.EntityIds.ToArray())
        {
            RemoveEntity(entityId);
        }

        _blocks.Remove(blockId);
        return true;
    }

    public bool IsBlockReferenced(BlockId blockId)
    {
        return _entities.Values
            .OfType<CadBlockReference>()
            .Any(x => x.DefinitionBlockId.Equals(blockId));
    }

    public IReadOnlyList<EntityId> GetBlockReferenceIds(BlockId definitionBlockId)
    {
        return _entities.Values
            .OfType<CadBlockReference>()
            .Where(x => x.DefinitionBlockId.Equals(definitionBlockId))
            .Select(x => x.Id)
            .ToArray();
    }

    private void EnsureBlockCanBeRemoved(BlockId blockId)
    {
        if (blockId.Equals(BlockId.ModelSpace))
            throw new InvalidOperationException("ModelSpace block cannot be removed.");

        if (blockId.Equals(BlockId.PaperSpace))
            throw new InvalidOperationException("PaperSpace block cannot be removed.");
    }

    public void MoveEntityToBlock(EntityId entityId, BlockId targetBlockId)
    {
        ValidateBlock(targetBlockId);

        var entity = GetEntity(entityId);

        if (entity.OwnerBlockId.Equals(targetBlockId))
            return;

        if (!_blocks.TryGetValue(entity.OwnerBlockId, out var oldBlock))
            throw new InvalidOperationException($"Owner block does not exist: {entity.OwnerBlockId}");

        if (!_blocks.TryGetValue(targetBlockId, out var newBlock))
            throw new InvalidOperationException($"Target block does not exist: {targetBlockId}");

        // 防止 BlockReference 引用自己的 owner block。
        if (entity is CadBlockReference blockReference &&
            blockReference.DefinitionBlockId.Equals(targetBlockId))
        {
            throw new InvalidOperationException("Block reference cannot be moved into its own definition block.");
        }

        oldBlock.RemoveEntity(entityId);
        newBlock.AddEntity(entityId);
        entity.ChangeOwnerInternal(targetBlockId);
    }

    #endregion Block

    #region Styles

    public StyleId CreateGraphicStyle(
        string name,
        CadColor strokeColor,
        CadLineWeight lineWeight,
        LineTypeId lineTypeId)
    {
        var style = new CadGraphicStyle(
            _ids.NewStyleId(),
            name,
            strokeColor,
            lineWeight,
            lineTypeId);

        AddStyleCore(style);
        return style.Id;
    }

    public StyleId CreateTextStyle(
        string name,
        string fontFamily,
        double textHeight,
        double widthFactor = 1.0,
        double obliqueAngle = 0.0,
        bool isBold = false,
        bool isItalic = false)
    {
        var style = new CadTextStyle(
            _ids.NewStyleId(),
            name,
            fontFamily,
            textHeight,
            widthFactor,
            obliqueAngle,
            isBold,
            isItalic);

        AddStyleCore(style);
        return style.Id;
    }

    public HatchPatternId CreateHatchPattern(
        string name,
        IEnumerable<CadHatchLineDefinition> lines,
        string description = "")
    {
        var pattern = new CadHatchPatternDefinition(
            _ids.NewHatchPatternId(),
            name,
            lines,
            description);

        AddHatchPatternCore(pattern);
        return pattern.Id;
    }

    public StyleId CreateHatchFillStyle(
        string name,
        HatchPatternId patternId,
        CadColor foregroundColor,
        CadColor? backgroundColor = null,
        double hatchScale = 1.0,
        double hatchAngle = 0.0,
        CadPointD? hatchOrigin = null,
        bool isAnnotative = false)
    {
        ValidateHatchPattern(patternId);

        var style = new CadHatchFillStyle(
            _ids.NewStyleId(),
            name,
            patternId,
            foregroundColor,
            backgroundColor,
            hatchScale,
            hatchAngle,
            hatchOrigin,
            isAnnotative);

        AddStyleCore(style);
        return style.Id;
    }

    public StyleId CreateGradientFillStyle(
        string name,
        CadGradientKind gradientKind,
        IEnumerable<CadGradientStop> stops,
        double gradientAngle = 0.0,
        double gradientScale = 1.0,
        CadPointD? gradientOrigin = null,
        bool isCentered = true)
    {
        var style = new CadGradientFillStyle(
            _ids.NewStyleId(),
            name,
            gradientKind,
            stops,
            gradientAngle,
            gradientScale,
            gradientOrigin,
            isCentered);

        AddStyleCore(style);
        return style.Id;
    }

    public StyleId CreateSolidFillStyle(string name, CadColor color)
    {
        var style = CadGradientFillStyle.CreateSolid(_ids.NewStyleId(), name, color);
        AddStyleCore(style);
        return style.Id;
    }

    public void SetHatchStylePattern(StyleId styleId, HatchPatternId patternId)
    {
        ValidateHatchPattern(patternId);
        GetStyle<CadHatchFillStyle>(styleId).SetPatternInternal(patternId);
    }

    #endregion Styles

    #region Entities

    //暂时默认只允许放在BlockId.ModelSpace内
    public CadLine AddLine(
        CadPointD start,
        CadPointD end,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "")
    {
        var entity = new CadLine(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
            BlockId.ModelSpace,
            start,
            end,
            name);

        entity.SetGraphicStyleInternal(graphicStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadCircle AddCircle(
        CadPointD center,
        double radius,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "")
    {
        var entity = new CadCircle(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
             BlockId.ModelSpace,
            center,
            radius,
            name);

        entity.SetGraphicStyleInternal(graphicStyleId);
        entity.SetFillStyleInternal(fillStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadEllipse AddEllipse(
        CadPointD center,
        double radiusX,
        double radiusY,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "")
    {
        var entity = new CadEllipse(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
            BlockId.ModelSpace,
            center,
            radiusX,
            radiusY,
            name);

        entity.SetGraphicStyleInternal(graphicStyleId);
        entity.SetFillStyleInternal(fillStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadArc AddArc(
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "")
    {
        var entity = new CadArc(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
             BlockId.ModelSpace,
            center,
            radius,
            startAngleRadians,
            sweepAngleRadians,
            name);

        entity.SetGraphicStyleInternal(graphicStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadArc AddArcDegrees(
        CadPointD center,
        double radius,
        double startAngleDegrees,
        double sweepAngleDegrees,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "")
    {
        return AddArc(
            center,
            radius,
            CadArc.DegreesToRadians(startAngleDegrees),
            CadArc.DegreesToRadians(sweepAngleDegrees),
            layerId,
            graphicStyleId,
            name);
    }

    public CadPolyline AddPolyline(
        IEnumerable<CadPointD> points,
        bool isClosed = false,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "")
    {
        var entity = new CadPolyline(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
             BlockId.ModelSpace,
            points,
            isClosed,
            name);

        entity.SetGraphicStyleInternal(graphicStyleId);
        entity.SetFillStyleInternal(fillStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadSpline AddSpline(
        IEnumerable<CadPointD> fitPoints,
        bool closed = false,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "")
    {
        var entity = new CadSpline(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
             BlockId.ModelSpace,
            fitPoints,
            closed,
            name);

        entity.SetGraphicStyleInternal(graphicStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadText AddText(
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? textStyleId = null,
        string name = "",
        bool isInverted = false,
        double invertedMarginFactor = CadText.DefaultInvertedMarginFactor)
    {
        ValidateGraphicStyle(graphicStyleId, allowNull: true);
        ValidateTextStyle(textStyleId, allowNull: true);

        var entity = new CadText(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
            BlockId.ModelSpace,
            text,
            position,
            height,
            rotationRadians,
            textStyleId,
            name,
            isInverted,
            invertedMarginFactor);

        entity.SetGraphicStyleInternal(graphicStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadShapeText AddShapeText(
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        double widthFactor = CadStrokeFont.DefaultWidthFactor,
        double characterSpacingFactor = CadStrokeFont.DefaultCharacterSpacingFactor,
        double obliqueAngleRadians = CadStrokeFont.DefaultObliqueAngleRadians,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        string name = "",
        bool isInverted = false,
        double invertedMarginFactor = CadShapeText.DefaultInvertedMarginFactor,
        CadShapeFontId shapeFontId = default)
    {
        ValidateGraphicStyle(graphicStyleId, allowNull: true);

        var entity = new CadShapeText(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
            BlockId.ModelSpace,
            text,
            position,
            height,
            rotationRadians,
            widthFactor,
            characterSpacingFactor,
            obliqueAngleRadians,
            name,
            isInverted,
            invertedMarginFactor,
            shapeFontId);

        entity.SetGraphicStyleInternal(graphicStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadRectangle AddRectangle(
        CadRectD bounds,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        StyleId? fillStyleId = null,
        string name = "")
    {
        var entity = new CadRectangle(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
            BlockId.ModelSpace,
            bounds,
            name);

        entity.SetGraphicStyleInternal(graphicStyleId);
        entity.SetFillStyleInternal(fillStyleId);
        AddEntityCore(entity);
        return entity;
    }

    public CadBlockReference AddBlockReference(
        BlockId definitionBlockId,
        CadPointD position,
        LayerId? layerId = null,
        StyleId? graphicStyleId = null,
        double rotationRadians = 0,
        double scaleX = 1.0,
        double scaleY = 1.0,
        string name = "")
    {
        ValidateBlock(definitionBlockId);

        var owner = BlockId.ModelSpace;
        if (definitionBlockId.Equals(owner))
            throw new InvalidOperationException("Block reference cannot reference its owner block.");

        var entity = new CadBlockReference(
            _ids.NewEntityId(),
            layerId ?? LayerId.Default,
            owner,
            definitionBlockId,
            position,
            rotationRadians,
            scaleX,
            scaleY,
            name);

        entity.SetGraphicStyleInternal(graphicStyleId);
        AddEntityCore(entity);
        return entity;
    }

    //public CadLine AddLine(
    //    CadPointD start,
    //    CadPointD end,
    //    LayerId? layerId = null,
    //    StyleId? graphicStyleId = null,
    //    BlockId? ownerBlockId = null,
    //    string name = "")
    //{
    //    var entity = new CadLine(
    //        _ids.NewEntityId(),
    //        layerId ?? LayerId.Default,
    //        ownerBlockId ?? BlockId.ModelSpace,
    //        start,
    //        end,
    //        name);

    //    entity.SetGraphicStyleInternal(graphicStyleId);
    //    AddEntityCore(entity);
    //    return entity;
    //}

    //public CadCircle AddCircle(
    //    CadPointD center,
    //    double radius,
    //    LayerId? layerId = null,
    //    StyleId? graphicStyleId = null,
    //    StyleId? fillStyleId = null,
    //    BlockId? ownerBlockId = null,
    //    string name = "")
    //{
    //    var entity = new CadCircle(
    //        _ids.NewEntityId(),
    //        layerId ?? LayerId.Default,
    //        ownerBlockId ?? BlockId.ModelSpace,
    //        center,
    //        radius,
    //        name);

    //    entity.SetGraphicStyleInternal(graphicStyleId);
    //    entity.SetFillStyleInternal(fillStyleId);
    //    AddEntityCore(entity);
    //    return entity;
    //}

    //public CadArc AddArc(
    //    CadPointD center,
    //    double radius,
    //    double startAngleRadians,
    //    double sweepAngleRadians,
    //    LayerId? layerId = null,
    //    StyleId? graphicStyleId = null,
    //    BlockId? ownerBlockId = null,
    //    string name = "")
    //{
    //    var entity = new CadArc(
    //        _ids.NewEntityId(),
    //        layerId ?? LayerId.Default,
    //        ownerBlockId ?? BlockId.ModelSpace,
    //        center,
    //        radius,
    //        startAngleRadians,
    //        sweepAngleRadians,
    //        name);

    //    entity.SetGraphicStyleInternal(graphicStyleId);
    //    AddEntityCore(entity);
    //    return entity;
    //}

    //public CadArc AddArcDegrees(
    //    CadPointD center,
    //    double radius,
    //    double startAngleDegrees,
    //    double sweepAngleDegrees,
    //    LayerId? layerId = null,
    //    StyleId? graphicStyleId = null,
    //    BlockId? ownerBlockId = null,
    //    string name = "")
    //{
    //    return AddArc(
    //        center,
    //        radius,
    //        CadArc.DegreesToRadians(startAngleDegrees),
    //        CadArc.DegreesToRadians(sweepAngleDegrees),
    //        layerId,
    //        graphicStyleId,
    //        ownerBlockId,
    //        name);
    //}

    //public CadPolyline AddPolyline(
    //    IEnumerable<CadPointD> points,
    //    bool isClosed = false,
    //    LayerId? layerId = null,
    //    StyleId? graphicStyleId = null,
    //    StyleId? fillStyleId = null,
    //    BlockId? ownerBlockId = null,
    //    string name = "")
    //{
    //    var entity = new CadPolyline(
    //        _ids.NewEntityId(),
    //        layerId ?? LayerId.Default,
    //        ownerBlockId ?? BlockId.ModelSpace,
    //        points,
    //        isClosed,
    //        name);

    //    entity.SetGraphicStyleInternal(graphicStyleId);
    //    entity.SetFillStyleInternal(fillStyleId);
    //    AddEntityCore(entity);
    //    return entity;
    //}

    //public CadText AddText(
    //    string text,
    //    CadPointD position,
    //    double height,
    //    double rotationRadians = 0,
    //    LayerId? layerId = null,
    //    StyleId? graphicStyleId = null,
    //    StyleId? textStyleId = null,
    //    BlockId? ownerBlockId = null,
    //    string name = "")
    //{
    //    ValidateTextStyle(textStyleId, allowNull: true);

    //    var entity = new CadText(
    //        _ids.NewEntityId(),
    //        layerId ?? LayerId.Default,
    //        ownerBlockId ?? BlockId.ModelSpace,
    //        text,
    //        position,
    //        height,
    //        rotationRadians,
    //        textStyleId,
    //        name);

    //    AddEntityCore(entity);
    //    return entity;
    //}

    //public CadBlockReference AddBlockReference(
    //    BlockId definitionBlockId,
    //    CadPointD position,
    //    LayerId? layerId = null,
    //    StyleId? graphicStyleId = null,
    //    BlockId? ownerBlockId = null,
    //    double rotationRadians = 0,
    //    double scaleX = 1.0,
    //    double scaleY = 1.0,
    //    string name = "")
    //{
    //    ValidateBlock(definitionBlockId);

    //    var owner = ownerBlockId ?? BlockId.ModelSpace;
    //    if (definitionBlockId.Equals(owner))
    //        throw new InvalidOperationException("Block reference cannot reference its owner block.");

    //    var entity = new CadBlockReference(
    //        _ids.NewEntityId(),
    //        layerId ?? LayerId.Default,
    //        owner,
    //        definitionBlockId,
    //        position,
    //        rotationRadians,
    //        scaleX,
    //        scaleY,
    //        name);

    //    entity.SetGraphicStyleInternal(graphicStyleId);
    //    AddEntityCore(entity);
    //    return entity;
    //}

    public bool RemoveEntity(EntityId entityId)
    {
        if (!_entities.TryGetValue(entityId, out var entity))
            return false;

        _entities.Remove(entityId);

        if (_blocks.TryGetValue(entity.OwnerBlockId, out var ownerBlock))
            ownerBlock.RemoveEntity(entityId);

        return true;
    }

    public void ChangeEntityLayer(EntityId entityId, LayerId layerId)
    {
        ValidateLayer(layerId);
        GetEntity(entityId).ChangeLayerInternal(layerId);
    }

    public void SetTextEntityStyle(EntityId entityId, StyleId? textStyleId)
    {
        ValidateTextStyle(textStyleId, allowNull: true);

        if (GetEntity(entityId) is not CadText text)
            throw new InvalidOperationException($"Entity is not text: {entityId}");

        text.SetTextStyleInternal(textStyleId);
    }

    public void ChangeBlockReferenceDefinition(EntityId entityId, BlockId definitionBlockId)
    {
        ValidateBlock(definitionBlockId);

        if (GetEntity(entityId) is not CadBlockReference reference)
            throw new InvalidOperationException($"Entity is not block reference: {entityId}");

        if (reference.OwnerBlockId.Equals(definitionBlockId))
            throw new InvalidOperationException("Block reference cannot reference its owner block.");

        reference.SetDefinitionBlockInternal(definitionBlockId);
    }

    #endregion Entities

    #region Query

    public bool TryGetEntity(EntityId entityId, out CadEntity? entity) => _entities.TryGetValue(entityId, out entity);

    public bool TryGetLayer(LayerId layerId, out CadLayer? layer) => _layers.TryGetValue(layerId, out layer);

    public bool TryGetBlock(BlockId blockId, out CadBlockDefinition? block) => _blocks.TryGetValue(blockId, out block);

    public bool TryGetStyle(StyleId styleId, out CadStyle? style) => _styles.TryGetValue(styleId, out style);

    public bool TryGetHatchPattern(HatchPatternId patternId, out CadHatchPatternDefinition? pattern) => _hatchPatterns.TryGetValue(patternId, out pattern);

    public CadEntity GetEntity(EntityId entityId)
    {
        if (!_entities.TryGetValue(entityId, out var entity))
            throw new KeyNotFoundException($"Entity does not exist: {entityId}");

        return entity;
    }

    public CadLayer GetLayer(LayerId layerId)
    {
        if (!_layers.TryGetValue(layerId, out var layer))
            throw new KeyNotFoundException($"Layer does not exist: {layerId}");

        return layer;
    }

    public CadBlockDefinition GetBlock(BlockId blockId)
    {
        if (!_blocks.TryGetValue(blockId, out var block))
            throw new KeyNotFoundException($"Block does not exist: {blockId}");

        return block;
    }

    public TStyle GetStyle<TStyle>(StyleId styleId) where TStyle : CadStyle
    {
        if (!_styles.TryGetValue(styleId, out var style))
            throw new KeyNotFoundException($"Style does not exist: {styleId}");

        if (style is not TStyle typedStyle)
            throw new InvalidOperationException($"Style {styleId} is {style.GetType().Name}, but requested {typeof(TStyle).Name}.");

        return typedStyle;
    }

    public CadHatchPatternDefinition GetHatchPattern(HatchPatternId patternId)
    {
        if (!_hatchPatterns.TryGetValue(patternId, out var pattern))
            throw new KeyNotFoundException($"Hatch pattern does not exist: {patternId}");

        return pattern;
    }

    public IEnumerable<CadEntity> GetEntitiesInBlock(BlockId blockId)
    {
        if (!_blocks.TryGetValue(blockId, out var block))
            yield break;

        foreach (var entityId in block.EntityIds)
        {
            if (_entities.TryGetValue(entityId, out var entity))
                yield return entity;
        }
    }

    public CadRectD GetBlockBounds(BlockId blockId)
    {
        var bounds = CadRectD.Empty;

        foreach (var entity in GetEntitiesInBlock(blockId))
        {
            if (!entity.IsErased)
                bounds = bounds.Union(entity.Bounds);
        }

        return bounds;
    }

    #endregion Query

    #region Internal Add For Storage

    internal void AddLayerCore(CadLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (_layers.ContainsKey(layer.Id))
            throw new InvalidOperationException($"Layer already exists: {layer.Id}");

        ValidateGraphicStyle(layer.DefaultGraphicStyleId, allowNull: true);
        _layers.Add(layer.Id, layer);
        _ids.RegisterExisting(layer.Id);
    }

    internal void AddBlockCore(CadBlockDefinition block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (_blocks.ContainsKey(block.Id))
            throw new InvalidOperationException($"Block already exists: {block.Id}");

        _blocks.Add(block.Id, block);
        _ids.RegisterExisting(block.Id);
    }

    internal void AddStyleCore(CadStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        if (_styles.ContainsKey(style.Id))
            throw new InvalidOperationException($"Style already exists: {style.Id}");

        ValidateStyleReferences(style);
        _styles.Add(style.Id, style);
        _ids.RegisterExisting(style.Id);
    }

    internal void AddHatchPatternCore(CadHatchPatternDefinition pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (_hatchPatterns.ContainsKey(pattern.Id))
            throw new InvalidOperationException($"Hatch pattern already exists: {pattern.Id}");

        _hatchPatterns.Add(pattern.Id, pattern);
        _ids.RegisterExisting(pattern.Id);
    }

    internal void AddEntityCore(CadEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (_entities.ContainsKey(entity.Id))
            throw new InvalidOperationException($"Entity already exists: {entity.Id}");

        ValidateEntityReferences(entity);

        if (!_blocks.TryGetValue(entity.OwnerBlockId, out var ownerBlock))
            throw new InvalidOperationException($"Owner block does not exist: {entity.OwnerBlockId}");

        _entities.Add(entity.Id, entity);
        ownerBlock.AddEntity(entity.Id);
        _ids.RegisterExisting(entity.Id);
    }

    #endregion Internal Add For Storage

    #region Validation

    private void ValidateEntityReferences(CadEntity entity)
    {
        ValidateLayer(entity.LayerId);
        ValidateBlock(entity.OwnerBlockId);

        if (entity is CadText text)
            ValidateTextStyle(text.TextStyleId, allowNull: true);

        if (entity is CadBlockReference blockReference)
        {
            ValidateBlock(blockReference.DefinitionBlockId);
            if (blockReference.DefinitionBlockId.Equals(blockReference.OwnerBlockId))
                throw new InvalidOperationException("Block reference cannot reference its owner block.");
        }
    }

    private void ValidateStyleReferences(CadStyle style)
    {
        if (style is CadHatchFillStyle hatchStyle)
            ValidateHatchPattern(hatchStyle.PatternId);
    }

    private void ValidateLayer(LayerId layerId)
    {
        if (!_layers.ContainsKey(layerId))
            throw new InvalidOperationException($"Layer does not exist: {layerId}");
    }

    private void ValidateBlock(BlockId blockId)
    {
        if (!_blocks.ContainsKey(blockId))
            throw new InvalidOperationException($"Block does not exist: {blockId}");
    }

    private void ValidateHatchPattern(HatchPatternId patternId)
    {
        if (!_hatchPatterns.ContainsKey(patternId))
            throw new InvalidOperationException($"Hatch pattern does not exist: {patternId}");
    }

    private void ValidateGraphicStyle(StyleId? styleId, bool allowNull)
    {
        if (styleId is null)
        {
            if (!allowNull)
                throw new ArgumentNullException(nameof(styleId));
            return;
        }

        if (!_styles.TryGetValue(styleId.Value, out var style))
            throw new InvalidOperationException($"Style does not exist: {styleId}");

        if (style is not CadGraphicStyle)
            throw new InvalidOperationException($"Style is not graphic style: {styleId}");
    }

    private void ValidateTextStyle(StyleId? styleId, bool allowNull)
    {
        if (styleId is null)
        {
            if (!allowNull)
                throw new ArgumentNullException(nameof(styleId));
            return;
        }

        if (!_styles.TryGetValue(styleId.Value, out var style))
            throw new InvalidOperationException($"Style does not exist: {styleId}");

        if (style is not CadTextStyle)
            throw new InvalidOperationException($"Style is not text style: {styleId}");
    }

    #endregion Validation

    public bool Equals(CadDocument? other) => other is not null && Id.Equals(other.Id);

    public override bool Equals(object? obj) => obj is CadDocument other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
