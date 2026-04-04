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
    private InputType passphraseInputType = InputType.Password;
    private string passphraseIcon = Icons.Material.Filled.VisibilityOff;

    private void TogglePassphraseVisibility()
    {
        showPassphrase = !showPassphrase;
        passphraseInputType = showPassphrase ? InputType.Text : InputType.Password;
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
        if (IsValid)
            MudDialog?.Close(DialogResult.Ok(Passphrase));
    }

    private void Cancel() => MudDialog?.Close(DialogResult.Cancel());

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            Submit();
    }
}
