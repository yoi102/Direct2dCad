using System;
using System.Collections.Generic;
using System.Text;

namespace Direct2dCad.ViewServices.Abstractions.Events;

public record class ThemeChangedEvent(bool IsDark);
