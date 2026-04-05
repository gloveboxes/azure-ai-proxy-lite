using Microsoft.AspNetCore.Components.Web;

namespace AzureAIProxy.Management.Components;

public partial class PassphraseDialog
{
    [CascadingParameter] public IMudDialogInstance? MudDialog { get; set; }

    [Parameter] public required string ContentText { get; set; }

    [Parameter] public string ButtonText { get; set; } = "OK";

    [Parameter] public int MinLength { get; set; } = 12;

    private string? Passphrase { get; set; }

    private bool showPassphrase;
    private string passphraseIcon = Icons.Material.Filled.VisibilityOff;
    private Dictionary<string, object> PassphraseAttributes => new()
    {
        ["autocomplete"] = "new-password",
        ["name"] = "backup-passphrase",
        ["autocorrect"] = "off",
        ["autocapitalize"] = "off",
        ["spellcheck"] = "false"
    };

    private void TogglePassphraseVisibility()
    {
        showPassphrase = !showPassphrase;
        passphraseIcon = showPassphrase ? Icons.Material.Filled.Visibility : Icons.Material.Filled.VisibilityOff;
    }

    private string? ValidatePassphrase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Passphrase is required.";
        if (value.Length < MinLength)
            return $"Passphrase must be at least {MinLength} characters.";
        return null;
    }

    private bool IsValid => ValidatePassphrase(Passphrase) is null;

    private void Submit()
    {
        if (!IsValid)
            return;

        var enteredPassphrase = Passphrase;
        Passphrase = string.Empty;
        showPassphrase = false;
        passphraseIcon = Icons.Material.Filled.VisibilityOff;

        MudDialog?.Close(DialogResult.Ok(enteredPassphrase));
    }

    private void Cancel()
    {
        Passphrase = string.Empty;
        showPassphrase = false;
        passphraseIcon = Icons.Material.Filled.VisibilityOff;
        MudDialog?.Close(DialogResult.Cancel());
    }

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            Submit();
    }
}
