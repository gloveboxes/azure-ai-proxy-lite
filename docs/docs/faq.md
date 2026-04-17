# Frequently asked questions

1. **I've created an event but no models are available. What's wrong?**

    Create a model deployment in Azure. From the Azure AI Proxy Admin portal, select the `Resources` tab and create a new resource of type `Foundry Model` using the Azure OpenAI resource key, endpoint and deployment name. Once the resource is created, edit the event to add the resource.

1. **What resource types does the proxy support?**

    The proxy supports five resource types: `Foundry Model` (chat completions, embeddings), `Foundry Agent` (Azure AI Foundry Agent Service), `MCP Server` (Model Context Protocol), `Foundry Toolkit` (Foundry Toolkit extension), and `Azure AI Search` (search queries). See [Configuring resources](resources.md) for details.

1. **How do I authenticate with the AI Proxy Admin portal?**

    When deployed to Azure, the admin portal uses **Microsoft Entra ID** authentication. Navigate to the admin UI URL and sign in with your organizational Microsoft account. When running locally with Docker, the admin portal uses username/password authentication (configured via `ADMIN_USERNAME` and `ADMIN_PASSWORD` environment variables).

1. **Can I use Managed Identity instead of API keys?**

    Yes. The proxy supports Azure Managed Identity authentication for all resource types. Enable the **Use Managed Identity** toggle when adding a resource. See the [Managed Identity guide](deployment/managed_identity.md) for RBAC setup instructions.

1. **What is the Daily Request Cap?**

    The Daily Request Cap limits the number of requests a single attendee can make per day. It resets at midnight UTC. This prevents runaway usage and abuse.

1. **What is the Max Token Cap?**

    The Max Token Cap limits the maximum tokens per request. This ensures that attendees don't consume excessive capacity. See [Capacity planning](capacity.md) for guidance.

1. **Can attendees access the proxy without a GitHub account?**

    Yes, using the `Event Shared Code` feature. Set a shared code on the event, and distribute the API key format `event-id@shared-code/email-address` to attendees. This is recommended only for short in-person workshops.

1. **How do I update the proxy after making code changes?**

    Run `azd deploy proxy` to redeploy the proxy API, `azd deploy admin` to redeploy the admin UI, or `azd deploy registration` to redeploy the registration app. Run `azd up` to redeploy everything.
