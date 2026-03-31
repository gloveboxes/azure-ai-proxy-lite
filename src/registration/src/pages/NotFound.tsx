import { makeStyles, Title1, Body1 } from "@fluentui/react-components";

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100vh",
    textAlign: "center",
    gap: "16px",
  },
});

export function NotFound() {
  const styles = useStyles();

  return (
    <div className={styles.container}>
      <Title1>Page Not Found</Title1>
      <Body1>
        The page you're looking for doesn't exist. Event registration links are
        in the format <code>/event/&lt;event-id&gt;</code>.
      </Body1>
    </div>
  );
}
