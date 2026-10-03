using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels;
public partial class CadDocumentViewModel
{
    private readonly List<CadDimensionAnchor> _dimensionAnchors=[];
    private EntityId? _reassociateDimension;
    private EntityId? _loadedDimension;
    private CadDimensionDefinition? _loadedDimensionDefinition;
    private Guid? _dimensionEditBatch;
    private bool _updatingDimension;
    public bool IsDimensionTool => CadCanvasToolMode >= CadCanvasToolMode.DimLinearX && CadCanvasToolMode <= CadCanvasToolMode.Leader;
    public bool IsDimensionContext => IsDimensionTool || SelectedDimension is not null;
    private CadDimension? SelectedDimension => CadEditor.Selection.EntityIds.Count==1 &&
        CadEditor.Document.TryGetEntity(CadEditor.Selection.EntityIds.Single(),out var entity) ? entity as CadDimension : null;
    [ObservableProperty] public partial string DimensionStyleName { get; set; }="ISO";
    [ObservableProperty] public partial CadUnit DimensionUnit { get; set; }=CadUnit.Millimeter;
    [ObservableProperty] public partial int DimensionPrecision { get; set; }=2;
    [ObservableProperty] public partial double DimensionTextHeight { get; set; }=2.5;
    [ObservableProperty] public partial double DimensionArrowSize { get; set; }=2.5;
    [ObservableProperty] public partial double DimensionAnnotationScale { get; set; }=1;
    [ObservableProperty] public partial double DimensionExtensionGap { get; set; }=1;
    [ObservableProperty] public partial double DimensionExtensionBeyond { get; set; }=1.5;
    [ObservableProperty] public partial double DimensionLineWeight { get; set; }=.18;
    [ObservableProperty] public partial string DimensionTextOverride { get; set; }="";
    [ObservableProperty] public partial string DimensionShapeFont {get;set;}="unicode";
    [ObservableProperty] public partial CadDimensionArrow DimensionArrow {get;set;}=CadDimensionArrow.Open;
    public IReadOnlyList<Direct2dCad.Db.Data.Text.CadShapeFont> DimensionFonts {get;}=Direct2dCad.Db.Data.Text.CadShapeFontRegistry.Defaults;
    public object[] DimensionArrowOptions => Enum.GetValues<CadDimensionArrow>().Select(a=>(object)new{Value=a,Name=CadUiText.Get("DimensionArrow"+a)}).ToArray();
    public object[] DimensionUnitOptions {get;}=Enum.GetValues<CadUnit>().Select(u=>(object)new{Unit=u,Symbol=CadUnitConversion.GetSymbol(u) is {Length:>0} s?s:"—"}).ToArray();
    public IReadOnlyList<CadUnit> DimensionUnits {get;}=Enum.GetValues<CadUnit>();
    public IReadOnlyList<string> DimensionStyleNames {get;}=["ISO","Fine","Large"];
    private bool _loadingDimension;
    public bool CanEditDimensionParameters => CanEditDocument && (IsDimensionTool || SelectedDimension is not { } dimension ||
        Direct2dCad.Db.Cad.CadEntityAccessPolicy.IsEditable(CadEditor.Document, dimension));

    // Keep consecutive keystrokes in a focused input together without delaying the drawing update.
    public void BeginDimensionPropertyEdit() => _dimensionEditBatch = Guid.NewGuid();
    public void EndDimensionPropertyEdit() => _dimensionEditBatch = null;

