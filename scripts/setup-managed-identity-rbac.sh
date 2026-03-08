#!/bin/bash
set -euo pipefail

#
# setup-managed-identity-rbac.sh
#
# Assigns RBAC roles to the proxy's system-assigned managed identity so it can
# authenticate to Azure OpenAI and/or Azure AI Foundry Agent Service resources.
#
# Usage:
#   ./scripts/setup-managed-identity-rbac.sh
#
# The script will interactively prompt for all required values.
#

# ---------------------------------------------------------------------------
# Colours (no-op when stdout is not a terminal)
# ---------------------------------------------------------------------------
if [[ -t 1 ]]; then
    BOLD="\033[1m"
    GREEN="\033[32m"
    YELLOW="\033[33m"
    CYAN="\033[36m"
    RED="\033[31m"
    RESET="\033[0m"
else
    BOLD="" GREEN="" YELLOW="" CYAN="" RED="" RESET=""
fi

info()  { echo -e "${CYAN}ℹ ${RESET}$*"; }
ok()    { echo -e "${GREEN}✔ ${RESET}$*"; }
warn()  { echo -e "${YELLOW}⚠ ${RESET}$*"; }
error() { echo -e "${RED}✖ ${RESET}$*"; }

# ---------------------------------------------------------------------------
# Pre-flight: ensure Azure CLI is logged in
# ---------------------------------------------------------------------------
if ! az account show &>/dev/null; then
    error "You must be logged in to Azure CLI. Run 'az login' first."
    exit 1
fi

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
SUBSCRIPTION_NAME=$(az account show --query name -o tsv)
echo ""
info "Active subscription: ${BOLD}${SUBSCRIPTION_NAME}${RESET} (${SUBSCRIPTION_ID})"
echo ""

# ---------------------------------------------------------------------------
# Role IDs
# ---------------------------------------------------------------------------
ROLE_COGNITIVE_SERVICES_OPENAI_USER="5e0bd9bd-7b93-4f28-af87-19fc36ad61bd"
ROLE_AZURE_AI_USER="53ca6127-db72-4e31-b599-04dc5da150b4"

# ---------------------------------------------------------------------------
# Helper: prompt with a default value
# ---------------------------------------------------------------------------
prompt() {
    local var_name=$1
    local message=$2
    local default=${3:-}
    local value

    if [[ -n "$default" ]]; then
        read -rp "$(echo -e "${BOLD}${message}${RESET} [${default}]: ")" value
        value=${value:-$default}
    else
        while true; do
            read -rp "$(echo -e "${BOLD}${message}${RESET}: ")" value
            if [[ -n "$value" ]]; then
                break
            fi
            warn "A value is required."
        done
    fi
    eval "$var_name=\"$value\""
}

# ---------------------------------------------------------------------------
# Helper: assign a role (idempotent)
# ---------------------------------------------------------------------------
assign_role() {
    local principal_id=$1
    local role_id=$2
    local scope=$3
    local role_name=$4

    info "Assigning ${BOLD}${role_name}${RESET} at scope:"
    echo "    ${scope}"

    # Check if assignment already exists
    existing=$(az role assignment list \
        --assignee "$principal_id" \
        --role "$role_id" \
        --scope "$scope" \
        --query "length(@)" -o tsv 2>/dev/null || echo "0")

    if [[ "$existing" -gt 0 ]]; then
        ok "Role already assigned — skipping."
    else
        az role assignment create \
            --assignee "$principal_id" \
            --role "$role_id" \
            --scope "$scope" \
            --output none
        ok "Role assigned successfully."
    fi
    echo ""
}

# ===========================================================================
echo -e "${BOLD}Azure AI Proxy — Managed Identity RBAC Setup${RESET}"
echo "============================================================"
echo ""
echo "This script assigns roles to your proxy's system-assigned managed"
echo "identity so it can authenticate to Azure AI resources without API keys."
echo ""

# ---------------------------------------------------------------------------
# Step 1: Get the proxy identity
# ---------------------------------------------------------------------------
echo -e "${BOLD}Step 1: Proxy Identity${RESET}"
echo "--------------------------------------------------------------"
echo ""
echo "The proxy runs as an Azure Container App with a system-assigned"
echo "managed identity. We need the container app details to look up"
echo "the identity's principal ID."
echo ""

prompt PROXY_RG "Proxy container app resource group" ""
prompt PROXY_APP_NAME "Proxy container app name" ""

info "Looking up system-assigned identity for ${PROXY_APP_NAME}..."

PRINCIPAL_ID=$(az containerapp show \
    --name "$PROXY_APP_NAME" \
    --resource-group "$PROXY_RG" \
    --query "identity.principalId" -o tsv 2>/dev/null || true)

if [[ -z "$PRINCIPAL_ID" || "$PRINCIPAL_ID" == "None" ]]; then
    error "Could not find a system-assigned identity on container app '${PROXY_APP_NAME}'."
    error "Ensure the container app exists and has a system-assigned identity enabled."
    exit 1
fi

ok "Principal ID: ${BOLD}${PRINCIPAL_ID}${RESET}"
echo ""

