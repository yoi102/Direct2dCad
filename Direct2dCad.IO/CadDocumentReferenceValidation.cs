using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.IO.FileFormat.Sections;

namespace Direct2dCad.IO;

internal static class CadDocumentReferenceValidation
{
    internal static void ValidateExternalContent(CadBlockReferencesSection references,CadImagesSection images,CadDocumentLoadLimits limits,CancellationToken token)
    {
        var graph=references.BlockReferences.Where(r=>!r.Entity.IsErased).GroupBy(r=>r.Entity.OwnerBlockId).ToDictionary(g=>g.Key,g=>g.Select(r=>r.DefinitionBlockId).Distinct().ToArray());
        var depths=new Dictionary<long,int>();var visiting=new HashSet<long>();
        int Visit(long id,int depth)
        {
            token.ThrowIfCancellationRequested();
            if(depth>limits.MaximumBlockDepth) throw new InvalidDataException("The drawing exceeds the block nesting budget.");
            if(!visiting.Add(id)) throw new InvalidDataException("The drawing contains a circular block reference.");
            if(depths.TryGetValue(id,out var saved))
            {
                visiting.Remove(id);
                if(depth+saved>limits.MaximumBlockDepth) throw new InvalidDataException("The drawing exceeds the block nesting budget.");
                return saved;
            }
            var maximum=0;
            if(graph.TryGetValue(id,out var children)) foreach(var child in children) maximum=Math.Max(maximum,1+Visit(child,depth+1));
            visiting.Remove(id);depths[id]=maximum;
            if(maximum>limits.MaximumBlockDepth) throw new InvalidDataException("The drawing exceeds the block nesting budget.");
            return maximum;
        }
        foreach(var id in graph.Keys) Visit(id,0);
        foreach(var image in images.Images)
        {
            token.ThrowIfCancellationRequested();
            if(image.PixelWidth<=0 || image.PixelHeight<=0 || (long)image.PixelWidth*image.PixelHeight>limits.MaximumImagePixels || image.Stride<(long)image.PixelWidth*4 ||
                (long)image.Stride*image.PixelHeight>image.Pixels.Length) throw new InvalidDataException("Embedded image dimensions or pixel content exceed the budget or are incomplete.");
        }
    }
    internal static void ValidateKnownReferences(CadDocument document,CancellationToken token)
    {
        void Require(Direct2dCad.Db.StyleId? id)
        { if(id is { } value && !document.TryGetStyle(value,out _)) throw new InvalidDataException("The drawing contains an unresolved style reference."); }
        foreach(var layer in document.Layers.Values) {token.ThrowIfCancellationRequested();Require(layer.DefaultGraphicStyleId);}
        foreach(var entity in document.Entities.Values)
        {
            token.ThrowIfCancellationRequested();
            switch(entity)
            {
                case CadLine e:Require(e.GraphicStyleId);break;
                case CadCircle e:Require(e.GraphicStyleId);Require(e.FillStyleId);break;
                case CadEllipse e:Require(e.GraphicStyleId);Require(e.FillStyleId);break;
                case CadEllipseArc e:Require(e.GraphicStyleId);break;
                case CadArc e:Require(e.GraphicStyleId);break;
                case CadRectangle e:Require(e.GraphicStyleId);Require(e.FillStyleId);break;
                case CadPolyline e:Require(e.GraphicStyleId);Require(e.FillStyleId);break;
                case CadSpline e:Require(e.GraphicStyleId);Require(e.FillStyleId);break;
                case CadRegion e:Require(e.GraphicStyleId);Require(e.FillStyleId);break;
                case CadCompositePath e:Require(e.GraphicStyleId);Require(e.FillStyleId);break;
                case CadText e:Require(e.GraphicStyleId);Require(e.TextStyleId);break;
                case CadShapeText e:Require(e.GraphicStyleId);break;
                case CadBlockReference e:Require(e.GraphicStyleId);break;
            }
        }
    }
}
