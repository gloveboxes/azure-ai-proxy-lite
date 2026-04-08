namespace AzureAIProxy.Management.Components.ModelManagement;

public partial class ModelEditor : ComponentBase
{
    [Parameter]
    public ModelEditorModel? Model { get; set; }

    [Parameter]
    public EventCallback<ModelEditorModel> ModelChanged { get; set; }

    [Parameter]
    public EventCallback<ModelEditorModel> OnValidSubmit { get; set; }

    private Microsoft.AspNetCore.Components.Forms.EditForm? editForm;

    private bool isSubmitting = false;

    private bool maskKey = true;

    private string EndpointHelperText => Model?.ModelType == ModelType.AI_Toolkit
        ? "For AI Toolkit, the Foundry model endpoint should include the api-version parameter"
        : "For example https://my-ai-resource.azure.com";

    private void ToggleMaskKey() => maskKey = !maskKey;

    private void OnModelTypeChanged(ModelType? newValue)
    {
        if (Model is null) return;
        Model.ModelType = newValue;
        if (newValue != ModelType.AI_Toolkit)
            Model.UseMaxCompletionTokens = false;
    }

    private async Task OnManagedIdentityChanged(bool newValue)
    {
        if (Model is null) return;
        Model.UseManagedIdentity = newValue;
        StateHasChanged();
        await Task.Yield();
        if (!newValue)
        {
            editForm?.EditContext?.NotifyFieldChanged(editForm.EditContext.Field(nameof(Model.EndpointKey)));
        }
        editForm?.EditContext?.Validate();
    }

    protected override Task OnInitializedAsync()
    {
        Model ??= new();
        return Task.CompletedTask;
    }

    public async Task HandleValidSubmit()
    {
        if (Model is null)
        {
            return;
        }

        isSubmitting = true;
        await OnValidSubmit.InvokeAsync(Model);
        isSubmitting = false;
    }
}
