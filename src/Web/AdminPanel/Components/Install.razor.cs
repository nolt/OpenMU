// <copyright file="Install.razor.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.Components;

using System.IO;
using BlazorInputFile;
using Microsoft.AspNetCore.Components;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence.Initialization;
using MUnique.OpenMU.Web.AdminPanel.Services;

/// <summary>
/// The component which allows to initialize the database.
/// </summary>
public sealed partial class Install
{
    /// <summary>
    /// Gets or sets the selected version.
    /// </summary>
    public IDataInitializationPlugIn? SelectedVersion { get; set; }

    /// <summary>
    /// Gets or sets the game server count.
    /// </summary>
    public int GameServerCount { get; set; } = 2;

    /// <summary>
    /// Gets or sets a value indicating whether to create test accounts.
    /// </summary>
    public bool CreateTestAccounts { get; set; }

    /// <summary>
    /// Gets or sets the optional configuration file to import instead of initializing a new
    /// configuration. The file is expected to be a previously downloaded game configuration json.
    /// </summary>
    public IFileListEntry? ImportConfigurationFile { get; set; }

    private byte[]? _importConfigurationData;

    /// <summary>
    /// Gets a value indicating whether this instance is installing.
    /// </summary>
    public bool IsInstalling { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this instance has installed.
    /// </summary>
    public bool IsInstalled { get; private set; }

    private int CurrentConnections => this.ServerProvider.Servers.Where(s => s.ServerState != ServerState.Timeout).Sum(s => s.CurrentConnections);

    /// <summary>
    /// Gets or sets the installation finished callback.
    /// </summary>
    [Parameter]
    public EventCallback InstallationFinished { get; set; }

    /// <summary>
    /// Gets or sets the setup service.
    /// </summary>
    [Inject]
    public SetupService SetupService { get; set; } = null!;

    /// <summary>
    /// Gets or sets the server provider.
    /// </summary>
    [Inject]
    public IServerProvider ServerProvider { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        this.SelectedVersion = this.SetupService.Versions.First();
    }

    /// <summary>
    /// Starts the installation.
    /// </summary>
    private async Task StartInstallationAsync()
    {
        this.IsInstalling = true;
        await this.InvokeAsync(this.StateHasChanged).ConfigureAwait(false);
        try
        {
            if (this._importConfigurationData is { } configurationData)
            {
                await this.ImportConfigurationAsync(configurationData).ConfigureAwait(false);
            }
            else
            {
                await this.SetupService.CreateDatabaseAsync(() => this.SelectedVersion!.CreateInitialDataAsync((byte)this.GameServerCount, this.CreateTestAccounts)).ConfigureAwait(false);
            }
        }
        finally
        {
            this.IsInstalled = true;
            this.IsInstalling = false;
        }
    }

    private async Task ImportConfigurationAsync(byte[] configurationData)
    {
        using var memoryStream = new MemoryStream(configurationData);
        await this.SetupService.ImportGameConfigurationAsync(memoryStream, this.SelectedVersion!.Key, (byte)this.GameServerCount).ConfigureAwait(false);
    }

    private async Task OnImportFileSelected(IFileListEntry[] files)
    {
        var file = files.FirstOrDefault();
        if (file is null)
        {
            this.ImportConfigurationFile = null;
            this._importConfigurationData = null;
            return;
        }

        // Read the file content immediately, while the input element is still attached.
        // BlazorInputFile streams the data from the DOM element through JS interop. If we
        // deferred the read until "Start import", the re-render caused by setting
        // ImportConfigurationFile (which hides the test accounts option) would detach the
        // element and the stream would fail with a "_blazorFilesById" null reference.
        using var memoryStream = new MemoryStream();
        await file.Data.CopyToAsync(memoryStream).ConfigureAwait(false);
        this._importConfigurationData = memoryStream.ToArray();
        this.ImportConfigurationFile = file;
    }

    private void OnSelectVersion(string key)
    {
        this.SelectedVersion = this.SetupService.Versions.First(v => v.Key == key);
    }

    private void OnGameServerCountChange(ChangeEventArgs obj)
    {
        if (obj.Value is string strValue
            && int.TryParse(strValue, out var count))
        {
            this.GameServerCount = count;
        }
    }

    private void OnTestAccountsChange(ChangeEventArgs obj)
    {
        if (obj.Value is bool value)
        {
            this.CreateTestAccounts = value;
        }
    }
}