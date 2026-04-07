# Managing models

## Understanding Azure AI model deployments

The proxy supports the following model deployment classes:

| Model deployment class | Models | Description |
| ---------------------- | ------ | ----------- |
| `foundry-model` | gpt-4o, gpt-4.1, gpt-4.1-mini, or newer | Azure OpenAI Chat Completions and Embeddings APIs. |
| `foundry-agent` | Not applicable | Azure AI Foundry Agent Service for agent, assistant, thread, file, conversation, and response operations. |
| `mcp-server` | Not applicable | Model Context Protocol server endpoints. |
| `ai-toolkit` | Not applicable | Models surfaced to attendees via the VS Code AI Toolkit extension. |
| `azure-ai-search` | Not applicable | Pass-through access to an instance of Azure AI Search. |

!!! tip
    Each model deployment must have a unique deployment name.

## Deploy Azure OpenAI models

1. Open the Azure Portal.
2. Create an Azure OpenAI resource in your subscription. See [Create and deploy an Azure OpenAI Service resource](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource) for more information.
   - Make a note of the `endpoint_key` and `endpoint_url` as you'll need them for the next step.
     - You can find the `endpoint_key` and `endpoint_url` in the Azure Portal under the `Keys and Endpoint` tab for the Azure OpenAI resource.
3. Create an Azure OpenAI model deployment. See [Create an Azure OpenAI model deployment](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource?pivots=web-portal#deploy-a-model) for more information. From the Azure Portal, select the Azure OpenAI resource, then select the `Deployments` tab, and finally select `Create deployment`

   1. Select the `+ Create new deployment`.
   2. Select the `Model`.
   3. `Name` the deployment. Make a note of the name as you'll need it for the next step.
   4. Select `Advanced options`, and select the `Tokens per Minute Rate Limit`.
   5. Select `Create`.
