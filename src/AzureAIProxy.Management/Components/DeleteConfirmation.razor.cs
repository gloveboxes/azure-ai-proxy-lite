namespace AzureAIProxy.Management.Components;
public partial class DeleteConfirmation
{
    [CascadingParameter] public required MudBlazor.IDialogReference MudDialog { get; set; }

    [Parameter] public required string ContentText { get; set; }

    [Parameter] public required string ButtonText { get; set; }

    [Parameter] public Color Color { get; set; }

    void Submit() => MudDialog.Close(DialogResult.Ok(true));
    void Cancel() => MudDialog.Close(DialogResult.Cancel());
}
