using System.Diagnostics.CodeAnalysis;
using Dock.Model.Core;

namespace Direct2dCad.Avalonia.Services;

internal sealed partial class NativeDockShell
{
    // Validate all persisted data before replacing Root or reparenting live views.
    // JSON can contain null even for properties declared non-nullable in C#.
    private void ValidateState(NativeDockState saved)
    {
        void Require([DoesNotReturnIf(false)] bool valid) { if (!valid) throw new InvalidDataException("Invalid persisted Dock layout."); }
        Require(saved is not null && saved.Version == 2 && saved.Layout is not null &&
            saved.Hidden is not null && saved.Left is not null && saved.Right is not null &&
            saved.Top is not null && saved.Bottom is not null && saved.Owners is not null && saved.Windows is not null);
        var tools = Tools.Values.ToDictionary(tool => tool.Id);
        var seen = new HashSet<string>();
        var nodes = 0;
        var mainDocument = false;
        var mainSplit = false;

        void Visit(NativeDockNode? node, int depth, bool floating, string? parent = null)
        {
            Require(node is not null && depth <= 32 && ++nodes <= 256);
            Require(!string.IsNullOrWhiteSpace(node!.Id) && seen.Add(node.Id) && node.Children is not null && double.IsFinite(node.Proportion));
            Require(node.Kind is "root" or "split" or "tools" or "documents" or "separator" or "tool" or "document");
            Require(depth == 0 ? node.Kind == "root" && node.Children.Count == 1 : node.Kind != "root");
            if (node.Kind is "tool" or "document" or "separator") Require(node.Children.Count == 0);
            if (node.Kind == "tool") Require(tools.ContainsKey(node.Id) && parent is "tools" or "documents");
            else if (node.Kind == "document")
            {
                Require(node.Id == "cad.documents" && parent == "documents" && !floating);
                mainDocument = true;
            }
            else if (node.Kind == "separator") Require(parent == "split");
            else if (node.Kind == "tools")
            {
                Require(Enum.IsDefined((Alignment)node.Alignment));
                Require(node.Children.All(child => child is not null && child.Kind == "tool"));
            }
            else if (node.Kind == "documents") Require(node.Children.All(child => child is not null && child.Kind is "tool" or "document"));
            else if (node.Kind == "split")
            {
                Require(Enum.IsDefined((Orientation)node.Orientation));
                Require(node.Children.All(child => child is not null && child.Kind is "split" or "tools" or "documents" or "separator"));
                if (!floating) mainSplit = true;
            }
            foreach (var child in node.Children) Visit(child, depth + 1, floating, node.Kind);
        }

        Visit(saved!.Layout, 0, false);
        Require(mainDocument && mainSplit);
        foreach (var id in saved.Hidden.Concat(saved.Left).Concat(saved.Right).Concat(saved.Top).Concat(saved.Bottom))
            Require(!string.IsNullOrWhiteSpace(id) && tools.ContainsKey(id) && seen.Add(id));
        Require(saved.Windows.Count <= 32);
        foreach (var window in saved.Windows)
        {
            Require(window is not null && double.IsFinite(window.X) && double.IsFinite(window.Y) &&
                double.IsFinite(window.Width) && double.IsFinite(window.Height) && window.Width > 0 && window.Height > 0);
            Visit(window!.Layout, 0, true);
        }
        foreach (var (id, owner) in saved.Owners)
            Require(tools.ContainsKey(id) && !string.IsNullOrWhiteSpace(owner));
    }
}
