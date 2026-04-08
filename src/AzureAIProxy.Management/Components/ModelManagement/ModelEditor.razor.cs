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

    private void ToggleMaskKey() => maskKey = !maskKey;

    private void OnModelTypeChanged(ModelType? newValue)
    {
        if (Model is null) return;
        Model.ModelType = newValue;
        if (newValue != ModelType.AI_Toolkit)
            Model.UseMaxCompletionTokens = false;
    }

    private void OnManagedIdentityChanged(bool newValue)
    {
        if (Model is null) return;
        Model.UseManagedIdentity = newValue;
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
