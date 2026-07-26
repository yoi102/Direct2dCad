using Direct2dCad.AI;

namespace Direct2dCad.ViewModels.AI;

internal static class CadAiToolSelector
{
    private static readonly string[] CoreTools =
    [
        "get_document_summary", "list_entities", "list_document_catalog",
        "list_documents", "create_document", "select_entities", "undo", "redo"
    ];

    private static readonly string[] EditingTools =
    [
        "get_entity_geometry", "set_entity_geometry", "transform_entities",
        "duplicate_entities", "move_entities", "delete_entities", "change_entity_layer",
        "set_entity_common_properties", "set_entity_fill", "set_entity_stroke_style"
    ];

    private static readonly string[] LayerTools =
    [
        "create_layer", "change_entity_layer", "set_entity_common_properties"
    ];

    private static readonly string[] BlockTools =
    [
        "create_block", "insert_block", "duplicate_entities",
        "get_entity_geometry", "transform_entities"
    ];

    private static readonly string[] DocumentTools =
    [
        "open_document", "activate_document", "rename_document",
        "save_document", "close_document"
    ];

    private static readonly (string Tool, string[] Terms)[] EntityCreationTools =
    [
        ("add_line", ["line", "segment", "直线", "线段"]),
        ("add_circle", ["circle", "圆形", "圆"]),
        ("add_arc", ["arc", "圆弧", "弧"]),
        ("add_ellipse", ["ellipse", "椭圆"]),
        ("add_rectangle", ["rectangle", "rect", "矩形"]),
        ("add_polygon", ["polygon", "多边形"]),
        ("add_polyline", ["polyline", "多段线"]),
        ("add_spline", ["spline", "样条"]),
        ("add_text", ["text", "label", "文字", "文本"])
    ];

    private static readonly string[] DrawingTerms =
    [
        "draw", "sketch", "drawing", "pattern", "outline",
        "绘制", "画", "图案", "轮廓", "猫"
    ];

    private static readonly string[] EditingTerms =
    [
        "edit", "change", "set", "move", "rotate", "scale", "mirror", "delete",
        "copy", "duplicate", "color", "fill", "stroke", "style", "property",
        "修改", "设置", "移动", "旋转", "缩放", "镜像", "删除", "复制",
        "颜色", "填充", "线型", "属性"
    ];

    internal static IReadOnlyList<AiToolDefinition> Select(
        string prompt,
        IReadOnlyList<AiToolDefinition> availableTools,
        bool aggressive = false)
    {
        if (availableTools.Count == 0)
            return [];

        var normalized = prompt?.Trim().ToLowerInvariant() ?? string.Empty;
        var requested = new List<string>();
        var requestedSet = new HashSet<string>(StringComparer.Ordinal);
        void Add(IEnumerable<string> names)
        {
            foreach (var name in names)
                if (requestedSet.Add(name))
                    requested.Add(name);
        }

        var isDrawing = ContainsAny(normalized, DrawingTerms) ||
                        EntityCreationTools.Any(item => ContainsAny(normalized, item.Terms));
        if (isDrawing)
        {
            var specificTools = EntityCreationTools
                .Where(item => ContainsAny(normalized, item.Terms))
                .Select(item => item.Tool)
                .ToList();
            if (specificTools.Contains("add_arc", StringComparer.Ordinal))
                specificTools.Remove("add_circle");
            if (specificTools.Contains("add_polyline", StringComparer.Ordinal) ||
                specificTools.Contains("add_spline", StringComparer.Ordinal))
            {
                specificTools.Remove("add_line");
            }
            var needsBulk = specificTools.Count == 0 || ContainsAny(normalized,
                "multiple", "many", "batch", "pattern", "outline", "drawing",
                "多个", "批量", "图案", "轮廓", "猫");
            if (needsBulk)
                Add(["add_entities"]);
            else
                Add(specificTools);

            if (ContainsAny(normalized, "composite", "mixed path", "closed contour", "复合路径", "混合路径", "闭合轮廓"))
                Add(["add_composite_path"]);
            Add(["create_layer"]);
        }

        if (ContainsAny(normalized, EditingTerms))
            Add(EditingTools);
        if (ContainsAny(normalized, "layer", "图层", "层级"))
            Add(LayerTools);
        if (ContainsAny(normalized, "block", "块", "块定义", "块引用"))
            Add(BlockTools);
        if (ContainsAny(normalized,
                "document", "file", "open", "save", "close", "rename",
                "文档", "文件", "打开", "保存", "关闭", "重命名", "新建"))
        {
            Add(DocumentTools);
        }

        if (!isDrawing && !ContainsAny(normalized, EditingTerms) && !aggressive)
            Add(["get_entity_geometry", "set_entity_common_properties"]);

        // Intent-specific tools are deliberately first so budget fitting never
        // discards the operation the user actually requested in favor of helpers.
        Add(CoreTools);

        var definitions = availableTools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        return requested
            .Where(definitions.ContainsKey)
            .Select(name => definitions[name])
            .ToArray();
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
