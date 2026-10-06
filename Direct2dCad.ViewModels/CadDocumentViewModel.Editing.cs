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
    private (EntityId Id, CadPointD Pick)? _editCornerPreviewTarget;
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
    [ObservableProperty] public partial bool EditWholePolyline { get; set; }
    public bool IsCurveEditTool => IsEditMode(CadCanvasToolMode);
    public bool HasDistanceParameter => CadCanvasToolMode is CadCanvasToolMode.Offset or CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer;
    public bool HasChamferParameter => CadCanvasToolMode==CadCanvasToolMode.Chamfer;
    public bool HasRectArrayParameters => CadCanvasToolMode==CadCanvasToolMode.RectArray;
    public bool HasPolarArrayParameters => CadCanvasToolMode==CadCanvasToolMode.PolarArray;
    public bool HasCornerParameters => CadCanvasToolMode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer;
    public bool CanChangeCornerTrim => !EditWholePolyline;
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
        foreach(var id in _editTargets)
        {
            var entity=CadEditor.Document.GetEntity(id);
            var pick=entity.Bounds.Center;
            if(mode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer)
                try { pick=CadCurveShape.From(entity).Segments[0].At(0.5); } catch(NotSupportedException) { }
            _editPicks.Add(pick);
        }
        if(mode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer && EditWholePolyline && _editTargets.Count>1)
        { _editTargets.RemoveRange(1,_editTargets.Count-1); _editPicks.RemoveRange(1,_editPicks.Count-1); }
        _editReady=_editTargets.Count>0 && mode is CadCanvasToolMode.Trim or CadCanvasToolMode.Extend or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray;
        _offsetDistanceLockedByParameter = false;
    }
    private void ClearEditInteraction()
    { _editTargets.Clear(); _editPicks.Clear(); _editReady=false; _editBreakPoint=null; _editCornerPreviewTarget=null; _editParameterInputValid=true; _editPreviewNotice=""; _offsetDistanceLockedByParameter=false; }
    private string EditPrompt => CadUiText.Get(CadCanvasToolMode.ToString())+": "+EditInstruction;
    private string EditPromptKey => CadCanvasToolMode switch
    {
        CadCanvasToolMode.Offset => _editTargets.Count==0 ? "EditChooseSource" : IsOffsetDistanceLocked ? "EditChooseSideLocked" : "EditChooseSide",
        CadCanvasToolMode.Trim or CadCanvasToolMode.Extend => !_editReady ? "EditChooseBoundaries" : "EditChooseSegment",
        CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer => EditWholePolyline
            ? _editTargets.Count==0 ? "EditChoosePolyline" : "EditConfirmCorner"
            : _editTargets.Count==0 ? "EditChooseFirstCurve" : _editTargets.Count==1 ? "EditChooseSecondCurve" : "EditConfirmCorner",
        CadCanvasToolMode.Join => "EditChooseChain",
        CadCanvasToolMode.Break => _editTargets.Count==0 ? "EditChooseSource" : _editBreakPoint is null ? "EditFirstBreak" : "EditSecondBreak",
        CadCanvasToolMode.RectArray => !_editReady ? "EditChooseArraySource" : "EditConfirmArray",
        CadCanvasToolMode.PolarArray => !_editReady ? "EditChooseArraySource" : "EditArrayCenter",
        _ => "EditChooseSource"
    };
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
    private void UpdateEditCornerPreviewTarget(CadPointD point)
    {
        if(!HasCornerParameters || EditWholePolyline || _editTargets.Count!=1) { _editCornerPreviewTarget=null; return; }
        if(PickEditEntity(point) is { } id && IsDifferentCornerSegment(id,point))
            _editCornerPreviewTarget=(id,point);
    }
    private bool IsDifferentCornerSegment(EntityId id,CadPointD point)
    {
        if(id!=_editTargets[0]) return true;
        try { return CadCurveEditing.PickSegment(CadEditor.Document.GetEntity(id),point)!=
            CadCurveEditing.PickSegment(CadEditor.Document.GetEntity(id),_editPicks[0]); }
        catch(NotSupportedException) { return false; }
    }
    private bool HandleEditClick(CadPointD rawWorld,bool explicitInput)
    {
        if(!IsCurveEditTool) return false;
        try
        {
            var mode=CadCanvasToolMode;
            if(mode==CadCanvasToolMode.Offset && _editTargets.Count==0 || mode==CadCanvasToolMode.Break && _editTargets.Count==0 ||
                mode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer && _editTargets.Count<(EditWholePolyline ? 1 : 2) ||
                mode==CadCanvasToolMode.Join || mode is CadCanvasToolMode.Trim or CadCanvasToolMode.Extend or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray && !_editReady)
            {
                if(PickEditEntity(rawWorld) is not { } id)
                    throw new InvalidOperationException("Choose another editable source or boundary curve.");
                if(_editTargets.IndexOf(id) is var index && index>=0)
                {
                    if(HasCornerParameters && !EditWholePolyline && _editTargets.Count==1 && IsDifferentCornerSegment(id,rawWorld))
                    { SelectEditTarget(id,rawWorld); return true; }
                    _editTargets.RemoveAt(index); _editPicks.RemoveAt(index);
                    CadEditor.Selection.Replace(_editTargets); StepInputError=""; return true;
                }
                StepInputError="";
                if(mode is CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray) _editReady=true;
                SelectEditTarget(id,rawWorld);
                return true;
            }
            if(mode==CadCanvasToolMode.Break && _editBreakPoint is null)
            { _editBreakPoint=explicitInput ? rawWorld : SnapWorld(rawWorld); return true; }
            CommitEdit(mode is CadCanvasToolMode.Break or CadCanvasToolMode.PolarArray && !explicitInput ? SnapWorld(rawWorld) : rawWorld);
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
                if(EditWholePolyline) return CadCurveEditing.AllCorners(Target(),Millimetres(EditDistance),Millimetres(EditSecondDistance),CadCanvasToolMode==CadCanvasToolMode.Fillet);
                return CadCurveEditing.Corner(Target(),Target(1),_editPicks[0],_editPicks[1],Millimetres(EditDistance),Millimetres(EditSecondDistance),CadCanvasToolMode==CadCanvasToolMode.Fillet,TrimCornerLines);
            case CadCanvasToolMode.Join: return CadCurveEditing.Join(_editTargets.Select(document.GetEntity).ToArray());
            case CadCanvasToolMode.Break: return CadCurveEditing.Break(Target(),_editBreakPoint ?? point,point);
            default: throw new InvalidOperationException("The tool is waiting for more input.");
        }
    }
    private void CommitEdit(CadPointD point,bool singleBreak=false)
    {
        if (!EditParametersValid) { StepInputError=CadUiText.Get("EditInvalidParameters"); NotifyEditUx(); return; }
        ICadCommand command;
        if(CadCanvasToolMode==CadCanvasToolMode.RectArray) command=new ArrayEntitiesCommand(_editTargets,ArrayRows,ArrayColumns,Millimetres(ArraySpacingX),Millimetres(ArraySpacingY));
        else if(CadCanvasToolMode==CadCanvasToolMode.PolarArray) command=new ArrayEntitiesCommand(_editTargets,point,ArrayCount,ArraySweepDegrees*Math.PI/180,ArrayRotateCopies);
        else
        {
            var plan=singleBreak ? CadCurveEditing.Break(CadEditor.Document.GetEntity(_editTargets[0]),_editBreakPoint!.Value) : CreateEditPlan(point);
            command=new EditCurvesCommand(CadCanvasToolMode.ToString(),plan);
        }
        CadEditor.DocumentCommands.Execute(command);
        if(HasCornerParameters && command is EditCurvesCommand cornerCommand)
            CadEditor.Selection.Replace(cornerCommand.ResultEntityIds);
        StepInputError=""; EditErrorDetail="";
        if(CadCanvasToolMode is CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer or CadCanvasToolMode.Break) ClearEditInteraction();
        else if(CadCanvasToolMode is CadCanvasToolMode.Join or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray) SetToolMode(CadCanvasToolMode.Select);
    }
    private bool CompleteEditInteraction()
    {
        if(!IsCurveEditTool) return false;
        if(CadCanvasToolMode==CadCanvasToolMode.Offset)
        { SetToolMode(CadCanvasToolMode.Select); return true; }
        StepInputError="";
        if(!EditParametersValid) { StepInputError=CadUiText.Get("EditInvalidParameters"); NotifyEditUx(); return true; }
        try
        {
            if(!_editReady && CadCanvasToolMode is CadCanvasToolMode.Trim or CadCanvasToolMode.Extend or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray)
            { if(_editTargets.Count==0) throw new InvalidOperationException("Select at least one source or boundary."); _editReady=true; }
            else if(CadCanvasToolMode==CadCanvasToolMode.Join || CadCanvasToolMode==CadCanvasToolMode.Break && _editBreakPoint is not null ||
                CadCanvasToolMode is CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray || HasCornerParameters && _editTargets.Count==(EditWholePolyline ? 1 : 2))
                CommitEdit(CadCanvasToolMode==CadCanvasToolMode.PolarArray ? SnapWorld(_dynamicInputPointer) : _dynamicInputPointer,singleBreak:CadCanvasToolMode==CadCanvasToolMode.Break);
            else if(CadCanvasToolMode is CadCanvasToolMode.Offset or CadCanvasToolMode.Trim or CadCanvasToolMode.Extend) SetToolMode(CadCanvasToolMode.Select);
            else StepInputError=EditInstruction;
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or NotSupportedException) { ReportEditError(ex); }
        NotifyDrawingUx(); RequestOverlayRender(); return true;
    }
    private void ReportEditError(Exception ex)
    { StepInputError=EditFailureMessage(ex); EditErrorDetail=ex.Message; OnPropertyChanged(nameof(EditErrorDetail)); NotifyEditUx(); }
    private void AddEditPreview(List<CadTransientItem> items,CadPointD point)
    {
        if(!IsCurveEditTool) return;
        var notice="";
        try
        {
            foreach(var id in _editTargets.Distinct())
                items.Add(new CadTransientEntityReference(id,default,EditSourceStyle,UseSourceAppearance:false));
            if(!EditParametersValid) return;
            var hovered=PickEditEntity(point);
            if(_editTargets.Count==0)
            {
                if(hovered is { } candidate) items.Add(new CadTransientEntityReference(candidate,default,EditSourceStyle,UseSourceAppearance:false));
                return;
            }
            if(CadCanvasToolMode is CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray)
            {
                AddArrayPreview(items,point);
                var placements=HasRectArrayParameters ? (long)ArrayRows*ArrayColumns : ArrayCount;
                if(_editReady && placements>256) notice=CadUiText.Get("EditArrayPreviewLimited");
                return;
            }
            if(CadCanvasToolMode is CadCanvasToolMode.Trim or CadCanvasToolMode.Extend && !_editReady) return;
            CadCurveEditPlan plan;
            if(HasCornerParameters && !EditWholePolyline && _editTargets.Count<2)
            {
                var candidate = hovered is { } id && IsDifferentCornerSegment(id,point)
                    ? (id,point) : _editCornerPreviewTarget;
                if(candidate is not { } picked) return;
                var (second, secondPick) = picked;
                items.Add(new CadTransientEntityReference(second,default,EditSourceStyle,UseSourceAppearance:false));
                plan=CadCurveEditing.Corner(CadEditor.Document.GetEntity(_editTargets[0]),CadEditor.Document.GetEntity(second),
                    _editPicks[0],secondPick,Millimetres(EditDistance),Millimetres(EditSecondDistance),CadCanvasToolMode==CadCanvasToolMode.Fillet,TrimCornerLines);
            }
            else if(CadCanvasToolMode==CadCanvasToolMode.Join && _editTargets.Count<2)
            {
                if(hovered is not { } second || _editTargets.Contains(second)) return;
                plan=CadCurveEditing.Join(_editTargets.Append(second).Select(CadEditor.Document.GetEntity).ToArray());
            }
            else if(CadCanvasToolMode==CadCanvasToolMode.Break)
            {
                var target=CadEditor.Document.GetEntity(_editTargets[0]);
                var marker=ProjectEditPoint(target,_editBreakPoint ?? SnapWorld(point));
                AddEditPointMarker(items,marker);
                if(_editBreakPoint is null) plan=CadCurveEditing.Break(target,marker);
                else
                {
                    AddEditPointMarker(items,ProjectEditPoint(target,SnapWorld(point)));
                    foreach(var segment in CadPlanarCurves.Get(target)) AddEditPrimitive(items,segment,EditRemovedStyle);
                    plan=CreateEditPlan(SnapWorld(point));
                }
            }
            else plan=CreateEditPlan(point);
            foreach(var shape in plan.PreviewGeometry ?? plan.Replacements.SelectMany(r=>r.Shapes).Concat(plan.Creations.Select(c=>c.Shape)).ToArray())
                foreach(var segment in shape.Segments) AddEditPrimitive(items,segment,CadCanvasToolMode==CadCanvasToolMode.Trim ? EditRemovedStyle : EditResultStyle);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            notice=EditFailureMessage(ex); EditErrorDetail=ex.Message; OnPropertyChanged(nameof(EditErrorDetail));
        }
        finally
        {
            if(_editPreviewNotice!=notice) { _editPreviewNotice=notice; NotifyEditUx(); }
        }
    }
    private static readonly CadTransientStyle EditSourceStyle=new(CadColor.FromArgb(255,255,183,77),2);
    private static readonly CadTransientStyle EditResultStyle=new(CadColor.FromArgb(255,92,230,145),2);
    private static readonly CadTransientStyle EditRemovedStyle=new(CadColor.FromArgb(255,255,92,92),2);
    private static CadPointD ProjectEditPoint(CadEntity entity,CadPointD point) => CadPlanarCurves.Get(entity)
        .Select(segment=>segment.NearestPoint(point)).MinBy(p=>p.DistanceTo(point));
    private void AddEditPointMarker(List<CadTransientItem> items,CadPointD point) =>
        items.Add(new CadTransientCircle(point,4/Math.Max(InteractionZoom,1e-9),EditSourceStyle with { FillColor=EditSourceStyle.StrokeColor }));
    private static string EditFailureMessage(Exception ex) => CadUiText.Get(ex switch
    {
        NotSupportedException => "EditUnsupportedObject",
        ArgumentException => "EditInvalidParameters",
        _ when ex.Message.Contains("adjacent",StringComparison.OrdinalIgnoreCase) => "EditChooseAdjacentSegments",
        _ when ex.Message.Contains("endpoint segment",StringComparison.OrdinalIgnoreCase) => "EditChoosePathEndpoint",
        _ when ex.Message.Contains("no sharp corners",StringComparison.OrdinalIgnoreCase) => "EditNoSharpCorners",
        _ when ex.Message.Contains("overlap",StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("exceed",StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("self-intersect",StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("reduce the radius",StringComparison.OrdinalIgnoreCase) => "EditCornerTooLarge",
        _ when ex.Message.Contains("intersect",StringComparison.OrdinalIgnoreCase) => "EditNoIntersection",
        _ when ex.Message.Contains("offset",StringComparison.OrdinalIgnoreCase) => "EditReduceDistance",
        _ when ex.Message.Contains("connected",StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("chain",StringComparison.OrdinalIgnoreCase) => "EditJoinDisconnected",
        _ => "EditCannotApply"
    });
    private static void AddEditPrimitive(List<CadTransientItem> items,CadPlanarPrimitive p,CadTransientStyle style)
    { items.Add(p.IsLine ? new CadTransientLine(p.Start,p.End,style) : p.IsEllipse
        ? new CadTransientEllipseArc(p.Center,p.RadiusX,p.RadiusY,p.StartAngle,p.Sweep,style,p.Rotation)
        : new CadTransientArc(p.Center,p.Radius,p.StartAngle,p.Sweep,style)); }
    private void AddArrayPreview(List<CadTransientItem> items,CadPointD pointer)
    {
        if(!_editReady) return;
        var document=CadEditor.Document; var bounds=_editTargets.Aggregate(CadRectD.Empty,(b,id)=>b.Union(document.GetEntity(id).Bounds));
        var center=SnapWorld(pointer);
        if(HasPolarArrayParameters)
        { AddEditPointMarker(items,center); items.Add(new CadTransientLine(center,bounds.Center,CadTransientStyle.Construction)); }
        var count=CadCanvasToolMode==CadCanvasToolMode.RectArray ? (long)ArrayRows*ArrayColumns : ArrayCount;
        if(count<2 || count*_editTargets.Count>10000) return;
        var children=_editTargets.Select(id=>(CadTransientItem)new CadTransientEntityReference(id,default,EditResultStyle,UseSourceAppearance:false)).ToArray();
        for(var i=1;i<Math.Min(count,256);i++)
        {
            CadMatrixD matrix;
            if(CadCanvasToolMode==CadCanvasToolMode.RectArray) matrix=CadMatrixD.CreateTranslation(i%ArrayColumns*Millimetres(ArraySpacingX),i/ArrayColumns*Millimetres(ArraySpacingY));
            else
            {
                var full=Math.Abs(Math.Abs(ArraySweepDegrees)-360)<1e-9;
                matrix=CadMatrixD.CreateRotation(ArraySweepDegrees*Math.PI/180*i/(full ? ArrayCount : ArrayCount-1),center);
                if(!ArrayRotateCopies) matrix=CadMatrixD.CreateTranslation(matrix.TransformPoint(bounds.Center)-bounds.Center);
            }
            items.Add(new CadTransientGroup(children,matrix));
        }
    }
    bool ICadCommandLineContext.SubmitScalarInput(double value)
    {
        StepInputError = "";
        if(!double.IsFinite(value) || value<=0 || IsPastePreviewActive) return false;
        if(HasDistanceParameter)
        {
            return TrySubmitEditDistance(value);
        }
        if(DrawingAnchor is not { } anchor || CadCanvasToolMode is not (CadCanvasToolMode.Line or CadCanvasToolMode.Polyline or CadCanvasToolMode.Polygon or CadCanvasToolMode.Spline or CadCanvasToolMode.CircleCenterRadius or CadCanvasToolMode.CircleCenterDiameter)) return false;
        var direction=_currentMousePoint is { } p ? (ScreenToWorld(p)-anchor).Normalize() : CadVectorD.UnitX;
        if(direction.Length==0) direction=CadVectorD.UnitX;
        if (DynamicInputFields.FirstOrDefault(field => field.Key == "Angle" && field.IsLocked) is { } angle)
        {
            if (!double.TryParse(angle.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture, out var degrees) || !double.IsFinite(degrees))
            {
                StepInputError = "Enter a finite dynamic-input angle.";
                return false;
            }
            var radians = degrees * Math.PI / 180;
            direction = new CadVectorD(Math.Cos(radians), Math.Sin(radians));
        }
        return HandleDrawingWorldPoint(anchor+direction*Millimetres(value));
    }
}
