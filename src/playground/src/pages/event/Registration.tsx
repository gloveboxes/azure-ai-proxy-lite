import { useClientPrincipal } from "@aaronpowell/react-static-web-apps-auth";
import {
  Button,
  Input,
  Link,
  Toast,
  ToastTitle,
  ToastTrigger,
  Toaster,
  makeStyles,
  shorthands,
  useId,
  useToastController,
} from "@fluentui/react-components";
import { CopyRegular, EyeOffRegular, EyeRegular } from "@fluentui/react-icons";
import { useEffect, useReducer } from "react";
import ReactMarkdown from "react-markdown";
import { Form, useLoaderData } from "react-router-dom";
import { reducer } from "./Registration.reducers";
import type { AiToolkitEndpoint, AttendeeRegistration, EventDetails } from "./Registration.state";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.margin("0px", "80px"),
    fontSize: "20px",
    fontFamily: "Arial, Verdana, sans-serif",
    lineHeight: "1.5",
  },
  apiKeyDisplay: { display: "flex", alignItems: "center", columnGap: "4px" },
  wideInput: { minWidth: "420px" },
  toolkitDescription: {
    ...shorthands.margin("4px", "0px", "12px", "0px"),
    fontSize: "16px",
    color: "#555",
  },
  toolkitTable: {
    width: "100%",
    borderCollapse: "collapse" as const,
    ...shorthands.margin("0px", "0px", "16px", "0px"),
    tableLayout: "fixed" as const,
  },
  toolkitThModel: {
    textAlign: "left" as const,
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderBottom("2px", "solid", "#e0e0e0"),
    fontSize: "16px",
    fontWeight: "600",
    color: "#333",
    width: "160px",
  },
  toolkitThEndpoint: {
    textAlign: "left" as const,
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderBottom("2px", "solid", "#e0e0e0"),
    fontSize: "16px",
    fontWeight: "600",
    color: "#333",
  },
  toolkitTd: {
    ...shorthands.padding("8px", "12px"),
    ...shorthands.borderBottom("1px", "solid", "#f0f0f0"),
    fontSize: "16px",
    verticalAlign: "middle" as const,
    ...shorthands.overflow("hidden"),
  },
  toolkitModelName: {
    fontWeight: "600",
    whiteSpace: "nowrap" as const,
  },
  toolkitEndpointCell: {
    display: "flex",
    alignItems: "center",
    columnGap: "4px",
    minWidth: "0px",
  },
  toolkitEndpointText: {
    flexGrow: 1,
    flexShrink: 1,
    minWidth: "0px",
    whiteSpace: "nowrap" as const,
    overflowX: "hidden" as const,
    textOverflow: "ellipsis" as const,
    fontSize: "14px",
    fontFamily: "monospace",
    color: "#666",
    backgroundColor: "#f5f5f5",
    ...shorthands.padding("6px", "10px"),
    ...shorthands.borderRadius("4px"),
    ...shorthands.border("1px", "solid", "#e0e0e0"),
  },
  toolkitCopyButton: {
    flexShrink: 0,
  },
});

