// <copyright file="ConfigurationImporter.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Json;

using System.Collections;
using System.Reflection;
using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Imports a (e.g. deserialized) <see cref="GameConfiguration"/> object graph into a
/// persistence <see cref="IContext"/> by recreating it with the context' own entity types.
/// </summary>
/// <remarks>
/// A downloaded configuration is deserialized into the loosely typed
/// <see cref="MUnique.OpenMU.Persistence.BasicModel"/> graph. Those objects are not known
/// to the persistence provider (e.g. entity framework core) and therefore cannot be saved
/// directly. This importer walks the source graph and builds an equivalent graph out of the
/// objects created by <see cref="IContext.CreateNew(Type, object?[])"/>, which the provider
/// is able to track and persist.
/// <para>
/// The original identifiers are preserved, so the imported configuration is an exact copy of
/// the source (a restore), not a new configuration with fresh identifiers.
/// </para>
/// </remarks>
public class ConfigurationImporter
{
    private readonly IContext _context;

    /// <summary>
    /// Maps a source object to the already created target object.
    /// Uses reference equality, so shared references and circular references resolve to the
    /// same target instance (and recursion terminates).
    /// </summary>
    private readonly Dictionary<object, object> _sourceToTarget = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationImporter"/> class.
    /// </summary>
    /// <param name="context">The target context. It must currently be in use on the calling thread.</param>
    public ConfigurationImporter(IContext context)
    {
        this._context = context;
    }

    /// <summary>
    /// Imports the given source configuration into the context by recreating it with the
    /// context' own entity types. The returned object (and its whole object graph) is created
    /// through the context and ready to be saved by <see cref="IContext.SaveChangesAsync"/>.
    /// </summary>
    /// <param name="source">The source configuration, e.g. deserialized from a downloaded json.</param>
    /// <returns>The imported configuration, created through the context.</returns>
    public GameConfiguration Import(GameConfiguration source)
    {
        return (GameConfiguration)this.Convert(source)!;
    }

    /// <summary>
    /// Imports the content of the given source configuration into an already existing target
    /// configuration which was created through the context. This is useful when the target
    /// root object has to be created in advance (e.g. with a specific identifier), and only its
    /// content should be filled from the source.
    /// </summary>
    /// <param name="target">The target configuration, created through the context.</param>
    /// <param name="source">The source configuration whose content should be imported.</param>
    public void ImportInto(GameConfiguration target, GameConfiguration source)
    {
        // Register the existing target as the result for the source root, so that all references
        // to the source root resolve to the target, instead of creating another instance.
        this._sourceToTarget[source] = target;

        foreach (var property in typeof(GameConfiguration).GetProperties())
        {
            // Keep the identifier of the already created target root untouched.
            if (property.Name == nameof(IIdentifiable.Id))
            {
                continue;
            }

            this.CopyProperty(property, source, target);
        }
    }

    private static Type GetModelBaseType(Type type)
    {
        // The basic model and the entity framework model types are siblings: both derive directly
        // from the same (shared) model base type. We need that shared base type so that the context
        // can resolve its own entity framework type for it. These base types live in several
        // namespaces (e.g. MUnique.OpenMU.DataModel, .AttributeSystem, .PlugIns), so we rely on the
        // direct base type instead of a namespace heuristic.
        var baseType = type.BaseType;
        return baseType is null || baseType == typeof(object) ? type : baseType;
    }

    private static Type? GetCollectionInterface(Type type)
    {
        if (type.IsArray || type == typeof(string))
        {
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>))
        {
            return type;
        }

        return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));
    }

    private object? Convert(object? source)
    {
        if (source is null)
        {
            return null;
        }

        if (this._sourceToTarget.TryGetValue(source, out var existing))
        {
            return existing;
        }

        var modelBaseType = GetModelBaseType(source.GetType());
        var target = this._context.CreateNew(modelBaseType);

        // Register before copying the properties, so circular references resolve to this instance.
        this._sourceToTarget[source] = target;

        if (source is IIdentifiable sourceIdentifiable && target is IIdentifiable targetIdentifiable)
        {
            targetIdentifiable.Id = sourceIdentifiable.Id;
        }

        foreach (var property in modelBaseType.GetProperties())
        {
            this.CopyProperty(property, source, target);
        }

        return target;
    }

    private object? ConvertValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        // Identifiable objects are part of the persistent object graph and need to be recreated.
        // Everything else (value types, strings, byte arrays, ...) can be assigned as-is.
        return value is IIdentifiable ? this.Convert(value) : value;
    }

    private void CopyProperty(PropertyInfo property, object source, object target)
    {
        if (property.GetIndexParameters().Length > 0)
        {
            return;
        }

        var getter = property.GetGetMethod(true);
        if (getter is null)
        {
            return;
        }

        var collectionInterface = GetCollectionInterface(property.PropertyType);
        if (collectionInterface is not null)
        {
            if (getter.Invoke(source, null) is not IEnumerable sourceCollection
                || getter.Invoke(target, null) is not { } targetCollection)
            {
                return;
            }

            var addMethod = collectionInterface.GetMethod(nameof(ICollection<object>.Add))!;
            foreach (var element in sourceCollection)
            {
                addMethod.Invoke(targetCollection, new[] { this.ConvertValue(element) });
            }

            return;
        }

        var setter = property.GetSetMethod(true);
        if (setter is null)
        {
            return;
        }

        var value = getter.Invoke(source, null);
        setter.Invoke(target, new[] { this.ConvertValue(value) });
    }
}
