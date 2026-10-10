using System.Reflection;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Security;
using Direct2dCad.ViewModels;
using Direct2dCad.ViewModels.Toolboxes;

// This tool runs at development time. Generated views use typed compiled bindings;
// neither reflection nor runtime XAML loading is used to select a view in the app.
var repo = Path.GetFullPath(args.Length > 0 ? args[0] : "../..");
var client = Path.Combine(repo, "Direct2dCad.Avalonia");
var resourceKeys = XDocument.Load(Path.Combine(repo, "Direct2dCad.Lang", "Strings", "Strings.resx")).Descendants("data").Select(n => (string?)n.Attribute("name")).Where(n => n is not null).ToHashSet(StringComparer.Ordinal);
var types = typeof(MainViewModel).Assembly.GetTypes().Where(t => t.IsPublic).ToArray();
if (args.Contains("--inspect"))
{
    foreach (var type in typeof(MessagePipe.IPublisher<>).Assembly.GetTypes()) foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.Contains("MessageBroker"))) Console.WriteLine(type.Name + ": " + method);
    foreach (var type in new[] { typeof(MessagePipe.IPublisher<>), typeof(MessagePipe.ISubscriber<>), typeof(MessagePipe.IAsyncPublisher<>), typeof(MessagePipe.IAsyncSubscriber<>), typeof(MessagePipe.MessagePipeOptions), typeof(MessagePipe.MessageBroker<>) }) { Console.WriteLine(type.FullName); foreach (var method in type.GetMethods().Concat<MethodBase>(type.GetConstructors())) Console.WriteLine(method); }
    return;
}
var typeIndex = types.ToDictionary(t => t.FullName!);
var allTypes = types.Concat(typeof(Direct2dCad.ViewModels.Services.Platform.IFileDialogService).Assembly.GetTypes()).Concat(typeof(Direct2dCad.ViewModels.Enums.CadCanvasToolMode).Assembly.GetTypes()).Distinct().ToArray();
var generated = new Dictionary<Type, string>();
var audit = new List<string>();
var enumTypes = new HashSet<Type>();
var namespaces = new Dictionary<string, string>();
var iconFactory = typeof(MaterialDesignThemes.Wpf.PackIcon).Assembly.GetType("MaterialDesignThemes.Wpf.PackIconDataFactory")!;
var iconData = (System.Collections.IDictionary)iconFactory.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
var commonIcons = new[] { "Github", "Pin", "PinOff", "WeatherNight", "WeatherSunny", "Translate", "Close", "Minus", "WindowMaximize", "WindowRestore", "FileDocumentOutline", "FolderOpenOutline", "Refresh", "Plus", "Delete", "Eye", "EyeOff", "Lock", "Unlocked", "Snowflake", "SnowflakeOff", "DotsVertical", "ConsoleLine", "FilterVariant", "Magnify", "VectorPolyline", "ShapeOutline", "ChatOutline", "RobotOutline", "History", "FormatListBulleted", "Layers", "Grid", "Magnet", "AngleRight", "AngleAcute", "Cog", "Check", "ContentSaveOutline", "OpenInApp", "HandWave", "FitToPageOutline", "Fullscreen", "ContentCopy", "OpenInNew", "Paperclip", "Send", "Stop", "Broom", "Target", "PrinterOutline", "ViewDashboardOutline", "Pencil", "InsertLink", "ExitRun", "HandWaveOutline", "Tune", "BackupRestore", "DockLeft", "DockRight", "DockBottom", "LinkVariant", "LinkVariantOff" };
File.WriteAllText(Path.Combine(client, "CadIconData.cs"), "// WPF Material icon paths generated at development time; no runtime icon reflection.\nnamespace Direct2dCad.Avalonia;\ninternal static class CadIconData { public static string? Get(string? kind) => kind switch {\n" + string.Join("\n", commonIcons.Select(name => Enum.TryParse<MaterialDesignThemes.Wpf.PackIconKind>(name, out var key) && iconData.Contains(key) ? $"\"{name}\" => \"{iconData[key]}\"," : throw new InvalidOperationException("Unknown WPF icon " + name))) + "\n_ => null }; }\n");
var cadIconTemplates = XDocument.Load(Path.Combine(repo, "Direct2dCad.wpf", "Views", "CadToolIconResources.xaml")).Root!.Elements()
    .Where(e => e.Name.LocalName == "DataTemplate").ToDictionary(e => e.Attributes().Single(a => a.Name.LocalName == "Key").Value);
