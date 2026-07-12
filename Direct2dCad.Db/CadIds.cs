using StronglyTypedIds;

namespace Direct2dCad.Db;

[StronglyTypedId(Template.Long)]
public partial struct DocumentId
{
}

[StronglyTypedId(Template.Long)]
public partial struct EntityId
{
}

[StronglyTypedId(Template.Long)]
public partial struct LayerId
{
    public static readonly LayerId Default = new(1);
}

[StronglyTypedId(Template.Long)]
public partial struct BlockId
{
    public static readonly BlockId ModelSpace = new(1);
    public static readonly BlockId PaperSpace = new(2);
}

[StronglyTypedId(Template.Long)]
public partial struct LayoutId
{
    public static readonly LayoutId Default = new(1);
}

[StronglyTypedId(Template.Long)]
public partial struct LayoutViewportId
{
}

[StronglyTypedId(Template.Long)]
public partial struct StyleId
{
    public static readonly StyleId DefaultGraphic = new(1);
}

[StronglyTypedId(Template.Long)]
public partial struct LineTypeId
{
    public static readonly LineTypeId Continuous = new(1);
}

[StronglyTypedId(Template.Long)]
public partial struct HatchPatternId
{

}
