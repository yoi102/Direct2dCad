using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Direct2dCad.Db.Cad;
namespace Direct2dCad.Commands;

/// <summary>Conservative retained managed payload estimate, not process RSS. No native resource inspection.</summary>
public static class CadCommandPayloadEstimate
{
    private static readonly ConcurrentDictionary<Type,FieldInfo[]> Fields=new();
    public static long Estimate(ICadCommand command)
    {
        var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);var pending=new Stack<object>();pending.Push(command);long bytes=0;
        while(pending.TryPop(out var value))
        {
            var type=value.GetType();if(!type.IsValueType && !seen.Add(value))continue;
            if(seen.Count>100_000)return Math.Max(bytes,256L*1024*1024);
            if(value is string text){bytes+=24+text.Length*2L;continue;}
            if(value is CadDocument || value is Delegate){bytes+=64;continue;}
            if(type.IsPrimitive || type.IsEnum){bytes+=16;continue;}
            if(value is Array array)
            {
                var element=type.GetElementType()!;bytes+=24;
                if(element.IsPrimitive){bytes+=Buffer.ByteLength(array);continue;}
                if(element.IsEnum){bytes+=array.LongLength*8;continue;}
                bytes+=array.LongLength*8;foreach(var item in array)if(item is not null)pending.Push(item);continue;
            }
            bytes+=32;
            if(type.Assembly!=typeof(ICadCommand).Assembly && !type.Assembly.GetName().Name!.StartsWith("Direct2dCad") &&
                type.Namespace?.StartsWith("System.Collections")==false)continue;
            foreach(var field in Fields.GetOrAdd(type,static t=>t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)))
                if(field.GetValue(value) is { } item)pending.Push(item);
        }
        return Math.Max(256,bytes);
    }
}
