using System.Text.Json;
using AzureAIProxy.Management.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace AzureAIProxy.Management.Components.Pages;

public partial class Backup : ComponentBase
{
    [Inject]
    public required IBackupService BackupService { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Inject]
    public required ISnackbar Snackbar { get; set; }

    private bool isBusy;
    private string currentOperation = "";

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task RestoreBackupAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null) return;

        isBusy = true;
        currentOperation = "restore";
        await InvokeAsync(StateHasChanged);

        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
            var data = await JsonSerializer.DeserializeAsync<BackupData>(stream, JsonReadOptions);

            if (data is null)
            {
                Snackbar.Add("Invalid backup file.", Severity.Error);
                return;
            }

            // Confirm before overwriting
            DialogParameters<DeleteConfirmation> parameters = new()
            {
                { x => x.ContentText, $"Restoring will overwrite any existing events and resources that share the same IDs. This backup contains {data.Events.Count} event(s) and {data.Resources.Count} resource(s). Do you want to proceed?" },
                { x => x.ButtonText, "Restore" },
                { x => x.Color, Color.Warning }
            };
            var options = new DialogOptions { CloseOnEscapeKey = true };
            var dialog = await DialogService.ShowAsync<DeleteConfirmation>("Restore Data", parameters, options);
            var result = await dialog.Result;

            if (result is null || result.Canceled)
            {
                Snackbar.Add("Restore cancelled.", Severity.Info);
                return;
            }

            await Task.Run(() => BackupService.RestoreBackupAsync(data));
            Snackbar.Add($"Restored {data.Events.Count} events and {data.Resources.Count} resources.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Restore failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            isBusy = false;
            currentOperation = "";
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OpenClearDialog()
    {
        DialogParameters<DeleteConfirmation> parameters = new()
        {
            { x => x.ContentText, "This will permanently delete ALL data from all Azure Storage tables including events, resources, attendees, metrics, and related data. This action cannot be undone. Are you sure?" },
            { x => x.ButtonText, "Clear All Data" },
            { x => x.Color, Color.Error }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true };
        var dialog = await DialogService.ShowAsync<DeleteConfirmation>("Clear All Data", parameters, options);
        var result = await dialog.Result;

        if (result is null || result.Canceled)
            return;

        isBusy = true;
        currentOperation = "clear";
        await InvokeAsync(StateHasChanged);

        try
        {
            await Task.Run(() => BackupService.ClearAllDataAsync());
            Snackbar.Add("All data has been cleared.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Clear failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            isBusy = false;
            currentOperation = "";
            await InvokeAsync(StateHasChanged);
        }
    }
}
