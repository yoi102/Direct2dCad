using System.Collections.Concurrent;
using System.Reflection;
using Direct2dCad.Db.Cad;
namespace Direct2dCad.Commands;

/// <summary>Conservative retained managed payload estimate, not process RSS. No native resource inspection.</summary>
public static class CadCommandPayloadEstimate
{
    private const int ReferenceSize = 8;
    private const int ObjectHeaderSize = 32;
    private const int ArrayHeaderSize = 24;

    private static readonly ConcurrentDictionary<Type, TypeLayout> Layouts = new();
    private static readonly ConcurrentDictionary<Type, int> UnmanagedSizes = new();

    public static long Estimate(ICadCommand command) => Estimate((object)command);

    /// <summary>Also supports editor commands without a dependency on the editor assembly.</summary>
    public static long Estimate(object command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(command);
        long bytes = 0;
        while (pending.TryPop(out var value))
        {
            var type = value.GetType();
            if (!type.IsValueType && !seen.Add(value)) continue;
            if (seen.Count > 100_000) return Math.Max(bytes, 256L * 1024 * 1024);
            if (value is string text) { bytes += 24 + text.Length * 2L; continue; }
            if (value is CadDocument || value is Delegate) { bytes += 64; continue; }
            if (value is Array array)
            {
                bytes += ArrayHeaderSize;
                var element = type.GetElementType()!;
                // Arrays of reference-free elements (primitives, enums, CadPointD, EntityId, ...) are sized in
                // one step; walking them element by element boxed every item and dominated command execution.
                var elementSize = GetUnmanagedSize(element);
                if (elementSize > 0) { bytes += array.LongLength * elementSize; continue; }
                bytes += array.LongLength * (element.IsValueType ? 0 : ReferenceSize);
                foreach (var item in array)
                    if (item is not null) pending.Push(item);
                continue;
            }

            var unmanagedSize = GetUnmanagedSize(type);
            if (unmanagedSize > 0) { bytes += unmanagedSize; continue; }

            bytes += ObjectHeaderSize;
            if (type.Assembly != typeof(ICadCommand).Assembly && !type.Assembly.GetName().Name!.StartsWith("Direct2dCad") &&
                type.Namespace?.StartsWith("System.Collections") == false)
                continue;
            var layout = Layouts.GetOrAdd(type, CreateLayout);
            bytes += layout.InlineBytes;
            foreach (var field in layout.TraversedFields)
                if (field.GetValue(value) is { } item) pending.Push(item);
        }
        return Math.Max(256, bytes);
    }

    /// <summary>Bytes of a type that holds no managed references, or 0 when it is (or contains) a reference.</summary>
    /// <remarks>Reflection over fields only, so it stays NativeAOT-safe (no MakeGenericMethod). Padding is ignored.</remarks>
    private static int GetUnmanagedSize(Type type) => UnmanagedSizes.GetOrAdd(type, ComputeUnmanagedSize);

    private static int ComputeUnmanagedSize(Type type)
    {
        if (type.IsEnum) return GetUnmanagedSize(Enum.GetUnderlyingType(type));
        if (type.IsPointer || type == typeof(nint) || type == typeof(nuint)) return IntPtr.Size;
        if (type.IsPrimitive)
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte => 1,
                TypeCode.Char or TypeCode.Int16 or TypeCode.UInt16 => 2,
                TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Single => 4,
                _ => 8,
            };
        if (!type.IsValueType) return 0;
        var size = 0;
        foreach (var field in GetInstanceFields(type))
        {
            var fieldSize = GetUnmanagedSize(field.FieldType);
            if (fieldSize == 0) return 0;
            size += fieldSize;
        }
        return Math.Max(1, size);
    }

    private static TypeLayout CreateLayout(Type type)
    {
        long inline = 0;
        var traversed = new List<FieldInfo>();
        foreach (var field in GetInstanceFields(type))
        {
            var size = GetUnmanagedSize(field.FieldType);
            if (size > 0) inline += size;
            else
            {
                if (!field.FieldType.IsValueType) inline += ReferenceSize;
                traversed.Add(field);
            }
        }
        return new TypeLayout(inline, traversed.ToArray());
    }

    private static FieldInfo[] GetInstanceFields(Type type)
    {
        // Selection commands retain their old selection in a private base field.
        // GetFields on the derived type alone does not include that payload.
        var fields = new List<FieldInfo>();
        for (var current = type; current is not null; current = current.BaseType)
            fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        return fields.ToArray();
    }

    private sealed record TypeLayout(long InlineBytes, FieldInfo[] TraversedFields);
}
