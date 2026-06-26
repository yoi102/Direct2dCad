using System;
using System.Collections.Generic;
using System.Text;
using Direct2dCad.Editor.Input.Arges;

namespace Direct2dCad.Editor.Input;

public interface ICadInputController
{
    void PointerPressed(CadPointerEventArgs e);
    void PointerMoved(CadPointerEventArgs e);
    void PointerReleased(CadPointerEventArgs e);
    void PointerWheelChanged(CadPointerWheelEventArgs e);
    void KeyPressed(CadKeyEventArgs e);
    void KeyReleased(CadKeyEventArgs e);
}
