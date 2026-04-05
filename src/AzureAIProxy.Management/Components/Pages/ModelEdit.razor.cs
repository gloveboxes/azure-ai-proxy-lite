using AzureAIProxy.Management.Components.ModelManagement;
using AzureAIProxy.Management.Services;
using Microsoft.AspNetCore.Components;

namespace AzureAIProxy.Management.Components.Pages;

public partial class ModelEdit : ComponentBase
{
    [Parameter]
    public required string Id { get; set; }

    private Guid catalogId;

    [Inject]
    public IModelService ModelService { get; set; } = null!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = null!;

    public ModelEditorModel Model { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(Id))
        {
            NavigationManager.NavigateTo("/models");
            return;
        }

        if (!Guid.TryParse(Id, out catalogId))
        {
            NavigationManager.NavigateTo("/models");
            return;
        }

        OwnerCatalog? m = await ModelService.GetOwnerCatalogAsync(catalogId);

        if (m is null)
        {
            NavigationManager.NavigateTo("/models");
            return;
        }

        Model = new()
        {
            FriendlyName = m.FriendlyName,
            DeploymentName = m.DeploymentName,
            EndpointKey = m.EndpointKey,
            EndpointUrl = m.EndpointUrl,
            ModelType = m.ModelType,
            Location = m.Location,
            Active = m.Active,
            UseManagedIdentity = m.UseManagedIdentity,
            UseMaxCompletionTokens = m.UseMaxCompletionTokens,
        };
    }

    private async Task OnValidSubmit(ModelEditorModel model)
    {
        OwnerCatalog m = new()
        {
            CatalogId = catalogId,
            FriendlyName = model.FriendlyName!,
            DeploymentName = model.DeploymentName!.Trim(),
            EndpointKey = model.EndpointKey!,
            ModelType = model.ModelType!.Value,
            EndpointUrl = model.EndpointUrl!,
            Location = model.Location!,
            Active = model.Active,
            UseManagedIdentity = model.UseManagedIdentity,
            UseMaxCompletionTokens = model.UseMaxCompletionTokens
        };

        await ModelService.UpdateOwnerCatalogAsync(m);

        NavigationManager.NavigateTo("/models");
    }
}