    partial void OnDimensionStyleNameChanged(string value)
    {
        if (_loadingDimension) return;
        var style = CadDimensionStyles.Get(value, DimensionUnit);
        _loadingDimension = true;
        try
        {
            DimensionTextHeight=style.TextHeight;DimensionArrowSize=style.ArrowSize;
            DimensionExtensionGap=style.ExtensionGap;DimensionExtensionBeyond=style.ExtensionBeyond;
            DimensionPrecision=style.Precision;
            DimensionLineWeight=style.LineWeight;
        }
        finally { _loadingDimension = false; }
        UpdateDimensionParameters();
    }
    partial void OnDimensionUnitChanged(CadUnit value) => UpdateDimensionParameters();
    partial void OnDimensionPrecisionChanged(int value) => UpdateDimensionParameters();
    partial void OnDimensionTextHeightChanged(double value) => UpdateDimensionParameters();
    partial void OnDimensionArrowSizeChanged(double value) => UpdateDimensionParameters();
    partial void OnDimensionAnnotationScaleChanged(double value) => UpdateDimensionParameters();
    partial void OnDimensionExtensionGapChanged(double value) => UpdateDimensionParameters();
    partial void OnDimensionExtensionBeyondChanged(double value) => UpdateDimensionParameters();
    partial void OnDimensionLineWeightChanged(double value) => UpdateDimensionParameters();
    partial void OnDimensionTextOverrideChanged(string value) => UpdateDimensionParameters();
    partial void OnDimensionShapeFontChanged(string value) => UpdateDimensionParameters();
    partial void OnDimensionArrowChanged(CadDimensionArrow value) => UpdateDimensionParameters();
    public string DimensionAssociationDisplay => SelectedDimension is { } d ? CadUiText.Get("Association"+d.AssociationState) : "";
    public bool HasSelectedDimension => SelectedDimension is not null;
    private int DimensionPointCount => CadCanvasToolMode==CadCanvasToolMode.DimAngular ? 3 : 2;
    private CadDimensionKind DimensionKind => CadCanvasToolMode switch
    { CadCanvasToolMode.DimLinearX=>CadDimensionKind.LinearX,CadCanvasToolMode.DimLinearY=>CadDimensionKind.LinearY,
      CadCanvasToolMode.DimAligned=>CadDimensionKind.Aligned,CadCanvasToolMode.DimRadius=>CadDimensionKind.Radius,
      CadCanvasToolMode.DimDiameter=>CadDimensionKind.Diameter,CadCanvasToolMode.DimAngular=>CadDimensionKind.Angular,_=>CadDimensionKind.Leader };
    private string DimensionPrompt => CadUiText.Get(CadCanvasToolMode.ToString())+": "+CadUiText.Get(
        _dimensionAnchors.Count >= DimensionPointCount ? "DimensionPlace" :
        CadCanvasToolMode is CadCanvasToolMode.DimRadius or CadCanvasToolMode.DimDiameter ? "DimensionChooseCircle" :
        _dimensionAnchors.Count==0 ? "DimensionFirst" : "DimensionNext");

