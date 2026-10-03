using System.Globalization;
using Direct2dCad.Commands;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    private bool _applyingEditDynamicInput;
    private bool _offsetDistanceLockedByParameter;
    private bool IsOffsetDistanceLocked => _offsetDistanceLockedByParameter ||
        DynamicInputFields.Any(input => input.Key == "EditDistance" && input.IsLocked);

    private void UpdateOffsetDistanceFromPointer(CadPointD point)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.Offset || _editTargets.Count == 0 || IsOffsetDistanceLocked) return;
        try
        {
            var segments = CadCurveShape.From(CadEditor.Document.GetEntity(_editTargets[0])).Segments;
            if (segments.Count == 0) return;
            // Use the same nearest segment and side as the offset plan. The distance
            // is perpendicular to its supporting line, or radial for a circular edge.
            var nearest = segments.MinBy(segment =>
                segment.At(Math.Clamp(segment.Parameter(point), 0, 1)).DistanceTo(point));
            var distance = nearest.IsLine
                ? Math.Abs((nearest.End - nearest.Start).Normalize().Cross(point - nearest.Start))
                : Math.Abs(nearest.Center.DistanceTo(point) - nearest.Radius);
            if (!double.IsFinite(distance)) return;
            var applying = _applyingEditDynamicInput;
            _applyingEditDynamicInput = true;
            try { EditDistance = CadUnitConversion.FromMillimeters(distance, DocumentUnit); }
            finally { _applyingEditDynamicInput = applying; }
        }
        catch (NotSupportedException) { } // The preview reports unsupported source curves.
    }

    private string[] GetEditDynamicInputKeys() => CadCanvasToolMode switch
    {
        CadCanvasToolMode.Offset => ["EditDistance"],
        CadCanvasToolMode.Fillet => ["EditRadius"],
        CadCanvasToolMode.Chamfer => ["EditDistance", "EditSecondDistance"],
        CadCanvasToolMode.RectArray => ["EditRows", "EditColumns", "EditSpacingX", "EditSpacingY"],
        CadCanvasToolMode.PolarArray => ["EditCount", "EditSweep"],
        _ => []
    };

    private double[] GetEditDynamicInputValues() => CadCanvasToolMode switch
    {
        CadCanvasToolMode.Offset or CadCanvasToolMode.Fillet => [EditDistance],
        CadCanvasToolMode.Chamfer => [EditDistance, EditSecondDistance],
        CadCanvasToolMode.RectArray => [ArrayRows, ArrayColumns, ArraySpacingX, ArraySpacingY],
        CadCanvasToolMode.PolarArray => [ArrayCount, ArraySweepDegrees],
        _ => []
    };

    private bool TryApplyEditDynamicInput()
    {
        var values = GetEditDynamicInputValues();
        for (var index = 0; index < values.Length; index++)
        {
            var field = DynamicInputFields[index];
            if (!field.IsLocked) continue;
            if (!(double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out values[index]) ||
                  double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[index])) ||
                !double.IsFinite(values[index]) ||
                field.Key is "EditRows" or "EditColumns" or "EditCount" &&
                    (values[index] != Math.Truncate(values[index]) || values[index] is < int.MinValue or > int.MaxValue))
            {
                SetEditParameterInputValid(false);
                return false;
            }
        }

        _applyingEditDynamicInput = true;
        try
        {
            switch (CadCanvasToolMode)
            {
                case CadCanvasToolMode.Offset:
                case CadCanvasToolMode.Fillet: EditDistance = values[0]; break;
                case CadCanvasToolMode.Chamfer:
                    EditDistance = values[0]; EditSecondDistance = values[1]; break;
                case CadCanvasToolMode.RectArray:
                    ArrayRows = (int)values[0]; ArrayColumns = (int)values[1];
                    ArraySpacingX = values[2]; ArraySpacingY = values[3]; break;
                case CadCanvasToolMode.PolarArray:
                    ArrayCount = (int)values[0]; ArraySweepDegrees = values[1]; break;
            }
            SetEditParameterInputValid(true);
            RefreshDynamicInputValues();
            NotifyEditUx();
            return EditParametersValid;
        }
        finally { _applyingEditDynamicInput = false; }
    }
}