string Icon(XElement source)
{
    var template = Regex.Match(source.Attribute("ContentTemplate")?.Value ?? source.Descendants().Select(e => e.Attribute("ContentTemplate")?.Value).FirstOrDefault(value => value is not null) ?? "", @"StaticResource\s+(\w+)");
    if (template.Success && cadIconTemplates.TryGetValue(template.Groups[1].Value, out var iconTemplate)) source = iconTemplate;
    var kind = source.Descendants().FirstOrDefault(n => n.Name.LocalName == "PackIcon")?.Attribute("Kind")?.Value
        ?? Regex.Match(source.Attribute("Content")?.Value ?? "", @"PackIcon\s+Kind=(\w+)").Groups[1].Value;
    if (kind is not null && Enum.TryParse<MaterialDesignThemes.Wpf.PackIconKind>(kind, out var key) && iconData.Contains(key)) return $"<PathIcon Width=\"26\" Height=\"26\" Data=\"{SecurityElement.Escape((string?)iconData[key])}\" />";
    var canvas = source.Descendants().FirstOrDefault(n => n.Name.LocalName == "Canvas");
    var shapes = (canvas?.Elements() ?? source.Descendants()).Where(n => n.Name.LocalName is "Path" or "Ellipse" or "Rectangle" or "Line" or "Polyline" or "Polygon" or "TextBlock").ToArray();
    if (shapes.Length == 0) return "";
    var parts = new StringBuilder();
    foreach (var shape in shapes)
    {
        parts.Append('<').Append(shape.Name.LocalName);
        foreach (var attr in shape.Attributes().Where(a => a.Name.NamespaceName.Length == 0 && a.Name.LocalName is "Data" or "Width" or "Height" or "Canvas.Left" or "Canvas.Top" or "StrokeThickness" or "StrokeDashArray" or "Points" or "X1" or "Y1" or "X2" or "Y2" or "Text" or "FontSize" or "FontWeight" or "Opacity"))
            if (!attr.Value.StartsWith('{') && attr.Name.LocalName is not ("X1" or "Y1" or "X2" or "Y2")) parts.Append($" {attr.Name.LocalName}=\"{SecurityElement.Escape(attr.Name.LocalName == "StrokeDashArray" ? attr.Value.Replace(' ', ',') : attr.Value)}\"");
        if (shape.Name.LocalName == "Line") parts.Append($" StartPoint=\"{shape.Attribute("X1")?.Value ?? "0"},{shape.Attribute("Y1")?.Value ?? "0"}\" EndPoint=\"{shape.Attribute("X2")?.Value ?? "0"},{shape.Attribute("Y2")?.Value ?? "0"}\"");
        var style = shape.Attribute("Style")?.Value ?? "";
        var stroke = shape.Attribute("Stroke") is not null || style.Contains("StrokePath");
        if (stroke) parts.Append(" Stroke=\"{DynamicResource MaterialBodyBrush}\" StrokeJoin=\"Round\" StrokeLineCap=\"Round\"");
        if (stroke && shape.Attribute("StrokeThickness") is null) parts.Append(" StrokeThickness=\"2\"");
        if (shape.Name.LocalName != "TextBlock") parts.Append(shape.Attribute("Fill")?.Value == "Transparent" || stroke ? " Fill=\"Transparent\"" : " Fill=\"{DynamicResource MaterialBodyBrush}\"");
        parts.Append(" />");
    }
    // A Path directly inside a WPF Viewbox measures its geometry. Wrapping it
    // in a made-up 32 px Canvas clips large coordinates (notably the spline).
    if (canvas is null && shapes.Length == 1) return $"<Viewbox Width=\"26\" Height=\"26\">{parts}</Viewbox>";
    return $"<Viewbox Width=\"26\" Height=\"26\"><Canvas Width=\"{canvas?.Attribute("Width")?.Value ?? "32"}\" Height=\"{canvas?.Attribute("Height")?.Value ?? "32"}\">{parts}</Canvas></Viewbox>";
}

// Read the existing WPF radial catalog at development time and compile its drawing resources.
var radialCatalog = File.ReadAllText(Path.Combine(repo, "Direct2dCad.wpf", "Controls", "CadRadialMenuActionIcon.cs"));
var radialTemplates = Regex.Matches(radialCatalog, @"CadRadialMenuAction\.(\w+) => ""(\w+)""").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
var radialFallbacks = new Dictionary<string,string>();
foreach (Match arm in Regex.Matches(radialCatalog, @"((?:CadRadialMenuAction\.\w+[\sor]*)+) => PackIconKind\.(\w+)"))
    foreach (Match action in Regex.Matches(arm.Groups[1].Value, @"CadRadialMenuAction\.(\w+)")) radialFallbacks[action.Groups[1].Value] = arm.Groups[2].Value;