    private void RefreshDimensionContext(bool force = false)
    {
        var dimension=SelectedDimension;
        var current=dimension?.Definition;
        if(force || dimension?.Id!=_loadedDimension || current is not null && (_loadedDimensionDefinition is null ||
            current.Style!=_loadedDimensionDefinition.Style || current.TextOverride!=_loadedDimensionDefinition.TextOverride ||
            current.AnnotationScale!=_loadedDimensionDefinition.AnnotationScale))
        {
            _loadedDimension=dimension?.Id;
            _loadedDimensionDefinition=current;
            if (!_updatingDimension) EndDimensionPropertyEdit();
            if(dimension is not null && !_updatingDimension)
            {
                _loadingDimension=true;
                try
                {
                    var d=dimension.Definition;DimensionStyleName=d.Style.Name;DimensionUnit=d.Style.Unit;DimensionPrecision=d.Style.Precision;
                    DimensionTextHeight=d.Style.TextHeight;DimensionArrowSize=d.Style.ArrowSize;DimensionExtensionGap=d.Style.ExtensionGap;
                    DimensionExtensionBeyond=d.Style.ExtensionBeyond;DimensionAnnotationScale=d.AnnotationScale;DimensionTextOverride=d.TextOverride??"";
                    DimensionShapeFont=d.Style.ShapeFont;DimensionArrow=d.Style.Arrow;
                    DimensionLineWeight=d.Style.LineWeight;
                    StepInputError = "";
                }
                finally { _loadingDimension=false; }
            }
        }
        foreach(var name in new[]{nameof(IsDimensionTool),nameof(IsDimensionContext),nameof(HasSelectedDimension),nameof(DimensionAssociationDisplay),nameof(CanEditDimensionParameters)})
            OnPropertyChanged(name);
    }
    private void BeginDimension(CadCanvasToolMode mode)
    {
        _dimensionAnchors.Clear();_reassociateDimension=null;
        if(mode>=CadCanvasToolMode.DimLinearX && mode<=CadCanvasToolMode.Leader)
        {
            _loadingDimension = true;
            try
            {
                if (TryGetActiveLayoutViewport(out _, out var viewport)) DimensionAnnotationScale=1/viewport.Scale;
                else if (IsPaperSpaceActive) DimensionAnnotationScale=1;
                DimensionUnit=CadEditor.Document.DocumentSettings.Unit;
            }
            finally { _loadingDimension = false; }
            var selected=CadEditor.Selection.EntityIds.Select(CadEditor.Document.GetEntity).ToArray();
            if(selected.Length==1)SeedDimension(selected[0],mode);
        }
    }
    private bool SeedDimension(CadEntity entity,CadCanvasToolMode? requestedMode=null)
    {
        var mode=requestedMode??CadCanvasToolMode;
        CadGeometryReference Reference(CadReferenceFeature f,double parameter=0)=>new(entity.Id.Value,entity.OwnerBlockId.Value,f,parameter);
        if(entity.OwnerBlockId!=CadEditor.ActiveOwnerBlockId || entity.IsErased)return false;
        if(mode is CadCanvasToolMode.DimRadius or CadCanvasToolMode.DimDiameter)
        {
            var center=entity switch {CadCircle c=>c.Center,CadArc a=>a.Center,_=>(CadPointD?)null};
            var radius=entity switch {CadCircle c=>c.Radius,CadArc a=>a.Radius,_=>0};
            if(center is not { } point)return false;
            _dimensionAnchors.Add(new(point,Reference(CadReferenceFeature.CircleCenter)));
            _dimensionAnchors.Add(new(point+new CadVectorD(radius,0),Reference(CadReferenceFeature.CirclePoint)));
            return true;
        }
        if(entity is CadLine line && mode is CadCanvasToolMode.DimLinearX or CadCanvasToolMode.DimLinearY or CadCanvasToolMode.DimAligned)
        {
            _dimensionAnchors.Add(new(line.Start,Reference(CadReferenceFeature.LineStart)));
            _dimensionAnchors.Add(new(line.End,Reference(CadReferenceFeature.LineEnd)));return true;
        }
        return false;
    }
    private CadGeometryReference? ExactDimensionReference(CadPointD point)
    {
        var refs=new List<CadGeometryReference>();
        var bounds=CadRectD.FromCenter(point,1e-6,1e-6);
        foreach(var id in _entityBoundsQuery(CadEditor.ActiveOwnerBlockId,bounds))
        {
            var e=CadEditor.Document.GetEntity(id);if(e.IsErased)continue;
            void Add(CadPointD p,CadReferenceFeature feature,int index=0,int count=0)
            {if(point.NearEquals(p,1e-7))refs.Add(new(e.Id.Value,e.OwnerBlockId.Value,feature,index,count));}
            switch(e)
            {
                case CadLine l:Add(l.Start,CadReferenceFeature.LineStart);Add(l.End,CadReferenceFeature.LineEnd);break;
                case CadCircle c:Add(c.Center,CadReferenceFeature.CircleCenter);break;
                case CadArc a:Add(a.StartPoint,CadReferenceFeature.ArcStart);Add(a.EndPoint,CadReferenceFeature.ArcEnd);break;
                case CadPolyline p:for(var i=0;i<p.Points.Count;i++)Add(p.Points[i],CadReferenceFeature.PolylineVertex,i,p.Points.Count);break;
            }
        }
        return refs.Count==1?refs[0]:null;
    }
    private CadDimensionDefinition DimensionDefinition(CadPointD placement) => new(DimensionKind,_dimensionAnchors.ToArray(),placement,
        new(DimensionStyleName,DimensionUnit,DimensionPrecision,DimensionTextHeight,DimensionArrowSize,DimensionExtensionGap,DimensionExtensionBeyond,LineWeight:DimensionLineWeight,ShapeFont:DimensionShapeFont,Arrow:DimensionArrow),
        DimensionAnnotationScale,string.IsNullOrWhiteSpace(DimensionTextOverride)?null:DimensionTextOverride);

