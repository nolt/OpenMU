// <copyright file="JsonImportController.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Web.AdminPanel.API;

using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Web.AdminPanel.Services;

/// <summary>
/// Controller which allows to import a previously downloaded game configuration json.
/// </summary>
/// <remarks>
/// This (re)creates the database and restores the uploaded configuration onto it, creating the
/// surrounding server definitions for the selected game version like an installation does.
/// It is the programmatic counterpart of the configuration download.
/// </remarks>
[ApiController]
[Route("import")]
public class JsonImportController : ControllerBase
{
    private readonly SetupService _setupService;
    private readonly ILogger<JsonImportController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonImportController"/> class.
    /// </summary>
    /// <param name="setupService">The setup service.</param>
    /// <param name="logger">The logger.</param>
    public JsonImportController(SetupService setupService, ILogger<JsonImportController> logger)
    {
        this._setupService = setupService;
        this._logger = logger;
    }

    /// <summary>
    /// Imports the uploaded game configuration json, recreating the database in the process.
    /// </summary>
    /// <param name="file">The uploaded game configuration json file.</param>
    /// <param name="version">The key of the game version to use for the server definitions.
    /// If omitted or unknown, the first available version is used.</param>
    /// <param name="gameServerCount">The number of game server definitions to create.</param>
    /// <returns>The result of the action.</returns>
    [HttpPost("gameconfiguration")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportGameConfigurationAsync(
        IFormFile? file,
        [FromQuery] string? version = null,
        [FromQuery] byte gameServerCount = 1)
    {
        if (file is null || file.Length == 0)
        {
            return this.BadRequest("No configuration file was provided.");
        }

        if (gameServerCount < 1)
        {
            return this.BadRequest("At least one game server is required.");
        }

        // Buffer into memory so the json deserializer can read the whole document.
        using var memoryStream = new MemoryStream();
        await using (var uploadStream = file.OpenReadStream())
        {
            await uploadStream.CopyToAsync(memoryStream).ConfigureAwait(false);
        }

        memoryStream.Position = 0;

        try
        {
            await this._setupService.ImportGameConfigurationAsync(memoryStream, version, gameServerCount).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error while importing the game configuration.");
            return this.StatusCode(StatusCodes.Status500InternalServerError, "The configuration could not be imported. See the server log for details.");
        }

        return this.Ok("The configuration was imported. Please restart the connect and game server containers.");
    }
}
