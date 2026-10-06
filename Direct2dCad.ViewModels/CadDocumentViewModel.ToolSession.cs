using Direct2dCad.Application.Tools;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel : ICadToolDocumentSession
{
    string ICadToolDocumentSession.ToolMode => CadCanvasToolMode.ToString();
}
