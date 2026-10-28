import Editor, { loader } from "@monaco-editor/react";
import * as monaco from "monaco-editor";
import EditorWorker from "monaco-editor/editor/editor.worker.js?worker";
import { usePreferences } from "../app/preferences";
self.MonacoEnvironment = { getWorker: () => new EditorWorker() };
loader.config({ monaco });
export default function CodeViewer({
  content,
  language = "diff",
}: {
  content: string;
  language?: string;
}) {
  const dark = usePreferences((s) => s.dark);
  return (
    <Editor
      height="60vh"
      language={language}
      value={content}
      theme={dark ? "vs-dark" : "light"}
      options={{
        readOnly: true,
        minimap: { enabled: false },
        wordWrap: "on",
        scrollBeyondLastLine: false,
        automaticLayout: true,
      }}
    />
  );
}