export const Registration = () => {
  const { event, attendee } = useLoaderData() as {
    event: EventDetails;
    attendee?: AttendeeRegistration;
  };

  const styles = useStyles();

  const [state, dispatch] = useReducer(reducer, {
    profileLoaded: false,
    showApiKey: false,
  });
  const { loaded, clientPrincipal } = useClientPrincipal();

  useEffect(() => {
    dispatch({
      type: "PROFILE_LOADED",
      payload: { loaded, profile: clientPrincipal || undefined },
    });
  }, [loaded, clientPrincipal]);

  const toasterId = useId("toaster");
  const { dispatchToast } = useToastController(toasterId);

  const notify = () =>
    dispatchToast(
      <Toast>
        <ToastTitle
          action={
            <ToastTrigger>
              <Link>Dismiss</Link>
            </ToastTrigger>
          }
        >
          Copied to clipboard.
        </ToastTitle>
      </Toast>,
      { position: "top", intent: "success" }
    );

  const copyToClipboard = async (value: string) => {
    await navigator.clipboard.writeText(value);
    notify();
  };

  const adjustedLocalTime = (
    timestamp: Date,
    utcOffsetInMinutes: number
  ): string => {
    // returns time zone adjusted date/time
    const date = new Date(timestamp);
    // get the timezone offset component that was added as no tz supplied in date time
    const tz = date.getTimezoneOffset();
    // remove the browser based timezone offset
    date.setMinutes(date.getMinutes() - tz);
    // add the event timezone offset
    date.setMinutes(date.getMinutes() - utcOffsetInMinutes);

    // Get the browser locale
    const locale = navigator.language || navigator.languages[0];

    // Specify the formatting options
    const options: Intl.DateTimeFormatOptions = {
      weekday: "long",
      year: "numeric",
      month: "long",
      day: "numeric",
      hour: "numeric",
      minute: "numeric",
    };

    // Create an Intl.DateTimeFormat object
    const formatter = new Intl.DateTimeFormat(locale, options);
    // Format the date
    const formattedDate = formatter.format(date);
    return formattedDate;
  };

  const trimmedEventCode = event?.eventCode?.trim();

  return (
    <section className={styles.container} >
      <h1>{trimmedEventCode}</h1>
      {event?.startTimestamp && event?.endTimestamp && event?.timeZoneLabel && (
        <div>
          <table>
            <tbody>
              <tr>
                <td>
                  <strong>Starts:</strong>
                </td>
                <td>
                  {adjustedLocalTime(
                    event?.startTimestamp,
                    event?.timeZoneOffset
                  )}
                </td>
              </tr>
              <tr>
                <td>
                  <strong>Ends:</strong>
                </td>
                <td>
                  {adjustedLocalTime(
                    event?.endTimestamp,
                    event?.timeZoneOffset
                  )}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      )}
      <h3>Generate your API Key</h3>
      Follow these steps to register and generate your API Key for this event:
      <ol>
        <li>Click <strong>Login with GitHub</strong> in the top right corner.</li>
        <li>Read the event description including the <strong>Terms of use</strong>.</li>
        <li>Scroll to the bottom of the page and click <strong>Register</strong>.</li>
        <li>Next, scroll down to the <strong>Registration Details</strong> section for your API Key and Endpoint.</li>
        <li>Then explore the <strong>Playground</strong> and <strong>SDK</strong> support.</li>
        <li>Forgotten your API Key? Just <strong>revisit</strong> this page.</li>
      </ol>
      <div style={{ textAlign: "left", padding: "0px" }}>
        <ReactMarkdown>{event?.eventMarkdown}</ReactMarkdown>
      </div>
      <h2>Terms of use</h2>
      <div>
        By registering for this event and gaining limited access to Azure AI services for the sole purpose of participating in the "{trimmedEventCode}" event, users acknowledge and agree to use the provided service responsibly and in accordance with the outlined terms. This privilege of limited access to Azure AI services is extended with the expectation that participants will refrain from any form of abuse, including but not limited to, malicious activities, unauthorized access, or any other actions that may disrupt the functionality of the services or compromise the experience for others. We reserve the right to revoke access to the free service in the event of any misuse or violation of these terms. Users are encouraged to engage with the service in a manner that fosters a positive and collaborative community environment.
      </div>
      {state.profileLoaded && state.profile && !attendee && (
        <div>
          <Form method="post">
            <Button type="submit" style={{ fontSize: "medium", marginBottom: "40px" }} appearance="primary">
              Register
            </Button>
          </Form>
        </div>
      )}
      {state.profileLoaded && state.profile && attendee && (
        <>
          <h2>Registration Details</h2>
          <div>
            <table>
              <tbody>
                <tr>
                  <td>
                    <strong>Your API Key:</strong>
                  </td>
                  <td>
                    <div className={styles.apiKeyDisplay}>
                      <Input
                        name="apiKey"
                        id="apiKey"
                        value={attendee.apiKey}
                        disabled={true}
                        type={state.showApiKey ? "text" : "password"}
                      />
                      <Button
                        icon={state.showApiKey ? <EyeRegular /> : <EyeOffRegular />}
                        onClick={() =>
                          dispatch({ type: "TOGGLE_API_KEY_VISIBILITY" })
                        }
                      />
                      <Button
                        icon={<CopyRegular />}
                        onClick={() => copyToClipboard(attendee.apiKey)}
                      />
                    </div>
                  </td>
                </tr>
                <tr>
                  <td>
                    <strong>Proxy Endpoint:</strong>
                  </td>
                  <td>
                    <div className={styles.apiKeyDisplay}>
                      <Input
                        name="endpoint"
                        id="endpoint"
                        type="text"
                        readOnly={true}
                        value={event?.proxyUrl ?? `${window.location.origin}/api/v1`}
                        disabled={true}
                        className={styles.wideInput}
                      />
                      <Button
                        icon={<CopyRegular />}
                        onClick={() =>
                          copyToClipboard(event?.proxyUrl ?? `${window.location.origin}/api/v1`)
                        }
                      />
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          {event?.aiToolkitEndpoints && event.aiToolkitEndpoints.length > 0 && (
            <>
              <h3>AI Toolkit Endpoints</h3>
              <p className={styles.toolkitDescription}>
                Use these endpoints to add custom models in the{" "}
                <Link href="https://github.com/microsoft/vscode-ai-toolkit" target="_blank" rel="noopener noreferrer" inline>
                  AI Toolkit for VS Code
                </Link>.
                Set your API Key as the authentication key when adding the model.
              </p>
              <table className={styles.toolkitTable}>
                <thead>
                  <tr>
                    <th className={styles.toolkitThModel}>Model</th>
                    <th className={styles.toolkitThEndpoint}>Endpoint URL</th>
                  </tr>
                </thead>
                <tbody>
                  {event.aiToolkitEndpoints.map((ep: AiToolkitEndpoint) => (
                    <tr key={ep.deploymentName}>
                      <td className={`${styles.toolkitTd} ${styles.toolkitModelName}`}>
                        <div className={styles.toolkitEndpointCell}>
                          {ep.deploymentName}
                          <Button
                            icon={<CopyRegular />}
                            onClick={() => copyToClipboard(ep.deploymentName)}
                            className={styles.toolkitCopyButton}
                            size="small"
                          />
                        </div>
                      </td>
                      <td className={styles.toolkitTd}>
                        <div className={styles.toolkitEndpointCell}>
                          <div className={styles.toolkitEndpointText} title={ep.endpointUrl}>
                            {ep.endpointUrl}
                          </div>
                          <Button
                            icon={<CopyRegular />}
                            onClick={() => copyToClipboard(ep.endpointUrl)}
                            className={styles.toolkitCopyButton}
                          />
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
          <h3>Playground Access</h3>
          The playground allows you to experiment with generative AI prompts.
          <ol>
            <li>Copy your API Key. </li>
            <li>When you navigate to the AI Proxy Playground, paste the API Key and Authorize.
            </li>
            <li>Navigate to the{" "}
              <Link href={`${window.location.origin}`} target="_blank" rel="noopener noreferrer">AI Proxy Playground</Link>.</li>
          </ol>
          <h3>SDK Access</h3>
          The real power of the Azure OpenAI Service is in the SDKs that allow you to integrate AI capabilities into your applications. You'll need your API Key and the proxy Endpoint to access AI resources using an SDK such as the OpenAI SDK or making REST calls.
          <h4>Python example using the OpenAI Python SDK</h4>
          The following Python code demonstrates how to use the OpenAI Python SDK to interact with the Azure OpenAI Service.
          <pre >
            <code style={{ lineHeight: "1", fontSize: "medium" }}>
              {`# pip install openai

from openai import AzureOpenAI

ENDPOINT = "${event?.proxyUrl ?? `${window.location.origin}/api/v1`}"
API_KEY = "<YOUR_API_KEY>"

API_VERSION = "2024-02-01"
MODEL_NAME = "gpt-35-turbo"

client = AzureOpenAI(
    azure_endpoint=ENDPOINT,
    api_key=API_KEY,
    api_version=API_VERSION,
)

MESSAGES = [
    {"role": "system", "content": "You are a helpful assistant."},
    {"role": "user", "content": "Who won the world series in 2020?"},
    {
        "role": "assistant",
        "content": "The Los Angeles Dodgers won the World Series in 2020.",
    },
    {"role": "user", "content": "Where was it played?"},
]

completion = client.chat.completions.create(
    model=MODEL_NAME,
    messages=MESSAGES,
)

print(completion.model_dump_json(indent=2))`}
            </code>
          </pre>
          <h3 style={{ "marginBottom": "10px" }}>More examples</h3>
          <ul>
            <li>
              <Link
                href="https://learn.microsoft.com/azure/ai-services/openai/quickstart"
                target="_blank"
                rel="noopener noreferrer"
              >
                Quickstart: Get started generating text using Azure OpenAI Service
              </Link>
            </li>
            <li>
              <Link
                href="https://github.com/microsoft/azure-openai-service-proxy/tree/main/examples"
                target="_blank"
                rel="noopener noreferrer"
              >
                Azure OpenAI Service Proxy Examples
              </Link>
            </li>
          </ul>
          <br />
          {/* </div> */}
        </>
      )}

      {state.profileLoaded && !state.profile && (
        <h3>Login with GitHub to register.</h3>
      )}

      <Toaster toasterId={toasterId} />
    </section>
  );
};
