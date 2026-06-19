// <copyright file="ConfigurationImportTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.Persistence.Json;

/// <summary>
/// Tests for the <see cref="ConfigurationImporter"/>, i.e. importing a (e.g. downloaded and
/// deserialized) <see cref="GameConfiguration"/> graph into a fresh persistence context.
/// </summary>
[TestFixture]
internal class ConfigurationImportTests
{
    /// <summary>
    /// Imports a full initial configuration into a fresh context and verifies that the result
    /// is an exact structural copy of the source, with all (also circular) references resolved
    /// and every object created through (and therefore persistable by) the target context.
    /// </summary>
    [Test]
    public async Task ImportCreatesStructuralCopyAsync()
    {
        // Arrange: create an initial configuration which acts as the source to import.
        var sourceProvider = new InMemoryPersistenceContextProvider();
        var dataInitialization = new VersionSeasonSix.DataInitialization(sourceProvider, new NullLoggerFactory());
        await dataInitialization.CreateInitialDataAsync(1, true).ConfigureAwait(false);

        using var sourceContext = sourceProvider.CreateNewContext();
        var source = (await sourceContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).First();

        // Act: import the source configuration into a fresh, empty context.
        var targetProvider = new InMemoryPersistenceContextProvider();
        using var targetContext = targetProvider.CreateNewContext();
        var imported = new ConfigurationImporter(targetContext).Import(source);
        await targetContext.SaveChangesAsync().ConfigureAwait(false);

        // Assert: the imported configuration is a distinct, but structurally identical copy.
        Assert.That(imported, Is.Not.SameAs(source));
        Assert.That(imported.GetId(), Is.EqualTo(source.GetId()), "the identifier must be preserved (restore semantics)");

        AssertSameCount(source, imported, c => c.Items.Count, nameof(GameConfiguration.Items));
        AssertSameCount(source, imported, c => c.Maps.Count, nameof(GameConfiguration.Maps));
        AssertSameCount(source, imported, c => c.Monsters.Count, nameof(GameConfiguration.Monsters));
        AssertSameCount(source, imported, c => c.Skills.Count, nameof(GameConfiguration.Skills));
        AssertSameCount(source, imported, c => c.CharacterClasses.Count, nameof(GameConfiguration.CharacterClasses));
        AssertSameCount(source, imported, c => c.DropItemGroups.Count, nameof(GameConfiguration.DropItemGroups));
        AssertSameCount(source, imported, c => c.Attributes.Count, nameof(GameConfiguration.Attributes));
        AssertSameCount(source, imported, c => c.ItemOptions.Count, nameof(GameConfiguration.ItemOptions));
        AssertSameCount(source, imported, c => c.MagicEffects.Count, nameof(GameConfiguration.MagicEffects));
        AssertSameCount(source, imported, c => c.PlugInConfigurations.Count, nameof(GameConfiguration.PlugInConfigurations));

        // The imported objects must be distinct instances (a deep copy, not the source objects).
        Assert.That(imported.Monsters.First(), Is.Not.SameAs(source.Monsters.First()));

        // Every imported object must be created through the target context, so it must be
        // retrievable from it - the prerequisite for persisting it to a database.
        var importedMonsters = (await targetContext.GetAsync<MonsterDefinition>().ConfigureAwait(false)).ToList();
        Assert.That(importedMonsters, Has.Count.EqualTo(source.Monsters.Count));

        // Reference integrity: a monster spawned on a map must be the very same instance as the
        // one in the global monster list (proves references were resolved, not duplicated).
        var importedSpawn = imported.Maps
            .SelectMany(m => m.MonsterSpawns)
            .FirstOrDefault(s => s.MonsterDefinition is not null);
        if (importedSpawn is not null)
        {
            Assert.That(imported.Monsters, Does.Contain(importedSpawn.MonsterDefinition));
            Assert.That(importedMonsters, Does.Contain(importedSpawn.MonsterDefinition));
        }
    }

    /// <summary>
    /// Imports a configuration through the full installation flow (like the admin panel does) and
    /// verifies that the imported configuration content is present and that the surrounding server
    /// definitions (scaffolding) were created, so the server is runnable.
    /// </summary>
    [Test]
    public async Task ImportConfigurationCreatesRunnableServerAsync()
    {
        // Arrange: create a source configuration which acts as the downloaded configuration.
        var sourceProvider = new InMemoryPersistenceContextProvider();
        await new VersionSeasonSix.DataInitialization(sourceProvider, new NullLoggerFactory())
            .CreateInitialDataAsync(1, true).ConfigureAwait(false);
        using var sourceContext = sourceProvider.CreateNewContext();
        var source = (await sourceContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).First();

        // Act: import it into a fresh server like the admin panel's installation does.
        var targetProvider = new InMemoryPersistenceContextProvider();
        await new VersionSeasonSix.DataInitialization(targetProvider, new NullLoggerFactory())
            .ImportConfigurationAsync(2, source).ConfigureAwait(false);

        // Assert: the imported configuration content is present...
        using var targetContext = targetProvider.CreateNewContext();
        var imported = (await targetContext.GetAsync<GameConfiguration>().ConfigureAwait(false)).Single();
        Assert.That(imported.GetId(), Is.EqualTo(source.GetId()));
        Assert.That(imported.Maps, Has.Count.EqualTo(source.Maps.Count));
        Assert.That(imported.Monsters, Has.Count.EqualTo(source.Monsters.Count));
        Assert.That(imported.Items, Has.Count.EqualTo(source.Items.Count));
        Assert.That(imported.PlugInConfigurations, Has.Count.EqualTo(source.PlugInConfigurations.Count));

        // ...and the scaffolding to run the server was created around it.
        var gameServers = (await targetContext.GetAsync<GameServerDefinition>().ConfigureAwait(false)).ToList();
        Assert.That(gameServers, Has.Count.EqualTo(2), "game server definitions");
        Assert.That(gameServers, Has.All.Property(nameof(GameServerDefinition.GameConfiguration)).EqualTo(imported));

        var connectServers = (await targetContext.GetAsync<ConnectServerDefinition>().ConfigureAwait(false)).ToList();
        Assert.That(connectServers, Is.Not.Empty, "connect server definitions");
    }

    private static void AssertSameCount(GameConfiguration source, GameConfiguration imported, Func<GameConfiguration, int> selector, string name)
    {
        Assert.That(selector(imported), Is.EqualTo(selector(source)), name);
    }
}
