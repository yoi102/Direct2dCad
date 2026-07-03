using System;
using System.Collections.Generic;
using System.Text;

namespace Direct2dCad.ViewModels.Services;

public interface IToolboxIconsService
{

    object Explorer { get; }
    object Layers { get; }
    object Terminal { get; }
    object Search { get; }
    object Git { get; }
    object Problems { get; }

}