var radialResources = new StringBuilder();
foreach (var action in Enum.GetNames<Direct2dCad.Client.Common.Settings.CadRadialMenuAction>().Where(name => name != "None"))
{
    var source = radialTemplates.TryGetValue(action, out var templateKey) ? new XElement("ContentControl",new XAttribute("ContentTemplate","{StaticResource " + templateKey + "}")) : new XElement("ContentControl",new XElement("PackIcon",new XAttribute("Kind",radialFallbacks.GetValueOrDefault(action,"Cancel"))));
    radialResources.Append($"<DataTemplate x:Key=\"{action}\">{Icon(source).Replace("Width=\"26\" Height=\"26\"", "Width=\"28\" Height=\"28\"").Replace("{DynamicResource MaterialBodyBrush}", "White").Replace("<PathIcon ", "<PathIcon Foreground=\"White\" ")}</DataTemplate>\n");
}
File.WriteAllText(Path.Combine(client,"Views","Generated","RadialIconResources.axaml"), "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"Direct2dCad.Avalonia.Views.Generated.RadialIconResources\"><UserControl.Resources>"+radialResources+"</UserControl.Resources></UserControl>");
File.WriteAllText(Path.Combine(client,"Views","Generated","RadialIconResources.axaml.cs"), "namespace Direct2dCad.Avalonia.Views.Generated; public partial class RadialIconResources : global::Avalonia.Controls.UserControl { public RadialIconResources() => InitializeComponent(); }");
var cursorResources = new StringBuilder();
foreach (var mode in Enum.GetNames<Direct2dCad.ViewModels.Enums.CadCanvasToolMode>().Append("Paste"))
{
    if (!cadIconTemplates.ContainsKey("Cad" + mode + "IconTemplate")) continue;
    var source = new XElement("ContentControl",new XAttribute("ContentTemplate","{StaticResource Cad"+mode+"IconTemplate}"));
    cursorResources.Append($"<DataTemplate x:Key=\"{mode}\">{Icon(source).Replace("{DynamicResource MaterialBodyBrush}","White").Replace("<PathIcon ","<PathIcon Foreground=\"White\" ")}</DataTemplate>\n");
}
File.WriteAllText(Path.Combine(client,"Views","Generated","CursorIconResources.axaml"),"<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"Direct2dCad.Avalonia.Views.Generated.CursorIconResources\"><UserControl.Resources>"+cursorResources+"</UserControl.Resources></UserControl>");
File.WriteAllText(Path.Combine(client,"Views","Generated","CursorIconResources.axaml.cs"),"namespace Direct2dCad.Avalonia.Views.Generated; public partial class CursorIconResources : global::Avalonia.Controls.UserControl { public CursorIconResources() => InitializeComponent(); }");
string Ns(Type type) { var key = $"clr-namespace:{type.Namespace};assembly={type.Assembly.GetName().Name}"; if (!namespaces.TryGetValue(key, out var alias)) namespaces[key] = alias = "t" + namespaces.Count; return alias; }
string E(string text) => SecurityElement.Escape(text) ?? "";
string TypeName(Type type) => (Nullable.GetUnderlyingType(type) ?? type).FullName!.Replace('+', '.');
string BindingPath(string? text)
{
    if (text is null || !text.StartsWith("{Binding")) return "";
    var match = Regex.Match(text, @"^\{Binding\s+(?:Path=)?([^,} ]+)");
    var path = match.Success ? match.Groups[1].Value : "";
    return path.StartsWith("ViewModel.") ? path[10..] : path;
}
PropertyInfo? Property(Type type, string path)
{
    PropertyInfo? result = null;
    foreach (var part in path.Split('.')) { result = type.GetProperty(part); if (result is null) return null; type = Nullable.GetUnderlyingType(result.PropertyType) ?? result.PropertyType; }
    return result;
}
Type? ItemType(Type type) => type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GenericTypeArguments[0];
string Label(XElement node, string fallback)
{
    var text = (string?)node.Attribute("AutomationProperties.Name") ?? (string?)node.Attribute("Header") ?? (string?)node.Attribute("ToolTip") ?? (string?)node.Attribute("Content");
    if (text is null) { var row = (string?)node.Attribute("Grid.Row") ?? "0"; text = node.Parent?.Name.LocalName == "Grid" ? node.Parent.Elements().FirstOrDefault(n => n.Name.LocalName == "TextBlock" && ((string?)n.Attribute("Grid.Row") ?? "0") == row)?.Attribute("Text")?.Value : node.ElementsBeforeSelf().LastOrDefault(n => n.Name.LocalName == "TextBlock")?.Attribute("Text")?.Value; }
    var key = Regex.Match(text ?? "", @"LangKeys\.([A-Za-z0-9_]+)");
    if (key.Success)
    {
        var labelKey = key.Groups[1].Value switch { "SourceName" => "Source", "IsVisible" => "Visible", "ZIndex" => "DrawOrder", var value => value };
        if (resourceKeys.Contains(labelKey)) return "{local:Loc " + labelKey + "}";
    }
    if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith('{')) return text;
    var resourceKey = fallback.Replace("Command", "");
    if (resourceKey == "SelectedColorSourceOption") resourceKey = "ColorSource";
    if (resourceKey == "SourceName") resourceKey = "Source";
    if (resourceKeys.Contains(resourceKey)) return "{local:Loc " + resourceKey + "}";
    return Regex.Replace(fallback, "([a-z])([A-Z])", "$1 $2").Replace("Command", "");
}
string ItemTemplate(Type type, bool full = false)
{
    if (type == typeof(string) || type.IsEnum) return "";
    var fields = full ? type.GetProperties().Where(p => p.GetMethod?.IsPublic == true && (p.PropertyType == typeof(string) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(double) || p.PropertyType == typeof(int)) && p.Name is not ("AutomationId" or "ContentId" or "Id" or "Index")).Take(10).ToArray() : new[] { type.GetProperty("Name") ?? type.GetProperty("Title") ?? type.GetProperty("Text") ?? type.GetProperty("DisplayName") }.Where(p => p != null).Cast<PropertyInfo>().ToArray();
    if (fields.Length == 0) return "";
    var alias = Ns(type); var sb = new StringBuilder($"<DataTemplate x:DataType=\"{alias}:{type.Name}\"><StackPanel Spacing=\"4\">");
    foreach (var p in fields)
    {
        if (p.PropertyType == typeof(bool)) sb.Append($"<CheckBox Content=\"{E(p.Name)}\" IsChecked=\"{{Binding {p.Name}, Mode={(p.SetMethod?.IsPublic == true ? "TwoWay" : "OneWay")}}}\" />");
        else if (full && p.SetMethod?.IsPublic == true && p.Name is not "Text") sb.Append($"<TextBox Text=\"{{Binding {p.Name}, Mode=TwoWay}}\" PlaceholderText=\"{E(p.Name)}\" />");
        else sb.Append($"<TextBlock Text=\"{{Binding {p.Name}}}\" TextWrapping=\"Wrap\" />");
    }
    sb.Append("</StackPanel></DataTemplate>"); return sb.ToString();
}
string StaticParameter(XElement node)
{
    var value = (string?)node.Attribute("CommandParameter");
    if (value is null) return "";
    var match = Regex.Match(value, @"\{x:Static\s+[^:]+:([\w]+)\.([\w]+)\}");
    if (match.Success)
    {
        var type = allTypes.FirstOrDefault(t => t.Name == match.Groups[1].Value);
        if (type is not null) return $" CommandParameter=\"{{x:Static {Ns(type)}:{type.Name}.{match.Groups[2].Value}}}\"";
        return "";
    }
    return value.StartsWith('{') ? "" : $" CommandParameter=\"{E(value)}\"";
}
string Controls(XElement source, Type model, bool ribbon = false)
{
    var seen = new HashSet<string>(); var content = new StringBuilder();
    IEnumerable<XElement> Nodes(XElement parent)
    {
        foreach (var node in parent.Name.LocalName == "Grid" ? parent.Elements().OrderBy(e => int.TryParse(e.Attribute("Grid.Row")?.Value, out var rowIndex) ? rowIndex : 0) : parent.Elements())
        {
            if (node.Name.LocalName.Contains("Resources") || node.Name.LocalName is "Style" or "ControlTemplate" or "DataTemplate" or "Setter" || node.Name.LocalName.EndsWith(".Style")) continue;
            if (node.Name.LocalName.EndsWith("PropertySection"))
            {
                var sectionFile = Path.Combine(repo, "Direct2dCad.wpf", "Views", "Toolboxes", "EntityProperty", node.Name.LocalName + ".xaml");
                if (File.Exists(sectionFile))
                {
                    var section = XDocument.Load(sectionFile).Root!;
                    foreach (var slot in section.Descendants().Where(e => e.Name.LocalName == "ContentPresenter").ToArray())
                    {
                        var name = BindingPath(slot.Attribute("Content")?.Value);
                        var supplied = node.Elements().FirstOrDefault(e => e.Name.LocalName == node.Name.LocalName + "." + name);
                        if (supplied is not null) slot.ReplaceWith(supplied.Elements().Select(e => new XElement(e)));
                        else slot.Remove();
                    }
                    node.ReplaceNodes(section.Elements().Select(n => new XElement(n)));
                }
            }
            yield return node;
            if (node.Name.LocalName is not ("Expander" or "GroupBox")) foreach (var child in Nodes(node)) yield return child;
        }
    }
    foreach (var node in Nodes(source))
    {
        var tag = node.Name.LocalName;
        if (!ribbon && tag == "GroupBox")
        {
            content.Append($"<StackPanel Spacing=\"4\" Margin=\"0,8,0,8\"><TextBlock Text=\"{E(Label(node, ""))}\" FontWeight=\"SemiBold\" Margin=\"0,0,0,6\" /><StackPanel Spacing=\"4\">{Controls(node, model)}</StackPanel></StackPanel>\n");
            continue;
        }
        if (!ribbon && tag == "Expander")
        {
            var expanded = node.Attribute("IsExpanded")?.Value == "True" ? "True" : "False";
            var expanderId = node.Attribute("AutomationProperties.AutomationId")?.Value;
            content.Append($"<Expander Header=\"{E(Label(node, ""))}\" IsExpanded=\"{expanded}\" HorizontalAlignment=\"Stretch\" HorizontalContentAlignment=\"Stretch\" Margin=\"0,2,0,4\"" + (expanderId is null ? "" : $" AutomationProperties.AutomationId=\"{E(expanderId)}\"") + $"><StackPanel Spacing=\"4\">{Controls(node, model)}</StackPanel></Expander>\n");
            continue;
        }
        if (!ribbon && tag == "TextBlock" && BindingPath(node.Attribute("Text")?.Value).Length == 0 &&
            (node.Attribute("Style")?.Value.Contains("PropertyTitle") == true || node.Attribute("FontWeight")?.Value == "SemiBold"))
        {
            var titleNode = new XElement(node); titleNode.SetAttributeValue("Content", node.Attribute("Text")?.Value);
            content.Append($"<TextBlock Text=\"{E(Label(titleNode, ""))}\" FontWeight=\"SemiBold\" Margin=\"0,4,0,8\" />\n");
            continue;
        }
        var command = BindingPath((string?)node.Attribute("Command"));
        if (command.Length > 0)
        {
            if (Property(model, command) is null) { audit.Add($"{model.Name}: unresolved command {command}"); continue; }
            var parameter = StaticParameter(node); if (!seen.Add(command + parameter)) continue;
            var label = Label(node, command.Split('.').Last());
            var icon = ribbon ? Icon(node) : "";
            content.Append(icon.Length > 0 ? $"<Button Command=\"{{Binding {command}}}\"{parameter} ToolTip.Tip=\"{E(label)}\" MinWidth=\"64\"><StackPanel Spacing=\"5\">{icon}<TextBlock Text=\"{E(label)}\" FontSize=\"11\" TextWrapping=\"Wrap\" TextAlignment=\"Center\" MaxWidth=\"72\" HorizontalAlignment=\"Center\" /></StackPanel></Button>\n" : $"<Button Command=\"{{Binding {command}}}\" Content=\"{E(label)}\"{parameter} ToolTip.Tip=\"{E(label)}\" />\n"); continue;
        }
        if (ribbon && tag is not ("ToggleButton" or "CheckBox" or "ComboBox" or "NumericUpDown" or "TextBox")) continue;
        string attr = tag switch { "TextBox" or "TextBlock" or "EditableTextBlock" => "Text", "NumericUpDown" or "Slider" => "Value", "CheckBox" or "ToggleButton" or "RadioButton" => "IsChecked", "ToggleSwitch" => "IsOn", "ColorPicker" => "SelectedColor", "ComboBox" => node.Attribute("SelectedItem") is null ? "SelectedValue" : "SelectedItem", "ListBox" or "ItemsControl" or "DataGrid" => "ItemsSource", _ => "" };
        if (attr.Length == 0) continue;
        var path = BindingPath((string?)node.Attribute(attr));
        if (model.Name == "InteractionUserSettingsViewModel" && path.StartsWith("RadialMenu.")) continue; // Dedicated typed radial editor handles nested slot actions.
        var property = Property(model, path);
        if (path.Length == 0 || property is null) { if (path.Length > 0 && path != "EntityIdText") audit.Add($"{model.Name}: unresolved {path}"); continue; }
        // The same value can have separate editable and ByLayer read-only presentations.
        if (!seen.Add(path + "|" + tag + "|" + node.Attribute("Visibility")?.Value)) continue;
        var labelText = Label(node, path.Split('.').Last()); var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var mode = property.SetMethod?.IsPublic == true && node.Attribute("IsReadOnly")?.Value != "True" && !((string?)node.Attribute(attr) ?? "").Contains("Mode=OneWay") ? "TwoWay" : "OneWay";
        var automation = node.Attributes().FirstOrDefault(a => a.Name.LocalName == "AutomationProperties.AutomationId")?.Value;
        var id = automation is null || automation.StartsWith('{') ? "" : $" AutomationProperties.AutomationId=\"{E(automation)}\"";
        string field;
        if (tag == "ComboBox" || type.IsEnum)
        {
            var items = BindingPath((string?)node.Attribute("ItemsSource")); var itemsProperty = Property(model, items);
            string itemsValue;
            if (type.IsEnum) { enumTypes.Add(type); itemsValue = $"{{x:Static local:EnumValues.{type.Name}}}"; itemsProperty = null; }
            else if (itemsProperty is not null) itemsValue = $"{{Binding {items}}}";
            else { audit.Add($"{model.Name}: no options for {path}"); continue; }
            var item = itemsProperty is not null ? ItemType(itemsProperty.PropertyType) : null; var template = type.IsEnum ? $"<DataTemplate x:DataType=\"{Ns(type)}:{type.Name}\"><controls:CadEnumText Value=\"{{Binding .}}\" /></DataTemplate>" : item is null ? "" : ItemTemplate(item);
            var valuePath = attr == "SelectedValue" && !type.IsEnum ? (string?)node.Attribute("SelectedValuePath") : null;
            var selectedBinding = valuePath is not null && item is not null ? $"SelectedValue=\"{{Binding {path}, Mode={mode}}}\" SelectedValueBinding=\"{{CompiledBinding {valuePath}, DataType={{x:Type {Ns(item)}:{item.Name}}}}}\"" : $"SelectedItem=\"{{Binding {path}, Mode={mode}}}\"";
            field = $"<ComboBox ItemsSource=\"{E(itemsValue)}\" {selectedBinding}{id}>" + (template.Length > 0 ? "<ComboBox.ItemTemplate>" + template + "</ComboBox.ItemTemplate>" : "") + "</ComboBox>";
        }
        else if (tag == "DataGrid" && ItemType(type) is { } rowType && rowType.GetProperty("X") is not null && rowType.GetProperty("Y") is not null)
        {
            var selected = BindingPath((string?)node.Attribute("SelectedItem")); var selection = Property(model, selected) is null ? "" : $" SelectedItem=\"{{Binding {selected}, Mode=TwoWay}}\"";
            var rowAlias = Ns(rowType);
            field = $"<DataGrid ItemsSource=\"{{Binding {path}}}\"{selection} AutoGenerateColumns=\"False\" MaxHeight=\"220\" CanUserReorderColumns=\"False\"><DataGrid.Columns>";
            foreach (var column in new[] { "Index", "X", "Y" })
                if (rowType.GetProperty(column) is { } member) field += $"<DataGridTextColumn Header=\"{(column == "Index" ? "#" : column)}\" Width=\"{(column == "Index" ? "36" : "*")}\" IsReadOnly=\"{(member.SetMethod?.IsPublic != true ? "True" : "False")}\" Binding=\"{{CompiledBinding {column}, Mode={(member.SetMethod?.IsPublic == true ? "TwoWay" : "OneWay")}, Converter={{x:Static local:CadGridNumberConverter.Instance}}, DataType={{x:Type {rowAlias}:{rowType.Name}}}}}\" />";
            field += "</DataGrid.Columns></DataGrid>";
        }
        else if (tag is "ListBox" or "ItemsControl" or "DataGrid")
        {
            var item = ItemType(type); if (item is null) continue;
            var selected = BindingPath((string?)node.Attribute("SelectedItem")); var selection = Property(model, selected) is null ? "" : $" SelectedItem=\"{{Binding {selected}, Mode=TwoWay}}\"";
            var template = ItemTemplate(item, true);
            field = $"<ListBox ItemsSource=\"{{Binding {path}}}\"{selection}{id} MaxHeight=\"260\">" + (template.Length > 0 ? "<ListBox.ItemTemplate>" + template + "</ListBox.ItemTemplate>" : "") + "</ListBox>";
        }
        else if (type == typeof(bool)) field = $"<CheckBox IsChecked=\"{{Binding {path}, Mode={mode}}}\" Content=\"{E(labelText)}\"{id} />";
        else if (type.FullName == "Direct2dCad.Db.Cad.CadColor") field = $"<ColorPicker Color=\"{{Binding {path}, Mode={mode}, Converter={{x:Static local:CadColorConverter.Instance}}}}\"{id} />";
        else if (tag == "TextBlock") field = $"<TextBlock Text=\"{{Binding {path}}}\" TextWrapping=\"Wrap\"{id} />";
        else if (type == typeof(string) || type.IsPrimitive || type == typeof(decimal))
        {
            var numberAttrs = new StringBuilder();
            foreach (var numberAttr in new[] { "Minimum", "Maximum", "Interval", "StringFormat" })
            {
                var value = node.Attribute(numberAttr)?.Value;
                if (numberAttr == "StringFormat" && value is null) value = Regex.Match(node.Attribute(attr)?.Value ?? "", @"StringFormat=([^,}]+)").Groups[1].Value;
                if (string.IsNullOrEmpty(value)) continue;
                var bound = BindingPath(value);
                if (bound.Length > 0) { if (Property(model, bound) is null) continue; value = "{Binding " + bound + "}"; }
                else if (value.StartsWith('{')) continue;
                numberAttrs.Append($" {(numberAttr == "StringFormat" ? "FormatString" : numberAttr)}=\"{E(value)}\"");
            }
            field = type == typeof(double) || type == typeof(float) || type == typeof(decimal) || type == typeof(int)
                ? $"<controls:CadPropertyNumberBox Value=\"{{Binding {path}, Mode={mode}}}\"{numberAttrs} ShowStepper=\"{(tag == "NumericUpDown" ? "True" : "False")}\" IsReadOnly=\"{(mode == "OneWay" ? "True" : "False")}\"{id} />"
                : $"<TextBox Text=\"{{Binding {path}, Mode={mode}}}\" IsReadOnly=\"{(mode == "OneWay" ? "True" : "False")}\"{id} />";
        }
        else { audit.Add($"{model.Name}: unsupported field {path} ({type.Name})"); continue; }
        var enabled = BindingPath((string?)node.Attribute("IsEnabled"));
        if (enabled.Length == 0) enabled = BindingPath(node.Descendants().FirstOrDefault(n => n.Name.LocalName == "Setter" && (string?)n.Attribute("Property") == "IsEnabled")?.Attribute("Value")?.Value);
        if (Property(model, enabled)?.PropertyType == typeof(bool)) field = field.Insert(field.IndexOf(' '), $" IsEnabled=\"{{Binding {enabled}}}\"");
        if (tag == "ColorPicker" && path == "StrokeColor" && Property(model, "SupportsColorSourceSelection") is not null)
        {
            // Mirror the WPF trigger: an explicit source is required when source selection is offered.
            field = $"<Border><Border.IsEnabled><MultiBinding Converter=\"{{x:Static local:CadColorEditorEnabledConverter.Instance}}\"><Binding Path=\"SupportsColorSourceSelection\" /><Binding Path=\"IsExplicitColorSource\" /><Binding Path=\"{enabled}\" /></MultiBinding></Border.IsEnabled>{field}</Border>";
        }
        var tip = BindingPath(node.Attribute("ToolTip")?.Value);
        if (Property(model, tip) is not null) field = field.Insert(field.IndexOf(' '), $" ToolTip.Tip=\"{{Binding {tip}}}\"");
        var visibilityConditions = new List<string>();
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var visibility = (string?)ancestor.Attribute("Visibility"); var condition = BindingPath(visibility);
            if (Property(model, condition)?.PropertyType != typeof(bool)) continue;
            var inverse = visibility!.Contains("DefaultToVisibilityConverter.CollapsedInstance");
            visibilityConditions.Add((inverse ? "!" : "") + condition);
        }
        var settingsForm = model.Namespace?.Contains(".Settings") == true;
        var labelWidth = settingsForm ? "190" : "84";
        if (settingsForm && type == typeof(bool)) field = field.Replace($" Content=\"{E(labelText)}\"", "");
        var row = ribbon ? $"<StackPanel Width=\"130\" Margin=\"5,0\"><TextBlock Text=\"{E(labelText)}\" FontSize=\"11\" />{field}</StackPanel>\n" : tag is "ListBox" or "ItemsControl" or "DataGrid" ? $"<StackPanel Spacing=\"3\" Margin=\"0,3\"><TextBlock Text=\"{E(labelText)}\" Opacity=\"0.7\" FontSize=\"12\" />{field}</StackPanel>\n" : type == typeof(bool) && !settingsForm ? field : $"<Grid ColumnDefinitions=\"{labelWidth},*\" Margin=\"0,3\"><TextBlock Text=\"{E(labelText)}\" Opacity=\"0.75\" FontSize=\"12\" VerticalAlignment=\"Center\" TextWrapping=\"Wrap\" /><Border Grid.Column=\"1\">{field}</Border></Grid>\n";
        if (tag == "TextBlock") row = field + "\n";
        foreach (var condition in visibilityConditions.Distinct()) row = $"<Border IsVisible=\"{{Binding {condition}}}\">{row}</Border>";
        content.Append(row);
    }
    return content.ToString();
}
string RibbonNode(XElement node)
{
    var tag = node.Name.LocalName;
    if (tag.Contains('.') || tag is "Style" or "ResourceDictionary") return "";
    if (tag is "Button" or "ToggleButton")
    {
        var command = BindingPath(node.Attribute("Command")?.Value);
        var menu = node.Elements().FirstOrDefault(e => e.Name.LocalName == "Button.ContextMenu")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "ContextMenu");
        if (command.Length == 0 && menu is null) return "";
        if (command.Length > 0 && Property(typeof(MainViewModel), command) is null) throw new InvalidOperationException("Unresolved ribbon command: " + command);
        var label = Label(node, command.Split('.').Last()); var icon = Icon(node);
        var size = node.Attribute("Width")?.Value == "80" || (node.Attribute("Style")?.Value.Contains("DropDown") ?? false) ? 80 : 34;
        if (size == 80) icon = icon.Replace("Width=\"26\" Height=\"26\"", "Width=\"50\" Height=\"50\"");
        icon = icon.Replace("MaterialBodyBrush", "CadRibbonAccentBrush");
        var attrs = command.Length == 0 ? "" : $" Command=\"{{Binding {command}}}\"{StaticParameter(node)}";
        var check = BindingPath(node.Attribute("IsChecked")?.Value);
        var parameter = node.Attribute("CommandParameter")?.Value;
        if (tag == "ToggleButton" && Property(typeof(MainViewModel), check) is not null && parameter is not null)
            attrs += $" IsChecked=\"{{Binding {check}, Mode=OneWay, Converter={{x:Static local:CadToolSelectionConverter.Instance}}, ConverterParameter={E(parameter)}}}\"";
        else tag = "Button";
        var id = node.Attribute("AutomationProperties.AutomationId")?.Value;
        if (id is not null) attrs += $" AutomationProperties.AutomationId=\"{E(id)}\"";
        var body = icon.Length > 0 ? icon : $"<TextBlock Text=\"{E(label)}\" TextWrapping=\"Wrap\" FontSize=\"10\" MaxWidth=\"72\" />";
        if (size == 80) body = $"<StackPanel Spacing=\"5\" HorizontalAlignment=\"Center\">{body}<StackPanel Orientation=\"Horizontal\" HorizontalAlignment=\"Center\" Spacing=\"2\"><TextBlock Text=\"{E(label)}\" FontSize=\"11\" TextWrapping=\"Wrap\" TextAlignment=\"Center\" MaxWidth=\"72\" HorizontalAlignment=\"Center\" />" + (menu is null ? "" : "<TextBlock Text=\"▾\" />") + "</StackPanel></StackPanel>";
        var flyout = menu is null ? "" : $"<Button.Flyout><MenuFlyout Placement=\"Bottom\">{string.Join("\n", menu.Elements().Select(item => MenuNode(item, typeof(MainViewModel))))}</MenuFlyout></Button.Flyout>";
        if (menu is not null) tag = "controls:CadRibbonDropDownButton";
        return $"<{tag}{attrs} Width=\"{size}\" Height=\"{size}\" Padding=\"3\" Margin=\"2\" ToolTip.Tip=\"{E(label)}\" AutomationProperties.Name=\"{E(label)}\">{flyout}{body}</{tag}>";
    }
    if (tag == "Border" && !node.HasElements) return "<Border Width=\"1\" Margin=\"10,2\" Background=\"{DynamicResource MaterialBodyLightBrush}\" Opacity=\"0.3\" />";
    var children = string.Join("\n", node.Elements().Select(RibbonNode));
    if (children.Length == 0) return "";
    if (tag is "StackPanel" or "WrapPanel")
    {
        var orientation = node.Attribute("Orientation")?.Value ?? (tag == "WrapPanel" ? "Horizontal" : "Vertical");
        var width = node.Attribute("Width")?.Value;
        return $"<{tag} Orientation=\"{orientation}\"" + (width is null ? "" : $" Width=\"{E(width)}\"") + $" Margin=\"2\">{children}</{tag}>";
    }
    return children;
}
var viewFiles = Directory.EnumerateFiles(Path.Combine(repo, "Direct2dCad.wpf", "Views"), "*.xaml", SearchOption.AllDirectories).ToList();
var oleFile = Path.Combine(client, "OleObjectPropertyView.xml");
var oleRoot = new XElement("UserControl", new XAttribute("DataContext", "{d:DesignInstance Type=vm:OleObjectPropertyViewModel}"),
    new XElement("EntityHeaderPropertySection"));
