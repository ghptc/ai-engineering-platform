import React, { useState } from "react";
import ReactDOM from "react-dom/client";
import {
  BrowserRouter,
  Link,
  Route,
  Routes,
  useLocation,
} from "react-router-dom";
import {
  QueryClient,
  QueryClientProvider,
  useQueryClient,
} from "@tanstack/react-query";
import {
  App,
  Button,
  ConfigProvider,
  Form,
  Input,
  Layout,
  Menu,
  Drawer,
  Grid,
  Spin,
  Switch,
  Typography,
  theme,
  Alert,
} from "antd";
import {
  ApartmentOutlined,
  AppstoreOutlined,
  BranchesOutlined,
  CheckSquareOutlined,
  CommentOutlined,
  DashboardOutlined,
  DollarOutlined,
  MenuOutlined,
  LogoutOutlined,
  RobotOutlined,
  SettingOutlined,
} from "@ant-design/icons";
import { api, ApiError } from "./services/api";
import { useApi } from "./components/Common";
import { usePreferences } from "./app/preferences";
import { Dashboard } from "./app/Dashboard";
import { Projects } from "./features/projects/Projects";
import { Workspace } from "./features/projects/Workspace";
import { Conversations } from "./features/conversations/Conversations";
import { Executions, ExecutionDetail } from "./features/executions/Executions";
import { Reviews } from "./features/reviews/Reviews";
import { Approvals } from "./features/reviews/Approvals";
import { Collaboration } from "./features/collaboration/Collaboration";
import { Usage } from "./features/usage/Usage";
import { Settings } from "./features/settings/Settings";
import "./styles/app.css";
const queryClient = new QueryClient();
function SignIn() {
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const client = useQueryClient();
  return (
    <div className="signin">
      <div className="signin-story">
        <span className="brand-icon">
          <ApartmentOutlined />
        </span>
        <Typography.Title>
          Build with a team.
          <br />
          Deliver with evidence.
        </Typography.Title>
        <Typography.Paragraph>
          AI Engineering Platform connects product context, specialized agents,
          and verified engineering workflows.
        </Typography.Paragraph>
        <div className="signin-steps">
          CONTEXT <span>→</span> ENGINEERING <span>→</span> DELIVERY
        </div>
      </div>
      <div className="signin-form">
        <span className="eyebrow">AI ENGINEERING PLATFORM</span>
        <Typography.Title level={2}>Welcome to your workspace</Typography.Title>
        <Typography.Paragraph type="secondary">
          Sign in with your configured owner credential.
        </Typography.Paragraph>
        {error && (
          <Alert type="error" title={error} style={{ marginBottom: 20 }} />
        )}
        <Form
          layout="vertical"
          onFinish={async ({ password }) => {
            setLoading(true);
            setError("");
            try {
              await api("/auth/login", "POST", { password });
              await client.invalidateQueries();
            } catch (e) {
              setError((e as Error).message);
            } finally {
              setLoading(false);
            }
          }}
        >
          <Form.Item
            label="Owner credential"
            name="password"
            rules={[{ required: true }]}
          >
            <Input.Password autoComplete="current-password" size="large" />
          </Form.Item>
          <Button
            type="primary"
            htmlType="submit"
            size="large"
            block
            loading={loading}
          >
            Sign in
          </Button>
        </Form>
      </div>
    </div>
  );
}
function Shell() {
  const auth = useApi<{ name: string }>("/auth/me", true, 60000);
  const screens = Grid.useBreakpoint();
  const [navigationOpen, setNavigationOpen] = useState(false);
  const location = useLocation();
  const dark = usePreferences((s) => s.dark);
  const toggle = usePreferences((s) => s.toggle);
  const client = useQueryClient();
  if (auth.isLoading)
    return (
      <div className="loading">
        <Spin size="large" />
      </div>
    );
  if (auth.error instanceof ApiError && auth.error.status === 401)
    return <SignIn />;
  if (auth.error)
    return (
      <Alert
        title="Platform unavailable"
        description={auth.error.message}
        type="error"
      />
    );
  const items = [
    ["/", "Dashboard", <DashboardOutlined />],
    ["/projects", "Projects", <AppstoreOutlined />],
    ["/executions", "Executions", <BranchesOutlined />],
    ["/conversations", "Conversations", <CommentOutlined />],
    ["/collaboration", "Agent collaboration", <RobotOutlined />],
    ["/reviews", "Code reviews", <CheckSquareOutlined />],
    ["/approvals", "Approvals", <CheckSquareOutlined />],
    ["/usage", "Usage & Costs", <DollarOutlined />],
    ["/settings", "Settings & Agents", <SettingOutlined />],
  ] as const;
  const active =
    items.find(
      ([path]) => path !== "/" && location.pathname.startsWith(path),
    )?.[0] || "/";
  return (
    <Layout className="platform">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      <Drawer
        title="Engineering workspace"
        placement="left"
        open={navigationOpen}
        onClose={() => setNavigationOpen(false)}
      >
        <Menu
          selectedKeys={[active]}
          items={items.map(([key, label, icon]) => ({
            key,
            icon,
            label: (
              <Link to={key} onClick={() => setNavigationOpen(false)}>
                {label}
              </Link>
            ),
          }))}
        />
      </Drawer>
      <Layout.Sider
        breakpoint="lg"
        collapsedWidth={64}
        width={236}
        theme={dark ? "dark" : "light"}
        className="sidebar"
        style={{ display: screens.md ? "block" : "none" }}
      >
        <Link to="/" className="brand">
          <span className="brand-icon">
            <ApartmentOutlined />
          </span>
          <span>
            AI Engineering<small>PLATFORM</small>
          </span>
        </Link>
        <div className="nav-caption">WORKSPACE</div>
        <Menu
          mode="inline"
          theme={dark ? "dark" : "light"}
          selectedKeys={[active]}
          items={items.map(([key, label, icon]) => ({
            key,
            icon,
            label: <Link to={key}>{label}</Link>,
          }))}
        />
        <div className="sidebar-footer">Single-owner workspace</div>
      </Layout.Sider>
      <Layout>
        <Layout.Header className="topbar">
          {!screens.md && (
            <Button
              aria-label="Open navigation"
              icon={<MenuOutlined />}
              onClick={() => setNavigationOpen(true)}
            />
          )}
          <Typography.Text type="secondary">
            Engineering operations /{" "}
            <strong>{items.find(([key]) => key === active)?.[1]}</strong>
          </Typography.Text>
          <div className="topbar-actions">
            <Switch
              checked={dark}
              onChange={toggle}
              checkedChildren="Dark"
              unCheckedChildren="Light"
              aria-label="Dark mode"
            />
            <Button
              icon={<LogoutOutlined />}
              onClick={async () => {
                await api("/auth/logout", "POST", {});
                client.clear();
                window.location.assign("/");
              }}
            >
              Sign out
            </Button>
          </div>
        </Layout.Header>
        <Layout.Content className="content" id="main-content" tabIndex={-1}>
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/projects" element={<Projects />} />
            <Route path="/projects/:id" element={<Workspace />} />
            <Route path="/executions" element={<Executions />} />
            <Route path="/executions/:id" element={<ExecutionDetail />} />
            <Route path="/conversations" element={<Conversations />} />
            <Route path="/collaboration" element={<Collaboration />} />
            <Route path="/usage" element={<Usage />} />
            <Route path="/reviews" element={<Reviews />} />
            <Route path="/approvals" element={<Approvals />} />
            <Route path="/settings" element={<Settings />} />
            <Route
              path="*"
              element={
                <Alert
                  type="info"
                  title="Page not found"
                  description={<Link to="/">Return to workspace</Link>}
                />
              }
            />
          </Routes>
        </Layout.Content>
      </Layout>
    </Layout>
  );
}
function Root() {
  const dark = usePreferences((s) => s.dark);
  return (
    <ConfigProvider
      theme={{
        algorithm: dark ? theme.darkAlgorithm : theme.defaultAlgorithm,
        token: {
          colorPrimary: dark ? "#6e98ef" : "#285fc2",
          colorLink: dark ? "#8bb1ff" : "#245bb8",
          borderRadius: 10,
          fontFamily: "Inter, system-ui, -apple-system, sans-serif",
          colorBgLayout: dark ? "#0d1420" : "#f3f5f9",
          colorBgContainer: dark ? "#141e2d" : "#ffffff",
        },
        components: {
          Layout: {
            headerBg: dark ? "#101a28" : "#fff",
            siderBg: dark ? "#101a28" : "#fff",
          },
          Menu: { darkItemBg: "#101a28", darkItemSelectedBg: "#203355" },
        },
      }}
    >
      <App>
        <Shell />
      </App>
    </ConfigProvider>
  );
}
ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Root />
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>,
);
