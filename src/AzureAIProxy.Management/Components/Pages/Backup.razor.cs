using System.Text.Json;
using AzureAIProxy.Management.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
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

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Inject]
    public required IAuthService AuthService { get; set; }

    private bool isBusy;
    private string currentOperation = "";

    private async Task<string?> PromptPassphraseAsync(string title, string message, string buttonText = "OK")
    {
        DialogParameters<PassphraseDialog> parameters = new()
        {
            { x => x.ContentText, message },
            { x => x.ButtonText, buttonText }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true };
        var dialog = await DialogService.ShowAsync<PassphraseDialog>(title, parameters, options);
        var result = await dialog.Result;

        if (result is null || result.Canceled)
            return null;

        return result.Data as string;
    }

    private async Task BackupAsync()
    {
        var passphrase = await PromptPassphraseAsync(
            "Encrypt Backup",
            "Enter a passphrase to encrypt the backup file. You will need this passphrase to restore.",
            "Backup");

        if (string.IsNullOrWhiteSpace(passphrase))
        {
            Snackbar.Add("Backup cancelled.", Severity.Info);
            return;
        }

        isBusy = true;
        currentOperation = "backup";
        await InvokeAsync(StateHasChanged);

        try
        {
            var encryptedBytes = await Task.Run(() => BackupService.CreateEncryptedBackupAsync(passphrase));
            var (email, _) = await AuthService.GetCurrentUserEmailNameAsync();
            var sanitizedEmail = email.Replace("@", "_at_").Replace(".", "_");
            var fileName = $"aiproxy-backup-{sanitizedEmail}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.enc";

            // Trigger browser download via JS interop
            using var streamRef = new DotNetStreamReference(new MemoryStream(encryptedBytes));
            await JS.InvokeVoidAsync("downloadFileFromStream", fileName, streamRef);

            Snackbar.Add("Encrypted backup downloaded.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Backup failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            isBusy = false;
            currentOperation = "";
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RestoreBackupAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null) return;

        var passphrase = await PromptPassphraseAsync(
            "Decrypt Backup",
            "Warning: Restoring will clear all your existing data before importing. Enter the passphrase used when this backup was created.",
            "Restore");

        if (string.IsNullOrWhiteSpace(passphrase))
        {
            Snackbar.Add("Restore cancelled.", Severity.Info);
            return;
        }

        isBusy = true;
        currentOperation = "restore";
        await InvokeAsync(StateHasChanged);

        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);

            // Buffer the stream since Blazor streams can't be passed across threads
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            await Task.Run(() => BackupService.RestoreEncryptedBackupAsync(passphrase, ms));
            Snackbar.Add("Backup restored successfully.", Severity.Success);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Snackbar.Add("Restore failed: incorrect passphrase or corrupted backup file.", Severity.Error);
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