foreach (var path in new[] { "SourceName", "ContentType", "Width", "Height", "CenterX", "CenterY", "Left", "Bottom", "Right", "Top", "ZIndex", "IsVisible", "Opacity" })
    oleRoot.Add(new XElement("TextBox", new XAttribute("Text", "{Binding " + path + "}"), new XAttribute("AutomationProperties.Name", "{I18N {x:Static r:LangKeys." + path + "}}")));
new XDocument(oleRoot).Save(oleFile); viewFiles.Add(oleFile);
var layoutFile = Path.Combine(client, "LayoutOptions.source.xml");
var layoutModel = typeof(Direct2dCad.ViewModels.Layouts.LayoutWorkspaceViewModel);
var layoutRoot = new XElement("UserControl", new XAttribute("DataContext", "{d:DesignInstance Type=layout:LayoutWorkspaceViewModel}"));
foreach (var property in layoutModel.GetProperties())
{
    var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
    var tag = typeof(System.Windows.Input.ICommand).IsAssignableFrom(type) ? "Button" : type == typeof(bool) ? "CheckBox" : type == typeof(string) || type.IsPrimitive ? "TextBox" : type.FullName == "Direct2dCad.Db.Cad.CadColor" ? "ColorPicker" : "";
    if (tag.Length == 0 || property.SetMethod?.IsPublic != true && tag != "Button") continue;
    var attr = tag switch { "Button" => "Command", "CheckBox" => "IsChecked", "ColorPicker" => "SelectedColor", _ => "Text" };
    layoutRoot.Add(new XElement(tag, new XAttribute(attr, "{Binding " + property.Name + "}")));
}
layoutRoot.Add(new XElement("ComboBox", new XAttribute("ItemsSource", "{Binding CurrentViewportOptions}"), new XAttribute("SelectedItem", "{Binding CurrentViewport}")));
layoutRoot.Add(new XElement("ComboBox", new XAttribute("ItemsSource", "{Binding Viewports}"), new XAttribute("SelectedItem", "{Binding SelectedViewport}")));
layoutRoot.Add(new XElement("TextBlock", new XAttribute("Text", "{Binding ValidationError}")));
new XDocument(layoutRoot).Save(layoutFile); viewFiles.Add(layoutFile);
foreach (var file in viewFiles)
{
    var root = XDocument.Load(file).Root!;
    var design = root.Attributes().FirstOrDefault(a => a.Name.LocalName == "DataContext")?.Value;
    var name = file == layoutFile ? "LayoutOptionsView" : Path.GetFileNameWithoutExtension(file);
    var match = Regex.Match(design ?? "", @"Type=(\w+):([\w]+)");
    var inferred = name is "CadEditParametersView" or "CadAnnotationParametersView" ? "CadDocumentViewModel" : name == "LayoutOptionsView" ? "LayoutWorkspaceViewModel" : name + "Model";
    var model = types.FirstOrDefault(t => t.Name == (match.Success ? match.Groups[2].Value : inferred)); if (model is null || model.IsAbstract) continue;
    if (name == "DrawingRecoveryToolboxView") continue;
    if (name is "CadDocumentView" or "EditorTabView" or "UserSettingsView" or "EntityPropertiesToolboxView") continue;
    var ribbon = name == "MainRibbonView";
    var editable = model.GetProperty("IsEditable") is not null ? " IsEnabled=\"{Binding IsEditable}\"" : name == "CadAnnotationParametersView" ? " IsEnabled=\"{Binding CanEditDimensionParameters}\"" : "";
    var content = ribbon ? "<TabControl Height=\"150\" TabStripPlacement=\"Bottom\" Classes=\"ribbon\" SelectedIndex=\"{Binding TabControlSelectedIndex, Mode=TwoWay}\">" + string.Join("\n", root.Descendants().Where(e => e.Name.LocalName == "TabItem").Select(tab => $"<TabItem Header=\"{E(Label(tab, "Tools"))}\"><ScrollViewer HorizontalScrollBarVisibility=\"Auto\" VerticalScrollBarVisibility=\"Disabled\" AllowAutoHide=\"False\">{RibbonNode(tab)}</ScrollViewer></TabItem>")) + "</TabControl>" : $"<ScrollViewer><StackPanel Spacing=\"4\" Margin=\"12,8\"{editable}>{Controls(root, model)}</StackPanel></ScrollViewer>";
    var propertyForm = name.EndsWith("PropertyView", StringComparison.Ordinal);
    if (propertyForm) content = content.Replace("Spacing=\"4\"", "Spacing=\"2\"").Replace("Margin=\"0,3\"", "Margin=\"0,1\"").Replace("Margin=\"0,4,0,8\"", "Margin=\"0,0,0,6\"");
    var propertyClass = propertyForm ? " Classes=\"cadProperties\"" : "";
    Directory.CreateDirectory(Path.Combine(client, "Views", "Generated"));
    var declarations = string.Join(" ", namespaces.Select(p => $"xmlns:{p.Value}=\"{p.Key}\""));
    File.WriteAllText(Path.Combine(client, "Views", "Generated", name + ".axaml"), $"<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"clr-namespace:{model.Namespace};assembly=Direct2dCad.ViewModels\" xmlns:local=\"clr-namespace:Direct2dCad.Avalonia\" xmlns:controls=\"clr-namespace:Direct2dCad.Avalonia.Controls\" {declarations}{propertyClass} x:Class=\"Direct2dCad.Avalonia.Views.Generated.{name}\" x:DataType=\"vm:{model.Name}\" x:CompileBindings=\"True\">\n{content}\n</UserControl>");
    File.WriteAllText(Path.Combine(client, "Views", "Generated", name + ".axaml.cs"), $"// Generated by Direct2dCad.Avalonia.Generate from {Path.GetRelativePath(repo, file).Replace('\\', '/')}.\nnamespace Direct2dCad.Avalonia.Views.Generated;\npublic partial class {name} : global::Avalonia.Controls.UserControl {{ public {name}() {{ InitializeComponent(); }} }}\n");
    generated.TryAdd(model, name);
}
var editorMenuSource = XDocument.Load(Path.Combine(repo, "Direct2dCad.wpf", "Views", "EditorTabView.xaml")).Descendants().Single(element => element.Name.LocalName == "ContextMenu");
string MenuNode(XElement node, Type model)
{
    if (node.Name.LocalName == "Separator") return "<Separator />";
    if (node.Name.LocalName != "MenuItem") return "";
    var attrs = new StringBuilder($" Header=\"{E(Label(node, ""))}\"");
    var gesture = BindingPath(node.Attribute("Command")?.Value) switch
    {
        "NewCommand" => "Ctrl+N", "OpenFileCommand" => "Ctrl+O", "CurrentEditorTabViewModel.SaveFileCommand" => "Ctrl+S",
        "CurrentEditorTabViewModel.SaveAsFileCommand" => "Ctrl+Shift+S", "CurrentEditorTabViewModel.PrintCommand" => "Ctrl+P", _ => null
    };
    if (gesture is not null) attrs.Append($" InputGesture=\"{gesture}\"");
    foreach (var name in new[] { "Command", "CommandParameter", "IsEnabled" })
    {
        if (node.Attribute(name) is not { } attr) continue;
        if (attr.Value.StartsWith("{Binding"))
        {
            var path = BindingPath(attr.Value);
            if (Property(model, path) is null) throw new InvalidOperationException($"Unresolved menu binding: {path}");
            attrs.Append($" {name}=\"{{Binding {path}}}\"");
        }
        else attrs.Append($" {name}=\"{E(attr.Value)}\"");
    }
    var hint = BindingPath(node.Attribute("ToolTip")?.Value);
    if (Property(model, hint) is not null) attrs.Append($" ToolTip.Tip=\"{{Binding {hint}}}\"");
    if (node.Attributes().FirstOrDefault(attr => attr.Name.LocalName == "AutomationProperties.AutomationId") is { } id) attrs.Append($" AutomationProperties.AutomationId=\"{E(id.Value)}\"");
    var iconSource = node.Elements().FirstOrDefault(element => element.Name.LocalName == "MenuItem.Icon");
    var icon = iconSource is null ? "" : Icon(iconSource);
    return $"<MenuItem{attrs}>" + (icon.Length == 0 ? "" : "<MenuItem.Icon>" + icon + "</MenuItem.Icon>") + string.Join("\n", node.Elements().Select(item => MenuNode(item, model))) + "</MenuItem>";
}
File.WriteAllText(Path.Combine(client, "Views", "Generated", "EditorContextMenu.axaml"), "<ContextMenu xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"clr-namespace:Direct2dCad.ViewModels;assembly=Direct2dCad.ViewModels\" xmlns:local=\"clr-namespace:Direct2dCad.Avalonia\" Classes=\"cadCanvasMenu\" x:Class=\"Direct2dCad.Avalonia.Views.Generated.EditorContextMenu\" x:DataType=\"vm:EditorTabViewModel\" x:CompileBindings=\"True\">\n" + string.Join("\n", editorMenuSource.Elements().Select(item => MenuNode(item, typeof(EditorTabViewModel)))) + "\n</ContextMenu>");
var sourceMenuCommandCount = editorMenuSource.Descendants().Count(element => element.Name.LocalName == "MenuItem" && element.Attribute("Command") is not null);
File.WriteAllText(Path.Combine(client, "Views", "Generated", "EditorContextMenu.axaml.cs"), $"// Generated from the WPF editor menu. All bindings are compiled.\nnamespace Direct2dCad.Avalonia.Views.Generated;\npublic partial class EditorContextMenu : global::Avalonia.Controls.ContextMenu {{ protected override global::System.Type StyleKeyOverride => typeof(global::Avalonia.Controls.ContextMenu); public const int SourceCommandCount = {sourceMenuCommandCount}; public EditorContextMenu() {{ InitializeComponent(); }} }}\n");
var mapping = string.Join("\n", generated.Select(p => $"        {TypeName(p.Key)} vm => new Views.Generated.{p.Value} {{ DataContext = vm }},"));
File.WriteAllText(Path.Combine(client, "GeneratedViews.cs"), "// Generated at development time. Static dispatch is NativeAOT safe.\nusing Avalonia.Controls;\nnamespace Direct2dCad.Avalonia;\ninternal static class GeneratedViews { public static Control? Create(object? model) => model switch {\n" + mapping + "\n        _ => null\n    }; }\n");

