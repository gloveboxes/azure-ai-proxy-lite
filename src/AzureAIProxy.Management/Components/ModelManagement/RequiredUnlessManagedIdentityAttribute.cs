using System.ComponentModel.DataAnnotations;

namespace AzureAIProxy.Management.Components.ModelManagement;

/// <summary>
/// Validation attribute that makes a field required unless UseManagedIdentity is true
/// </summary>
public class RequiredUnlessManagedIdentityAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Get the model instance
        var model = validationContext.ObjectInstance;

        // Check if UseManagedIdentity property exists
        var useManagedIdentityProperty = model.GetType().GetProperty("UseManagedIdentity");

        if (useManagedIdentityProperty != null)
        {
            var useManagedIdentity = (bool?)useManagedIdentityProperty.GetValue(model);

            // If managed identity is enabled, the field is not required
            if (useManagedIdentity == true)
            {
                return ValidationResult.Success;
            }
        }

        // Otherwise, apply standard required validation
        if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
        {
            return new ValidationResult(ErrorMessage ?? $"The {validationContext.DisplayName} field is required when not using Managed Identity.");
        }

        return ValidationResult.Success;
    }
}
