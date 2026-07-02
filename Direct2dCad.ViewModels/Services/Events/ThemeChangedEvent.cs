using System;
using System.Collections.Generic;
using System.Text;

namespace Direct2dCad.ViewModels.Services.Events;

public record class ThemeChangedEvent(bool IsDark);
