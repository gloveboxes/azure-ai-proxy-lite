using System.ComponentModel.DataAnnotations;

namespace AzureAIProxy.Management.Components.ModelManagement;

/// <summary>
/// Validation attribute that requires the api-version query parameter in the endpoint URL when ModelType is AI_Toolkit.
/// </summary>
public class ApiVersionRequiredForAIToolkitAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var model = validationContext.ObjectInstance;
        var modelTypeProperty = model.GetType().GetProperty("ModelType");

        if (modelTypeProperty == null)
        {
            return ValidationResult.Success;
        }

        var modelType = modelTypeProperty.GetValue(model) as ModelType?;

        if (modelType != ModelType.AI_Toolkit)
        {
            return ValidationResult.Success;
        }

        if (value is string url && !string.IsNullOrWhiteSpace(url))
        {
            if (url.Contains("api-version=", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Success;
            }
        }

        return new ValidationResult("The Endpoint URL must include the api-version parameter for AI Toolkit resources.");
    }
}
