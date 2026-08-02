using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DesktopBuddy.Domain.Characters;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Characters;

public sealed class CompiledCharacterAppearanceArchitectureTests
{
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Godot",
        "DesktopBuddy.Domain.Physics",
        "DesktopBuddy.Domain.Persistence",
        "DesktopBuddy.Domain.Platform",
        "System.IO",
        "System.Resources",
    ];

    [Fact]
    public void CompiledAppearance_PublicBoundaryIsEngineFreeAppearanceOnly()
    {
        var visited = new HashSet<Type>();
        var failures = new List<string>();

        Inspect(typeof(CompiledCharacterAppearance), "CompiledCharacterAppearance", visited, failures);

        Assert.Empty(failures);
    }

    private static void Inspect(
        Type type,
        string path,
        ISet<Type> visited,
        ICollection<string> failures)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsAllowedLeaf(type) || !visited.Add(type))
            return;

        string typeName = type.FullName ?? type.Name;
        if (ForbiddenNamespacePrefixes.Any(prefix =>
            typeName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            failures.Add($"{path} references forbidden type {typeName}.");
            return;
        }

        if (type != typeof(string) &&
            (typeof(IEnumerable).IsAssignableFrom(type) ||
             typeof(ICollection<>).IsAssignableFromGeneric(type)))
        {
            failures.Add($"{path} references mutable/collection type {typeName}.");
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Inspect(property.PropertyType, $"{path}.{property.Name}", visited, failures);
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            Inspect(field.FieldType, $"{path}.{field.Name}", visited, failures);
    }

    private static bool IsAllowedLeaf(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(Guid) ||
        type == typeof(decimal);
}

internal static class ReflectionTypeExtensions
{
    public static bool IsAssignableFromGeneric(this Type openGeneric, Type candidate)
    {
        if (!openGeneric.IsGenericTypeDefinition)
            return openGeneric.IsAssignableFrom(candidate);

        if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openGeneric)
            return true;

        return candidate.GetInterfaces().Any(interfaceType =>
            interfaceType.IsGenericType &&
            interfaceType.GetGenericTypeDefinition() == openGeneric);
    }
}