var localizedEnums = enumTypes.Concat(new[] { typeof(Direct2dCad.ViewModels.Enums.ViewModelCadUnit), typeof(Direct2dCad.Client.Common.Settings.CadRadialMenuAction) }).Distinct();
var enumLabels = localizedEnums.SelectMany(type => type.GetFields(BindingFlags.Public|BindingFlags.Static).Select(field => $"{TypeName(type)}.{field.Name} => \"{field.CustomAttributes.FirstOrDefault(attribute => attribute.AttributeType.Name == "LocalizedDescriptionAttribute")?.ConstructorArguments.FirstOrDefault().Value ?? field.Name}\","));
File.WriteAllText(Path.Combine(client,"CadEnumLabelData.cs"), "namespace Direct2dCad.Avalonia; internal static class CadEnumLabelData { public static string Key(object? value) => value switch {\n"+string.Join("\n",enumLabels)+"\n_ => value?.ToString() ?? \"\" }; }");
File.WriteAllText(Path.Combine(client, "EnumValues.cs"), "namespace Direct2dCad.Avalonia;\npublic static class EnumValues {\n" + string.Join("\n", enumTypes.Select(t => $"public static {TypeName(t)}[] {t.Name} {{ get; }} = System.Enum.GetValues<{TypeName(t)}>();")) + "\n}\n");
File.WriteAllText(Path.Combine(client, "view-generation-audit.txt"), string.Join("\n", audit.Distinct()));
Console.WriteLine($"Generated {generated.Count} typed views. {audit.Distinct().Count()} items require review.");






