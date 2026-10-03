using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.CommandLine;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Lang;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    private readonly List<EntityId> _editTargets=[];
    private readonly List<CadPointD> _editPicks=[];
    private CadPointD? _editBreakPoint;
    private CadPointD? _editArrayCenter;
    private bool _editReady;
    private readonly Dictionary<CadCanvasToolMode,(double Distance,double Second)> _editParameterMemory=[];
    [ObservableProperty] public partial double EditDistance { get; set; }=10;
    [ObservableProperty] public partial double EditSecondDistance { get; set; }=10;
    [ObservableProperty] public partial int ArrayRows { get; set; }=2;
    [ObservableProperty] public partial int ArrayColumns { get; set; }=3;
    [ObservableProperty] public partial int ArrayCount { get; set; }=6;
    [ObservableProperty] public partial double ArraySpacingX { get; set; }=20;
    [ObservableProperty] public partial double ArraySpacingY { get; set; }=20;
    [ObservableProperty] public partial double ArraySweepDegrees { get; set; }=360;
    [ObservableProperty] public partial bool ArrayRotateCopies { get; set; }=true;
    [ObservableProperty] public partial bool TrimCornerLines { get; set; }=true;
    public bool IsCurveEditTool => IsEditMode(CadCanvasToolMode);
    public bool HasDistanceParameter => CadCanvasToolMode is CadCanvasToolMode.Offset or CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer;
    public bool HasChamferParameter => CadCanvasToolMode==CadCanvasToolMode.Chamfer;
    public bool HasRectArrayParameters => CadCanvasToolMode==CadCanvasToolMode.RectArray;
    public bool HasPolarArrayParameters => CadCanvasToolMode==CadCanvasToolMode.PolarArray;
    public bool HasCornerParameters => CadCanvasToolMode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer;
    public string EditErrorDetail { get; private set; }="";
    public static bool IsEditMode(CadCanvasToolMode mode) => mode>=CadCanvasToolMode.Offset && mode<=CadCanvasToolMode.PolarArray;
    private double Millimetres(double value) => CadUnitConversion.ToMillimeters(value,DocumentUnit);
    private void BeginEditInteraction(CadCanvasToolMode mode)
    {
        if(HasDistanceParameter) _editParameterMemory[CadCanvasToolMode]=(EditDistance,EditSecondDistance);
        ClearEditInteraction();
        if(!IsEditMode(mode)) return;
        if(mode is CadCanvasToolMode.Offset or CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer)
        {var saved=_editParameterMemory.GetValueOrDefault(mode,(10,10));EditDistance=saved.Distance;EditSecondDistance=saved.Second;}
        _editTargets.AddRange(CadEditor.Selection.EntityIds.Where(id=>CadEditor.Document.TryGetEntity(id,out var e) && e is not null && CadEntityAccessPolicy.IsEditable(CadEditor.Document,e)));
        if(mode==CadCanvasToolMode.Offset && _editTargets.Count>1) _editTargets.RemoveRange(1,_editTargets.Count-1);
        if(mode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer && _editTargets.Count>2) _editTargets.RemoveRange(2,_editTargets.Count-2);
        foreach(var id in _editTargets) _editPicks.Add(CadEditor.Document.GetEntity(id).Bounds.Center);
        _editReady=_editTargets.Count>0 && mode is CadCanvasToolMode.Trim or CadCanvasToolMode.Extend or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray;
    }
    private void ClearEditInteraction()
    { _editTargets.Clear(); _editPicks.Clear(); _editReady=false; _editBreakPoint=null; _editArrayCenter=null; }
    private string EditPrompt => CadUiText.Get(CadCanvasToolMode.ToString())+": "+CadUiText.Get(CadCanvasToolMode switch
    {
        CadCanvasToolMode.Offset => _editTargets.Count==0 ? "EditChooseSource" : "EditChooseSide",
        CadCanvasToolMode.Trim or CadCanvasToolMode.Extend => !_editReady ? "EditChooseBoundaries" : "EditChooseSegment",
        CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer => _editTargets.Count<2 ? "EditChooseTwoLines" : "EditConfirmCorner",
        CadCanvasToolMode.Join => "EditChooseChain",
        CadCanvasToolMode.Break => _editTargets.Count==0 ? "EditChooseSource" : _editBreakPoint is null ? "EditFirstBreak" : "EditSecondBreak",
        CadCanvasToolMode.RectArray => !_editReady ? "EditChooseArray" : "EditConfirmArray",
        CadCanvasToolMode.PolarArray => !_editReady ? "EditChooseArray" : _editArrayCenter is null ? "EditArrayCenter" : "EditConfirmArray",
        _ => "EditChooseSource"
    });
    private EntityId? PickEditEntity(CadPointD world,bool excludeBoundaries=false)
    {
        var document=CadEditor.Document;var tolerance=8/Math.Max(InteractionZoom,1e-9);
        var bounds=CadRectD.FromCenter(world,tolerance*2,tolerance*2);
        return _entityBoundsQuery(CadEditor.ActiveOwnerBlockId,bounds).Select(document.GetEntity)
            .Where(e=>CanSelectEntity(e) && CadEntityAccessPolicy.IsEditable(document,e) && (!excludeBoundaries || !_editTargets.Contains(e.Id)))
            .OrderByDescending(e=>document.DocumentSettings.LayerDrawingPriority.GetPriority(e.LayerId)).ThenByDescending(e=>e.ZIndex)
            .ThenByDescending(e=>document.GetEntityInsertionIndex(e.Id))
            .FirstOrDefault(e=>Direct2dCad.HitTesting.CadEntityHitTester.HitTestEdge(document,e,world,tolerance,CreateHitTestOptions(),out _))?.Id;
    }
    private void SelectEditTarget(EntityId id,CadPointD point)
    { _editTargets.Add(id); _editPicks.Add(point); CadEditor.Selection.Replace(_editTargets); NotifyDrawingUx(); }
    private bool HandleEditClick(CadPointD rawWorld,bool explicitInput)
    {
        if(!IsCurveEditTool) return false;
        try
        {
            var mode=CadCanvasToolMode;
            if(mode==CadCanvasToolMode.Offset && _editTargets.Count==0 || mode==CadCanvasToolMode.Break && _editTargets.Count==0 ||
                mode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer && _editTargets.Count<2 ||
                mode==CadCanvasToolMode.Join || mode is CadCanvasToolMode.Trim or CadCanvasToolMode.Extend or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray && !_editReady)
            {
                if(PickEditEntity(rawWorld) is not { } id || _editTargets.Contains(id))
                    throw new InvalidOperationException("Choose another editable source or boundary curve.");
                SelectEditTarget(id,rawWorld);
                return true;
            }
            if(mode==CadCanvasToolMode.Break && _editBreakPoint is null)
            { _editBreakPoint=explicitInput ? rawWorld : SnapWorld(rawWorld); return true; }
            if(mode==CadCanvasToolMode.PolarArray && _editArrayCenter is null)
            { _editArrayCenter=explicitInput ? rawWorld : SnapWorld(rawWorld); return true; }
            CommitEdit(mode==CadCanvasToolMode.Break && !explicitInput ? SnapWorld(rawWorld) : rawWorld);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or NotSupportedException)
        { ReportEditError(ex); return false; }
        finally { NotifyDrawingUx(); RequestOverlayRender(updateHandleScene:true); }
        return true;
    }
    private CadCurveEditPlan CreateEditPlan(CadPointD point)
    {
        var document=CadEditor.Document;
        CadEntity Target(int index=0) => document.GetEntity(_editTargets[index]);
        switch(CadCanvasToolMode)
        {
            case CadCanvasToolMode.Offset: return CadCurveEditing.Offset(Target(),Millimetres(EditDistance),point);
            case CadCanvasToolMode.Trim:
            case CadCanvasToolMode.Extend:
                var hit=PickEditEntity(point,true) ?? throw new InvalidOperationException("Choose a target curve away from the selected boundaries.");
                return CadCanvasToolMode==CadCanvasToolMode.Trim ? CadCurveEditing.Trim(document.GetEntity(hit),_editTargets.Select(document.GetEntity),point) : CadCurveEditing.Extend(document.GetEntity(hit),_editTargets.Select(document.GetEntity),point);
            case CadCanvasToolMode.Fillet:
            case CadCanvasToolMode.Chamfer:
                return CadCurveEditing.Corner(Target(),Target(1),_editPicks[0],_editPicks[1],Millimetres(EditDistance),Millimetres(EditSecondDistance),CadCanvasToolMode==CadCanvasToolMode.Fillet,TrimCornerLines);
            case CadCanvasToolMode.Join: return CadCurveEditing.Join(_editTargets.Select(document.GetEntity).ToArray());
            case CadCanvasToolMode.Break: return CadCurveEditing.Break(Target(),_editBreakPoint ?? point,point);
            default: throw new InvalidOperationException("The tool is waiting for more input.");
        }
    }
    private void CommitEdit(CadPointD point,bool singleBreak=false)
    {
        ICadCommand command;
        if(CadCanvasToolMode==CadCanvasToolMode.RectArray) command=new ArrayEntitiesCommand(_editTargets,ArrayRows,ArrayColumns,Millimetres(ArraySpacingX),Millimetres(ArraySpacingY));
        else if(CadCanvasToolMode==CadCanvasToolMode.PolarArray) command=new ArrayEntitiesCommand(_editTargets,_editArrayCenter ?? throw new InvalidOperationException("Choose the array center."),ArrayCount,ArraySweepDegrees*Math.PI/180,ArrayRotateCopies);
        else
        {
            var plan=singleBreak ? CadCurveEditing.Break(CadEditor.Document.GetEntity(_editTargets[0]),_editBreakPoint!.Value) : CreateEditPlan(point);
            command=new EditCurvesCommand(CadCanvasToolMode.ToString(),plan);
        }
        CadEditor.DocumentCommands.Execute(command);
        StepInputError=""; EditErrorDetail="";
        if(CadCanvasToolMode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer or CadCanvasToolMode.Break) ClearEditInteraction();
        else if(CadCanvasToolMode is CadCanvasToolMode.Join or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray) SetToolMode(CadCanvasToolMode.Select);
    }
    private bool CompleteEditInteraction()
    {
        if(!IsCurveEditTool) return false;
        StepInputError="";
        try
        {
            if(!_editReady && CadCanvasToolMode is CadCanvasToolMode.Trim or CadCanvasToolMode.Extend or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray)
            { if(_editTargets.Count==0) throw new InvalidOperationException("Select at least one source or boundary."); _editReady=true; }
            else if(CadCanvasToolMode==CadCanvasToolMode.Join || CadCanvasToolMode==CadCanvasToolMode.Break && _editBreakPoint is not null ||
                CadCanvasToolMode is CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray || CadCanvasToolMode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer && _editTargets.Count==2)
                CommitEdit(_currentMousePoint is { } p ? ScreenToWorld(p) : default,singleBreak:CadCanvasToolMode==CadCanvasToolMode.Break);
            else SetToolMode(CadCanvasToolMode.Select);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or NotSupportedException) { ReportEditError(ex); }
        NotifyDrawingUx(); RequestOverlayRender(); return true;
    }
    private void ReportEditError(Exception ex)
    { StepInputError=CadUiText.Get("EditCannotApply"); EditErrorDetail=ex.Message; OnPropertyChanged(nameof(EditErrorDetail)); }
    private void AddEditPreview(List<CadTransientItem> items,CadPointD point)
    {
        if(!IsCurveEditTool || _editTargets.Count==0) return;
        if(CadCanvasToolMode is CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray) { AddArrayPreview(items); return; }
        if(CadCanvasToolMode is CadCanvasToolMode.Trim or CadCanvasToolMode.Extend && !_editReady ||
            CadCanvasToolMode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer && _editTargets.Count<2 || CadCanvasToolMode==CadCanvasToolMode.Break && _editBreakPoint is null) return;
        try
        {
            var plan=CreateEditPlan(CadCanvasToolMode==CadCanvasToolMode.Break ? SnapWorld(point) : point);
            foreach(var shape in plan.PreviewGeometry ?? plan.Replacements.SelectMany(r=>r.Shapes).Concat(plan.Creations.Select(c=>c.Shape)).ToArray())
                foreach(var segment in shape.Segments) AddEditPrimitive(items,segment,CadCanvasToolMode==CadCanvasToolMode.Trim ? new CadTransientStyle(CadColor.Red,1) : CadTransientStyle.Construction);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or NotSupportedException) { }
    }
    private static void AddEditPrimitive(List<CadTransientItem> items,CadPlanarPrimitive p,CadTransientStyle style)
    { items.Add(p.IsLine ? new CadTransientLine(p.Start,p.End,style) : new CadTransientArc(p.Center,p.Radius,p.StartAngle,p.Sweep,style)); }
    private void AddArrayPreview(List<CadTransientItem> items)
    {
        if(!_editReady || CadCanvasToolMode==CadCanvasToolMode.PolarArray && _editArrayCenter is null) return;
        var document=CadEditor.Document; var bounds=_editTargets.Aggregate(CadRectD.Empty,(b,id)=>b.Union(document.GetEntity(id).Bounds));
        var count=CadCanvasToolMode==CadCanvasToolMode.RectArray ? (long)ArrayRows*ArrayColumns : ArrayCount;
        if(count<2 || count*_editTargets.Count>10000 || ArrayRows<1 || ArrayColumns<1) return;
        for(var i=1;i<Math.Min(count,256);i++)
        {
            CadMatrixD matrix;
            if(CadCanvasToolMode==CadCanvasToolMode.RectArray) matrix=CadMatrixD.CreateTranslation(i%ArrayColumns*Millimetres(ArraySpacingX),i/ArrayColumns*Millimetres(ArraySpacingY));
            else
            {
                var full=Math.Abs(Math.Abs(ArraySweepDegrees)-360)<1e-9;
                matrix=CadMatrixD.CreateRotation(ArraySweepDegrees*Math.PI/180*i/(full ? ArrayCount : ArrayCount-1),_editArrayCenter!.Value);
                if(!ArrayRotateCopies) matrix=CadMatrixD.CreateTranslation(matrix.TransformPoint(bounds.Center)-bounds.Center);
            }
            var children=_editTargets.Select(id=>(CadTransientItem)new CadTransientEntityReference(id,default,CadTransientStyle.Construction)).ToArray();
            items.Add(new CadTransientGroup(children,matrix));
        }
    }
    bool ICadCommandLineContext.SubmitScalarInput(double value)
    {
        if(!double.IsFinite(value) || value<=0) return false;
        if(HasDistanceParameter) { EditDistance=value; StepInputError=""; RequestOverlayRender(); return true; }
        if(DrawingAnchor is not { } anchor || CadCanvasToolMode is not (CadCanvasToolMode.Line or CadCanvasToolMode.CircleCenterRadius or CadCanvasToolMode.CircleCenterDiameter)) return false;
        var direction=_currentMousePoint is { } p ? (ScreenToWorld(p)-anchor).Normalize() : CadVectorD.UnitX;
        if(direction.Length==0) direction=CadVectorD.UnitX;
        return HandleDrawingWorldPoint(anchor+direction*Millimetres(value));
    }
}
