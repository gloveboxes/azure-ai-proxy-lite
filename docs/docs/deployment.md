# OpenAI proxy service

The solution consists of three parts; the proxy service, the proxy playground, with a similar look and feel to the official Azure OpenAI Playground, and event admin.

## Setup

This repo is set up for deployment on Azure Container Apps using the configuration files in the `infra` folder.

### Prerequisites

1. An Azure subscription
2. Deployed Azure OpenAI Models


### Required software

Tested on Windows, macOS and Ubuntu 22.04.

Install:

1. [VS Code](https://code.visualstudio.com/)
2. [VS Code Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers)
3. [Docker](https://www.docker.com/products/docker-desktop)

<!-- ## Create an Entra app registration

The AI Proxy admin is secured using Entra. You first need to register an application in your organizations Entra directory.

1. Log into the Azure Portal.
1. Select `Microsoft Entra ID` from the left-hand menu.
1. Select `+ Add` dropdown, then select `App registration`.
1. Name the registration, ensure `Accounts in this organizational directory only` is selected, and select `Register`.
1. Navigate to `Overview`, and make a note of the `Application (client) ID` as you will need it when you deploy the solution.

    ![](media/app-registration.png)

1. When you deploy the solution, you will need to create a client secret.
1. After the solution has been deployed, you will need to amend the app registration to add the redirect URI and enable the `ID tokens` under `Authentication`. -->

## Deploying

The recommended way to deploy this app is with Dev Containers. Install the [VS Code Remote Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers) and Docker, open this repository in a container and you'll be ready to go.

1. Ensure Docker is installed and running.
2. Clone the repo:

    ```shell
    git clone https://github.com/gloveboxes/azure-ai-proxy-lite.git
    ```

3. Open the repo in VS Code.
4. You will be prompted to `Reopen in Container`, click the button to do so. This will build the container and open the repo in a container.
5. In the VS Code dev container, open a terminal and run the following commands to authenticate with Azure:

    ```shell
    azd auth login --use-device-code
    ```

6. Provision and deploy the proxy solution by running the following command in the terminal:

    ```shell
    azd up
    ```

    You will be prompted for the following:

    1. The environment name, keep the name short, max 7 characters to avoid invalid resource names being generated.
    2. Select a subscription from your Azure account.
    3. Select a location (like "eastus" or "sweden central"). Then azd will provision the resources in your account and deploy the latest code. Recommend deploying the proxy to the same location you plan to deploy your models.
    4. Select the 'swaLocation' infrastructure parameter. Recommend selecting a location close to or the same as the Azure location you previously selected.

    On completion, the following Azure resources will be provisioned:

    ![Azure OpenAI Playground experience](media/azure_resources.png)

7. When `azd` has finished deploying you'll see a link to the Azure Resource Group created for the solution.

    The Admin and Playground links will be displayed when `azd up` completes.

    ![](media/app_deployed.png)

## Authenticating with the AI Proxy Admin

1. Navigate to the Azure Container URL
2. You will be prompted for the user name and password.
3. The username is `admin`
4. The password is obtained from the proxy container deployment.
   1. Navigate the the `secrets` sidebar menu.
   2. Copy the `admin password` and paste into the proxy auth page.

## Updating the deployed app

To make any changes to the app code, just run:

```shell
azd deploy [admin | playground | proxy]
```

## Next steps

1. [Deploy an Azure AI Resources](#deploy-an-azure-ai-resources)
1. [Map AI Resources to the AI Proxy](./resources.md)
1. [Create and manage events](./events.md)
1. [Capacity planning](./capacity.md)

## Deploy an Azure AI Resources

1. The proxy supports model deployments from the `OpenAI Service` and from `Foundry Projects`.
2. Make a note of the `endpoint_key` and `endpoint_url` as you'll need them when you configure resources for the AI Proxy.

## Troubleshooting

If you encounter any issues deploying the solution, please raise an issue on the [GitHub repo](https://gloveboxes.github.io/azure-ai-proxy-lite//issues)
