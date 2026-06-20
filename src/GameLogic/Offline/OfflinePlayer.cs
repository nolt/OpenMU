// <copyright file="OfflinePlayer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Offline;

using MUnique.OpenMU.GameLogic.MuHelper;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// An offline player that continues leveling after the real client disconnects.
/// </summary>
public sealed class OfflinePlayer : Player
{
    private OfflinePlayerMuHelper? _intelligence;

    /// <summary>
    /// Initializes a new instance of the <see cref="OfflinePlayer"/> class.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    public OfflinePlayer(IGameContext gameContext)
        : base(gameContext)
    {
    }

    /// <summary>
    /// Gets the login name this offline player belongs to.
    /// </summary>
    public string? AccountLoginName => this.Account?.LoginName;

    /// <summary>
    /// Gets the start timestamp of the offline session.
    /// </summary>
    public DateTime StartTimestamp { get; internal set; }

    /// <summary>
    /// Initializes the offline player by loading its account and character freshly
    /// into its own persistence context.
    /// </summary>
    /// <param name="loginName">The account login name.</param>
    /// <param name="characterName">The name of the character to continue playing.</param>
    /// <returns><c>true</c> if successfully started.</returns>
    public async ValueTask<bool> InitializeAsync(string loginName, string characterName)
    {
        try
        {
            this.StartTimestamp = DateTime.UtcNow;

            // Load the account freshly into this offline player's own persistence context
            // instead of attaching the real player's entity graph from a now-disposed
            // context. Re-using foreign entity instances corrupts EF change tracking and
            // makes every SaveChanges fail, which loses all offline progress.
            var account = await this.PersistenceContext.GetAccountByLoginNameAsync(loginName).ConfigureAwait(false);
            if (account is null)
            {
                this.Logger.LogError("Offline player could not load account {LoginName}.", loginName);
                return false;
            }

            this.Account = account;

            await this.AdvanceToCharacterSelectionStateAsync().ConfigureAwait(false);

            await this.SetupCharacterAsync(characterName).ConfigureAwait(false);
            if (this.SelectedCharacter is not { } selectedCharacter)
            {
                this.Logger.LogError("Offline player could not select character {CharacterName} for account {LoginName}.", characterName, loginName);
                return false;
            }

            await this.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);

            this.StartIntelligence();

            this.Logger.LogDebug(
                "Offline player started for character {CharacterName} on map {Map} at {Position}.",
                selectedCharacter.Name,
                selectedCharacter.CurrentMap?.Name,
                this.Position);

            return true;
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to initialize offline player for {player}.", this);
            return false;
        }
    }

    /// <summary>
    /// Stops the offline player and removes it from the world.
    /// </summary>
    public async ValueTask StopAsync()
    {
        if (this._intelligence is { } intelligence)
        {
            await intelligence.DisposeAsync().ConfigureAwait(false);
            this._intelligence = null;
        }

        try
        {
            await this.SaveProgressAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "Failed to save progress of offline player {AccountLoginName}.", this.AccountLoginName);
        }

        await this.DisconnectAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override ICustomPlugInContainer<IViewPlugIn> CreateViewPlugInContainer()
        => new OfflineViewPlugInContainer(this);

    private async ValueTask AdvanceToCharacterSelectionStateAsync()
    {
        // Advance state to allow the intelligence to perform actions.
        await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.LoginScreen).ConfigureAwait(false);
        await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.Authenticated).ConfigureAwait(false);
        await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.CharacterSelection).ConfigureAwait(false);
    }

    private async ValueTask SetupCharacterAsync(string characterName)
    {
        var character = this.Account?.Characters.FirstOrDefault(c => c.Name.Equals(characterName));
        if (character is null)
        {
            return;
        }

        // Add to context and set character. The character is already tracked by this
        // player's own persistence context (loaded via GetAccountByLoginNameAsync), so
        // it must not be attached again from a foreign context.
        await this.GameContext.AddPlayerAsync(this).ConfigureAwait(false);
        await this.SetSelectedCharacterAsync(character).ConfigureAwait(false);
    }

    private void StartIntelligence()
    {
        this._intelligence = new OfflinePlayerMuHelper(this);
        this._intelligence.Start();
    }
}