# ---------------------------------------------------------------------------
# Step 2: Choose which services to configure
# ---------------------------------------------------------------------------
echo -e "${BOLD}Step 2: Select services to configure${RESET}"
echo "--------------------------------------------------------------"
echo ""
echo "  1) Azure OpenAI Service only"
echo "  2) Azure AI Foundry Agent Service only"
echo "  3) Both Azure OpenAI and Foundry Agent Service"
echo ""

prompt SERVICE_CHOICE "Enter choice (1/2/3)" "3"

SETUP_OPENAI=false
SETUP_FOUNDRY=false

case "$SERVICE_CHOICE" in
    1) SETUP_OPENAI=true ;;
    2) SETUP_FOUNDRY=true ;;
    3) SETUP_OPENAI=true; SETUP_FOUNDRY=true ;;
    *)
        error "Invalid choice. Please enter 1, 2, or 3."
        exit 1
        ;;
esac

echo ""

# ---------------------------------------------------------------------------
# Step 3a: Azure OpenAI Service RBAC
# ---------------------------------------------------------------------------
if [[ "$SETUP_OPENAI" == true ]]; then
    echo -e "${BOLD}Step 3a: Azure OpenAI Service Permissions${RESET}"
    echo "--------------------------------------------------------------"
    echo ""
    echo "The proxy needs 'Cognitive Services OpenAI User' on each Azure"
    echo "OpenAI account it will proxy to."
    echo ""

    while true; do
        prompt OPENAI_RG "Azure OpenAI resource group" ""
        prompt OPENAI_ACCOUNT "Azure OpenAI account name" ""

        OPENAI_SCOPE="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${OPENAI_RG}/providers/Microsoft.CognitiveServices/accounts/${OPENAI_ACCOUNT}"

        assign_role "$PRINCIPAL_ID" "$ROLE_COGNITIVE_SERVICES_OPENAI_USER" "$OPENAI_SCOPE" "Cognitive Services OpenAI User"

        prompt ADD_ANOTHER "Add another Azure OpenAI account? (y/n)" "n"
        if [[ "${ADD_ANOTHER,,}" != "y" ]]; then
            break
        fi
        echo ""
    done
fi

# ---------------------------------------------------------------------------
# Step 3b: Azure AI Foundry Agent Service RBAC
# ---------------------------------------------------------------------------
if [[ "$SETUP_FOUNDRY" == true ]]; then
    echo -e "${BOLD}Step 3b: Azure AI Foundry Agent Service Permissions${RESET}"
    echo "--------------------------------------------------------------"
    echo ""
    echo "Foundry agents need two roles:"
    echo "  • 'Cognitive Services OpenAI User' on the AI Services hub"
    echo "    (for access to the underlying models)"
    echo "  • 'Azure AI User' on the AI Foundry project"
    echo "    (for agent create/run/delete operations)"
    echo ""

    while true; do
        prompt FOUNDRY_RG "AI Foundry resource group" ""
        prompt FOUNDRY_HUB "AI Services hub account name" ""
        prompt FOUNDRY_PROJECT "AI Foundry project name" ""

        # Role 1: Cognitive Services OpenAI User on hub
        HUB_SCOPE="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${FOUNDRY_RG}/providers/Microsoft.CognitiveServices/accounts/${FOUNDRY_HUB}"

        assign_role "$PRINCIPAL_ID" "$ROLE_COGNITIVE_SERVICES_OPENAI_USER" "$HUB_SCOPE" "Cognitive Services OpenAI User (hub)"

        # Role 2: Azure AI User on project
        PROJECT_SCOPE="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${FOUNDRY_RG}/providers/Microsoft.CognitiveServices/accounts/${FOUNDRY_HUB}/projects/${FOUNDRY_PROJECT}"

        assign_role "$PRINCIPAL_ID" "$ROLE_AZURE_AI_USER" "$PROJECT_SCOPE" "Azure AI User (project)"

        prompt ADD_ANOTHER "Add another Foundry project? (y/n)" "n"
        if [[ "${ADD_ANOTHER,,}" != "y" ]]; then
            break
        fi
        echo ""
    done
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo "============================================================"
echo -e "${BOLD}Setup Complete${RESET}"
echo "============================================================"
echo ""
ok "All RBAC roles have been assigned to principal: ${PRINCIPAL_ID}"
echo ""
echo "Roles assigned:"
if [[ "$SETUP_OPENAI" == true ]]; then
    echo "  • Cognitive Services OpenAI User → Azure OpenAI account(s)"
fi
if [[ "$SETUP_FOUNDRY" == true ]]; then
    echo "  • Cognitive Services OpenAI User → AI Services hub(s)"
    echo "  • Azure AI User → AI Foundry project(s)"
fi
echo ""
warn "RBAC role assignments can take up to 5 minutes to propagate."
echo ""
echo "Next steps:"
echo "  1. Add models in the Admin UI with 'Use Managed Identity' enabled"
echo "  2. For Foundry agents, use the AI Services endpoint format:"
echo "     https://<account>.services.ai.azure.com/api/projects/<project>"
echo ""