    private bool HandleDimensionPoint(CadPointD point,bool explicitInput)
    {
        try
        {
            if(_dimensionAnchors.Count==0 && !explicitInput && PickEditEntity(point) is { } id && SeedDimension(CadEditor.Document.GetEntity(id))) { }
            else if(_dimensionAnchors.Count<DimensionPointCount)
            {
                if(CadCanvasToolMode is CadCanvasToolMode.DimRadius or CadCanvasToolMode.DimDiameter)
                    throw new InvalidOperationException(CadUiText.Get("DimensionChooseCircle"));
                if(_dimensionAnchors.Count>0 && _dimensionAnchors[^1].Point.NearEquals(point))throw new ArgumentException();
                _dimensionAnchors.Add(new(point,explicitInput?null:ExactDimensionReference(point)));
            }
            else
            {
                var definition=DimensionDefinition(point);definition.Validate();
                if(_reassociateDimension is { } existing)CadEditor.DocumentCommands.Execute(new SetDimensionCommand(existing,definition));
                else CadEditor.DocumentCommands.Execute(new AddDimensionCommand(definition,DrawingLayerId,CadEditor.ActiveOwnerBlockId));
                _dimensionAnchors.Clear();_reassociateDimension=null;
            }
            StepInputError="";RaiseInteractionStateChanged();RequestOverlayRender();return true;
        }
        catch(Exception ex)when(ex is ArgumentException or InvalidOperationException)
        {StepInputError=CadUiText.Get("InvalidStepInput");NotifyDrawingUx();return false;}
    }
    private void AddDimensionPreview(List<CadTransientItem> items,CadPointD point)
    {
        if(!IsDimensionTool || _dimensionAnchors.Count<DimensionPointCount)return;
        try
        {
            var document=Direct2dCad.Db.Cad.CadDocument.Create("preview");
            var dimension=document.AddDimension(DimensionDefinition(point) with
            {Anchors=_dimensionAnchors.Select(a=>a with {Reference=null}).ToArray()});
            foreach(var s in dimension.Strokes)items.Add(new CadTransientLine(s.Start,s.End,CadTransientStyle.Construction));
        }
        catch(ArgumentException){ }
    }
    private void AddDimensionAnchorMarkers(List<CadTransientItem> items)
    {
        if (!IsDimensionTool) return;
        var radius = 4 / Math.Max(InteractionZoom, 1e-9);
        var style = CadTransientStyle.Construction with
        {
            LinePattern = CadTransientLinePattern.Solid,
            StrokeWidth = 1.5,
            FillColor = Direct2dCad.Db.Cad.CadColor.FromArgb(96, 64, 196, 255)
        };
        foreach (var anchor in _dimensionAnchors)
            items.Add(new CadTransientCircle(anchor.Point, radius, style));
    }
    private void UpdateDimensionParameters()
    {
        if (_loadingDimension) return;
        try
        {
            if (IsDimensionTool || SelectedDimension is not { } d)
            {
                RequestOverlayRender();
                return;
            }
            if (!CanEditDimensionParameters) return;
            var previous = d.Definition;
            var definition = DimensionDefinition(previous.Placement) with
            { Kind=previous.Kind, Anchors=previous.Anchors, LinearRotationRadians=previous.LinearRotationRadians };
            definition.Validate();
            if (previous.Style == definition.Style && previous.TextOverride == definition.TextOverride &&
                previous.AnnotationScale == definition.AnnotationScale)
            {
                StepInputError = "";
                return;
            }
            _updatingDimension = true;
            try
            {
                var command = new SetDimensionCommand(d.Id, definition);
                if (_dimensionEditBatch is { } batch)
                    CadEditor.DocumentCommands.ExecuteInBatch(command, batch);
                else CadEditor.DocumentCommands.Execute(command);
                _loadedDimension = d.Id;
                _loadedDimensionDefinition = d.Definition;
            }
            finally { _updatingDimension = false; }
            StepInputError="";RaiseInteractionStateChanged();RequestRender();
        }
        catch(ArgumentException){StepInputError=CadUiText.Get("InvalidStepInput");}
    }
    [RelayCommand] private void DetachDimension()
    {
        if(SelectedDimension is not { } d)return;
        CadEditor.DocumentCommands.Execute(new SetDimensionCommand(d.Id,d.Definition with
        {Anchors=d.Definition.Anchors.Select(a=>a with{Reference=null}).ToArray()}));RaiseInteractionStateChanged();RequestRender();
    }
    [RelayCommand] private void ReassociateDimension()
    {
        if(SelectedDimension is not { } d)return;
        var mode=d.Definition.Kind switch {CadDimensionKind.LinearX=>CadCanvasToolMode.DimLinearX,CadDimensionKind.LinearY=>CadCanvasToolMode.DimLinearY,
            CadDimensionKind.Aligned=>CadCanvasToolMode.DimAligned,CadDimensionKind.Radius=>CadCanvasToolMode.DimRadius,CadDimensionKind.Diameter=>CadCanvasToolMode.DimDiameter,
            CadDimensionKind.Angular=>CadCanvasToolMode.DimAngular,_=>CadCanvasToolMode.Leader};
        var id=d.Id;SetToolMode(mode);_dimensionAnchors.Clear();_reassociateDimension=id;NotifyDrawingUx();
    }
}
